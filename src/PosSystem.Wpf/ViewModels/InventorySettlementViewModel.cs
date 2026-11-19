using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Wpf.ViewModels;

public partial class InventorySettlementViewModel : FeatureViewModelBase
{
    private readonly IPosRepository _repository;
    private readonly ISettlementRepository _settlementRepository;

    public event Func<Task>? StockAdjusted;

    public InventorySettlementViewModel(
        IPosRepository repository,
        ISettlementRepository settlementRepository)
    {
        _repository = repository;
        _settlementRepository = settlementRepository;
    }

    public ObservableCollection<Product> Products { get; } = new();
    public ObservableCollection<SettlementRecord> SettlementHistory { get; } = new();

    [ObservableProperty] private SettlementSummary _dailySummary = new();
    [ObservableProperty] private decimal _actualDrawerCashInput;

    public async Task RefreshAsync()
    {
        var products = await _repository.GetProductsAsync();
        Products.Clear();
        foreach (var p in products)
            Products.Add(p);

        DailySummary = await _settlementRepository.GetDailySummaryAsync(DateTime.Today);
        ActualDrawerCashInput = DailySummary.ExpectedDrawerCash;

        var history = await _settlementRepository.GetSettlementHistoryAsync();
        SettlementHistory.Clear();
        foreach (var h in history)
            SettlementHistory.Add(h);
    }

    [RelayCommand]
    private async Task AdjustStockAsync(Product? product)
    {
        if (product is null) return;
        await _repository.UpdateProductStockAsync(product.Id, 10);
        await RefreshAsync();

        if (StockAdjusted is not null)
            await StockAdjusted.Invoke();

        ReportStatus($"{product.Name} 재고 +10 입고 처리되었습니다.");
    }

    [RelayCommand]
    private async Task CloseDailySettlementAsync()
    {
        try
        {
            var summary = await _settlementRepository.GetDailySummaryAsync(DateTime.Today);
            var record = new SettlementRecord
            {
                BusinessDate = summary.BusinessDate,
                TotalOrders = summary.TotalOrders,
                NetSales = summary.NetSales,
                CardSales = summary.CardSales,
                EasyPaySales = summary.EasyPaySales,
                CashSales = summary.CashSales + summary.CashReceiptSales,
                ExpectedDrawerCash = summary.ExpectedDrawerCash,
                ActualDrawerCash = ActualDrawerCashInput,
                ClosedBy = "POS-ADMIN",
                ClosedAt = DateTime.Now
            };
            await _settlementRepository.SaveSettlementAsync(record);
            await RefreshAsync();
            ReportStatus($"일일 정산 마감이 저장되었습니다. (차액: {record.CashDifference:N0}원)");
        }
        catch (Exception ex)
        {
            ReportStatus($"정산 마감 실패: {ex.Message}");
        }
    }
}
