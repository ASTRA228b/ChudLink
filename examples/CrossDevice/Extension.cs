using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;

[ChudExtension("example.mesh", "Cross-device dashboard switcher")]
public sealed class CrossDevice : IChudExtension
{
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        ctx.RegisterDashboard(new DashboardBuilder("Device Mesh").Id("example.mesh.home").Button("switch", "OTHER PHONE → HARDWARE", "example.mesh.switch").Build());
        ctx.RegisterAction("example.mesh.switch", (source, arg) =>
        {
            var target = ctx.Settings.TryGetValue("targetDeviceId", out var configured) ? ctx.Devices.FirstOrDefault(d => d.Id == configured) : ctx.Devices.FirstOrDefault(d => d.Id != source);
            if (target != null) ctx.AssignDashboard(target.Id, "hardware"); else ctx.Notify(source, "Connect another phone first.");
            ctx.SetShared("lastSource", source); return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
