using System.IO.Ports;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Peripherals;

public class SerialBarcodeScanner : IBarcodeScanner, IDisposable
{
    private SerialPort? _serialPort;
    public PeripheralStatus Status { get; private set; } = PeripheralStatus.Simulated;
    public string PortName { get; private set; } = "COM4";

    public event EventHandler<string>? BarcodeScanned;

    public void StartListening(string portName)
    {
        PortName = portName;
        StopListening();

        if (!SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase))
        {
            Status = PeripheralStatus.Simulated;
            return;
        }

        try
        {
            _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
            {
                NewLine = "\r\n",
                ReadTimeout = 500
            };
            _serialPort.DataReceived += OnSerialDataReceived;
            _serialPort.Open();
            Status = PeripheralStatus.Connected;
        }
        catch
        {
            Status = PeripheralStatus.Error;
        }
    }

    public void StopListening()
    {
        if (_serialPort is null) return;
        _serialPort.DataReceived -= OnSerialDataReceived;
        if (_serialPort.IsOpen)
            _serialPort.Close();
        _serialPort.Dispose();
        _serialPort = null;
    }

    public void SimulateScan(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;
        BarcodeScanned?.Invoke(this, barcode.Trim());
    }

    private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort is null || !_serialPort.IsOpen) return;
        try
        {
            string data = _serialPort.ReadLine().Trim();
            if (!string.IsNullOrEmpty(data))
                BarcodeScanned?.Invoke(this, data);
        }
        catch
        {
            // 시리얼 타임아웃 무시
        }
    }

    public void Dispose() => StopListening();
}
