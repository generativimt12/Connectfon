using System.Text;
using System.Text.RegularExpressions;
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
        => ParseContacts(await GetVcardsAsync(device, "telecom/pb.vcf", ct));

    public async Task<IReadOnlyList<CallEntry>> GetCallHistoryAsync(PhoneDevice device, CancellationToken ct = default)
    {
        var all = new List<CallEntry>();
        foreach (var item in new[] { ("Incoming","telecom/ich.vcf"), ("Outgoing","telecom/och.vcf"), ("Missed","telecom/mch.vcf") })
        {
            try { all.AddRange(ParseCalls(await GetVcardsAsync(device, item.Item2, ct), item.Item1)); } catch { }
        }
        return all.OrderByDescending(x => x.Timestamp ?? DateTimeOffset.MinValue).ToArray();
    }

    private async Task<string> GetVcardsAsync(PhoneDevice device, string path, CancellationToken ct)
    {
        var service = await _bluetooth.OpenServiceAsync(device, PbapUuid);
        if (service is null) return string.Empty;
        using var socket = new StreamSocket();
        await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(ct);
        using var writer = new DataWriter(socket.OutputStream);
        using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

        await WriteAsync(writer, new byte[] {0x80,0x00,0x07,0x10,0x00,0x00,0x00}, ct);
        var response = await ReadPacketAsync(reader, ct);
        if (response.Length < 3 || (response[0] & 0xF0) != 0xA0) return string.Empty;

        var name = Encoding.Unicode.GetBytes(path + "\0");
        var type = Encoding.ASCII.GetBytes("x-bt/vcard\0");
        var headers = new List<byte>();
        AddHeader(headers, 0x01, name);
        AddHeader(headers, 0x42, type);
        var len = 3 + headers.Count;
        var packet = new List<byte>{0x83,(byte)(len>>8),(byte)len};
        packet.AddRange(headers);
        await WriteAsync(writer, packet.ToArray(), ct);

        var body = new List<byte>();
        while (true)
        {
            var p = await ReadPacketAsync(reader, ct);
            if (p.Length < 3) break;
            ParseBodyHeaders(p, body);
            var code = p[0] & 0xF0;
            if (code == 0xA0 || code == 0xC0) break;
        }
        return DecodeVcard(body.ToArray());
    }

    private static void AddHeader(List<byte> packet, byte id, byte[] data)
    {
        packet.Add(id); packet.Add((byte)((data.Length + 3) >> 8)); packet.Add((byte)(data.Length + 3)); packet.AddRange(data);
    }

    private static string DecodeVcard(byte[] data)
    {
        var utf8 = Encoding.UTF8.GetString(data);
        if (utf8.Contains("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase)) return utf8;
        return Encoding.Unicode.GetString(data);
    }

    private static void ParseBodyHeaders(byte[] packet, List<byte> body)
    {
        var i = 3;
        while (i < packet.Length)
        {
            var id = packet[i++];
            var kind = id & 0xC0;
            if (kind == 0x00 || kind == 0x40)
            {
                if (i + 2 > packet.Length) break;
                var len = (packet[i] << 8) | packet[i + 1];
                i += 2;
                var dataLen = Math.Max(0, len - 3);
                var take = Math.Min(dataLen, packet.Length - i);
                if ((id == 0x48 || id == 0x49) && take > 0) body.AddRange(packet.AsSpan(i, take).ToArray());
                i += dataLen;
            }
            else if (kind == 0x80) i = Math.Min(packet.Length, i + 1);
            else break;
        }
    }

    private static async Task WriteAsync(DataWriter writer, byte[] data, CancellationToken ct)
    { writer.WriteBytes(data); await writer.StoreAsync().AsTask(ct); }

    private static async Task<byte[]> ReadPacketAsync(DataReader reader, CancellationToken ct)
    {
        try
        {
            var n = await reader.LoadAsync(3).AsTask(ct);
            if (n < 3) return Array.Empty<byte>();
            var h = new byte[3]; reader.ReadBytes(h);
            var len = (h[1] << 8) | h[2];
            if (len < 3 || len > 65535) return Array.Empty<byte>();
            var restLen = len - 3;
            var rest = new byte[restLen];
            if (restLen > 0)
            {
                var got = await reader.LoadAsync((uint)restLen).AsTask(ct);
                if (got < (uint)restLen) return h;
                reader.ReadBytes(rest);
            }
            return h.Concat(rest).ToArray();
        }
        catch { return Array.Empty<byte>(); }
    }

    private static List<ContactEntry> ParseContacts(string text)
    {
        var list = new List<ContactEntry>();
        foreach (var card in Regex.Split(text, "END:VCARD", RegexOptions.IgnoreCase))
        {
            var name = Match(card, @"(?:^|\r?\n)FN(?:;[^:]*)?:(.*)");
            var number = Match(card, @"(?:^|\r?\n)TEL(?:;[^:]*)?:(.*)");
            if (!string.IsNullOrWhiteSpace(number)) list.Add(new(name ?? number, number));
        }
        return list;
    }

    private static List<CallEntry> ParseCalls(string text, string type)
    {
        var list = new List<CallEntry>();
        foreach (var card in Regex.Split(text, "END:VCARD", RegexOptions.IgnoreCase))
        {
            var name = Match(card, @"(?:^|\r?\n)FN(?:;[^:]*)?:(.*)");
            var number = Match(card, @"(?:^|\r?\n)TEL(?:;[^:]*)?:(.*)");
            var ts = Match(card, @"(?:^|\r?\n)(?:X-IRMC-CALL-DATETIME|X-IRMC-CALL-DATETIME;[^:]*):(.*)");
            DateTimeOffset? time = DateTimeOffset.TryParse(ts, out var parsed) ? parsed : null;
            if (!string.IsNullOrWhiteSpace(number)) list.Add(new(number, name, type, time));
        }
        return list;
    }

    private static string? Match(string text, string pattern)
    { var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value.Trim() : null; }
}
