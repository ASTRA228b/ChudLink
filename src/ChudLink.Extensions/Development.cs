using System.IO.Compression;
using System.Text.Json;
using ChudLink.Protocol;
using ChudLink.SDK;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ChudLink.Extensions;

public sealed record CompilationResult(bool Success, string[] Diagnostics, string? AssemblyPath);
public static class ExtensionCompiler
{
    public static CompilationResult Build(string source, string output)
    {
        Directory.CreateDirectory(output);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator).Where(File.Exists)
            .Concat([typeof(IChudExtension).Assembly.Location, typeof(Dashboard).Assembly.Location]).Distinct()
            .Select(p => MetadataReference.CreateFromFile(p));
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("Extension", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        var path = Path.Combine(output, "Extension.dll");
        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        return new(result.Success, result.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(d => d.ToString()).ToArray(), result.Success ? path : null);
    }
}
public static class ExtensionPackage
{
    public static ExtensionManifest Inspect(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 256 || zip.Entries.Sum(e => e.Length) > 32 * 1024 * 1024) throw new InvalidDataException("Package exceeds 32 MB / 256 files");
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing manifest.json");
        using var reader = new StreamReader(entry.Open());
        var manifest = Wire.Parse<ExtensionManifest>(reader.ReadToEnd());
        ValidateManifest(manifest);
        foreach (var item in zip.Entries)
        {
            if (item.FullName.Contains("..") || item.FullName.Contains(':') || Path.IsPathRooted(item.FullName) || item.FullName.Contains('\\'))
                throw new InvalidDataException("Unsafe package path");
        }
        if (zip.GetEntry(manifest.Assembly) == null) throw new InvalidDataException("Compiled assembly missing");
        return manifest;
    }
    public static void ValidateManifest(ExtensionManifest m)
    {
        if (m.Id.Length is < 1 or > 80 || m.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-') || m.SdkVersion != "0.1.0" ||
            m.Assembly.Contains('/') || m.Assembly.Contains('\\') || m.Assembly.Contains(':') || !m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || m.Permissions.Length > 20)
            throw new InvalidDataException("Invalid extension identity, SDK version or assembly path");
        string[] known = ["dashboard", "notifications", "mesh", "actions", "sensors", "providers"];
        if (m.Permissions.Any(p => !known.Contains(p))) throw new InvalidDataException("Unknown requested permission");
    }
    public static ExtensionManifest Install(string path, string root)
    {
        var manifest = Inspect(path);
        Directory.CreateDirectory(root);
        var destination = Path.Combine(Path.GetFullPath(root), manifest.Id);
        if (Directory.Exists(destination)) throw new IOException("Extension already installed. Uninstall before updating.");
        Directory.CreateDirectory(destination);
        try { ZipFile.ExtractToDirectory(path, destination); }
        catch { Directory.Delete(destination, true); throw; }
        return manifest;
    }
    public static void Pack(string folder, string output)
    {
        using var manifest = File.OpenRead(Path.Combine(folder, "manifest.json"));
        ValidateManifest(JsonSerializer.Deserialize<ExtensionManifest>(manifest, Wire.Json) ?? throw new InvalidDataException());
        if (File.Exists(output)) File.Delete(output);
        ZipFile.CreateFromDirectory(folder, output);
        Inspect(output);
    }
}
