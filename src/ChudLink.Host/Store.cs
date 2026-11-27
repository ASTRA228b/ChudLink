using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ChudLink.Core;
using ChudLink.Protocol;

namespace ChudLink.Host;

public sealed class Store
{
    public string Root { get; }
    public HostSettings Settings { get; }
    public List<DeviceRecord> Devices { get; }
    public System.Collections.Concurrent.ConcurrentDictionary<string, Dashboard> Dashboards { get; } = new();
    readonly object gate = new();
    public Store(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChudAI", "ChudLink");
        Directory.CreateDirectory(Root);
        Settings = Read<HostSettings>("settings.json") ?? new();
        Devices = Read<List<DeviceRecord>>("devices.json") ?? [];
        foreach (var d in Devices) d.Connected = false;
        foreach (var d in LibraryDashboards()) Dashboards[d.Id] = d;
        var folder = Path.Combine(Root, "dashboards"); Directory.CreateDirectory(folder);
        foreach (var f in Directory.GetFiles(folder, "*.chudlink"))
            try { var d = Wire.Parse<Dashboard>(File.ReadAllText(f)); d.Validate(); Dashboards[d.Id] = d; } catch (Exception) { /* Invalid files remain available for manual repair. */ }
    }
    static IReadOnlyList<Dashboard> LibraryDashboards() => ChudLink.Presets.Library.Create();
    public T? Read<T>(string name)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return default;
        return Wire.Parse<T>(File.ReadAllText(path));
    }
    public void Write<T>(string name, T value)
    {
        lock (gate)
        {
            var path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", Wire.Serialize(value)); File.Move(path + ".tmp", path, true);
        }
    }
    public void Save() { Write("settings.json", Settings); Write("devices.json", Devices); }
    public void SaveDashboard(Dashboard dashboard)
    {
        dashboard.Validate();
        Dashboards[dashboard.Id] = dashboard.Clone();
        Write("dashboards/" + Credentials.Hash(dashboard.Id) + ".chudlink", dashboard);
    }
    public X509Certificate2 Certificate()
    {
        var path = Path.Combine(Root, "host.pfx");
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=ChudLink Local Host", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
        File.WriteAllBytes(path, generated.Export(X509ContentType.Pfx));
        return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
    }
}
