using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;
using PosSystem.Core.Services;

namespace PosSystem.Wpf.ViewModels;

public partial class OrderHistoryViewModel : FeatureViewModelBase
{
    private readonly IPosRepository _repository;
    private readonly OrderService _orderService;
    private readonly IReceiptPrinter _receiptPrinter;

    public event Func<Task>? OrderCancelled;
    public event Action<string, string>? ReceiptReprinted;

    public OrderHistoryViewModel(
        IPosRepository repository,
        OrderService orderService,
        IReceiptPrinter receiptPrinter)
    {
        _repository = repository;
        _orderService = orderService;
        _receiptPrinter = receiptPrinter;
    }

    public ObservableCollection<Order> Orders { get; } = new();

    [ObservableProperty] private Order? _selectedOrder;
    [ObservableProperty] private bool _isBusy;

    public async Task RefreshAsync()
    {
        var list = await _repository.GetOrdersAsync();
        Orders.Clear();
        foreach (var o in list)
            Orders.Add(o);
    }

    [RelayCommand]
    private async Task CancelSelectedOrderAsync()
    {
        if (SelectedOrder is null)
        {
            ReportStatus("취소할 주문을 선택해 주세요.");
            return;
        }

        try
        {
            IsBusy = true;
            string orderNumber = SelectedOrder.OrderNumber;
            await _orderService.CancelOrderAsync(SelectedOrder.Id);
            await RefreshAsync();

            if (OrderCancelled is not null)
                await OrderCancelled.Invoke();

            ReportStatus($"주문 {orderNumber} 취소(VAN 취소 및 재고 복원)가 완료되었습니다.");
        }
        catch (Exception ex)
        {
            ReportStatus($"주문 취소 실패: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReprintSelectedReceiptAsync()
    {
        if (SelectedOrder is null)
        {
            ReportStatus("재발행할 주문을 선택해 주세요.");
            return;
        }

        var (preview, hex) = await _receiptPrinter.PrintReceiptAsync(
            SelectedOrder, SelectedOrder.Payments.FirstOrDefault());

        ReceiptReprinted?.Invoke(preview, hex);
        ReportStatus($"주문 {SelectedOrder.OrderNumber} 영수증 재발행 완료.");
    }
}
