using System.Buffers;

namespace Claupet;

/// <summary>
/// Las líneas de un JSONL (las conversaciones de Claude Code) sin cargar el archivo entero: se leen en trozos, cada línea
/// queda en UTF-8 (sin pasar a texto) y el búfer crece solo hasta lo que mide la más larga. Así una conversación de cientos
/// de MB no ocupa cientos de MB, ni tres veces (bytes, texto y líneas) como al leerla de una vez. Del pool compartido
/// sale solo el trozo de 64 KB: el búfer de una línea de varios MB es propio y se libera, porque el pool guardaría los
/// arreglos grandes para siempre.
/// </summary>
static class JsonLines
{
    const int Chunk = 64 * 1024;

    /// <summary>
    /// Pasa a line cada línea completa (terminada en \n) desde donde está el stream, sin el \n. La línea es una vista del
    /// búfer: vale solo mientras dura la llamada. Devuelve los bytes que ocupaban; lo que queda después del último \n (una
    /// línea a medio escribir) no se pasa ni se cuenta.
    /// </summary>
    public static long ForEach(Stream stream, Action<ReadOnlyMemory<byte>> line)
    {
        var pooled = ArrayPool<byte>.Shared.Rent(Chunk);
        var buffer = pooled;
        int start = 0, filled = 0;
        long used = 0;
        try
        {
            while (true)
            {
                if (filled == buffer.Length)
                {
                    if (start > 0)
                    {
                        // Lo ya pasado se descarta: la línea en curso va al principio.
                        Buffer.BlockCopy(buffer, start, buffer, 0, filled - start);
                        filled -= start;
                        start = 0;
                    }
                    else
                    {
                        // Una línea más larga que el búfer: se agranda (fuera del pool).
                        var bigger = new byte[buffer.Length * 2];
                        Buffer.BlockCopy(buffer, 0, bigger, 0, filled);
                        buffer = bigger;
                    }
                }
                var read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read == 0) return used;
                var from = filled;
                filled += read;
                int end;
                while ((end = Array.IndexOf(buffer, (byte)'\n', from, filled - from)) >= 0)
                {
                    line(new ReadOnlyMemory<byte>(buffer, start, end - start));
                    used += end - start + 1;
                    start = from = end + 1;
                }
                if (start == filled) start = filled = 0;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(pooled); }
    }
}
