namespace PosSystem.Core.Models;

public class VanRequest
{
    public VanTransactionType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public string PosNo { get; set; } = "POS-01";
    public int InstallmentMonths { get; set; }
    public string? OriginalApprovalNumber { get; set; }
    public string? OriginalTransDate { get; set; }
    public string? PayProvider { get; set; }
    public string? CashReceiptIdentity { get; set; }
}

public class VanResponse
{
    public bool IsSuccess { get; set; }
    public string ResponseCode { get; set; } = string.Empty;
    public string ResponseMessage { get; set; } = string.Empty;
    public string ApprovalNumber { get; set; } = string.Empty;
    public string CardMaskedNumber { get; set; } = string.Empty;
    public string CardIssuer { get; set; } = string.Empty;
    public string VanCode { get; set; } = "KFTC-VAN";
    public DateTime TransactedAt { get; set; } = DateTime.Now;
    public string RawRequestHex { get; set; } = string.Empty;
    public string RawResponseHex { get; set; } = string.Empty;
    public string ParsedSummary { get; set; } = string.Empty;
}
