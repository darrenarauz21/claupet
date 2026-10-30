using System.Drawing;

namespace Claupet;

/// <summary>
/// Ciel (Codex): pelo corto, blanco con las puntas verdes, y un gancho con el logo de OpenAI que destella (en lugar de
/// los ojitos de cangrejo, el brillo corre por el gancho según hacia dónde mira). Chaqueta negra con puños y corbata
/// verdes, camisa blanca, falda gris oscuro y medias.
/// </summary>
static partial class ChibiSprite
{
    static ChibiSkin? _ciel;

    public static ChibiSkin Ciel => _ciel ??= new()
    {
        Id = "ciel", Name = "Ciel", Body = CielArt.Body, Head = CielArt.Head, Armless = CielArt.Armless,
        Colors = CielArt.Colors, Accent = CielArt.Glint, Overlay = CielArt.Clip, SmallOverlay = CielArt.SmallClip, Lid = CielArt.Lid,
        ArmTint = new() { ['o'] = 'u' }, // el puño de la manga, verde
        HeadClose = "....KKKKKKKKKKKKKKKKKKKKKKKK....",
    };

    static class CielArt
    {
        public static readonly Dictionary<char, Color> Colors = new()
        {
            ['K'] = Hex(0x1E2226), // contorno
            ['O'] = Hex(0x2C3038), // chaqueta
            ['o'] = Hex(0x1C1F25), // chaqueta, sombra
            ['h'] = Hex(0x474D59), // chaqueta, brillo
            ['R'] = Hex(0xEEF3F1), // pelo
            ['r'] = Hex(0xC2CFCB), // pelo, sombra
            ['y'] = Hex(0xFFFFFF), // pelo, brillo
            ['i'] = Hex(0x9EE2C6), // pelo, hacia las puntas
            ['u'] = Hex(0x10A37F), // verde de OpenAI: puntas, puños y corbata
            ['l'] = Hex(0x0B7A5F), // verde, sombra
            ['S'] = Hex(0xFCE6D9), // piel
            ['s'] = Hex(0xEEC4B0), // piel, sombra
            ['b'] = Hex(0xF4A3A0), // rubor
            ['a'] = Hex(0x0D6B55), // iris oscuro
            ['A'] = Hex(0x46D8A6), // iris claro
            ['m'] = Hex(0x8A3A48), // boca
            ['I'] = Hex(0x3A3F48), // falda y medias
            ['X'] = Hex(0x2A2E35), // falda, sombra
        };

        // Mitad izquierda del cuerpo, 16 × 48: melena corta hasta el mentón.
        public static readonly string[] Body =
        [
            "................", // 0  (el brillo del gancho va aparte: Glint)
            "................",
            "................",
            "................",
            "................",
            "................", // 5
            "..........KKKKKK", //    coronilla
            "........KKRRRRRR",
            "......KKRRRRRRyy",
            ".....KRRRRRRRyRR",
            "....KRRRRRRRRRRR", // 10
            "...KRRRRRRRRRRRR",
            "...KRRRRRRrRRRRR",
            "..KRRRRRRrRRRRRR",
            "..KRRRRRrRRRRRrR",
            "..KRRRRRrRRRRrRR", // 15
            ".KRRRRRrRRRRrRRR",
            ".KRRRRrRRRRrRRrR",
            ".KRRRRrRRrSSrRrS", //    flequillo
            ".KRRRRRrKKKKKSSS",
            ".KRRRRRrKaWWaSSS", // 20 ojos
            ".KRRRRRrSaWKaSSS",
            ".KRRRRRrSaKKaSSS",
            ".KRRRRRrSAKKASSS",
            ".KRRRRirSSAASSSS",
            ".KRRRiirbbbSSSmS", // 25 rubor y boca
            ".KRiiiuiSSSSSSSm", //    las puntas verdes
            ".KiuuluiSSSSSSSS",
            "..KuKKuuKKKKssss", //    mentón y cuello
            "...KK.KOOOOOKWWW", //    cuello de la camisa
            "....KOOOOOOhKWWu", // 30 corbata
            "...KOOOOOOOhKWWu",
            "..KOOOKOOOOhKWul",
            "..KOOOKoOOOhKWuu",
            ".KOOOKoOOOOhKWWu",
            ".KuuuKoOOOOhKWWW", // 35 puño verde de la manga
            ".KSSSKoOOOOOKKKK", //    mano; el ruedo de la chaqueta
            ".KSSKKIIIIIIIIII", //    falda
            "..KKIIIIIIIIIIII",
            "...KIIXIIIIIXIII",
            "...KIIXIIIIIXIII", // 40
            "...KKKKKKKKKKKKK",
            "...........KSSSK", //    piernas
            "...........KSSSK",
            "...........KIIIK", //    medias
            "...........KIIIK", // 45
            "..........KKKKK.", //    zapatos
            "..........KKKKK.",
        ];

        // Mitad izquierda de la cabeza chica, 8 × 16.
        public static readonly string[] Head =
        [
            "........", // 0
            "........",
            "......KK",
            "....KKRR",
            "...KRRRR",
            "..KRRRRy", // 5
            ".KRRRRRR",
            ".KRRRRRR",
            "KRRRRrRR",
            "KRRRrRRR",
            "KRRRrSRS", // 10 flequillo
            "KRRRSWKS", //    ojos
            "KRRiSKKS",
            "KRiubSSs", //    rubor y puntas verdes
            ".KuuKSSS",
            "..KKKKKK", // 15
        ];

        // Torso sin brazo (mitad izquierda, filas 31 a 38).
        public static readonly string[] Armless =
        [
            "...KOOOOOOOhKWWu",
            "....KOoOOOOhKWul",
            "....KOoOOOOhKWuu",
            "....KOoOOOOhKWWu",
            "....KOoOOOOhKWWW",
            "....KOoOOOOOKKKK",
            "...KKIIIIIIIIIII",
            "..KKIIIIIIIIIIII",
        ];

        // El gancho con el logo de OpenAI (7 × 7), del lado derecho de la cabeza.
        static readonly string[] Logo =
        [
            ".KKKKK.",
            "KuKuKuK",
            "KKuuuKK",
            "KuuKuuK",
            "KKuuuKK",
            "KuKuKuK",
            ".KKKKK.",
        ];
        const int ClipX = 22, ClipY = 9;

        // La tapa de la laptop con el mismo logo.
        public static readonly string[] Lid =
        [
            "KKKKKKKKKKKKKKKK",
            "KEEEEKKKKKEEEEEK",
            "KEEEKuKuKuKEEEEK",
            "KEEEKKuuuKKEEEEK",
            "KEEEKuuKuuKEEEEK",
            "KEEEKKuuuKKEEEEK",
            "KEEEKuKuKuKEEEEK",
            "KEEEEKKKKKEEEEEK",
            "KeeeeeeeeeeeeeeK",
            "KKKKKKKKKKKKKKKK",
        ];

        public static void Clip(char[][] g) => Stamp(g, ClipX, ClipY, Logo);

        public static void SmallClip(char[][] g) => Stamp(g, 11, 4, [".KKK.", "KuKuK", "KKuKK", "KuKuK", ".KKK."]);

        /// <summary>El brillo del gancho: arriba a la izquierda, al medio o a la derecha según hacia dónde mira; nada con los ojos cerrados.</summary>
        public static void Glint(char[][] g, (int X, int Y)? look)
        {
            if (look is not { } at) return;
            Stamp(g, ClipX + 1 + at.X * 2, ClipY + 1 + at.Y, ["W"]);
            if (at.Y == 0) Stamp(g, ClipX + 5, ClipY - 2, [".Y.", "YWY", ".Y."]);
        }
    }
}
