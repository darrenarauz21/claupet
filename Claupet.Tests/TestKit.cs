using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ClaudeUsageTray.Tests;

/// <summary>Una carpeta temporal que se borra al terminar la prueba.</summary>
sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claupet-pruebas", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string this[string relative] => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string Write(string relative, string text)
    {
        var file = this[relative];
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        return file;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (Exception) { }
    }
}

/// <summary>Manifiestos, paquetes y claves de prueba.</summary>
static class TestKit
{
    public static string SkillManifest(string id = "prueba.notas", string version = "1.0.0", string extra = "") => $$"""
        {
          "schema": 1,
          "id": "{{id}}",
          "name": { "es": "Notas", "en": "Notes" },
          "version": "{{version}}",
          "author": "Autor de prueba",
          "license": "MIT",
          "provides": ["skill"],
          "runtime": { "type": "process", "command": "bin/notas.cmd" },
          "commands": [ { "name": "notas", "description": "Toma notas" } ],
          "permissions": ["pet", "notices"]{{extra}}
        }
        """;

    /// <summary>Una carpeta con el manifiesto y los archivos dados.</summary>
    public static string Folder(TempDir dir, string manifest, params (string Path, string Text)[] files)
    {
        var folder = System.IO.Path.Combine(dir.Path, "fuente-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "manifest.json"), manifest);
        foreach (var (path, text) in files)
        {
            var file = System.IO.Path.Combine(folder, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        return folder;
    }

    /// <summary>Un .claupet armado con PackageBuilder (firmado si se da quien firma).</summary>
    public static string Package(TempDir dir, string folder, ExtensionSigner? signer = null)
    {
        var output = System.IO.Path.Combine(dir.Path, Guid.NewGuid().ToString("N")[..8] + ".claupet");
        var (digest, errors) = PackageBuilder.Build(folder, output, signer);
        Assert.True(digest is not null, string.Join("\n", errors));
        return output;
    }

    /// <summary>Un zip a mano, con las entradas tal cual (para paquetes hostiles).</summary>
    public static string RawZip(TempDir dir, params (string Name, byte[] Data)[] entries)
    {
        var output = System.IO.Path.Combine(dir.Path, Guid.NewGuid().ToString("N")[..8] + ".claupet");
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(data);
        }
        return output;
    }

    public static ExtensionSigner NewSigner(string name = "Autor de prueba")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ExtensionSigner(key.ExportPkcs8PrivateKeyPem(), null, name);
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
