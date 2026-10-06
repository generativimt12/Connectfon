using System.Text;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Connectfon.Core;

namespace Connectfon.Bluetooth;

public sealed class PbapClient
{
    private static readonly Guid PbapUuid = new("0000112f-0000-1000-8000-00805f9b34fb");
    private readonly BluetoothManager _bluetooth;
    public PbapClient(BluetoothManager bluetooth) => _bluetooth = bluetooth;

    public async Task<IReadOnlyList<ContactEntry>> GetContactsAsync(PhoneDevice device, CancellationToken ct = default)
    {
        var service = await _bluetooth.OpenServiceAsync(device, PbapUuid);
        if (service is null) return Array.Empty<ContactEntry>();
        using var socket = new StreamSocket();
        await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(ct);
        using var writer = new DataWriter(socket.OutputStream);
        using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        // PBAP is OBEX over RFCOMM. We perform a minimal OBEX CONNECT followed by a phonebook GET.
        await ObexWriteAsync(writer, new byte[] { 0x80, 0x00, 0x07, 0x10, 0x00, 0x00, 0x00 }, ct);
        var connectResponse = await ReadObexAsync(reader, ct);
        if (connectResponse.Length < 3 || connectResponse[0] / 0x10 != 0xC) return Array.Empty<ContactEntry>();
        var name = Encoding.Unicode.GetBytes("telecom/pb\0");
        var packet = new List<byte> { 0x83, 0x00, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x30, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00 };
        packet[2] = (byte)(packet.Count + name.Length + 1 >> 8); packet[3] = (byte)(packet.Count + name.Length + 1);
        packet.Add(0x01); packet.AddRange(name);
        await ObexWriteAsync(writer, packet.ToArray(), ct);
        var body = new List<byte>();
        while (true)
        {
            var chunk = await ReadObexAsync(reader, ct);
            if (chunk.Length < 3) break;
            body.AddRange(chunk.Skip(3));
            if (chunk[0] == 0xA0 || chunk[0] == 0xC0) break;
            if (chunk[0] != 0x90 && chunk[0] != 0x10) break;
        }
        return ParseVcards(Encoding.UTF8.GetString(body.ToArray()));
    }

    private static async Task ObexWriteAsync(DataWriter writer, byte[] data, CancellationToken ct)
    {
        writer.WriteBytes(data); await writer.StoreAsync().AsTask(ct);
    }
    private static async Task<byte[]> ReadObexAsync(DataReader reader, CancellationToken ct)
    {
        try { var n = await reader.LoadAsync(4096).AsTask(ct); var b = new byte[n]; reader.ReadBytes(b); return b; } catch { return Array.Empty<byte>(); }
    }
    private static IReadOnlyList<ContactEntry> ParseVcards(string text)
    {
        var list = new List<ContactEntry>();
        foreach (var card in Regex.Split(text, "END:VCARD", RegexOptions.IgnoreCase))
        {
            var name = Regex.Match(card, @"(?:^|\r?\n)FN(?:;[^:]*)?:(.*)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var number = Regex.Match(card, @"(?:^|\r?\n)TEL(?:;[^:]*)?:(.*)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(number)) list.Add(new ContactEntry(string.IsNullOrWhiteSpace(name) ? number : name, number));
        }
        return list;
    }
}
