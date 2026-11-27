using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.hello", "Hello World")]
public sealed class HelloWorld : IChudExtension
{
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        var board = new DashboardBuilder("Hello World").Id("example.hello.home")
            .Button("hello", "SAY HELLO", "example.hello.greet").Text("message", "Made with C#").Build();
        ctx.RegisterDashboard(board);
        ctx.RegisterAction("example.hello.greet", (device, argument) =>
        {
            ctx.Update(board.Id, new WidgetPatch("message", "Hello from your PC!"));
            ctx.Notify(device, "Your C# action ran successfully.");
            return Task.CompletedTask;
        });
        ctx.Log("Hello World ready. Assign the dashboard to a paired phone; enable its actions permission.");
        return Task.CompletedTask;
    }
}
