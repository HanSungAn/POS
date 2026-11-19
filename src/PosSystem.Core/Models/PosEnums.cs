namespace PosSystem.Core.Models;

public enum PaymentMethod
{
    Card,
    EasyPay,
    Cash,
    CashReceipt
}

public enum OrderStatus
{
    Pending,
    Completed,
    Cancelled
}

public enum PaymentStatus
{
    Approved,
    Cancelled,
    Failed
}

public enum VanTransactionType
{
    Approval,
    Cancellation,
    EasyPayApproval,
    CashReceiptApproval,
    CashReceiptCancel
}

public enum SyncStatus
{
    Pending,
    Synced,
    Failed
}

public enum PeripheralStatus
{
    Connected,
    Disconnected,
    Error,
    Simulated
}

public enum DiagnosticLevel
{
    Info,
    Warning,
    Error,
    Protocol
}
