using CommunityToolkit.Mvvm.ComponentModel;

namespace PosSystem.Wpf.ViewModels;

public abstract class FeatureViewModelBase : ObservableObject
{
    public event Action<string>? StatusReported;

    protected void ReportStatus(string message) => StatusReported?.Invoke(message);
}
