using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Wpf.ViewModels;

public partial class HqSyncDiagnosticsViewModel : FeatureViewModelBase
{
    private readonly IPosRepository _repository;
    private readonly ISyncService _syncService;

    public event Action<string>? HqStatusChanged;
    public event Func<Task>? ProductsPulled;

    public HqSyncDiagnosticsViewModel(IPosRepository repository, ISyncService syncService)
    {
        _repository = repository;
        _syncService = syncService;
    }

    public ObservableCollection<SyncOutboxItem> PendingOutbox { get; } = new();
    public ObservableCollection<DiagnosticLog> DiagnosticLogs { get; } = new();

    [ObservableProperty] private bool _isBusy;

    public async Task RefreshAsync()
    {
        var outbox = await _repository.GetPendingOutboxAsync();
        PendingOutbox.Clear();
        foreach (var o in outbox)
            PendingOutbox.Add(o);

        var logs = await _repository.GetRecentLogsAsync();
        DiagnosticLogs.Clear();
        foreach (var l in logs)
            DiagnosticLogs.Add(l);
    }

    [RelayCommand]
    public async Task CheckHqHealthAsync()
    {
        bool ok = await _syncService.CheckHqHealthAsync();
        HqStatusChanged?.Invoke(ok ? "본사 API: 정상 연결 (localhost:5180)" : "본사 API: 오프라인 (Outbox 대기 모드)");
    }

    [RelayCommand]
    private async Task SyncOutboxAsync()
    {
        try
        {
            IsBusy = true;
            var (_, _, msg) = await _syncService.SyncPendingOutboxAsync();
            ReportStatus(msg);
            await CheckHqHealthAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ReportStatus($"동기화 오류: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PullProductsFromHqAsync()
    {
        try
        {
            IsBusy = true;
            int count = await _syncService.PullProductsFromHqAsync();
            await RefreshAsync();

            if (ProductsPulled is not null)
                await ProductsPulled.Invoke();

            ReportStatus($"본사 상품 마스터 {count}건 동기화 완료.");
        }
        catch (Exception ex)
        {
            ReportStatus($"상품 마스터 수신 실패 (HqApi 실행 여부를 확인하세요): {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
