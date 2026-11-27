using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("example.json", "JSON API status monitor")]
public sealed class JsonStatus : IChudExtension
{
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        ctx.RegisterDashboard(new DashboardBuilder("JSON API Status").Id("example.json.home").Add(new Widget { Id = "service", Kind = WidgetKind.Status, Label = "YOUR SERVICE", DataKey = "example.json.status" }).Build());
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        ct.Register(client.Dispose);
        ctx.RegisterProvider("example.json.status", async token =>
        {
            if (!ctx.Settings.TryGetValue("url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return "Configure url in extension settings";
            try
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode) return $"HTTP {(int)response.StatusCode}";
                await using var stream = await response.Content.ReadAsStreamAsync(token); var bytes = new byte[65537]; var length = 0;
                while (length < bytes.Length) { var n = await stream.ReadAsync(bytes.AsMemory(length), token); if (n == 0) break; length += n; }
                if (length > 65536) return "Response exceeds 64 KB";
                using var json = JsonDocument.Parse(bytes.AsMemory(0, length));
                return ctx.Settings.TryGetValue("property", out var property) && json.RootElement.TryGetProperty(property, out var value) ? value.ToString() : "JSON service is healthy";
            }
            catch (Exception ex) { return "Unavailable: " + ex.GetType().Name; }
        });
        return Task.CompletedTask;
    }
}
