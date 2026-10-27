using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray;

/// <summary>
/// Las claves de las extensiones (ajustes «secret»: una clave de API, un token), cifradas con DPAPI de
/// Windows para esta cuenta: otro usuario del equipo, o el archivo copiado a otro equipo, no las lee. Cada
/// valor lleva su extensión y su ajuste como entropía, para que no se puedan cambiar de lugar. Se guardan
/// en claves.json, junto a las extensiones, nunca en settings.json.
/// </summary>
sealed class DpapiSecretStore : ISecretStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string _path;
    readonly object _lock = new();

    public DpapiSecretStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(ExtensionStore.DefaultRoot, "claves.json");

    public string? Get(string extensionId, string key)
    {
        lock (_lock)
        {
            if (!Load().TryGetValue(extensionId, out var values) || !values.TryGetValue(key, out var sealedValue)) return null;
            try { return Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(sealedValue), Entropy(extensionId, key))); }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                ErrorLog.Write(ex);
                return null;
            }
        }
    }

    public void Set(string extensionId, string key, string? value)
    {
        lock (_lock)
        {
            var all = Load();
            if (!all.TryGetValue(extensionId, out var values)) all[extensionId] = values = new();
            if (string.IsNullOrEmpty(value)) values.Remove(key);
            else values[key] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(value), Entropy(extensionId, key)));
            if (values.Count == 0) all.Remove(extensionId);
            Save(all);
        }
    }

    public void Remove(string extensionId)
    {
        lock (_lock)
        {
            var all = Load();
            if (all.Remove(extensionId)) Save(all);
        }
    }

    Dictionary<string, Dictionary<string, string>> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(_path), Options) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            return new();
        }
    }

    void Save(Dictionary<string, Dictionary<string, string>> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, Options), new UTF8Encoding(false));
        File.Move(temp, _path, overwrite: true);
    }

    static byte[] Entropy(string extensionId, string key) => Encoding.UTF8.GetBytes($"claupet-extension-secret\n{extensionId}\n{key}");

    // DPAPI (crypt32) directamente: sin el paquete System.Security.Cryptography.ProtectedData.

    static byte[] Protect(byte[] data, byte[] entropy) => Crypt(data, entropy, protect: true);

    static byte[] Unprotect(byte[] data, byte[] entropy) => Crypt(data, entropy, protect: false);

    static byte[] Crypt(byte[] data, byte[] entropy, bool protect)
    {
        var input = Pin(data);
        var salt = Pin(entropy);
        var output = new DataBlob();
        try
        {
            const int UiForbidden = 0x1;
            var ok = protect
                ? CryptProtectData(ref input, null, ref salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            Marshal.FreeHGlobal(salt.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    static DataBlob Pin(byte[] bytes)
    {
        var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
        Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
        return blob;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DataBlob data, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref DataBlob data, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);
}
