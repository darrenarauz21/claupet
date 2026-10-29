using System.Buffers.Binary;
using System.Drawing;
using System.IO.Compression;

namespace Claupet;

/// <summary>
/// Un PNG leído a pixeles (ARGB, fila por fila), sin depender de Windows: las mascotas de los paquetes se
/// dibujan igual en Windows y en Avalonia. Lee PNG de 8 bits (gris, gris con alfa, RGB, RGBA y con
/// paleta, con su transparencia) sin entrelazar, que es lo que exporta cualquier editor de pixel art.
/// Lanza InvalidDataException con el motivo si no puede.
/// </summary>
sealed class PngImage
{
    const int MaxSide = 4096;
    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public int Width { get; }
    public int Height { get; }
    /// <summary>Width × Height colores en ARGB (0xAARRGGBB).</summary>
    public uint[] Pixels { get; }

    PngImage(int width, int height, uint[] pixels) => (Width, Height, Pixels) = (width, height, pixels);

    public uint this[int x, int y] => Pixels[y * Width + x];

    public static PngImage Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>
    /// Guarda pixeles BGRA (de arriba abajo, como los da Windows) como PNG de 8 bits por canal: RGB, salvo que se pida
    /// el alfa (un ícono; una captura de pantalla no tiene transparencia). Cada fila va con el filtro «Sub», que en las
    /// capturas comprime bien.
    /// </summary>
    public static void Save(string path, int width, int height, ReadOnlySpan<byte> bgra, bool alpha = false)
    {
        using var file = File.Create(path);
        Save(file, width, height, bgra, alpha);
    }

    public static void Save(Stream output, int width, int height, ReadOnlySpan<byte> bgra, bool alpha = false)
    {
        if (bgra.Length < width * height * 4) throw new ArgumentException("bgra", nameof(bgra));
        var channels = alpha ? 4 : 3;
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            raw[row] = 1; // Sub: cada byte, menos el del pixel de su izquierda
            for (var x = 0; x < width; x++)
            {
                var s = (y * width + x) * 4;
                var d = row + 1 + x * channels;
                raw[d] = bgra[s + 2];
                raw[d + 1] = bgra[s + 1];
                raw[d + 2] = bgra[s];
                if (alpha) raw[d + 3] = bgra[s + 3];
            }
            for (var i = stride; i > channels; i--) raw[row + i] -= raw[row + i - channels]; // los datos van desde row + 1
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);

        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits por canal
        header[9] = (byte)(alpha ? 6 : 2); // RGBA o RGB
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
    }

    static void Chunk(Stream output, string type, byte[] body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, body.Length);
        output.Write(number);
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(Crc(0xFFFFFFFF, name), body) ^ 0xFFFFFFFF);
        output.Write(number);
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    public static PngImage Read(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException(L.T("No es un PNG."));
        int width = 0, height = 0, depth = 0, color = -1;
        byte[]? palette = null, transparency = null;
        using var data = new MemoryStream();
        var at = 8;
        while (true)
        {
            if (at + 12 > png.Length) throw new InvalidDataException(L.T("El PNG está cortado."));
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (length < 0 || at + 12L + length > png.Length) throw new InvalidDataException(L.T("El PNG está cortado."));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length);
            at += 12 + length;
            switch (type)
            {
                case "IHDR":
                    if (length != 13) throw new InvalidDataException(L.T("El PNG está dañado."));
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    depth = body[8];
                    color = body[9];
                    if (width is < 1 or > MaxSide || height is < 1 or > MaxSide) throw new InvalidDataException(L.T("El PNG es demasiado grande."));
                    if (body[10] != 0 || body[11] != 0) throw new InvalidDataException(L.T("El PNG está dañado."));
                    if (body[12] != 0) throw new InvalidDataException(L.T("El PNG está entrelazado: expórtelo sin entrelazar."));
                    if (depth != 8 || color is not (0 or 2 or 3 or 4 or 6))
                        throw new InvalidDataException(L.T("El PNG debe ser de 8 bits por canal."));
                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    transparency = body.ToArray();
                    break;
                case "IDAT":
                    data.Write(body);
                    break;
                case "IEND":
                    if (width == 0) throw new InvalidDataException(L.T("El PNG está dañado."));
                    return Decode(width, height, color, palette, transparency, data.ToArray());
            }
        }
    }

    static PngImage Decode(int width, int height, int color, byte[]? palette, byte[]? transparency, byte[] compressed)
    {
        var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        using (var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
        {
            var have = 0;
            while (have < raw.Length)
            {
                var read = z.Read(raw, have, raw.Length - have);
                if (read == 0) throw new InvalidDataException(L.T("El PNG está cortado."));
                have += read;
            }
        }
        if (color == 3 && (palette is null || palette.Length % 3 != 0)) throw new InvalidDataException(L.T("Al PNG le falta su paleta."));

        var pixels = new uint[width * height];
        var previous = new byte[stride];
        var line = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, line, 0, stride);
            Unfilter(filter, line, previous, channels);
            for (var x = 0; x < width; x++)
            {
                var p = x * channels;
                uint a = 255, r, g, b;
                switch (color)
                {
                    case 0:
                        r = g = b = line[p];
                        if (transparency is { Length: >= 2 } && line[p] == transparency[1]) a = 0;
                        break;
                    case 4:
                        r = g = b = line[p];
                        a = line[p + 1];
                        break;
                    case 2:
                        (r, g, b) = (line[p], line[p + 1], line[p + 2]);
                        if (transparency is { Length: >= 6 } && line[p] == transparency[1] && line[p + 1] == transparency[3] && line[p + 2] == transparency[5]) a = 0;
                        break;
                    case 3:
                        var i = line[p];
                        if (i * 3 + 2 >= palette!.Length) throw new InvalidDataException(L.T("El PNG usa un color que no está en su paleta."));
                        (r, g, b) = (palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2]);
                        if (transparency is not null && i < transparency.Length) a = transparency[i];
                        break;
                    default:
                        (r, g, b, a) = (line[p], line[p + 1], line[p + 2], line[p + 3]);
                        break;
                }
                pixels[y * width + x] = a << 24 | r << 16 | g << 8 | b;
            }
            (previous, line) = (line, previous);
        }
        return new PngImage(width, height, pixels);
    }

    static void Unfilter(byte filter, byte[] line, byte[] previous, int bpp)
    {
        switch (filter)
        {
            case 0: return;
            case 1:
                for (var i = bpp; i < line.Length; i++) line[i] += line[i - bpp];
                return;
            case 2:
                for (var i = 0; i < line.Length; i++) line[i] += previous[i];
                return;
            case 3:
                for (var i = 0; i < line.Length; i++) line[i] += (byte)(((i >= bpp ? line[i - bpp] : 0) + previous[i]) / 2);
                return;
            case 4:
                for (var i = 0; i < line.Length; i++)
                {
                    int a = i >= bpp ? line[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    line[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                }
                return;
            default:
                throw new InvalidDataException(L.T("El PNG está dañado."));
        }
    }
}

/// <summary>
/// La mascota de un paquete: cada animación es una tira PNG de cuadros del tamaño de la mascota, de
/// izquierda a derecha. Las animaciones se llaman como los gestos de Claupet (idle, dance, wave, think,
/// carryLeft…); la que falte se reemplaza por «idle». Los pixeles con alfa bajo la mitad son transparentes
/// (la ventana de la mascota no tiene medias transparencias).
/// </summary>
sealed class PetSheet
{
    readonly Dictionary<string, (PngImage Strip, int Frames, int Ms)> _animations;

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }

    PetSheet(string name, int width, int height, Dictionary<string, (PngImage, int, int)> animations) =>
        (Name, Width, Height, _animations) = (name, width, height, animations);

    /// <summary>El nombre de la animación de un gesto: el del enum con la primera letra en minúscula.</summary>
    public static string AnimationOf(ChibiMove move)
    {
        var name = move.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Lee las tiras del paquete (con sus archivos ya verificados). Lanza InvalidDataException con el motivo.</summary>
    public static PetSheet Load(PetSpec spec, Func<string, string> fileOf)
    {
        var animations = new Dictionary<string, (PngImage, int, int)>();
        foreach (var (name, animation) in spec.Animations)
        {
            PngImage strip;
            try { strip = PngImage.Read(fileOf(animation.File)); }
            catch (InvalidDataException ex) { throw new InvalidDataException($"{animation.File}: {ex.Message}"); }
            if (strip.Height != spec.Height || strip.Width % spec.Width != 0)
                throw new InvalidDataException(L.T("{0}: la tira debe medir {1} de alto y un múltiplo de {2} de ancho.", animation.File, spec.Height, spec.Width));
            animations[name] = (strip, strip.Width / spec.Width, animation.Ms);
        }
        return new PetSheet(spec.Name.Text, spec.Width, spec.Height, animations);
    }

    public bool Has(ChibiMove move) => _animations.ContainsKey(AnimationOf(move));

    /// <summary>Pinta el cuadro del gesto en el momento t (ms desde que empezó), en casillas de la mascota.</summary>
    public void Paint(PixelFill fill, ChibiMove move, long t)
    {
        var (strip, frames, ms) = _animations.TryGetValue(AnimationOf(move), out var a) ? a : _animations["idle"];
        var frame = (int)(Math.Max(0, t) / Math.Max(1, ms) % frames);
        var x0 = frame * Width;
        for (var y = 0; y < Height; y++)
        {
            // Los pixeles seguidos del mismo color van en un solo rectángulo.
            var x = 0;
            while (x < Width)
            {
                var argb = strip[x0 + x, y];
                if (argb >> 24 < 128) { x++; continue; }
                var run = 1;
                while (x + run < Width && strip[x0 + x + run, y] == argb) run++;
                // El magenta puro es el fondo transparente de la ventana en Windows: se corre un tono.
                var rgb = (argb & 0xFFFFFF) == 0xFF00FF ? 0xFE00FFu : argb & 0xFFFFFF;
                fill(Color.FromArgb((int)(rgb | 0xFF000000)), x, y, run, 1);
                x += run;
            }
        }
    }
}
