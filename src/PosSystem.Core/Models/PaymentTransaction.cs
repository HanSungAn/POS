namespace PosSystem.Core.Models;

public class PaymentTransaction
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string ApprovalNumber { get; set; } = string.Empty;
    public string CardMaskedNumber { get; set; } = string.Empty;
    public string CardIssuer { get; set; } = string.Empty;
    public string VanCode { get; set; } = "KFTC-VAN";
    public PaymentStatus Status { get; set; } = PaymentStatus.Approved;
    public string RawRequestPacket { get; set; } = string.Empty;
    public string RawResponsePacket { get; set; } = string.Empty;
    public DateTime TransactedAt { get; set; } = DateTime.Now;
    public int? OriginalTransactionId { get; set; }
}
