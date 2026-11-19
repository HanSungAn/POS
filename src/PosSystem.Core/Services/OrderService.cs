using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Core.Services;

/// <summary>
/// POS의 핵심 비즈니스 로직(금액/부가세 계산, 결제 승인, 매출 취소)을 담당하는 서비스입니다.
/// UI(WPF)나 DB(MSSQL) 구현체를 직접 알지 않고 인터페이스에만 의존하도록 설계해 테스트와 유지보수가 쉽습니다.
/// </summary>
public class OrderService
{
    private readonly IPosRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    // 생성자 주입(Dependency Injection): 필요한 저장소와 결제 모듈을 외부에서 전달받습니다.
    public OrderService(IPosRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    /// <summary>
    /// 장바구니 상품 목록과 주문 할인을 받아 총 결제금액, 총 할인액, 공급가액, 부가세(VAT)를 계산합니다.
    /// </summary>
    public static (decimal TotalAmount, decimal DiscountAmount, decimal SupplyAmount, decimal VatAmount) CalculateTotals(
        IEnumerable<OrderItem> items, decimal orderDiscount = 0m)
    {
        var itemList = items.ToList();

        // 1. 할인 적용 전 원래 판매가 합계 (단가 × 수량)
        decimal gross = itemList.Sum(i => i.UnitPrice * i.Quantity);

        // 2. 개별 상품 할인 + 전체 주문 할인을 합산
        decimal itemDiscounts = itemList.Sum(i => i.DiscountAmount);
        decimal totalDiscount = itemDiscounts + orderDiscount;

        // 3. 실제 고객이 결제해야 할 최종 금액 (음수가 되지 않도록 0으로 하한선 처리)
        decimal netTotal = Math.Max(0m, gross - totalDiscount);

        // 4. 한국 부가세법 기준 (부가세 포함가 기준)
        //    예: 11,000원 상품 -> 부가세 = 11,000 / 11 = 1,000원, 공급가액 = 11,000 - 1,000 = 10,000원
        decimal vat = Math.Round(netTotal / 11m, 0, MidpointRounding.AwayFromZero);
        decimal supply = netTotal - vat;

        return (netTotal, totalDiscount, supply, vat);
    }

    /// <summary>
    /// 영수증과 DB에서 주문을 고유하게 구분하기 위한 주문번호를 생성합니다. (예: 20261119-071530-4821)
    /// </summary>
    public static string GenerateOrderNumber(DateTime now)
    {
        return $"{now:yyyyMMdd-HHmmss}-{Random.Shared.Next(1000, 9999)}";
    }

    /// <summary>
    /// 결제를 진행하고 주문을 DB에 저장합니다.
    /// (현금 외 결제는 VAN 게이트웨이 승인 성공 후에만 주문을 저장합니다.)
    /// </summary>
    public async Task<(Order Order, VanResponse? VanResponse)> CheckoutAsync(
        List<OrderItem> items,
        PaymentMethod method,
        decimal orderDiscount = 0m,
        int installmentMonths = 0,
        string? payProvider = null,
        string? cashReceiptIdentity = null,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            throw new InvalidOperationException("주문 항목이 비어 있습니다.");

        var now = DateTime.Now;
        var (total, discount, supply, vat) = CalculateTotals(items, orderDiscount);

        VanResponse? vanResponse = null;
        PaymentTransaction payment;

        if (method == PaymentMethod.Cash)
        {
            // 단순 현금 결제는 외부 VAN 단말기 통신 없이 로컬에서 바로 승인 처리합니다.
            payment = new PaymentTransaction
            {
                Method = PaymentMethod.Cash,
                Amount = total,
                ApprovalNumber = $"CSH{now:HHmmss}",
                CardIssuer = "현금결제",
                VanCode = "LOCAL",
                Status = PaymentStatus.Approved,
                TransactedAt = now
            };
        }
        else
        {
            // 결제 수단에 따라 VAN 전문 거래 코드를 매핑합니다.
            var txType = method switch
            {
                PaymentMethod.Card => VanTransactionType.Approval,
                PaymentMethod.EasyPay => VanTransactionType.EasyPayApproval,
                PaymentMethod.CashReceipt => VanTransactionType.CashReceiptApproval,
                _ => VanTransactionType.Approval
            };

            // VAN 단말기(또는 시뮬레이터)에 승인 요청을 보냅니다.
            vanResponse = await _paymentGateway.ProcessPaymentAsync(new VanRequest
            {
                TransactionType = txType,
                Amount = total,
                TaxAmount = vat,
                InstallmentMonths = installmentMonths,
                PayProvider = payProvider,
                CashReceiptIdentity = cashReceiptIdentity
            }, ct);

            // 승인 거절/오류 시 진단 로그를 DB에 남기고 예외를 발생시켜 주문 저장을 중단합니다.
            if (!vanResponse.IsSuccess)
            {
                await _repository.AddDiagnosticLogAsync(new DiagnosticLog
                {
                    Category = "VAN",
                    Level = DiagnosticLevel.Warning,
                    Message = $"결제 승인 거절 ({vanResponse.ResponseCode}): {vanResponse.ResponseMessage}",
                    Detail = vanResponse.RawResponseHex
                }, ct);

                throw new InvalidOperationException($"결제 승인 실패 [{vanResponse.ResponseCode}]: {vanResponse.ResponseMessage}");
            }

            // 승인 성공 시 원문 Hex 패킷과 승인번호를 결제 이력에 함께 보관합니다. (추후 장애 분석 및 취소용)
            payment = new PaymentTransaction
            {
                Method = method,
                Amount = total,
                ApprovalNumber = vanResponse.ApprovalNumber,
                CardMaskedNumber = vanResponse.CardMaskedNumber,
                CardIssuer = vanResponse.CardIssuer,
                VanCode = vanResponse.VanCode,
                Status = PaymentStatus.Approved,
                RawRequestPacket = vanResponse.RawRequestHex,
                RawResponsePacket = vanResponse.RawResponseHex,
                TransactedAt = vanResponse.TransactedAt
            };
        }

        // 주문 엔티티 조립 (OrderItems는 새 객체로 복사해 EF Core 추적 충돌을 방지합니다.)
        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(now),
            CreatedAt = now,
            TotalAmount = total,
            DiscountAmount = discount,
            SupplyAmount = supply,
            VatAmount = vat,
            PaidAmount = total,
            Status = OrderStatus.Completed,
            Items = items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Barcode = i.Barcode,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                DiscountAmount = i.DiscountAmount
            }).ToList(),
            Payments = new List<PaymentTransaction> { payment }
        };

        // DB 저장 + 재고 차감 + 본사 동기화 Outbox 적재를 한 번에 처리합니다.
        var saved = await _repository.SaveOrderAsync(order, ct);
        return (saved, vanResponse);
    }

    /// <summary>
    /// 완료된 주문을 취소(반품)합니다.
    /// 원거래 승인번호와 일자를 이용해 VAN 취소 승인을 먼저 받은 뒤 DB 상태 변경 및 재고 복원을 수행합니다.
    /// </summary>
    public async Task<VanResponse?> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _repository.GetOrderByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"주문을 찾을 수 없습니다. (ID: {orderId})");

        if (order.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("이미 취소된 주문입니다.");

        var originalPayment = order.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Approved)
            ?? throw new InvalidOperationException("원거래 결제 내역이 없습니다.");

        VanResponse? vanResponse = null;
        PaymentTransaction cancelPayment;

        if (originalPayment.Method == PaymentMethod.Cash)
        {
            // 현금 취소는 마이너스(-) 금액의 취소 결제 레코드를 생성합니다.
            cancelPayment = new PaymentTransaction
            {
                OrderId = order.Id,
                Method = PaymentMethod.Cash,
                Amount = -originalPayment.Amount,
                ApprovalNumber = $"CNL{DateTime.Now:HHmmss}",
                CardIssuer = "현금취소",
                VanCode = "LOCAL",
                Status = PaymentStatus.Cancelled,
                OriginalTransactionId = originalPayment.Id,
                TransactedAt = DateTime.Now
            };
        }
        else
        {
            // 카드/간편결제/현금영수증 취소 시 원거래 승인번호와 원거래일자(yyMMdd)가 필수입니다.
            var cancelType = originalPayment.Method == PaymentMethod.CashReceipt
                ? VanTransactionType.CashReceiptCancel
                : VanTransactionType.Cancellation;

            vanResponse = await _paymentGateway.ProcessPaymentAsync(new VanRequest
            {
                TransactionType = cancelType,
                Amount = originalPayment.Amount,
                TaxAmount = order.VatAmount,
                OriginalApprovalNumber = originalPayment.ApprovalNumber,
                OriginalTransDate = originalPayment.TransactedAt.ToString("yyMMdd")
            }, ct);

            if (!vanResponse.IsSuccess)
                throw new InvalidOperationException($"VAN 취소 실패 [{vanResponse.ResponseCode}]: {vanResponse.ResponseMessage}");

            cancelPayment = new PaymentTransaction
            {
                OrderId = order.Id,
                Method = originalPayment.Method,
                Amount = -originalPayment.Amount,
                ApprovalNumber = vanResponse.ApprovalNumber,
                CardMaskedNumber = originalPayment.CardMaskedNumber,
                CardIssuer = originalPayment.CardIssuer,
                VanCode = vanResponse.VanCode,
                Status = PaymentStatus.Cancelled,
                OriginalTransactionId = originalPayment.Id,
                RawRequestPacket = vanResponse.RawRequestHex,
                RawResponsePacket = vanResponse.RawResponseHex,
                TransactedAt = vanResponse.TransactedAt
            };
        }

        await _repository.CancelOrderAsync(order.Id, cancelPayment, ct);
        return vanResponse;
    }
}
