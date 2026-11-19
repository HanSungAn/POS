using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;
using PosSystem.Infrastructure.Protocols;

namespace PosSystem.Wpf.ViewModels;

public partial class PeripheralMonitorViewModel : FeatureViewModelBase
{
    private readonly ICashDrawer _cashDrawer;
    private readonly IPosRepository _repository;

    public event Func<Task>? DrawerOpened;

    public PeripheralMonitorViewModel(
        ICashDrawer cashDrawer,
        IPosRepository repository,
        MockVanTerminalClient vanClient)
    {
        _cashDrawer = cashDrawer;
        _repository = repository;
        vanClient.PacketTransacted += OnVanPacketTransacted;
    }

    public ObservableCollection<string> VanMonitorLogs { get; } = new();

    [ObservableProperty] private string _vfdLine1 = "WELCOME TO POS STORE";
    [ObservableProperty] private string _vfdLine2 = "READY TO SCAN       ";
    [ObservableProperty] private string _receiptPreviewText = "결제 완료 시 영수증 미리보기가 표시됩니다.";
    [ObservableProperty] private string _escPosHexText = string.Empty;
    [ObservableProperty] private string _lastDrawerInfo = "대기 중";

    public void UpdateVfd(string line1, string line2)
    {
        VfdLine1 = line1;
        VfdLine2 = line2;
    }

    public void UpdateReceipt(string previewText, string escPosHex)
    {
        ReceiptPreviewText = previewText;
        EscPosHexText = escPosHex;
    }

    public void MarkDrawerOpened()
    {
        LastDrawerInfo = $"열림 ({DateTime.Now:HH:mm:ss}) [ESC p 0 25 250]";
    }

    [RelayCommand]
    private async Task OpenCashDrawerAsync()
    {
        await _cashDrawer.OpenDrawerAsync();
        MarkDrawerOpened();
        await _repository.AddDiagnosticLogAsync(new DiagnosticLog
        {
            Category = "Peripheral",
            Level = DiagnosticLevel.Info,
            Message = "캐시드로어 개방 신호 전송 (ESC/POS Kick Pulse)"
        });

        if (DrawerOpened is not null)
            await DrawerOpened.Invoke();

        ReportStatus("캐시드로어 개방 신호를 전송했습니다.");
    }

    private void OnVanPacketTransacted(object? sender, VanResponse response)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            string entry = $"[{response.TransactedAt:HH:mm:ss}] {response.ParsedSummary}\n  REQ: {response.RawRequestHex}\n  RES: {response.RawResponseHex}";
            VanMonitorLogs.Insert(0, entry);
            if (VanMonitorLogs.Count > 30)
                VanMonitorLogs.RemoveAt(VanMonitorLogs.Count - 1);
        });
    }
}
