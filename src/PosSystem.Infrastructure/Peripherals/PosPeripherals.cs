using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Peripherals;

public class SimulatedCashDrawer : ICashDrawer
{
    public PeripheralStatus Status { get; private set; } = PeripheralStatus.Simulated;
    public DateTime? LastOpenedAt { get; private set; }

    public Task<bool> OpenDrawerAsync(CancellationToken ct = default)
    {
        // ESC/POS Cash Drawer Kick Pulse: ESC p 0 25 250 (0x1B 0x70 0x00 0x19 0xFA)
        LastOpenedAt = DateTime.Now;
        Status = PeripheralStatus.Simulated;
        return Task.FromResult(true);
    }
}

public class SimulatedCustomerDisplay : ICustomerDisplay
{
    public PeripheralStatus Status { get; private set; } = PeripheralStatus.Simulated;
    public string CurrentLine1 { get; private set; } = "WELCOME TO POS STORE";
    public string CurrentLine2 { get; private set; } = "READY TO SCAN       ";

    public event EventHandler<(string Line1, string Line2)>? DisplayUpdated;

    public Task DisplayAsync(string line1, string line2, CancellationToken ct = default)
    {
        CurrentLine1 = line1.Length > 20 ? line1[..20] : line1.PadRight(20);
        CurrentLine2 = line2.Length > 20 ? line2[..20] : line2.PadRight(20);
        DisplayUpdated?.Invoke(this, (CurrentLine1, CurrentLine2));
        return Task.CompletedTask;
    }
}
