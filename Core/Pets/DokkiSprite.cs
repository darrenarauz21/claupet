using System.Drawing;

namespace Claupet;

/// <summary>
/// Dokki (DeepSeek): sirvienta ballena. Un rizo parado arriba (se mece en lugar de los ojitos de cangrejo), la cofia
/// de volantes en arco hasta las orejas con moños celestes, aletas de ballena por orejas con el borde blanco, pelo
/// azul muy largo y ondulado que se aclara hacia las puntas y cae por detrás hasta el ruedo, vestido azul marino con
/// cuello y delantal blancos y la ballena de DeepSeek, y la cola de ballena que sale de la falda hacia un costado.
/// </summary>
static partial class ChibiSprite
{
    static ChibiSkin? _dokki;

    public static ChibiSkin Dokki => _dokki ??= new()
    {
        Id = "dokki", Name = "Dokki", Body = DokkiArt.Body, Head = DokkiArt.Head, Armless = DokkiArt.Armless,
        Colors = DokkiArt.Colors, Accent = DokkiArt.Curl, Overlay = DokkiArt.Overlay, SmallOverlay = DokkiArt.SmallOverlay,
        Behind = DokkiArt.Behind, Lid = DokkiArt.Lid,
        ArmTint = new() { ['o'] = 'W' }, // el puño de la manga, blanco
        HeadClose = "..KKKKKKKKKKKKKKKKKKKKKKKKKKKK..",
    };

    static class DokkiArt
    {
        public static readonly Dictionary<char, Color> Colors = new()
        {
            ['K'] = Hex(0x1A1736), // contorno
            ['O'] = Hex(0x262C66), // vestido
            ['o'] = Hex(0x1A1F48), // vestido, sombra
            ['h'] = Hex(0x3D4794), // vestido, brillo
            ['R'] = Hex(0x2F3F9E), // pelo
            ['r'] = Hex(0x212D74), // pelo, sombra
            ['y'] = Hex(0x5C78DA), // pelo, brillo
            ['i'] = Hex(0x3B63C8), // pelo, más abajo
            ['u'] = Hex(0x5490E6), // pelo, las puntas
            ['S'] = Hex(0xFDE8DA), // piel
            ['s'] = Hex(0xF0C8B4), // piel, sombra
            ['b'] = Hex(0xF6AAA6), // rubor
            ['a'] = Hex(0x223D9A), // iris oscuro
            ['A'] = Hex(0x6DB7F5), // iris claro
            ['m'] = Hex(0x8E3B50), // boca
            ['w'] = Hex(0xC9D3EE), // volantes, sombra
            ['x'] = Hex(0x3C58B8), // aletas y cola
            ['X'] = Hex(0x9EC2F2), // aletas y cola, claro
            ['j'] = Hex(0x6FB9F5), // moños
            ['l'] = Hex(0x3479D4), // moños, sombra
        };

        // Mitad izquierda del cuerpo, 16 × 48. El pelo largo, las aletas y la cola van por detrás (Behind).
        public static readonly string[] Body =
        [
            "................", // 0  (el rizo va aparte: Curl)
            "................",
            "................",
            "...........K.K.K", //    puntas de los volantes
            ".........KKWKWKW",
            "........KWWWWWWW", // 5  cofia en arco
            "......KKWwWwWwWw",
            ".....KWWKKKKKKKK",
            "....KWWwKRRRRRRR",
            "...KWWwKRRRRRRyy",
            "...KWwKRRRRRRyRR", // 10
            "..KWWwKRRRRRRRRR",
            ".KjjKRRRRRRRRRRR", //    moño en la punta de la cofia
            "KjWjjKRRRRRRrRRR",
            "KljjKRRRRRRrRRRR",
            ".KKKRRRRRRRrRRRR", // 15
            "..KRRRRRRRrRRRRR", //    las aletas asoman por detrás
            "..KRRRRRRrRRRrRR",
            "..KRRRRRrSSrRrSS", //    flequillo
            "..KRRRRrKKKKKSSS",
            "..KRRRRrKaWWaSSS", // 20 ojos
            ".KRRRRRrSaWKaSSS",
            ".KRRRRRrSaKKaSSS",
            ".KRRRRrrSAKKASSS",
            ".KRRRRrrSSAASSSS",
            ".KRRRRrRbbbSSSmS", // 25 rubor y boca
            ".KRRRiRRSSSSSSSm",
            ".KRRiRRRRSSSSSSS",
            "..KRiRrRRRRKssss", //    mentón y cuello
            "...KKKwWWWWWKKKK", //    cuello blanco y cinta
            "....KWWWWWWWWKjK", // 30
            "....KwWwWwWKOOOW", //    el peto del delantal
            "..KOOOKOOOOOOOWW",
            "..KOOOKoOOOOOOWW",
            ".KOOOKoOOOOOOWWW",
            ".KWwWKoOOOOOWWWW", // 35 puño blanco; el delantal se abre
            ".KSSSKoOOOWWWWWW", //    mano
            ".KSSKKoOOWwWWWWW",
            "..KKKoOOWWWWWWWW",
            "...KoOOOWWWWWWWW",
            "...KYYYYKWwWwWwW", // 40 bordado dorado del ruedo y encaje del delantal
            "...KwWwWwWwWwWwW", //    enagua de volantes
            "...........KWWWK", //    medias con volante
            "...........KWWWK",
            "...........KwWwK",
            "...........KWWWK", // 45
            "..........KKKKK.", //    zapatos
            "..........KKKKK.",
        ];

        // Mitad izquierda de la cabeza chica, 8 × 16.
        public static readonly string[] Head =
        [
            "........", // 0
            "......KK",
            "....KKWW", //    cofia
            "...KWWww",
            "..KWwKKK",
            ".KWKRRRR", // 5
            ".KWKRRyR",
            "KjjKRRRR", //    moño
            "KljKRRRR",
            "KxKRRRrR", //    aleta
            "KRRRrSRS", // 10 flequillo
            "KRRRSWKS", //    ojos
            "KRRRSKKS",
            "KRiRbSSs", //    rubor
            "KiuRKSSS",
            ".KKKKKKK", // 15
        ];

        // Torso sin brazo (mitad izquierda, filas 31 a 38).
        public static readonly string[] Armless =
        [
            "....KwWwWwWKOOOW",
            "....KOoOOOOOOOWW",
            "....KOoOOOOOOOWW",
            "....KOoOOOOOOWWW",
            "....KOoOOOOOWWWW",
            "....KOoOOOWWWWWW",
            "....KOoOOWwWWWWW",
            "...KKoOOWWWWWWWW",
        ];

        // El rizo de arriba (8 × 5 desde la columna 12, fila 0): inclinado a un lado, derecho o al otro, y caído
        // con los ojos cerrados.
        static readonly Dictionary<int, string[]> Loop = new()
        {
            [0] = [".KKKK...", "KRRRRK..", "KRKKRK..", ".KKRK...", "...KKR.."],
            [1] = ["..KKKK..", ".KRRRRK.", ".KRKKRK.", "..KKRK..", "....KR.."],
            [2] = ["...KKKK.", "..KRRRRK", "..KRKKRK", "...KRKK.", "....RK.."],
        };
        static readonly string[] Drooped = ["........", "........", "..KKKK..", ".KRRRRK.", "..KKKRR."];

        public static void Curl(char[][] g, (int X, int Y)? look) => Stamp(g, 12, 0, look is { } at ? Loop[at.X] : Drooped);

        // El pelo largo por detrás, de las orejas al ruedo: más ancho que el cuerpo, en mechones ondulados que se
        // aclaran hacia abajo y terminan a distinto largo. El costado izquierdo va de 4 casillas a la izquierda del
        // cuerpo a la columna 8; el medio queda detrás del cuerpo (se ve entre las piernas) y el derecho es el izquierdo
        // espejado. Desde la fila 18.
        static readonly string[] ManeSide =
        [
            ".....KRRRRRRR", // 18
            "....KRRRRRRRR",
            "....KRRrRRRRR", // 20
            "....KRRrRRRRR",
            "...KRRRrRRRRR",
            "...KRRrRRRRRR",
            "...KRRrRRRRRR",
            "..KRRRrRRRRRR", // 25
            "..KRRrRRRRRRR",
            "..KRRrRRRrRRR",
            ".KRRRrRRRrRRR",
            ".KRRrRRRRrRRR",
            ".KRRrRRRrRRRR", // 30
            "KRRRrRRRrRRRR",
            "KRRrRRRrRRRRR",
            "KRyrRRRrRRRiR",
            "KRRrRRiRRRiiR",
            ".KRriRiiRriiR", // 35
            "KRirriiirRiiR",
            "KiiriiiriiiiR",
            "KiirKiiriiiui",
            ".KiuKiuriiuui",
            "KiuuKuuKiuuui", // 40
            "KuuK.KuKuuuuu",
            ".KuK.KuK.Kuuu",
            "..K...K...KKK",
        ];
        const int ManeTop = 18;

        static readonly string[] Mane =
        [
            .. ManeSide.Select((side, i) =>
            {
                var y = ManeTop + i;
                var middle = new string(i == ManeSide.Length - 1 ? 'K' : y < 33 ? 'R' : y < 38 ? 'i' : 'u', 14);
                return side + middle + Reverse(side);
            }),
        ];

        // La aleta izquierda a la altura de la oreja (desde 4 casillas a la izquierda del cuerpo, fila 15): azul arriba
        // y el borde de abajo blanco y esponjoso. La derecha es la misma, espejada.
        static readonly string[] Fin =
        [
            "..KKKKK.",
            ".KxxxxxK",
            "KxxxxxxK",
            "KxxxxXxK",
            "KXXXXXXK",
            "KWXWXWWK",
            ".KWKWKK.",
        ];

        // La punta de la cola, apoyada en el piso junto al pie derecho (desde la columna 22 y la fila 41 del cuerpo):
        // baja por detrás del pelo y abre la aleta de ballena en dos lóbulos a los lados, con la muesca al medio.
        static readonly string[] Tail =
        [
            ".....KxK.....",
            ".....KxK.....",
            ".....KxK.....",
            "..KKKxxxKKK..",
            ".KXXxxKxxXXK.",
            "KXXXKK.KKXXXK",
            ".KKK.....KKK.",
        ];

        public static void Behind(char[][] canvas, int x, int y)
        {
            Stamp(canvas, x - 4, y + ManeTop, Mane);
            Stamp(canvas, x - 4, y + 15, Fin);
            Stamp(canvas, x + 28, y + 15, [.. Fin.Select(Reverse)]);
            Stamp(canvas, x + 22, y + 41, Tail);
        }

        // La tapa de la laptop con la ballena de DeepSeek.
        public static readonly string[] Lid =
        [
            "KKKKKKKKKKKKKKKK",
            "KEEEEEEEEEEEEEEK",
            "KEEEEEEEEEExxEEK",
            "KEEEExxxxxEExEEK",
            "KEEExxxxxxxxEEEK",
            "KEEExWxxxxxxEEEK",
            "KEEEExXXXXxEEEEK",
            "KEEEEEEEEEEEEEEK",
            "KeeeeeeeeeeeeeeK",
            "KKKKKKKKKKKKKKKK",
        ];

        /// <summary>La ballena de DeepSeek en el delantal: mirando a la izquierda, con el ojo, la panza clara y la cola arriba.</summary>
        public static void Overlay(char[][] g) =>
            Stamp(g, 11, 36, ["........KK", ".KKKKK.KxK", "KxxxxxKxK.", "KxWxxxxxK.", ".KXXXXKK.."]);

        public static void SmallOverlay(char[][] g) => Stamp(g, 7, 0, ["KKK", "K.K", ".KR"]);
    }
}
