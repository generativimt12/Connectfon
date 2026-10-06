using System.Text;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Connectfon.Core;

namespace Connectfon.Bluetooth;

public sealed class RfcommAtClient : IAsyncDisposable
{
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private DataReader? _reader;

    public bool IsOpen => _socket is not null;

    public async Task OpenAsync(RfcommDeviceService service, CancellationToken cancellationToken = default)
    {
        await CloseAsync();
        _socket = new StreamSocket();
        await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(cancellationToken);
        _writer = new DataWriter(_socket.OutputStream);
        _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
    }

    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_writer is null) throw new InvalidOperationException("RFCOMM is not open.");
        _writer.WriteString(command.EndsWith("\r", StringComparison.Ordinal) ? command : command + "\r");
        await _writer.StoreAsync().AsTask(cancellationToken);
    }

    public async Task<string> ReadAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null) return string.Empty;
        try
        {
            var n = await _reader.LoadAsync(4096).AsTask(cancellationToken);
            return n == 0 ? string.Empty : _reader.ReadString(n);
        }
        catch { return string.Empty; }
    }

    public Task DialAsync(string number, CancellationToken ct = default) => SendAsync($"ATD{number};", ct);
    public Task AnswerAsync(CancellationToken ct = default) => SendAsync("ATA", ct);
    public Task HangupAsync(CancellationToken ct = default) => SendAsync("AT+CHUP", ct);
    public Task QuerySignalAsync(CancellationToken ct = default) => SendAsync("AT+CSQ", ct);

    public async ValueTask DisposeAsync() => await CloseAsync();
    public async Task CloseAsync()
    {
        _writer?.DetachStream();
        _reader?.DetachStream();
        _writer = null; _reader = null;
        _socket?.Dispose(); _socket = null;
    }
}
