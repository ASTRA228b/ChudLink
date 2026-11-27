using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.monitor", "Custom system monitor")]
public sealed class SystemMonitor : IChudExtension
{
    public async Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        ctx.RegisterWidget("example.monitor.memory", new Widget { Kind = WidgetKind.Progress, Label = "Worker memory", Maximum = 256 });
        ctx.RegisterDashboard(new DashboardBuilder("Custom System Monitor").Id("example.monitor.home")
            .Add(new Widget { Id = "cpu", Kind = WidgetKind.Gauge, Label = "SYSTEM CPU", DataKey = "host.cpu" })
            .Add(new Widget { Id = "ram", Kind = WidgetKind.Progress, Label = "SYSTEM RAM", DataKey = "host.ram", Y = 210 })
            .Add(new Widget { Id = "worker", Kind = WidgetKind.Custom, Label = "WORKER MEMORY", Y = 400, Properties = { ["type"] = "example.monitor.memory" } }).Build());
        while (!ct.IsCancellationRequested)
        {
            using var process = Process.GetCurrentProcess();
            if (ctx.Devices.Count > 0) ctx.Update("example.monitor.home", new WidgetPatch("worker", $"Extension memory: {process.WorkingSet64 / 1048576.0:F1} MB", process.WorkingSet64 / 1048576.0));
            await Task.Delay(3000, ct);
        }
    }
}
