using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.build", "Developer build monitor")]
public sealed class BuildMonitor : IChudExtension
{
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        ctx.RegisterDashboard(new DashboardBuilder("Build Monitor").Id("example.build.home")
            .Button("build", "RUN APPROVED BUILD", "example.build.run")
            .Add(new Widget { Id = "output", Kind = WidgetKind.Terminal, Label = "BUILD OUTPUT", Y = 220, Height = 500, DataKey = "build.output", Text = "Set buildActionId in extension settings." }).Build());
        ctx.RegisterAction("example.build.run", (device, arg) =>
        {
            if (ctx.Settings.TryGetValue("buildActionId", out var id)) ctx.RunApprovedAction(id, device);
            else ctx.Notify(device, "Approve a build action in Settings and configure buildActionId for this extension.");
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
