using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.sensor", "Sensor-controlled dashboard")]
public sealed class SensorDashboard : IChudExtension
{
    IChudContext? context;
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        context = ctx;
        ctx.RegisterDashboard(new DashboardBuilder("Phone Motion").Id("example.sensor.home").Text("motion", "Enable phone sensors and host sensor permission.").Build());
        return Task.CompletedTask;
    }
    public Task OnSensorAsync(string deviceId, SensorEvent sensor, CancellationToken ct)
    {
        context!.Update("example.sensor.home", new WidgetPatch("motion", $"X {sensor.X:F1}  Y {sensor.Y:F1}  Z {sensor.Z:F1} m/s²"));
        return Task.CompletedTask;
    }
}
