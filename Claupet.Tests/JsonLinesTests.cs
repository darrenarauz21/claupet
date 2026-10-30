using System.Text;

namespace Claupet.Tests;

/// <summary>Las líneas de un JSONL leídas por trozos: largas, cortas, a medio escribir y desde la mitad del archivo.</summary>
public sealed class JsonLinesTests
{
    static List<string> Lines(byte[] data, out long used, long from = 0)
    {
        using var stream = new MemoryStream(data);
        stream.Seek(from, SeekOrigin.Begin);
        var lines = new List<string>();
        used = JsonLines.ForEach(stream, line => lines.Add(Encoding.UTF8.GetString(line.Span)));
        return lines;
    }

    [Fact]
    public void Pasa_cada_linea_completa_y_cuenta_sus_bytes()
    {
        var data = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":\"ñandú\"}\n");
        Assert.Equal(["{\"a\":1}", "{\"b\":\"ñandú\"}"], Lines(data, out var used));
        Assert.Equal(data.Length, used);
    }

    [Fact]
    public void La_linea_a_medio_escribir_no_se_pasa_ni_se_cuenta()
    {
        var data = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":");
        Assert.Equal(["{\"a\":1}"], Lines(data, out var used));
        Assert.Equal(8, used);
    }

    [Fact]
    public void Una_linea_mas_larga_que_el_bufer_llega_entera()
    {
        // Mayor que el trozo de 64 KB, y otras cortas alrededor, para cruzar los bordes del búfer.
        var big = "{\"x\":\"" + new string('z', 300_000) + "\"}";
        var text = string.Join("\n", Enumerable.Range(0, 50).Select(i => $"{{\"i\":{i}}}").Append(big).Append("{\"fin\":1}")) + "\n";
        var lines = Lines(Encoding.UTF8.GetBytes(text), out var used);
        Assert.Equal(52, lines.Count);
        Assert.Equal(big, lines[50]);
        Assert.Equal("{\"fin\":1}", lines[^1]);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), used);
    }

    [Fact]
    public void Sigue_desde_donde_quedo()
    {
        var data = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":2}\n");
        Assert.Equal(["{\"b\":2}"], Lines(data, out var used, from: 8));
        Assert.Equal(8, used);
        Assert.Empty(Lines(data, out var none, from: data.Length));
        Assert.Equal(0, none);
    }
}
