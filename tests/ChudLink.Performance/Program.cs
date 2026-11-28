using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ChudLink.Host;
using ChudLink.Phone;
using ChudLink.Protocol;

// Compile once against captured baseline binaries; swap the same dependencies for the after run.
var seconds = args.Length > 0 ? int.Parse(args[0]) : 15;
var output = args.Length > 1 ? args[1] : "performance.json";
var data = Path.Combine(Path.GetTempPath(), "ChudLink-performance", Guid.NewGuid().ToString("N"));
var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
await using var host = new HostRuntime(data); await host.StartAsync("127.0.0.1", port);
using var process = Process.GetCurrentProcess(); var results = new List<object>();
foreach (var scenario in new[] { "idle", "control", "hardware", "audio" })
{
    PhoneConnection? phone = null; Task? client = null; using var cancel = new CancellationTokenSource();
    if (scenario != "idle")
    {
        phone = new(Guid.NewGuid().ToString("N"), host.Address, host.Fingerprint, "");
        var pairing = phone.PairAsync(host.NewPairingCode(), "Performance probe", "Protocol client", cancel.Token);
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!host.Pairings.Values.Any(p => p.Request.DeviceId == phone.DeviceId)) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Pairing"); await Task.Delay(20); }
        host.Approve(host.Pairings.Values.Single(p => p.Request.DeviceId == phone.DeviceId).Ticket, true); await pairing;
        await host.AssignAsync(phone.DeviceId, scenario);
        client = phone.RunAsync(() => new("Performance probe", "Protocol client", "test", 90, "Portrait", ["canvas"], RenderProfile.Balanced), cancel.Token);
        while (!phone.Connected) await Task.Delay(20, cancel.Token);
    }
    await Task.Delay(2500);
    process.Refresh(); var cpu = process.TotalProcessorTime; var allocated = GC.GetTotalAllocatedBytes(); var bytes = host.BytesSent;
    var gen0 = GC.CollectionCount(0); var gen1 = GC.CollectionCount(1); var gen2 = GC.CollectionCount(2); var watch = Stopwatch.StartNew();
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    process.Refresh(); var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
    var row = new { scenario, seconds = watch.Elapsed.TotalSeconds, cpuMs, normalizedCpuPercent = 100 * cpuMs / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount,
        allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64,
        gen0 = GC.CollectionCount(0) - gen0, gen1 = GC.CollectionCount(1) - gen1, gen2 = GC.CollectionCount(2) - gen2, bytesSent = host.BytesSent - bytes };
    results.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
    await cancel.CancelAsync(); phone?.Dispose(); if (client != null) try { await client; } catch (OperationCanceledException) { }
    if (phone != null) host.Revoke(phone.DeviceId);
}
File.WriteAllText(output, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, processors = Environment.ProcessorCount, serverGc = System.Runtime.GCSettings.IsServerGC, results }, new JsonSerializerOptions { WriteIndented = true }));
