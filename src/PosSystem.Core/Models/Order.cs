namespace PosSystem.Core.Models;

public class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string StoreCode { get; set; } = "STR-001";
    public string PosNo { get; set; } = "POS-01";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public decimal TotalAmount { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Completed;

    public List<OrderItem> Items { get; set; } = new();
    public List<PaymentTransaction> Payments { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal => Math.Max(0, (UnitPrice * Quantity) - DiscountAmount);
}
