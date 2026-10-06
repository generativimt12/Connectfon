namespace Connectfon.Core;

public sealed record PhoneDevice(string Id, string Name, ulong BluetoothAddress, bool IsPaired, bool IsConnected, IReadOnlyList<string> Services);
public sealed record BluetoothCapability(string Name, bool Supported, string Details);
public sealed record CallEntry(string Number, string? Name, string Type, DateTimeOffset? Timestamp);
public sealed record ContactEntry(string Name, string Number);
public sealed record DeviceCapabilities(bool Hfp, bool Pbap, bool CallHistory, IReadOnlyList<string> RfcommServices);
public enum CallState { Idle, Incoming, Dialing, Alerting, Active, Held, Disconnected }
public sealed record HfpCallInfo(CallState State, string? Number, string? Name, string? Raw);
