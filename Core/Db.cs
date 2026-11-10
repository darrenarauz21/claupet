using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Claupet;

/// <summary>
/// SQLite con la biblioteca que ya trae cada sistema: winsqlite3.dll en Windows 10 y 11, libsqlite3.dylib en macOS y
/// libsqlite3.so.0 en Linux (la de siempre, no la del paquete -dev). Claupet no lleva una propia: si el sistema no la
/// tiene, Available es false y lo que la usa se queda sin esos datos. Todo pasa por aquí, así que cambiar de biblioteca
/// (por ejemplo, una que cargue extensiones como sqlite-vec) es cambiar Load.
/// </summary>
static class Db
{
    static readonly Lazy<bool> Ready = new(Init);

    /// <summary>Si se encontró y se cargó la biblioteca del sistema (se intenta una sola vez).</summary>
    public static bool Available => Ready.Value;

    /// <summary>Las bibliotecas que se prueban en este sistema, en orden.</summary>
    internal static string[] Candidates() =>
        OperatingSystem.IsWindows() ? ["winsqlite3.dll", "sqlite3.dll"]
        : OperatingSystem.IsMacOS() ? ["/usr/lib/libsqlite3.dylib", "libsqlite3.dylib"]
        : ["libsqlite3.so.0", "libsqlite3.so"];

    static bool Init()
    {
        try
        {
            foreach (var name in Candidates())
            {
                if (!NativeLibrary.TryLoad(name, out var handle)) continue;
                SQLite3Provider_dynamic_cdecl.Setup("sqlite3", new Functions(handle));
                raw.SetProvider(new SQLite3Provider_dynamic_cdecl());
                return true;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
        return false;
    }

    /// <summary>
    /// Abre una base de otro programa solo para leer (nunca la cambia, aunque ese programa esté escribiendo en ella).
    /// null si no hay SQLite o no existe el archivo. Sin pool: al cerrarla no queda el archivo tomado.
    /// </summary>
    public static SqliteConnection? OpenReadOnly(string path)
    {
        if (!Available || !File.Exists(path)) return null;
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString());
        try
        {
            connection.Open();
            using var busy = connection.CreateCommand();
            busy.CommandText = "PRAGMA busy_timeout = 2000";
            busy.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    sealed class Functions(IntPtr library) : IGetFunctionPointer
    {
        public IntPtr GetFunctionPointer(string name) => NativeLibrary.TryGetExport(library, name, out var address) ? address : IntPtr.Zero;
    }
}
