using CommunityToolkit.Mvvm.ComponentModel;

namespace PosSystem.Wpf.ViewModels;

public partial class CartItemViewModel : ObservableObject
{
    public int ProductId { get; init; }
    public string Barcode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private int _quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _discountAmount;

    public decimal LineTotal => Math.Max(0, (UnitPrice * Quantity) - DiscountAmount);
}
