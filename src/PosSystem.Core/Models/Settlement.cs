namespace PosSystem.Core.Models;

public class SettlementSummary
{
    public DateTime BusinessDate { get; set; }
    public int TotalOrders { get; set; }
    public int CancelledOrders { get; set; }
    public decimal GrossSales { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal NetSales { get; set; }
    public decimal VatTotal { get; set; }
    public decimal CardSales { get; set; }
    public decimal EasyPaySales { get; set; }
    public decimal CashSales { get; set; }
    public decimal CashReceiptSales { get; set; }
    public decimal ExpectedDrawerCash => CashSales + CashReceiptSales;
}

public class SettlementRecord
{
    public int Id { get; set; }
    public DateTime BusinessDate { get; set; }
    public string StoreCode { get; set; } = "STR-001";
    public string PosNo { get; set; } = "POS-01";
    public int TotalOrders { get; set; }
    public decimal NetSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal EasyPaySales { get; set; }
    public decimal CashSales { get; set; }
    public decimal ExpectedDrawerCash { get; set; }
    public decimal ActualDrawerCash { get; set; }
    public decimal CashDifference => ActualDrawerCash - ExpectedDrawerCash;
    public string ClosedBy { get; set; } = "Admin";
    public DateTime ClosedAt { get; set; } = DateTime.Now;
}
