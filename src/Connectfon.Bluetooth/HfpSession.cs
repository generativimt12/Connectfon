using System.Text.RegularExpressions;
using Connectfon.Core;

namespace Connectfon.Bluetooth;

public sealed class HfpSession : IAsyncDisposable
{
    private readonly RfcommAtClient _client = new();
    public event EventHandler<HfpCallInfo>? CallStateChanged;
    public event EventHandler<string>? Diagnostic;
    public HfpCallInfo State { get; private set; } = new(CallState.Idle, null, null, null);

    public async Task ConnectAsync(Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService service, CancellationToken ct = default)
    {
        await _client.OpenAsync(service, ct);
        _client.LineReceived += OnLine;
        await _client.SendAsync("AT+BRSF=20", ct);
        await _client.EnableCallerIdAsync(ct);
        await _client.SendAsync("AT+CMEE=1", ct);
        await _client.SendAsync("AT+CIND=?", ct);
        await _client.SendAsync("AT+CIND?", ct);
        await _client.SendAsync("AT+CHLD=?", ct);
    }

    private void OnLine(object? sender, string line)
    {
        Diagnostic?.Invoke(this, line);
        if (line.Equals("RING", StringComparison.OrdinalIgnoreCase))
            Set(CallState.Incoming, State.Number, State.Name, line);
        else if (line.StartsWith("+CLIP:", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(line, @"\+CLIP:\s*""([^""]*)""(?:,\s*\d+)?");
            Set(State.State == CallState.Idle ? CallState.Incoming : State.State, m.Success ? m.Groups[1].Value : State.Number, State.Name, line);
        }
        else if (line.StartsWith("+CIEV:", StringComparison.OrdinalIgnoreCase))
        {
            var p = line.Split(',');
            if (p.Length >= 2 && int.TryParse(p[0].Split(':')[1].Trim(), out _) && int.TryParse(p[1].Trim(), out var v))
                if (v == 0 && State.State == CallState.Active) Set(CallState.Disconnected, State.Number, State.Name, line);
        }
        else if (line.Contains("NO CARRIER", StringComparison.OrdinalIgnoreCase) || line.Contains("BUSY", StringComparison.OrdinalIgnoreCase))
            Set(CallState.Disconnected, State.Number, State.Name, line);
        else if (line.Equals("OK", StringComparison.OrdinalIgnoreCase) && State.State == CallState.Dialing)
            Set(CallState.Alerting, State.Number, State.Name, line);
    }

    private void Set(CallState state, string? number, string? name, string? raw)
    {
        State = new(state, number, name, raw);
        CallStateChanged?.Invoke(this, State);
    }

    public async Task DialAsync(string number, CancellationToken ct = default) { Set(CallState.Dialing, number, null, null); await _client.DialAsync(number, ct); }
    public async Task AnswerAsync(CancellationToken ct = default) { await _client.AnswerAsync(ct); Set(CallState.Active, State.Number, State.Name, "ATA"); }
    public async Task HangupAsync(CancellationToken ct = default) { await _client.HangupAsync(ct); Set(CallState.Disconnected, State.Number, State.Name, "AT+CHUP"); }
    public Task QuerySignalAsync(CancellationToken ct = default) => _client.QuerySignalAsync(ct);

    public async ValueTask DisposeAsync()
    {
        _client.LineReceived -= OnLine;
        await _client.DisposeAsync();
    }
}
