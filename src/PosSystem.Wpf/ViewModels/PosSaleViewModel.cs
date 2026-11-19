using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;
using PosSystem.Core.Services;

namespace PosSystem.Wpf.ViewModels;

/// <summary>결제 완료 시 다른 기능 탭(영수증 모니터, 정산 등)으로 전달할 결과 데이터입니다.</summary>
public record CheckoutCompletedEventArgs(
    Order Order,
    string ReceiptPreview,
    string EscPosHex,
    bool CashDrawerOpened);

/// <summary>
/// [1. POS 판매] 탭의 화면 상태와 명령(카테고리 필터, 바코드 스캔, 장바구니, 결제)을 담당하는 ViewModel입니다.
/// </summary>
public partial class PosSaleViewModel : FeatureViewModelBase
{
    private readonly IPosRepository _repository;
    private readonly OrderService _orderService;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly ICashDrawer _cashDrawer;
    private readonly IBarcodeScanner _barcodeScanner;
    private readonly ICustomerDisplay _customerDisplay;

    public event Func<CheckoutCompletedEventArgs, Task>? CheckoutCompleted;
    public event Action<string, string>? VfdUpdated;
    public event Action? CheckoutFailed;

    public PosSaleViewModel(
        IPosRepository repository,
        OrderService orderService,
        IReceiptPrinter receiptPrinter,
        ICashDrawer cashDrawer,
        IBarcodeScanner barcodeScanner,
        ICustomerDisplay customerDisplay)
    {
        _repository = repository;
        _orderService = orderService;
        _receiptPrinter = receiptPrinter;
        _cashDrawer = cashDrawer;
        _barcodeScanner = barcodeScanner;
        _customerDisplay = customerDisplay;

        // 실제 COM 바코드 스캐너 또는 시뮬레이션에서 바코드가 입력되면 OnBarcodeScanned가 호출됩니다.
        _barcodeScanner.BarcodeScanned += OnBarcodeScanned;
    }

    public ObservableCollection<Product> AllProducts { get; } = new();
    public ObservableCollection<Product> FilteredProducts { get; } = new();
    public ObservableCollection<CartItemViewModel> CartItems { get; } = new();
    public ObservableCollection<string> Categories { get; } = new() { "전체", "커피/음료", "베이커리", "편의/MD" };

    [ObservableProperty] private string _selectedCategory = "전체";
    [ObservableProperty] private string _barcodeInput = string.Empty;
    [ObservableProperty] private decimal _orderDiscount;
    [ObservableProperty] private decimal _totalAmount;
    [ObservableProperty] private decimal _supplyAmount;
    [ObservableProperty] private decimal _vatAmount;
    [ObservableProperty] private bool _isBusy;

    public async Task RefreshProductsAsync()
    {
        var list = await _repository.GetProductsAsync();
        AllProducts.Clear();
        foreach (var p in list)
            AllProducts.Add(p);
        ApplyProductFilter();
    }

    // SelectedCategory 값이 바뀔 때마다 자동으로 상품 목록 필터링을 다시 수행합니다.
    partial void OnSelectedCategoryChanged(string value) => ApplyProductFilter();

    // 주문 할인 금액이 입력될 때마다 총 결제금액과 부가세를 다시 계산합니다.
    partial void OnOrderDiscountChanged(decimal value) => RecalculateCartTotals();

    private void ApplyProductFilter()
    {
        FilteredProducts.Clear();
        var items = SelectedCategory == "전체"
            ? AllProducts
            : AllProducts.Where(p => p.Category == SelectedCategory);

        foreach (var item in items)
            FilteredProducts.Add(item);
    }

    [RelayCommand]
    private void SelectCategory(string category) => SelectedCategory = category;

    [RelayCommand]
    private void AddToCart(Product? product)
    {
        if (product is null) return;

        // 이미 장바구니에 담긴 상품이면 수량만 +1 하고, 없으면 새 항목으로 추가합니다.
        var existing = CartItems.FirstOrDefault(c => c.ProductId == product.Id);
        if (existing is not null)
        {
            existing.Quantity += 1;
        }
        else
        {
            CartItems.Add(new CartItemViewModel
            {
                ProductId = product.Id,
                Barcode = product.Barcode,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = 1
            });
        }

        RecalculateCartTotals();
        _ = UpdateVfdAsync(product.Name, $"{TotalAmount:N0} WON");
    }

    [RelayCommand]
    private void IncreaseQty(CartItemViewModel? item)
    {
        if (item is null) return;
        item.Quantity += 1;
        RecalculateCartTotals();
    }

    [RelayCommand]
    private void DecreaseQty(CartItemViewModel? item)
    {
        if (item is null) return;
        if (item.Quantity <= 1)
            CartItems.Remove(item);
        else
            item.Quantity -= 1;
        RecalculateCartTotals();
    }

    [RelayCommand]
    private void ClearCart()
    {
        CartItems.Clear();
        OrderDiscount = 0;
        RecalculateCartTotals();
        _ = UpdateVfdAsync("WELCOME TO POS STORE", "READY TO SCAN       ");
    }

    [RelayCommand]
    private void SimulateBarcodeScan()
    {
        if (string.IsNullOrWhiteSpace(BarcodeInput))
            BarcodeInput = "880100000001";

        _barcodeScanner.SimulateScan(BarcodeInput);
        BarcodeInput = string.Empty;
    }

    [RelayCommand]
    private async Task CheckoutAsync(string methodName)
    {
        if (CartItems.Count == 0)
        {
            ReportStatus("장바구니가 비어 있습니다.");
            return;
        }

        if (!Enum.TryParse<PaymentMethod>(methodName, out var method))
            method = PaymentMethod.Card;

        try
        {
            IsBusy = true;
            ReportStatus($"{method} 결제 진행 중...");

            var items = CartItems.Select(c => new OrderItem
            {
                ProductId = c.ProductId,
                ProductName = c.ProductName,
                Barcode = c.Barcode,
                UnitPrice = c.UnitPrice,
                Quantity = c.Quantity,
                DiscountAmount = c.DiscountAmount
            }).ToList();

            string? payProvider = method == PaymentMethod.EasyPay ? "KAKAOPAY" : null;
            string? cashReceiptId = method == PaymentMethod.CashReceipt ? "010-1234-5678" : null;

            // 1. OrderService를 통해 VAN 결제 승인 및 주문 DB 저장 실행
            var (order, vanResp) = await _orderService.CheckoutAsync(
                items, method, OrderDiscount, 0, payProvider, cashReceiptId);

            // 2. 영수증 ESC/POS 데이터 생성 및 출력
            var payment = order.Payments.FirstOrDefault();
            var (preview, hex) = await _receiptPrinter.PrintReceiptAsync(order, payment);

            // 3. 현금/현금영수증 결제 시 돈통(캐시드로어) 자동 개방
            bool drawerOpened = false;
            if (method is PaymentMethod.Cash or PaymentMethod.CashReceipt)
            {
                await _cashDrawer.OpenDrawerAsync();
                drawerOpened = true;
            }

            // 4. 고객용 디스플레이 안내 문구 변경 및 장바구니 초기화
            await UpdateVfdAsync("THANK YOU!", $"PAID {order.TotalAmount:N0} WON");
            ClearCart();
            await RefreshProductsAsync();

            if (CheckoutCompleted is not null)
                await CheckoutCompleted.Invoke(new CheckoutCompletedEventArgs(order, preview, hex, drawerOpened));

            ReportStatus(vanResp is not null
                ? $"결제 완료! 주문번호: {order.OrderNumber} / 승인번호: {vanResp.ApprovalNumber}"
                : $"현금 결제 완료! 주문번호: {order.OrderNumber}");
        }
        catch (Exception ex)
        {
            ReportStatus($"결제 오류: {ex.Message}");
            CheckoutFailed?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnBarcodeScanned(object? sender, string barcode)
    {
        // 시리얼 포트(COM) 데이터 수신 이벤트는 백그라운드 스레드에서 발생하므로,
        // WPF UI 컬렉션(CartItems)을 수정하려면 반드시 UI 스레드(Dispatcher)로 넘겨야 합니다.
        Application.Current.Dispatcher.Invoke(async () =>
        {
            var product = await _repository.GetProductByBarcodeAsync(barcode);
            if (product is null)
            {
                ReportStatus($"미등록 바코드입니다: {barcode}");
                await _repository.AddDiagnosticLogAsync(new DiagnosticLog
                {
                    Category = "Peripheral",
                    Level = DiagnosticLevel.Warning,
                    Message = $"미등록 바코드 스캔: {barcode}"
                });
                return;
            }

            AddToCart(product);
            ReportStatus($"바코드 스캔 추가: {product.Name}");
        });
    }

    private void RecalculateCartTotals()
    {
        var orderItems = CartItems.Select(c => new OrderItem
        {
            ProductId = c.ProductId,
            ProductName = c.ProductName,
            Barcode = c.Barcode,
            UnitPrice = c.UnitPrice,
            Quantity = c.Quantity,
            DiscountAmount = c.DiscountAmount
        });

        var (total, _, supply, vat) = OrderService.CalculateTotals(orderItems, OrderDiscount);
        TotalAmount = total;
        SupplyAmount = supply;
        VatAmount = vat;
    }

    private async Task UpdateVfdAsync(string line1, string line2)
    {
        await _customerDisplay.DisplayAsync(line1, line2);
        VfdUpdated?.Invoke(_customerDisplay.CurrentLine1, _customerDisplay.CurrentLine2);
    }
}
