using System.IO.Ports;
using System.Text;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;
using PosSystem.Infrastructure.Protocols;

namespace PosSystem.Infrastructure.Peripherals;

public class EscPosReceiptPrinter : IReceiptPrinter
{
    public PeripheralStatus Status { get; private set; } = PeripheralStatus.Simulated;
    public string PortName { get; set; } = "COM3";

    public byte[] BuildEscPosBytes(Order order, PaymentTransaction? payment)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding("utf-8");

        using var ms = new MemoryStream();

        // ESC @ : 프린터 초기화
        ms.Write(new byte[] { 0x1B, 0x40 });
        // ESC a 1 : 가운데 정렬
        ms.Write(new byte[] { 0x1B, 0x61, 0x01 });
        // GS ! 0x11 : 가로/세로 2배 확대
        ms.Write(new byte[] { 0x1D, 0x21, 0x11 });
        ms.Write(enc.GetBytes("[POS SYSTEM STORE]\n"));
        // GS ! 0x00 : 기본 크기
        ms.Write(new byte[] { 0x1D, 0x21, 0x00 });
        ms.Write(enc.GetBytes("사업자: 123-45-67890 | TEL: 02-1234-5678\n"));
        ms.Write(enc.GetBytes("----------------------------------------\n"));

        // ESC a 0 : 좌측 정렬
        ms.Write(new byte[] { 0x1B, 0x61, 0x00 });
        ms.Write(enc.GetBytes($"주문번호: {order.OrderNumber}\n"));
        ms.Write(enc.GetBytes($"거래일시: {order.CreatedAt:yyyy-MM-dd HH:mm:ss}\n"));
        ms.Write(enc.GetBytes($"상태구분: {(order.Status == OrderStatus.Cancelled ? "매출취소" : "정상매출")}\n"));
        ms.Write(enc.GetBytes("----------------------------------------\n"));
        ms.Write(enc.GetBytes("상품명              수량         금액\n"));
        ms.Write(enc.GetBytes("----------------------------------------\n"));

        foreach (var item in order.Items)
        {
            string name = item.ProductName.Length > 12 ? item.ProductName[..12] : item.ProductName;
            ms.Write(enc.GetBytes($"{name,-14} {item.Quantity,4} {item.LineTotal,12:N0}\n"));
        }

        ms.Write(enc.GetBytes("----------------------------------------\n"));
        ms.Write(enc.GetBytes($"공급가액: {order.SupplyAmount,28:N0}원\n"));
        ms.Write(enc.GetBytes($"부가세액: {order.VatAmount,28:N0}원\n"));
        if (order.DiscountAmount > 0)
            ms.Write(enc.GetBytes($"할인금액: {-order.DiscountAmount,28:N0}원\n"));
        ms.Write(enc.GetBytes($"합계금액: {order.TotalAmount,28:N0}원\n"));
        ms.Write(enc.GetBytes("----------------------------------------\n"));

        if (payment is not null)
        {
            ms.Write(enc.GetBytes($"결제수단: {payment.Method} ({payment.CardIssuer})\n"));
            if (!string.IsNullOrWhiteSpace(payment.CardMaskedNumber))
                ms.Write(enc.GetBytes($"카드/식별: {payment.CardMaskedNumber}\n"));
            ms.Write(enc.GetBytes($"승인번호: {payment.ApprovalNumber}\n"));
        }

        // ESC a 1 : 가운데 정렬 + 감사 문구 + 피드 + 부분 컷(GS V 66 0)
        ms.Write(new byte[] { 0x1B, 0x61, 0x01 });
        ms.Write(enc.GetBytes("\n이용해 주셔서 감사합니다.\n\n\n"));
        ms.Write(new byte[] { 0x1D, 0x56, 0x42, 0x00 });

        return ms.ToArray();
    }

    public async Task<(string PreviewText, string EscPosHex)> PrintReceiptAsync(
        Order order, PaymentTransaction? payment, CancellationToken ct = default)
    {
        byte[] escPosBytes = BuildEscPosBytes(order, payment);
        string hex = VanPacketCodec.ToHexString(escPosBytes);
        string preview = BuildPreviewText(order, payment);

        // 실제 COM 포트가 연결되어 있으면 시리얼 전송 시도, 없으면 시뮬레이터 모드 유지
        if (SerialPort.GetPortNames().Contains(PortName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var port = new SerialPort(PortName, 9600, Parity.None, 8, StopBits.One);
                port.WriteTimeout = 1000;
                port.Open();
                await port.BaseStream.WriteAsync(escPosBytes, ct);
                Status = PeripheralStatus.Connected;
            }
            catch
            {
                Status = PeripheralStatus.Simulated;
            }
        }
        else
        {
            Status = PeripheralStatus.Simulated;
        }

        return (preview, hex);
    }

    public Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        bool hasPort = SerialPort.GetPortNames().Contains(PortName, StringComparer.OrdinalIgnoreCase);
        Status = hasPort ? PeripheralStatus.Connected : PeripheralStatus.Simulated;
        return Task.FromResult(true);
    }

    private static string BuildPreviewText(Order order, PaymentTransaction? payment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("        [POS SYSTEM STORE]        ");
        sb.AppendLine("  사업자: 123-45-67890 | 02-1234-5678  ");
        sb.AppendLine("========================================");
        sb.AppendLine($"주문번호: {order.OrderNumber}");
        sb.AppendLine($"거래일시: {order.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"거래구분: {(order.Status == OrderStatus.Cancelled ? "매출취소" : "정상승인")}");
        sb.AppendLine("----------------------------------------");
        foreach (var item in order.Items)
        {
            sb.AppendLine($"{item.ProductName,-16} x{item.Quantity,-3} {item.LineTotal,10:N0}원");
        }
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"공급가액: {order.SupplyAmount,26:N0}원");
        sb.AppendLine($"부 가 세: {order.VatAmount,26:N0}원");
        if (order.DiscountAmount > 0)
            sb.AppendLine($"할인금액: {-order.DiscountAmount,26:N0}원");
        sb.AppendLine($"합계금액: {order.TotalAmount,26:N0}원");
        sb.AppendLine("----------------------------------------");
        if (payment is not null)
        {
            sb.AppendLine($"결제수단: {payment.Method} / {payment.CardIssuer}");
            if (!string.IsNullOrWhiteSpace(payment.CardMaskedNumber))
                sb.AppendLine($"번호정보: {payment.CardMaskedNumber}");
            sb.AppendLine($"승인번호: {payment.ApprovalNumber}");
        }
        sb.AppendLine("========================================");
        sb.AppendLine("       이용해 주셔서 감사합니다.       ");
        return sb.ToString();
    }
}
