using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.Interfaces;

namespace PosSystem.Wpf.ViewModels;

/// <summary>
/// 메인 화면(Shell)의 상태바를 관리하고, 5개 기능 탭의 ViewModel을 조율(연결)하는 코디네이터 ViewModel입니다.
/// [ObservableProperty]와 [RelayCommand]는 CommunityToolkit.Mvvm이 컴파일 시 자동으로
/// INotifyPropertyChanged 프로퍼티와 ICommand를 생성해 주는 소스 생성기 어트리뷰트입니다.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IPosRepository _repository;

    // 각 탭의 UserControl에 바인딩되는 하위 ViewModel들
    public PosSaleViewModel PosSale { get; }
    public OrderHistoryViewModel OrderHistory { get; }
    public InventorySettlementViewModel InventorySettlement { get; }
    public PeripheralMonitorViewModel PeripheralMonitor { get; }
    public HqSyncDiagnosticsViewModel HqSyncDiagnostics { get; }

    [ObservableProperty] private string _statusMessage = "POS 초기화 대기 중...";
    [ObservableProperty] private string _dbStatusText = "MSSQL: 연결 중";
    [ObservableProperty] private string _hqStatusText = "본사 API: 미확인";
    [ObservableProperty] private bool _isBusy;

    public MainViewModel(
        IPosRepository repository,
        PosSaleViewModel posSale,
        OrderHistoryViewModel orderHistory,
        InventorySettlementViewModel inventorySettlement,
        PeripheralMonitorViewModel peripheralMonitor,
        HqSyncDiagnosticsViewModel hqSyncDiagnostics)
    {
        _repository = repository;
        PosSale = posSale;
        OrderHistory = orderHistory;
        InventorySettlement = inventorySettlement;
        PeripheralMonitor = peripheralMonitor;
        HqSyncDiagnostics = hqSyncDiagnostics;

        WireFeatureEvents();
    }

    /// <summary>
    /// 서로 다른 탭(기능) 간에 영향을 주는 동작을 이벤트로 느슨하게 연결합니다.
    /// 예: [POS 판매]에서 결제가 완료되면 [거래내역], [재고/정산], [영수증 모니터], [동기화 대기열]을 함께 갱신합니다.
    /// </summary>
    private void WireFeatureEvents()
    {
        // 모든 하위 ViewModel의 상태 메시지를 하단 상태바(StatusMessage)로 전달
        PosSale.StatusReported += msg => StatusMessage = msg;
        OrderHistory.StatusReported += msg => StatusMessage = msg;
        InventorySettlement.StatusReported += msg => StatusMessage = msg;
        PeripheralMonitor.StatusReported += msg => StatusMessage = msg;
        HqSyncDiagnostics.StatusReported += msg => StatusMessage = msg;

        // POS 판매 -> 주변기기 모니터 / 거래내역 / 재고정산 / Outbox 동기화
        PosSale.VfdUpdated += PeripheralMonitor.UpdateVfd;
        PosSale.CheckoutFailed += () => _ = HqSyncDiagnostics.RefreshAsync();
        PosSale.CheckoutCompleted += async args =>
        {
            PeripheralMonitor.UpdateReceipt(args.ReceiptPreview, args.EscPosHex);
            if (args.CashDrawerOpened)
                PeripheralMonitor.MarkDrawerOpened();

            await Task.WhenAll(
                OrderHistory.RefreshAsync(),
                InventorySettlement.RefreshAsync(),
                HqSyncDiagnostics.RefreshAsync());
        };

        // 거래내역 취소/재발행 -> 주변기기 영수증 / 상품 재고 / 정산 / Outbox 동기화
        OrderHistory.ReceiptReprinted += PeripheralMonitor.UpdateReceipt;
        OrderHistory.OrderCancelled += () => Task.WhenAll(
            PosSale.RefreshProductsAsync(),
            InventorySettlement.RefreshAsync(),
            HqSyncDiagnostics.RefreshAsync());

        // 재고 입고 -> POS 판매 상품 목록 갱신
        InventorySettlement.StockAdjusted += PosSale.RefreshProductsAsync;

        // 돈통 강제 개방 -> 진단 로그 갱신
        PeripheralMonitor.DrawerOpened += HqSyncDiagnostics.RefreshAsync;

        // 본사 API 상태 및 상품 마스터 수신 -> 상태바 및 상품 목록 갱신
        HqSyncDiagnostics.HqStatusChanged += status => HqStatusText = status;
        HqSyncDiagnostics.ProductsPulled += () => Task.WhenAll(
            PosSale.RefreshProductsAsync(),
            InventorySettlement.RefreshAsync());
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            await _repository.EnsureDatabaseInitializedAsync();
            DbStatusText = "MSSQL: 연결됨 (SQL Server)";
            await RefreshAllDataAsync();
            await HqSyncDiagnostics.CheckHqHealthAsync();
            StatusMessage = "POS 시스템이 정상적으로 준비되었습니다.";
        }
        catch (Exception ex)
        {
            DbStatusText = "MSSQL: 연결 오류";
            StatusMessage = $"DB 초기화 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAllDataAsync()
    {
        await Task.WhenAll(
            PosSale.RefreshProductsAsync(),
            OrderHistory.RefreshAsync(),
            InventorySettlement.RefreshAsync(),
            HqSyncDiagnostics.RefreshAsync());
    }
}
