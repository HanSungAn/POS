using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Sync;

/// <summary>
/// 본사 ASP.NET Core REST API(PosSystem.HqApi)와 통신하여
/// 미전송 매출/취소 Outbox 데이터를 업로드하고 본사 상품 마스터를 내려받는 동기화 클라이언트입니다.
/// </summary>
public class HqApiSyncClient : ISyncService
{
    private readonly HttpClient _httpClient;
    private readonly IPosRepository _repository;

    public HqApiSyncClient(HttpClient httpClient, IPosRepository repository)
    {
        _httpClient = httpClient;
        _repository = repository;
    }

    /// <summary>본사 서버가 살아있는지 /api/health 호출로 확인합니다.</summary>
    public async Task<bool> CheckHqHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _httpClient.GetAsync("/api/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            // 서버가 꺼져 있거나 네트워크가 단절된 경우 예외 대신 false(오프라인) 반환
            return false;
        }
    }

    /// <summary>
    /// 로컬 DB의 SyncOutboxItems 테이블에서 아직 전송되지 않은(Pending/Failed) 건을 꺼내 본사 서버로 전송합니다.
    /// </summary>
    public async Task<(int SyncedCount, int FailedCount, string Message)> SyncPendingOutboxAsync(CancellationToken ct = default)
    {
        var pending = await _repository.GetPendingOutboxAsync(ct);
        if (pending.Count == 0)
            return (0, 0, "동기화할 대기 데이터가 없습니다.");

        int synced = 0;
        int failed = 0;

        foreach (var item in pending)
        {
            try
            {
                var payload = new
                {
                    item.Id,
                    item.EventType,
                    item.AggregateId,
                    item.PayloadJson,
                    item.CreatedAt
                };

                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var resp = await _httpClient.PostAsync("/api/sync/events", content, ct);

                if (resp.IsSuccessStatusCode)
                {
                    // 전송 성공 시 Synced 상태로 변경해 재전송 대상에서 제외합니다.
                    await _repository.MarkOutboxSyncedAsync(item.Id, ct);
                    synced++;
                }
                else
                {
                    // 실패 시 재시도 횟수(RetryCount)와 오류 메시지를 기록합니다.
                    string err = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                    await _repository.MarkOutboxFailedAsync(item.Id, err, ct);
                    failed++;
                }
            }
            catch (Exception ex)
            {
                await _repository.MarkOutboxFailedAsync(item.Id, ex.Message, ct);
                failed++;
            }
        }

        await _repository.AddDiagnosticLogAsync(new DiagnosticLog
        {
            Category = "Sync",
            Level = failed > 0 ? DiagnosticLevel.Warning : DiagnosticLevel.Info,
            Message = $"본사 Outbox 동기화 완료 (성공: {synced}건, 실패: {failed}건)"
        }, ct);

        return (synced, failed, $"동기화 완료: 성공 {synced}건, 실패 {failed}건");
    }

    /// <summary>본사 서버에서 최신 상품 목록을 받아 로컬 DB에 Upsert(신규 추가/가격 갱신)합니다.</summary>
    public async Task<int> PullProductsFromHqAsync(CancellationToken ct = default)
    {
        var products = await _httpClient.GetFromJsonAsync<List<Product>>("/api/products", ct);
        if (products is null || products.Count == 0)
            return 0;

        await _repository.UpsertProductsAsync(products, ct);

        await _repository.AddDiagnosticLogAsync(new DiagnosticLog
        {
            Category = "Sync",
            Level = DiagnosticLevel.Info,
            Message = $"본사 상품 마스터 {products.Count}건 수신 및 로컬 DB 반영 완료"
        }, ct);

        return products.Count;
    }
}
