using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.audio", "Custom audio controls")]
public sealed class AudioControls : IChudExtension
{
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        ctx.RegisterDashboard(new DashboardBuilder("C# Audio Console").Id("example.audio.home")
            .Button("play", "PLAY / PAUSE", "media")
            .Add(new Widget { Id = "volume", Kind = WidgetKind.Slider, Label = "VOLUME", Y = 220, DataKey = "audio.volume", Bindings = [new("volume", Event: "change")] })
            .Add(new Widget { Id = "mute", Kind = WidgetKind.Button, Label = "MUTE", Y = 420, Bindings = [new("media", "mute")] }).Build());
        return Task.CompletedTask;
    }
}
