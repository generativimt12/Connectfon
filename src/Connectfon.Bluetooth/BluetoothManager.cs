using Connectfon.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.Rfcomm;

namespace Connectfon.Bluetooth;

public sealed class BluetoothManager : IAsyncDisposable
{
    private DeviceWatcher? _watcher;
    private readonly Dictionary<string, BluetoothDevice> _devices = new();
    public event EventHandler? DevicesChanged;
    public IReadOnlyCollection<BluetoothDevice> Devices => _devices.Values;

    public async Task<IReadOnlyList<PhoneDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var selector = BluetoothDevice.GetDeviceSelector();
        var infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
        var result = new List<PhoneDevice>();
        foreach (var info in infos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var device = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
                if (device is null) continue;
                _devices[info.Id] = device;
                result.Add(new PhoneDevice(info.Id, device.Name, device.BluetoothAddress, info.Pairing.IsPaired, device.ConnectionStatus == BluetoothConnectionStatus.Connected, Array.Empty<string>()));
            }
            catch { }
        }
        return result.OrderBy(x => x.Name).ToArray();
    }

    public void StartWatcher()
    {
        if (_watcher is not null) return;
        _watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelector());
        _watcher.Added += async (_, e) => await RefreshDevice(e.Id);
        _watcher.Updated += async (_, e) => await RefreshDevice(e.Id);
        _watcher.Removed += (_, e) => { _devices.Remove(e.Id); DevicesChanged?.Invoke(this, EventArgs.Empty); };
        _watcher.Start();
    }

    private async Task RefreshDevice(string id)
    {
        try
        {
            var d = await BluetoothDevice.FromIdAsync(id);
            if (d is not null) _devices[id] = d;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    public async Task<bool> PairAsync(PhoneDevice device)
    {
        var info = await DeviceInformation.CreateFromIdAsync(device.Id);
        if (info is null) return false;
        if (info.Pairing.IsPaired) return true;
        var result = await info.Pairing.PairAsync(DevicePairingProtectionLevel.None);
        return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
    }

    public async Task<DeviceCapabilities> DetectCapabilitiesAsync(PhoneDevice device)
    {
        var d = await BluetoothDevice.FromIdAsync(device.Id);
        if (d is null) return new(false, false, false, Array.Empty<string>());
        var services = await d.GetRfcommServicesAsync().AsTask();
        var ids = services.Services.Select(s => s.ServiceId.Uuid.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        bool hfp = ids.Any(x => x.StartsWith("0000111f-", StringComparison.OrdinalIgnoreCase) || x.StartsWith("0000111e-", StringComparison.OrdinalIgnoreCase));
        bool pbap = ids.Any(x => x.StartsWith("0000112f-", StringComparison.OrdinalIgnoreCase));
        bool callHistory = pbap;\n        return new(hfp, pbap, callHistory, ids);
    }

    public async Task<RfcommDeviceService?> OpenServiceAsync(PhoneDevice device, Guid uuid)
    {
        var d = await BluetoothDevice.FromIdAsync(device.Id);
        if (d is null) return null;
        var id = RfcommServiceId.FromUuid(uuid);
        try { return await d.GetRfcommServicesForIdAsync(id).AsTask().ContinueWith(t => t.Result.Services.FirstOrDefault()); }
        catch { return null; }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher?.Stop();
        _watcher = null;
        foreach (var d in _devices.Values) d.Dispose();
        _devices.Clear();
        await Task.CompletedTask;
    }
}
