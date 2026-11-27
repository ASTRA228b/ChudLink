using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using ChudLink.Protocol;

namespace ChudLink.Core;

public static class LocalNetwork
{
    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 169 && b[1] == 254);
    }
    public static IPAddress[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a)).Distinct().Append(IPAddress.Loopback).ToArray();
}
public static class Credentials
{
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    public static bool Verify(string token, string hash)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(token)), Convert.FromHexString(hash)); }
        catch (FormatException) { return false; }
    }
}
public sealed class DeviceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Android";
    public string TokenHash { get; set; } = "";
    public string DashboardId { get; set; } = "hardware";
    public string Group { get; set; } = "";
    public HashSet<string> Permissions { get; set; } = ["dashboard", "counter", "media"];
    public DeviceHello? Info { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public bool Connected { get; set; }
    public double LatencyMs { get; set; }
    public override string ToString() => $"{Name} · {(Connected ? "connected" : "offline")} · {Info?.Battery.ToString() ?? "?"}% · {Info?.Orientation ?? "unknown"}";
}
public sealed class HostSettings
{
    public string Address { get; set; } = "";
    public int Port { get; set; } = Wire.Port;
    public bool PersonalEdition { get; set; }
    public List<ApprovedAction> Actions { get; set; } = [];
    public List<ContextRule> ContextRules { get; set; } = [];
    public List<ServiceEndpoint> Endpoints { get; set; } = [];
}
public sealed record ContextRule(string Process, string DashboardId, string? DeviceId = null);
public sealed record ApprovedAction(string Id, string Name, string Kind, string Target, string Arguments = "");
public sealed record ServiceEndpoint(string Id, string Name, string Url, string? JsonProperty = null);
public sealed class History<T>(int capacity)
{
    readonly Queue<T> values = new();
    public void Add(T value) { lock (values) { values.Enqueue(value); while (values.Count > capacity) values.Dequeue(); } }
    public T[] Snapshot() { lock (values) return values.ToArray(); }
}
public sealed class DashboardHistory
{
    readonly Stack<string> undo = new(), redo = new();
    public void Save(Dashboard d) { undo.Push(Wire.Serialize(d)); redo.Clear(); if (undo.Count > 100) { var top = undo.Take(100).Reverse().ToArray(); undo.Clear(); foreach (var x in top) undo.Push(x); } }
    public Dashboard Undo(Dashboard d) { if (undo.Count == 0) return d; redo.Push(Wire.Serialize(d)); return Wire.Parse<Dashboard>(undo.Pop()); }
    public Dashboard Redo(Dashboard d) { if (redo.Count == 0) return d; undo.Push(Wire.Serialize(d)); return Wire.Parse<Dashboard>(redo.Pop()); }
}
