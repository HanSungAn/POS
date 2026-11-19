namespace PosSystem.Core.Models;

public class SyncOutboxItem
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SyncedAt { get; set; }
}

public class DiagnosticLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Category { get; set; } = string.Empty;
    public DiagnosticLevel Level { get; set; } = DiagnosticLevel.Info;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
