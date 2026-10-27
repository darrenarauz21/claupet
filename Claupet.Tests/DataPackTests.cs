using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;

namespace ClaudeUsageTray.Tests;

/// <summary>PNG armados a mano, para probar cada filtro y la paleta con transparencia.</summary>
static class TestPng
{
    public static byte[] Rgba(int width, int height, uint[] argb, Func<int, byte>? filterOf = null)
    {
        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        var previous = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var line = new byte[stride];
            for (var x = 0; x < width; x++)
            {
                var c = argb[y * width + x];
                line[x * 4] = (byte)(c >> 16);
                line[x * 4 + 1] = (byte)(c >> 8);
                line[x * 4 + 2] = (byte)c;
                line[x * 4 + 3] = (byte)(c >> 24);
            }
            var filter = filterOf?.Invoke(y) ?? 0;
            raw[y * (stride + 1)] = filter;
            for (var i = 0; i < stride; i++)
            {
                int a = i >= 4 ? line[i - 4] : 0, b = previous[i], c = i >= 4 ? previous[i - 4] : 0;
                var predicted = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                raw[y * (stride + 1) + 1 + i] = (byte)(line[i] - predicted);
            }
            previous = line;
        }
        return Build(width, height, 6, raw);
    }

    public static byte[] Palette(int width, int height, byte[] indices, byte[] palette, byte[]? transparency)
    {
        var raw = new byte[height * (width + 1)];
        for (var y = 0; y < height; y++) Array.Copy(indices, y * width, raw, y * (width + 1) + 1, width);
        return Build(width, height, 3, raw, ("PLTE", palette), transparency is null ? default : ("tRNS", transparency));
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static byte[] Build(int width, int height, byte color, byte[] raw, params (string Type, byte[] Data)[] extra)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = color;
        Chunk(png, "IHDR", header);
        foreach (var (type, data) in extra.Where(e => e.Type is not null)) Chunk(png, type, data);
        using var z = new MemoryStream();
        using (var deflate = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(raw);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed));
        png.Write(crc);
    }

    static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}

public class PngTests
{
    static uint[] Gradient(int w, int h) =>
        Enumerable.Range(0, w * h).Select(i => 0xFF000000u | (uint)(i * 7 % 256) << 16 | (uint)(i * 13 % 256) << 8 | (uint)(i * 29 % 256)).ToArray();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Cada_filtro_se_deshace(byte filter)
    {
        var pixels = Gradient(9, 5);
        var image = PngImage.Read(TestPng.Rgba(9, 5, pixels, _ => filter));
        Assert.Equal(9, image.Width);
        Assert.Equal(pixels, image.Pixels);
    }

    [Fact]
    public void Filtros_distintos_por_fila()
    {
        var pixels = Gradient(16, 10);
        pixels[3] = 0x00123456; // un pixel transparente
        var image = PngImage.Read(TestPng.Rgba(16, 10, pixels, y => (byte)(y % 5)));
        Assert.Equal(pixels, image.Pixels);
    }

    [Fact]
    public void Paleta_con_transparencia()
    {
        var image = PngImage.Read(TestPng.Palette(3, 1, [0, 1, 2], [255, 0, 0, 0, 255, 0, 0, 0, 255], [0, 128]));
        Assert.Equal([0x00FF0000u, 0x8000FF00u, 0xFF0000FFu], image.Pixels);
    }

    [Fact]
    public void Un_png_de_gdi_se_lee_igual()
    {
        // Como lo guarda un editor cualquiera (GDI+ elige sus propios filtros).
        using var bitmap = new Bitmap(13, 7, PixelFormat.Format32bppArgb);
        for (var y = 0; y < 7; y++)
            for (var x = 0; x < 13; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(x == y ? 0 : 255, x * 19 % 256, y * 37 % 256, (x + y) * 11 % 256));
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var image = PngImage.Read(stream.ToArray());
        for (var y = 0; y < 7; y++)
            for (var x = 0; x < 13; x++)
            {
                var expected = (uint)bitmap.GetPixel(x, y).ToArgb();
                if (expected >> 24 == 0) Assert.Equal(0u, image[x, y] >> 24);
                else Assert.Equal(expected, image[x, y]);
            }
    }

    [Fact]
    public void Lo_que_no_es_png_o_esta_cortado_se_rechaza()
    {
        Assert.Throws<InvalidDataException>(() => PngImage.Read(Encoding.ASCII.GetBytes("no es un png")));
        var png = TestPng.Rgba(4, 4, Gradient(4, 4));
        Assert.Throws<InvalidDataException>(() => PngImage.Read(png[..(png.Length - 20)]));
    }
}

public class ResamplerTests
{
    [Fact]
    public void De_48_kHz_a_16_kHz_por_partes()
    {
        var sine = Enumerable.Range(0, 48000).Select(i => 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / 48000)).ToArray();
        var whole = new PcmResampler(48000).Push(sine);
        var parts = new PcmResampler(48000);
        var pieces = new List<short>();
        for (var at = 0; at < sine.Length; at += 480) pieces.AddRange(parts.Push(sine.AsSpan(at, 480)));
        Assert.InRange(whole.Length, 15990, 16000);
        Assert.Equal(whole.Length, pieces.Count);
        Assert.Equal(whole, pieces.ToArray());
        // 440 Hz pasa el filtro casi entero: el pico sigue cerca de la mitad de la escala.
        Assert.InRange(whole.Max(s => (int)s), 15000, 16500);
    }

    [Fact]
    public void De_44_1_kHz_estereo_ya_mezclado()
    {
        var silence = new float[44100];
        var output = new PcmResampler(44100).Push(silence);
        Assert.InRange(output.Length, 15990, 16000);
        Assert.All(output, s => Assert.Equal(0, s));
    }
}

public class SecretStoreTests
{
    [Fact]
    public void Las_claves_van_cifradas_y_atadas_a_su_lugar()
    {
        using var dir = new TempDir();
        var path = dir["claves.json"];
        var store = new DpapiSecretStore(path);
        store.Set("autor.uno", "apiKey", "sk-secreta-123");
        store.Set("autor.uno", "otra", "valor-2");
        Assert.Equal("sk-secreta-123", store.Get("autor.uno", "apiKey"));
        Assert.DoesNotContain("sk-secreta-123", File.ReadAllText(path));

        // Copiada a otro ajuste u otra extensión, no se descifra.
        var json = File.ReadAllText(path);
        var doc = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        doc["autor.uno"]!["otra"] = doc["autor.uno"]!["apiKey"]!.GetValue<string>();
        File.WriteAllText(path, doc.ToJsonString());
        Assert.Null(new DpapiSecretStore(path).Get("autor.uno", "otra"));

        store.Set("autor.uno", "apiKey", null);
        Assert.Null(store.Get("autor.uno", "apiKey"));
        store.Remove("autor.uno");
        Assert.Null(store.Get("autor.uno", "otra"));
    }
}

public class DataPackTests
{
    static byte[] Wav() => AudioCodec.ToWav(new short[1600], 16000);

    static byte[] Strip(int frames, uint color)
    {
        var pixels = new uint[8 * frames * 8];
        for (var f = 0; f < frames; f++)
            for (var y = 0; y < 8; y++)
                for (var x = 0; x < 8; x++)
                    pixels[y * 8 * frames + f * 8 + x] = x == 0 && y == 0 ? 0u : color + (uint)f; // la esquina, transparente
        return TestPng.Rgba(8 * frames, 8, pixels, y => (byte)(y % 5));
    }

    const string PetManifest = """
        {
          "schema": 1, "id": "prueba.gatito", "name": "Gatito", "version": "1.0.0",
          "provides": ["pet"],
          "pet": { "name": "Gatito", "width": 8, "height": 8,
                   "animations": { "idle": { "file": "idle.png", "ms": 100 }, "dance": { "file": "dance.png", "ms": 50 } } }
        }
        """;

    [Fact]
    public async Task La_mascota_se_lee_y_lo_que_falta_es_idle()
    {
        await using var f = new ManagerFixture();
        await f.InstallAsync(PetManifest, [("idle.png", Strip(2, 0xFF102030)), ("dance.png", Strip(3, 0xFFFF00FF))]);
        Assert.Equal(new[] { ("prueba.gatito", "Gatito") }, f.Manager.Pets());
        var pet = await f.Manager.PetAsync("prueba.gatito");
        Assert.NotNull(pet);
        Assert.Equal((8, 8), (pet!.Width, pet.Height));

        List<(Color Color, int X, int Y, int W)> Painted(ChibiMove move, long t)
        {
            var list = new List<(Color, int, int, int)>();
            pet.Paint((c, x, y, w, _) => list.Add((c, x, y, w)), move, t);
            return list;
        }
        var idle = Painted(ChibiMove.Idle, 150); // segundo cuadro
        Assert.Equal(Color.FromArgb(unchecked((int)0xFF102031)), idle[0].Color);
        Assert.DoesNotContain(idle, p => p.X == 0 && p.Y == 0); // el pixel transparente no se pinta
        Assert.Equal(Painted(ChibiMove.Idle, 150), Painted(ChibiMove.Think, 150)); // sin «think»: idle
        // El magenta puro es el fondo de la ventana: se corre un tono para que no quede un hueco.
        Assert.Equal(Color.FromArgb(unchecked((int)0xFFFE00FF)), Painted(ChibiMove.Dance, 0)[0].Color);
    }

    [Fact]
    public async Task Una_mascota_tocada_en_el_disco_se_apaga()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(PetManifest, [("idle.png", Strip(2, 0xFF102030)), ("dance.png", Strip(1, 0xFF405060))]);
        File.WriteAllBytes(ext.FileOf("idle.png"), Strip(2, 0xFF000000));
        Assert.Null(await f.Manager.PetAsync(ext.Id));
        Assert.False(f.Manager.IsEnabled(ext.Id));
        Assert.Empty(f.Manager.Pets());
    }

    [Fact]
    public async Task Una_tira_de_otro_alto_no_sirve()
    {
        await using var f = new ManagerFixture();
        var tall = TestPng.Rgba(8, 9, new uint[72].Select(_ => 0xFF112233u).ToArray());
        var ext = await f.InstallAsync(PetManifest, [("idle.png", tall), ("dance.png", Strip(1, 0xFF405060))]);
        Assert.Null(await f.Manager.PetAsync(ext.Id));
        Assert.Contains("idle.png", f.Manager.StateOf(ext.Id).DisabledReason);
    }

    [Fact]
    public async Task Los_sonidos_suenan_solo_ya_verificados()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync("""
            { "schema": 1, "id": "prueba.campanas", "name": "Campanas", "version": "1.0.0", "provides": ["sounds"],
              "sounds": { "terminado": "s/fin.wav", "fallo": "s/ay.wav" } }
            """, [("s/fin.wav", Wav()), ("s/ay.wav", Wav())]);
        var pack = ExtensionManager.PackPrefix + ext.Id;
        Assert.Equal(new[] { (pack, "Campanas") }, f.Manager.SoundPacks());
        Assert.Null(f.Manager.SoundFile(pack, "terminado")); // todavía sin verificar: suena el clásico
        await f.Manager.VerifyPacksAsync();
        Assert.Equal(ext.FileOf("s/fin.wav"), f.Manager.SoundFile(pack, "terminado"));
        Assert.Null(f.Manager.SoundFile(pack, "pregunta")); // no lo trae
        Assert.Null(f.Manager.SoundFile("clasico", "terminado"));
    }

    [Fact]
    public async Task Las_voces_de_un_paquete()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync("""
            { "schema": 1, "id": "prueba.voces", "name": "Voces", "version": "1.0.0", "provides": ["voice-style"],
              "voices": [ { "id": "suave", "name": "Suave", "file": "v/suave.json", "language": "es" } ] }
            """, [("v/suave.json", TestKit.Utf8("{}"))]);
        Assert.Equal(new[] { ("ext:prueba.voces/suave", "Suave · Voces") }, f.Manager.PackVoices());
        Assert.Equal(ext.FileOf("v/suave.json"), await f.Manager.VoiceFileAsync("ext:prueba.voces/suave"));
        Assert.Null(await f.Manager.VoiceFileAsync("ext:prueba.voces/otra"));
        Assert.Null(await f.Manager.VoiceFileAsync("F4"));
        await f.Manager.SetEnabledAsync(ext.Id, false);
        Assert.Null(await f.Manager.VoiceFileAsync("ext:prueba.voces/suave"));
    }

    [Fact]
    public async Task Los_eventos_de_sesiones_piden_su_permiso()
    {
        await using var f = new ManagerFixture();
        var manifest = TestKit.SkillManifest(extra: """, "events": ["session", "notice"]""");
        // Sin «sessions» en sus permisos no se entera de las sesiones: ni se intenta arrancar. Con los avisos sí
        // (y este programa no habla JSON-RPC, así que su intento queda en el registro).
        var ext = await f.InstallAsync(manifest, [("bin/notas.cmd", TestKit.Utf8("@exit /b 0"))]);
        var log = new ExtensionLog(ext.DataFolder).FilePath;
        Assert.DoesNotContain(ExtensionPermissions.Sessions, f.Manager.StateOf(ext.Id).Granted);
        await f.Manager.BroadcastAsync("session", new System.Text.Json.Nodes.JsonObject { ["id"] = "x" });
        Assert.False(File.Exists(log) && File.ReadAllText(log).Contains("[claupet]"));
        await f.Manager.BroadcastAsync("notice", new System.Text.Json.Nodes.JsonObject { ["kind"] = "done" });
        Assert.Contains("[claupet]", File.ReadAllText(log));
    }
}
