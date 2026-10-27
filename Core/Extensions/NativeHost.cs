using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Las DLL de terceros (cualquier lenguaje con la ABI de C: C, C++, Rust, Go, Zig, .NET con NativeAOT…)
/// no se cargan dentro de Claupet: esta misma app arranca otra vez con «--anfitrion-extension &lt;dll&gt;»,
/// carga la DLL ahí y la traduce a JSON-RPC por stdio. Si la DLL se cuelga o revienta, cae ese proceso y
/// Claupet sigue (y vuelve al integrado). La ABI (docs/extensiones/claupet.h):
///
///   int32_t claupet_api_version(void);                       // 1
///   int32_t claupet_init(const claupet_host* host);          // 0: lista
///   int32_t claupet_call(int64_t id, const char* method, const char* params_json, char** result_json);
///   void    claupet_free(char* p);                           // libera lo que devolvió claupet_call
///   void    claupet_shutdown(void);
///   void    claupet_cancel(int64_t id);                      // opcional
///
/// claupet_host trae size, context y tres funciones: call (le pide algo a Claupet; la respuesta la libera
/// free_string), notify (una notificación; «$/progress» con {"id": &lt;id de claupet_call&gt;, "value": …}
/// manda un avance) y free_string. Todo el texto es UTF-8 terminado en cero. Un claupet_call distinto de 0
/// es un error y result_json, si viene, es {"code": n, "message": "…"}.
/// </summary>
static class NativeExtensionHost
{
    public const string Flag = "--anfitrion-extension", SingleFlag = "--un-hilo";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ApiVersionFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn(IntPtr host);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CallFn(long id, IntPtr method, IntPtr parameters, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeFn(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CancelFn(long id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int HostCallFn(IntPtr context, IntPtr method, IntPtr parameters, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void HostNotifyFn(IntPtr context, IntPtr method, IntPtr parameters);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void HostFreeFn(IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    struct HostTable
    {
        public uint Size;
        public IntPtr Context, Call, Notify, FreeString;
    }

    /// <summary>La máquina de un PE (x64, x86, arm64) leída del encabezado; null si no es un PE.</summary>
    public static string? MachineOf(string dll)
    {
        try
        {
            using var f = File.OpenRead(dll);
            using var r = new BinaryReader(f);
            if (r.ReadUInt16() != 0x5A4D) return null; // MZ
            f.Position = 0x3C;
            var pe = r.ReadInt32();
            if (pe <= 0 || pe > f.Length - 6) return null;
            f.Position = pe;
            if (r.ReadUInt32() != 0x00004550) return null; // PE\0\0
            return r.ReadUInt16() switch
            {
                0x8664 => "x64",
                0x014C => "x86",
                0xAA64 => "arm64",
                _ => "?",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Por qué no se puede cargar esa DLL en este proceso (la arquitectura no coincide); null si se puede.</summary>
    public static string? ArchitectureProblem(string dll)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var mine = ExtensionPlatform.Current.Split('-')[1];
        var machine = MachineOf(dll);
        if (machine is null) return L.T("No es una DLL de Windows.");
        return machine == mine ? null : L.T("La DLL es de {0} y Claupet es de {1}: el autor debe incluir la de {1}.", machine, mine);
    }

    /// <summary>
    /// El modo anfitrión: carga la DLL y atiende por stdio hasta que Claupet lo cierre. Lo que la DLL
    /// escriba en la salida estándar rompería el protocolo: la consola de .NET va a stderr.
    /// </summary>
    public static int Run(string dll, bool singleThreaded)
    {
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);
        IntPtr library;
        try { library = NativeLibrary.Load(Path.GetFullPath(dll)); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"No se pudo cargar {dll}: {ex.Message}");
            return 2;
        }
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        T? Optional<T>(string name) where T : Delegate =>
            NativeLibrary.TryGetExport(library, name, out var p) ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;

        ApiVersionFn version; InitFn init; CallFn call; FreeFn free; VoidFn shutdown;
        try
        {
            version = Export<ApiVersionFn>("claupet_api_version");
            init = Export<InitFn>("claupet_init");
            call = Export<CallFn>("claupet_call");
            free = Export<FreeFn>("claupet_free");
            shutdown = Export<VoidFn>("claupet_shutdown");
        }
        catch (EntryPointNotFoundException ex)
        {
            Console.Error.WriteLine($"La DLL no exporta la ABI de Claupet: {ex.Message}");
            return 3;
        }
        var cancel = Optional<CancelFn>("claupet_cancel");
        var api = version();
        if (api < 1 || api > ExtensionManifest.ApiVersion)
        {
            Console.Error.WriteLine($"La DLL usa la versión {api} de la ABI; esta app entiende la {ExtensionManifest.ApiVersion}.");
            return 4;
        }

        var one = singleThreaded ? new SemaphoreSlim(1, 1) : null;
        var calls = new ConcurrentDictionary<long, JsonNode>(); // id de claupet_call → id de la petición de Claupet
        long nextCall = 0;
        JsonRpcConnection? rpc = null;
        var done = new ManualResetEventSlim();

        // Lo que la DLL le pide a Claupet: se reenvía por el mismo stdio y se espera la respuesta.
        HostCallFn hostCall = (IntPtr _, IntPtr method, IntPtr parameters, out IntPtr result) =>
        {
            result = IntPtr.Zero;
            try
            {
                var m = Marshal.PtrToStringUTF8(method) ?? "";
                var p = JsonNode.Parse(Marshal.PtrToStringUTF8(parameters) ?? "{}");
                var answer = rpc!.CallAsync(m, p, CancellationToken.None).GetAwaiter().GetResult();
                result = Marshal.StringToCoTaskMemUTF8(answer.ValueKind == JsonValueKind.Undefined ? "null" : answer.GetRawText());
                return 0;
            }
            catch (Exception ex)
            {
                var code = ex is JsonRpcException rpcError ? rpcError.Code : JsonRpcException.InternalError;
                result = Marshal.StringToCoTaskMemUTF8(new JsonObject { ["code"] = code, ["message"] = ex.Message }.ToJsonString());
                return code == 0 ? JsonRpcException.InternalError : code;
            }
        };
        HostNotifyFn hostNotify = (context, method, parameters) =>
        {
            try
            {
                var m = Marshal.PtrToStringUTF8(method) ?? "";
                var p = JsonNode.Parse(Marshal.PtrToStringUTF8(parameters) ?? "{}");
                if (m == "$/progress" && p is JsonObject o && o["id"] is JsonNode id && id.GetValueKind() == JsonValueKind.Number
                    && calls.TryGetValue(id.GetValue<long>(), out var requestId))
                    _ = rpc!.ProgressAsync(requestId, o["value"]?.DeepClone());
                else _ = rpc!.NotifyAsync(m, p);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
        };
        HostFreeFn hostFree = Marshal.FreeCoTaskMem;
        var table = new HostTable
        {
            Size = (uint)Marshal.SizeOf<HostTable>(),
            Call = Marshal.GetFunctionPointerForDelegate(hostCall),
            Notify = Marshal.GetFunctionPointerForDelegate(hostNotify),
            FreeString = Marshal.GetFunctionPointerForDelegate(hostFree),
        };
        var tablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<HostTable>());
        Marshal.StructureToPtr(table, tablePtr, false);

        // La DLL puede llamar a Claupet desde claupet_init: la conexión ya lee, pero lo que llegue espera a que termine.
        var initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        rpc = new JsonRpcConnection(stdin, stdout)
        {
            Handler = async (method, parameters, token) =>
            {
                if (!await initialized.Task) throw new JsonRpcException(JsonRpcException.InternalError, L.T("La DLL no se pudo iniciar."));
                if (method == "shutdown")
                {
                    shutdown();
                    return null;
                }
                var local = Interlocked.Increment(ref nextCall);
                // El id de la petición de Claupet, para mandar los avances de esta llamada con él.
                calls[local] = JsonRpcConnection.CurrentRequestId.Value!.DeepClone();
                if (one is not null) await one.WaitAsync(token);
                try
                {
                    using var cancelOnToken = cancel is null ? default : token.Register(() => cancel(local));
                    return await Task.Run(() => Invoke(call, free, local, method, parameters), CancellationToken.None);
                }
                finally
                {
                    one?.Release();
                    calls.TryRemove(local, out _);
                }
            },
            OnNotification = (method, _) => { if (method == "exit") done.Set(); },
        };
        rpc.Closed += _ => done.Set();

        rpc.Start();
        var ready = init(tablePtr);
        initialized.SetResult(ready == 0);
        if (ready != 0)
        {
            Console.Error.WriteLine($"claupet_init devolvió {ready}.");
            return 5;
        }
        done.Wait();
        GC.KeepAlive(hostCall);
        GC.KeepAlive(hostNotify);
        GC.KeepAlive(hostFree);
        return 0;
    }

    static JsonNode? Invoke(CallFn call, FreeFn free, long id, string method, JsonElement parameters)
    {
        var m = Marshal.StringToCoTaskMemUTF8(method);
        var p = Marshal.StringToCoTaskMemUTF8(parameters.ValueKind == JsonValueKind.Undefined ? "{}" : parameters.GetRawText());
        try
        {
            var status = call(id, m, p, out var result);
            string? text = null;
            if (result != IntPtr.Zero)
            {
                text = Marshal.PtrToStringUTF8(result);
                free(result);
            }
            if (status == 0) return text is null ? null : JsonNode.Parse(text);
            string message = L.T("La DLL devolvió el error {0}.", status);
            var code = status;
            if (text is not null)
                try
                {
                    var error = JsonNode.Parse(text);
                    message = error?["message"]?.GetValue<string>() ?? message;
                    code = error?["code"]?.GetValue<int>() ?? code;
                }
                catch (Exception) { }
            throw new JsonRpcException(code, message);
        }
        finally
        {
            Marshal.FreeCoTaskMem(m);
            Marshal.FreeCoTaskMem(p);
        }
    }
}
