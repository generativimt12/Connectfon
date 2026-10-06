using System.Windows;
using Connectfon.Bluetooth;
using Connectfon.Core;

namespace Connectfon.UI;

public partial class MainWindow : Window
{
    private readonly BluetoothManager _bluetooth = new();
    private readonly PbapClient _pbap;
    private PhoneDevice? _selected;
    private DeviceCapabilities? _caps;
    private RfcommAtClient? _hfp;

    public MainWindow()
    {
        InitializeComponent();
        _pbap = new PbapClient(_bluetooth);
        Loaded += async (_, _) => { _bluetooth.StartWatcher(); await RefreshAsync(); };
        Closing += async (_, _) => { if (_hfp is not null) await _hfp.DisposeAsync(); await _bluetooth.DisposeAsync(); };
    }

    private sealed record DeviceRow(string Name, string State, string Address, PhoneDevice Device);

    private async Task RefreshAsync()
    {
        StatusText.Text = "Bluetooth: scanning…";
        var devices = await _bluetooth.DiscoverAsync();
        DevicesList.ItemsSource = devices.Select(d => new DeviceRow(d.Name, d.IsConnected ? "Connected" : d.IsPaired ? "Paired" : "Not paired", FormatAddress(d.BluetoothAddress), d)).ToArray();
        StatusText.Text = $"Bluetooth: {devices.Count} device(s)";
    }
    private static string FormatAddress(ulong a) => string.Join(":", Enumerable.Range(0,6).Reverse().Select(i => ((a >> (i*8)) & 0xff).ToString("X2")));

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void DevicesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DevicesList.SelectedItem is not DeviceRow row) return;
        _selected = row.Device; PhoneTitle.Text = row.Name; CapabilitiesText.Text = "Detecting profiles…";
        _caps = await _bluetooth.DetectCapabilitiesAsync(row.Device);
        CapabilitiesText.Text = $"HFP: {YesNo(_caps.Hfp)}   PBAP: {YesNo(_caps.Pbap)}   Call history: {YesNo(_caps.CallHistory)}";
    }
    private static string YesNo(bool b) => b ? "YES" : "NO";

    private async void Call_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || string.IsNullOrWhiteSpace(NumberBox.Text)) return;
        if (_caps?.Hfp != true) { MessageBox.Show("This phone does not expose an HFP service through the Windows Bluetooth API.", "HFP unavailable"); return; }
        try
        {
            var service = await _bluetooth.OpenServiceAsync(_selected, new Guid("0000111f-0000-1000-8000-00805f9b34fb"));
            if (service is null) { MessageBox.Show("HFP service could not be opened by the Windows RFCOMM layer."); return; }
            _hfp ??= new RfcommAtClient(); await _hfp.OpenAsync(service); await _hfp.DialAsync(NumberBox.Text);
            MessageBox.Show("Dial command sent to the phone through the HFP RFCOMM service.", "Connectfon");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "HFP error"); }
    }
    private async void Hangup_Click(object sender, RoutedEventArgs e) { if (_hfp?.IsOpen == true) await _hfp.HangupAsync(); }
    private void Digit_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b) NumberBox.Text += b.Content?.ToString(); }
    private void Backspace_Click(object sender, RoutedEventArgs e) { if (NumberBox.Text.Length > 0) NumberBox.Text = NumberBox.Text[..^1]; }

    private async void Contacts_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _caps?.Pbap != true) { MessageBox.Show("PBAP is not available for this device."); return; }
        try { ContactsList.ItemsSource = await _pbap.GetContactsAsync(_selected); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "PBAP error"); }
    }
    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var services = _caps?.RfcommServices.Count > 0 ? string.Join(Environment.NewLine, _caps.RfcommServices) : "None detected";
        MessageBox.Show($"Device: {_selected?.Name ?? "none"}\nHFP: {_caps?.Hfp}\nPBAP: {_caps?.Pbap}\nCall history: {_caps?.CallHistory}\n\nRFCOMM SDP UUIDs:\n{services}", "Connectfon diagnostics");
    }
}
