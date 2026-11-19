using System.Collections.Concurrent;

namespace PosSystem.HqApi.Services;

/// <summary>본사 서버가 가맹점 POS로부터 수신한 동기화 이벤트 레코드입니다.</summary>
public record SyncedEventRecord(
    int OutboxId,
    string EventType,
    string AggregateId,
    string PayloadJson,
    DateTime CreatedAt,
    DateTime ReceivedAt);

/// <summary>
/// 가맹점 동기화 이벤트를 메모리에 저장하는 본사 저장소입니다.
/// 네트워크 재시도로 같은 주문 이벤트가 여러 번 도착해도 중복 저장되지 않도록 멱등성(Idempotency)을 보장합니다.
/// </summary>
public class HqSyncStore
{
    // 멀티스레드 환경(동시 HTTP 요청)에서도 안전하게 접근할 수 있는 ConcurrentDictionary 사용
    private readonly ConcurrentDictionary<string, SyncedEventRecord> _events = new();

    /// <summary>
    /// "이벤트유형:주문번호"를 고유 키로 사용해 이미 처리된 이벤트면 무시(false)하고, 처음 온 이벤트만 저장(true)합니다.
    /// </summary>
    public bool TryAddIdempotent(SyncedEventRecord record)
    {
        string key = $"{record.EventType}:{record.AggregateId}";
        return _events.TryAdd(key, record);
    }

    public IReadOnlyCollection<SyncedEventRecord> GetAll() =>
        _events.Values.OrderByDescending(e => e.ReceivedAt).ToList();
}
