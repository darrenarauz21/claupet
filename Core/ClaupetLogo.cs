using System.Drawing;

namespace Claupet;

/// <summary>
/// El ícono de Claupet, la app (no es una mascota): una figura de 9 × 9 cuadritos, con el cuerpo de acento, la cara
/// clara, dos ojos y la base más oscura. Es el del .exe, los accesos directos, las ventanas y los paquetes de Linux y
/// macOS (herramientas/empaquetar.py lo repite en Python; si cambia aquí, cambia allá y en app.ico).
/// </summary>
static class ClaupetLogo
{
    public const int Size = 9;

    static readonly Color Body = Color.FromArgb(0xD9, 0x77, 0x57);
    static readonly Color Face = Color.FromArgb(0xF4, 0xE3, 0xD3);
    static readonly Color Eye = Color.FromArgb(0x1F, 0x1E, 0x1D);
    static readonly Color Base = Color.FromArgb(0xB4, 0x53, 0x2F);

    /// <summary>Los cuadritos en orden: los de después van encima (la cara sobre el cuerpo, los ojos sobre la cara).</summary>
    public static void Paint(PixelFill fill)
    {
        fill(Body, 1, 1, 7, 6);
        fill(Body, 0, 2, 1, 3);
        fill(Body, 8, 2, 1, 3);
        fill(Face, 2, 3, 5, 3);
        fill(Eye, 3, 4, 1, 1);
        fill(Eye, 5, 4, 1, 1);
        fill(Base, 2, 7, 5, 2);
    }
}
