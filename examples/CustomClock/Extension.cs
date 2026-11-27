using System;
using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.clock", "Custom clock")]
public sealed class CustomClock : IChudExtension
{
    public async Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        ctx.RegisterDashboard(new DashboardBuilder("UTC Clock").Id("example.clock.home").Text("utc", "UTC").Build());
        while (!ct.IsCancellationRequested)
        {
            if (ctx.Devices.Count > 0) ctx.Update("example.clock.home", new WidgetPatch("utc", DateTime.UtcNow.ToString("HH:mm:ss 'UTC'")));
            await Task.Delay(1000, ct);
        }
    }
}
