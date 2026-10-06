using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Connectfon.Bluetooth;

public sealed class RfcommAtClient : IAsyncDisposable
{
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private DataReader? _reader;
    private CancellationTokenSource? _readCts;
    public bool IsOpen => _socket is not null;
    public event EventHandler<string>? LineReceived;

    public async Task OpenAsync(RfcommDeviceService service, CancellationToken cancellationToken = default)
    {
        await CloseAsync();
        _socket = new StreamSocket();
        await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(cancellationToken);
        _writer = new DataWriter(_socket.OutputStream);
        _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        _readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_writer is null) throw new InvalidOperationException("RFCOMM is not open.");
        _writer.WriteString(command.EndsWith('\r') ? command : command + '\r');
        await _writer.StoreAsync().AsTask(cancellationToken);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new System.Text.StringBuilder();
        while (!ct.IsCancellationRequested && _reader is not null)
        {
            try
            {
                var n = await _reader.LoadAsync(4096).AsTask(ct);
                if (n == 0) break;
                buffer.Append(_reader.ReadString(n));
                while (true)
                {
                    var s = buffer.ToString();
                    var idx = s.IndexOfAny(new[] {'\r','\n'});
                    if (idx < 0) break;
                    var line = s[..idx].Trim();
                    buffer.Remove(0, idx + 1);
                    if (line.Length > 0) LineReceived?.Invoke(this, line);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    public Task DialAsync(string number, CancellationToken ct = default) => SendAsync($"ATD{number};", ct);
    public Task AnswerAsync(CancellationToken ct = default) => SendAsync("ATA", ct);
    public Task HangupAsync(CancellationToken ct = default) => SendAsync("AT+CHUP", ct);
    public Task QuerySignalAsync(CancellationToken ct = default) => SendAsync("AT+CSQ", ct);
    public Task QueryOperatorAsync(CancellationToken ct = default) => SendAsync("AT+COPS?", ct);
    public Task EnableCallerIdAsync(CancellationToken ct = default) => SendAsync("AT+CLIP=1", ct);

    public async ValueTask DisposeAsync() => await CloseAsync();
    public async Task CloseAsync()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;
        _writer?.DetachStream();
        _reader?.DetachStream();
        _writer = null; _reader = null;
        _socket?.Dispose(); _socket = null;
    }
}
