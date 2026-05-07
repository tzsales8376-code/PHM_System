// ============================================================================
// Tranzx.iVS4.App / ViewModels / AddSensorDialogViewModel.cs
// 「+ 加入 Sensor」對話框：選 Slot / 顯示名稱 / COM Port / Sensor ID
// ============================================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tranzx.iVS4.App.Services;
using Tranzx.iVS4.Communication.Discovery;
using Tranzx.iVS4.Core.Models;

namespace Tranzx.iVS4.App.ViewModels;

public partial class AddSensorDialogViewModel : ObservableObject
{
    public ObservableCollection<int> AvailableSlots { get; } = new();
    public ObservableCollection<UsbDeviceInfo> AvailableUsbDevices { get; } = new();

    [ObservableProperty] private int slotIndex;
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private UsbDeviceInfo? selectedUsbDevice;
    [ObservableProperty] private string sensorId = "";
    [ObservableProperty] private string scanStatus = "";

    public bool DialogResult { get; private set; }
    public event Action<bool>? RequestClose;

    private static LocalizationService Loc => LocalizationService.Instance;

    public AddSensorDialogViewModel(IEnumerable<int> freeSlots)
    {
        foreach (var s in freeSlots) AvailableSlots.Add(s);
        if (AvailableSlots.Count > 0)
        {
            SlotIndex = AvailableSlots[0];
            DisplayName = Loc.Format("Dialog.DefaultChannelNameFmt", SlotIndex + 1);
        }
        ScanUsb();
    }

    [RelayCommand]
    private void ScanUsb()
    {
        AvailableUsbDevices.Clear();
        var all = UsbScanner.ScanAll();
        var ranked = all.OrderByDescending(d => UsbScanner.KnownVids.Contains(d.Vid)).ToList();
        foreach (var d in ranked) AvailableUsbDevices.Add(d);

        int hits = ranked.Count(d => UsbScanner.KnownVids.Contains(d.Vid));
        ScanStatus = Loc.Format("Status.ScanResultFmt", ranked.Count, hits);

        if (SelectedUsbDevice is null && AvailableUsbDevices.Count > 0)
            SelectedUsbDevice = AvailableUsbDevices[0];
    }

    [RelayCommand] private void Confirm()  { DialogResult = true;  RequestClose?.Invoke(true); }
    [RelayCommand] private void Cancel()   { DialogResult = false; RequestClose?.Invoke(false); }

    public ChannelConfig ToConfig() => new()
    {
        Index = SlotIndex,
        DisplayName = string.IsNullOrWhiteSpace(DisplayName)
                      ? Loc.Format("Dialog.DefaultChannelNameFmt", SlotIndex + 1)
                      : DisplayName,
        Transport = TransportType.UsbCdc,
        PortName = SelectedUsbDevice?.PortName,
        SensorId = SensorId.Trim(),
        FullScale = FullScale.G16,
        Odr = OutputDataRate.Hz3332,
        Enabled = true
    };
}
