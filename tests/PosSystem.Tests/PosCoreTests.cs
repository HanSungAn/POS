using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;
using PosSystem.Core.Services;
using PosSystem.Infrastructure.Peripherals;
using PosSystem.Infrastructure.Protocols;

namespace PosSystem.Tests;

public class PosCoreTests
{
    [Fact]
    public void VanPacketCodec_EncodeAndDecode_ValidLrcAndFields()
    {
        var codec = new VanPacketCodec();
        var req = new VanRequest
        {
            TransactionType = VanTransactionType.Approval,
            Amount = 11000m,
            TaxAmount = 1000m,
            PosNo = "POS-01"
        };

        byte[] reqPacket = codec.EncodeRequest(req);
        Assert.Equal(VanPacketCodec.Stx, reqPacket[0]);
        Assert.Equal(VanPacketCodec.Etx, reqPacket[^2]);
        Assert.True(codec.VerifyLrc(reqPacket));

        byte[] respPacket = codec.EncodeMockResponse(req, simulateFailure: false);
        Assert.True(codec.VerifyLrc(respPacket));

        var decoded = codec.DecodeResponse(respPacket, VanPacketCodec.ToHexString(reqPacket));
        Assert.True(decoded.IsSuccess);
        Assert.Equal("0000", decoded.ResponseCode);
        Assert.False(string.IsNullOrWhiteSpace(decoded.ApprovalNumber));
    }

    [Fact]
    public void VanPacketCodec_CorruptedPacket_FailsLrcVerification()
    {
        var codec = new VanPacketCodec();
        byte[] packet = codec.EncodeRequest(new VanRequest
        {
            TransactionType = VanTransactionType.Approval,
            Amount = 5000m,
            TaxAmount = 455m
        });

        packet[6] ^= 0xFF; // 데이터 변조
        Assert.False(codec.VerifyLrc(packet));
        Assert.Throws<FormatException>(() => codec.DecodeResponse(packet, string.Empty));
    }

    [Fact]
    public void OrderService_CalculateTotals_ComputesKoreanVatCorrectly()
    {
        var items = new List<OrderItem>
        {
            new() { ProductId = 1, ProductName = "아메리카노", UnitPrice = 4500m, Quantity = 2 },
            new() { ProductId = 2, ProductName = "소금빵", UnitPrice = 3500m, Quantity = 1 }
        };

        var (total, discount, supply, vat) = OrderService.CalculateTotals(items, orderDiscount: 1500m);

        Assert.Equal(11000m, total);
        Assert.Equal(1500m, discount);
        Assert.Equal(1000m, vat);
        Assert.Equal(10000m, supply);
    }

    [Fact]
    public void EscPosReceiptPrinter_BuildEscPosBytes_ContainsInitAndCutCommands()
    {
        var printer = new EscPosReceiptPrinter();
        var order = new Order
        {
            OrderNumber = "20261119-0001",
            TotalAmount = 4500m,
            SupplyAmount = 4091m,
            VatAmount = 409m,
            Items = new List<OrderItem>
            {
                new() { ProductName = "아메리카노", Quantity = 1, UnitPrice = 4500m }
            }
        };

        byte[] bytes = printer.BuildEscPosBytes(order, null);

        // ESC @ (0x1B, 0x40)
        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal(0x40, bytes[1]);
        // GS V B 0 (0x1D, 0x56, 0x42, 0x00)
        Assert.Equal(0x1D, bytes[^4]);
        Assert.Equal(0x56, bytes[^3]);
        Assert.Equal(0x42, bytes[^2]);
        Assert.Equal(0x00, bytes[^1]);
    }

    [Fact]
    public async Task OrderService_CheckoutAndCancel_WorkflowSucceeds()
    {
        var repo = new InMemoryPosRepository();
        var codec = new VanPacketCodec();
        var gateway = new MockVanTerminalClient(codec, repo);
        var service = new OrderService(repo, gateway);

        var items = new List<OrderItem>
        {
            new() { ProductId = 1, ProductName = "아메리카노", Barcode = "880100000001", UnitPrice = 4500m, Quantity = 2 }
        };

        var (order, vanResp) = await service.CheckoutAsync(items, PaymentMethod.Card);

        Assert.NotNull(vanResp);
        Assert.True(vanResp.IsSuccess);
        Assert.Equal(9000m, order.TotalAmount);
        Assert.Equal(OrderStatus.Completed, order.Status);

        var cancelResp = await service.CancelOrderAsync(order.Id);
        Assert.NotNull(cancelResp);
        Assert.True(cancelResp.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, order.Payments.Count);
        Assert.Equal(-9000m, order.Payments[1].Amount);
    }

    [Fact]
    public async Task OrderService_Checkout99999_ThrowsVanRejection()
    {
        var repo = new InMemoryPosRepository();
        var gateway = new MockVanTerminalClient(new VanPacketCodec(), repo);
        var service = new OrderService(repo, gateway);

        var items = new List<OrderItem>
        {
            new() { ProductId = 1, ProductName = "테스트상품", UnitPrice = 99999m, Quantity = 1 }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CheckoutAsync(items, PaymentMethod.Card));

        Assert.Contains("E001", ex.Message);
        Assert.Contains(repo.Logs, l => l.Level == DiagnosticLevel.Warning);
    }

    private sealed class InMemoryPosRepository : IPosRepository
    {
        private readonly List<Order> _orders = new();
        public List<DiagnosticLog> Logs { get; } = new();
        private int _nextOrderId = 1;

        public Task EnsureDatabaseInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Product>> GetProductsAsync(bool activeOnly = true, CancellationToken ct = default) => Task.FromResult(new List<Product>());
        public Task<Product?> GetProductByBarcodeAsync(string barcode, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task UpdateProductStockAsync(int productId, int deltaQuantity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertProductsAsync(IEnumerable<Product> products, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Order> SaveOrderAsync(Order order, CancellationToken ct = default)
        {
            order.Id = _nextOrderId++;
            _orders.Add(order);
            return Task.FromResult(order);
        }

        public Task<List<Order>> GetOrdersAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default) =>
            Task.FromResult(_orders.ToList());

        public Task<Order?> GetOrderByIdAsync(int orderId, CancellationToken ct = default) =>
            Task.FromResult(_orders.FirstOrDefault(o => o.Id == orderId));

        public Task CancelOrderAsync(int orderId, PaymentTransaction cancelPayment, CancellationToken ct = default)
        {
            var order = _orders.First(o => o.Id == orderId);
            order.Status = OrderStatus.Cancelled;
            order.Payments.Add(cancelPayment);
            return Task.CompletedTask;
        }

        public Task<List<SyncOutboxItem>> GetPendingOutboxAsync(CancellationToken ct = default) => Task.FromResult(new List<SyncOutboxItem>());
        public Task MarkOutboxSyncedAsync(int outboxId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkOutboxFailedAsync(int outboxId, string error, CancellationToken ct = default) => Task.CompletedTask;

        public Task AddDiagnosticLogAsync(DiagnosticLog log, CancellationToken ct = default)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<List<DiagnosticLog>> GetRecentLogsAsync(int count = 100, CancellationToken ct = default) =>
            Task.FromResult(Logs.ToList());
    }
}
