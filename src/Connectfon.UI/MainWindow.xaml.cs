using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Connectfon.Bluetooth;
using Connectfon.Core;

namespace Connectfon.UI;

public partial class MainWindow : Window
{
    private readonly BluetoothManager _bluetooth = new();
    private readonly PbapClient _pbap;
    private HfpSession? _hfp;
    private PhoneDevice? _selected;
    private DeviceCapabilities? _caps;
    private IReadOnlyList<ContactEntry> _contacts = Array.Empty<ContactEntry>();
    private bool _updatingAudio;

    private sealed record DeviceRow(string Name, string State, string Address, string ProfileText, PhoneDevice Device);
    private sealed record RecentRow(string Name, string Number, string Type, string Time, CallEntry Entry);

    public MainWindow()
    {
        InitializeComponent();
        _pbap = new PbapClient(_bluetooth);
        Loaded += async (_, _) => { _bluetooth.StartWatcher(); await RefreshAsync(); };
        Closing += async (_, _) => { if (_hfp is not null) await _hfp.DisposeAsync(); await _bluetooth.DisposeAsync(); };
        VolumeSlider.Value = 0.75;
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "Bluetooth: scanning…";
        var devices = await _bluetooth.DiscoverAsync();
        var rows = new List<DeviceRow>();
        foreach (var d in devices)
        {
            var caps = await _bluetooth.DetectCapabilitiesAsync(d);
            rows.Add(new(d.Name, d.IsConnected ? "Connected" : d.IsPaired ? "Paired" : "Not paired",
                FormatAddress(d.BluetoothAddress), $"HFP {(caps.Hfp ? "✓" : "—")}  PBAP {(caps.Pbap ? "✓" : "—")}", d));
        }
        DevicesList.ItemsSource = rows;
        StatusText.Text = $"Bluetooth: {devices.Count} device(s)";
    }

    private static string FormatAddress(ulong a) => string.Join(":", Enumerable.Range(0,6).Reverse().Select(i => ((a >> (i*8)) & 0xff).ToString("X2")));
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void DevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DevicesList.SelectedItem is not DeviceRow row) return;
        _selected = row.Device;
        PhoneTitle.Text = row.Name;
        _caps = await _bluetooth.DetectCapabilitiesAsync(row.Device);
        CapabilitiesText.Text = $"HFP: {YesNo(_caps.Hfp)}   PBAP: {YesNo(_caps.Pbap)}   Call history: {YesNo(_caps.CallHistory)}";
        DiagnosticsBox.Text = string.Join(Environment.NewLine, _caps.RfcommServices);
    }

    private static string YesNo(bool b) => b ? "YES" : "NO";

    private async void ConnectHfp_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _caps?.Hfp != true) { MessageBox.Show("HFP is not available."); return; }
        try
        {
            var service = await FindHfpServiceAsync(_selected);
            if (service is null) { MessageBox.Show("Windows did not expose an HFP RFCOMM service."); return; }
            if (_hfp is not null) await _hfp.DisposeAsync();
            _hfp = new HfpSession();
            _hfp.Diagnostic += (_, line) => Dispatcher.Invoke(() => DiagnosticsBox.AppendText(line + Environment.NewLine));
            _hfp.CallStateChanged += (_, state) => Dispatcher.Invoke(() => ApplyCallState(state));
            await _hfp.ConnectAsync(service);
            StatusText.Text = "Bluetooth: HFP control channel connected";
            ApplyCallState(_hfp.State);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "HFP connection error"); }
    }

    private async Task<Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService?> FindHfpServiceAsync(PhoneDevice device)
    {
        foreach (var uuid in new[] {
            new Guid("0000111e-0000-1000-8000-00805f9b34fb"),
            new Guid("0000111f-0000-1000-8000-00805f9b34fb") })
        {
            var s = await _bluetooth.OpenServiceAsync(device, uuid);
            if (s is not null) return s;
        }
        return null;
    }

    private async void Call_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || string.IsNullOrWhiteSpace(NumberBox.Text)) return;
        try
        {
            if (_hfp is null) await ConnectHfpForCallAsync();
            if (_hfp is null) return;
            await _hfp.DialAsync(NumberBox.Text.Trim());
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Dial error"); }
    }

    private async Task ConnectHfpForCallAsync()
    {
        if (_selected is null || _caps?.Hfp != true) throw new InvalidOperationException("HFP is unavailable.");
        var service = await FindHfpServiceAsync(_selected);
        if (service is null) throw new InvalidOperationException("HFP service could not be opened.");
        _hfp = new HfpSession();
        _hfp.Diagnostic += (_, line) => Dispatcher.Invoke(() => DiagnosticsBox.AppendText(line + Environment.NewLine));
        _hfp.CallStateChanged += (_, state) => Dispatcher.Invoke(() => ApplyCallState(state));
        await _hfp.ConnectAsync(service);
    }

    private void ApplyCallState(HfpCallInfo state)
    {
        CallBanner.Visibility = state.State is CallState.Incoming or CallState.Dialing or CallState.Alerting or CallState.Active ? Visibility.Visible : Visibility.Collapsed;
        CallTitle.Text = state.State switch { CallState.Incoming => "Incoming call", CallState.Dialing => "Dialing…", CallState.Alerting => "Calling…", CallState.Active => "Active call", _ => "Call" };
        CallNumber.Text = string.IsNullOrWhiteSpace(state.Number) ? "Unknown number" : state.Number;
        AnswerButton.Visibility = state.State == CallState.Incoming ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"Call: {state.State}";
    }

    private async void Answer_Click(object sender, RoutedEventArgs e) { if (_hfp is not null) await _hfp.AnswerAsync(); }
    private async void Hangup_Click(object sender, RoutedEventArgs e) { if (_hfp is not null) await _hfp.HangupAsync(); }

    private void Digit_Click(object sender, RoutedEventArgs e) { if (sender is Button b) NumberBox.Text += b.Content?.ToString(); }
    private void Backspace_Click(object sender, RoutedEventArgs e) { if (NumberBox.Text.Length > 0) NumberBox.Text = NumberBox.Text[..^1]; }

    private async void Contacts_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _caps?.Pbap != true) { MessageBox.Show("PBAP is not available for this device."); return; }
        try { _contacts = await _pbap.GetContactsAsync(_selected); ContactsList.ItemsSource = _contacts; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "PBAP error"); }
    }

    private void ContactSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var q = ContactSearch.Text.Trim();
        ContactsList.ItemsSource = string.IsNullOrEmpty(q) ? _contacts : _contacts.Where(x => x.Name.Contains(q,StringComparison.OrdinalIgnoreCase) || x.Number.Contains(q,StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void Contact_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContactsList.SelectedItem is ContactEntry c) { NumberBox.Text = c.Number; _ = DialNumberAsync(c.Number); }
    }

    private async void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _caps?.CallHistory != true) { MessageBox.Show("PBAP call history is not available."); return; }
        try
        {
            var list = await _pbap.GetCallHistoryAsync(_selected);
            RecentList.ItemsSource = list.Select(x => new RecentRow(x.Name ?? x.Number, x.Number, x.Type, x.Timestamp?.ToLocalTime().ToString("g") ?? "", x)).ToArray();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Call history error"); }
    }

    private void Recent_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is RecentRow r) { NumberBox.Text = r.Number; _ = DialNumberAsync(r.Number); }
    }

    private async Task DialNumberAsync(string number)
    {
        try { if (_hfp is null) await ConnectHfpForCallAsync(); if (_hfp is not null) await _hfp.DialAsync(number); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Dial error"); }
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingAudio) return;
        try
        {
            var endpoint = new NAudio.CoreAudioApi.MMDeviceEnumerator().GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = (float)e.NewValue;
        } catch { }
    }

    private void Mute_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            var endpoint = new NAudio.CoreAudioApi.MMDeviceEnumerator().GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            endpoint.AudioEndpointVolume.Mute = MuteBox.IsChecked == true;
        } catch { }
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticsBox.Text.Length == 0) DiagnosticsBox.Text = "Select a phone. Diagnostics will show SDP UUIDs and HFP AT traffic.";
    }
}
