using System.Drawing;

namespace Claupet;

/// <summary>
/// Un personaje sobre la base del chibi: el mismo esqueleto (la cara, los ojos, la boca, los brazos y las piernas en
/// las mismas casillas), así que hace todos los movimientos de ChibiSprite, con su pelo, su ropa y sus colores. El
/// cuerpo se escribe como su mitad izquierda con las letras de siempre (O la ropa, R el pelo, S la piel…) y Colors
/// les da sus colores; lo que no es simétrico (un mechón, un gancho, la cola) lo agrega Overlay después de espejar.
/// Accent reemplaza a los ojitos de cangrejo: lo que se mueve arriba de la cabeza según hacia dónde mira.
/// </summary>
sealed class ChibiSkin
{
    /// <summary>Lo que se guarda en los ajustes.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Mitad izquierda del cuerpo de frente, 16 × 48, con la cara en las casillas del chibi.</summary>
    internal required string[] Body { get; init; }
    /// <summary>Mitad izquierda de la cabeza chica (bandeja), 8 × 16, con los ojos en las casillas del chibi.</summary>
    internal required string[] Head { get; init; }
    /// <summary>El torso sin brazo (mitad izquierda, filas 31 a 38), para dibujar encima el brazo en otra pose.</summary>
    internal required string[] Armless { get; init; }
    /// <summary>Los colores de sus letras; las demás (efectos, caja, objetos del flujo) son las del chibi.</summary>
    internal required Dictionary<char, Color> Colors { get; init; }
    /// <summary>Lo de arriba de la cabeza según hacia dónde mira (x e y de 0 a 2), o null con los ojos cerrados.</summary>
    internal required Action<char[][], (int X, int Y)?> Accent { get; init; }
    /// <summary>
    /// Letras que cambian en los brazos levantados o abiertos (las poses de ChibiSprite.Arms se dibujan con O, o y S):
    /// por ejemplo, el puño de la manga (o) en otro color.
    /// </summary>
    internal Dictionary<char, char> ArmTint { get; init; } = [];
    /// <summary>
    /// Lo que va detrás del cuerpo en el lienzo de 40 × 52 (las coletas que caen por los costados): recibe el lienzo y
    /// dónde queda la esquina del cuerpo. Puede salirse del cuerpo hasta el margen del lienzo.
    /// </summary>
    internal Action<char[][], int, int>? Behind { get; init; }
    /// <summary>Lo que no es simétrico, sobre el cuerpo ya espejado (32 × 48), de frente.</summary>
    internal Action<char[][]>? Overlay { get; init; }
    /// <summary>Lo mismo en la cabeza chica ya espejada (16 × 16).</summary>
    internal Action<char[][]>? SmallOverlay { get; init; }
    /// <summary>De espaldas (32 × 48); null: no tiene esa vista y lo que la usa se hace de frente.</summary>
    internal Func<char[][]>? Back { get; init; }
    /// <summary>
    /// La tapa de la laptop al ejecutar (16 × 10, vista por detrás) con el logo de su IA; null: la del chibi, con el
    /// cangrejito de Claude, que es solo suyo.
    /// </summary>
    internal string[]? Lid { get; init; }
    /// <summary>Tiene las vistas de perfil para construir la casita (ChibiBuild); si no, «otra herramienta» es escribir.</summary>
    internal bool Builds { get; init; }
    /// <summary>La cabeza grande (filas 0 a 29) antes de cerrarla abajo: lo que sobra del cuerpo.</summary>
    internal Action<List<char[]>>? TrimHead { get; init; }
    /// <summary>La fila que cierra la cabeza grande abajo, bajo el cuello (32 de ancho).</summary>
    internal string HeadClose { get; init; } = "...KKKKKKKKKKKKKKKKKKKKKKKKKK...";

    /// <summary>Sus colores sobre los de la base, armados una vez.</summary>
    internal Dictionary<char, Color>? Palette { get; set; }

    public override string ToString() => Id;
}

static partial class ChibiSprite
{
    static ChibiSkin? _chibi;

    /// <summary>El chibi de Claude: pelo naranja y hoodie de cangrejo, con los ojitos de cangrejo sobre antenas.</summary>
    public static ChibiSkin Chibi => _chibi ??= new()
    {
        Id = Mascots.Chibi, Name = "Chibi", Body = Body, Head = Head, Armless = Armless, Colors = [],
        Accent = SetCrab, Back = BackBody, Builds = true,
        TrimHead = rows => rows[29] = [.. rows[29].Select(ch => ch == 'W' ? 'O' : ch)], // sin la punta de los cordones
    };

    /// <summary>Los personajes, en el orden de la lista de Ajustes.</summary>
    public static IReadOnlyList<ChibiSkin> Skins => [Chibi, Dokki, Gemma, Ciel];

    /// <summary>El personaje de ese id, o null si no es uno de ellos (la clásica, otra animación).</summary>
    public static ChibiSkin? SkinOf(string? id) => Skins.FirstOrDefault(s => s.Id == id);
}
