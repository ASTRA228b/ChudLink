using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.counter", "Custom counter")]
public sealed class CustomCounter : IChudExtension
{
    int count;
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        ctx.RegisterDashboard(new DashboardBuilder("Custom Counter").Id("example.counter.home")
            .Button("increment", "COUNT +1", "example.counter.increment").Text("value", "0").Build());
        ctx.RegisterAction("example.counter.increment", (device, arg) =>
        {
            ctx.Update("example.counter.home", new WidgetPatch("value", Interlocked.Increment(ref count).ToString())); return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
