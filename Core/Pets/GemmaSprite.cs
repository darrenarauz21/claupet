using System.Drawing;

namespace Claupet;

/// <summary>
/// Gemma (Gemini): tsundere de coletas altas, atadas a los costados de la coronilla con la estrella de cuatro puntas de
/// Gemini (azul, violeta y rosa), que suben, se separan de la cabeza y caen por fuera hasta la cadera. Pelo rosa
/// orquídea que se aclara hacia las puntas y ojos azules. Uniforme de marinera (blusa blanca, cuello azul, moño violeta
/// y falda tableada). En lugar de los ojitos de cangrejo, sus cejas: fruncidas casi siempre, arriba cuando se sorprende
/// y relajadas con los ojos cerrados. La boca, en puchero.
/// </summary>
static partial class ChibiSprite
{
    static ChibiSkin? _gemma;

    public static ChibiSkin Gemma => _gemma ??= new()
    {
        Id = "gemma", Name = "Gemma", Body = GemmaArt.Body, Head = GemmaArt.Head, Armless = GemmaArt.Armless,
        Colors = GemmaArt.Colors, Accent = GemmaArt.Brows, Behind = GemmaArt.Tails, Lid = GemmaArt.Lid,
        ArmTint = new() { ['o'] = 'U' }, // el puño de la manga, azul como el cuello
        HeadClose = "....KKKKKKKKKKKKKKKKKKKKKKKK....",
    };

    static class GemmaArt
    {
        public static readonly Dictionary<char, Color> Colors = new()
        {
            ['K'] = Hex(0x2A1A33), // contorno
            ['O'] = Hex(0xF7F6FC), // blusa
            ['o'] = Hex(0xCDD0EA), // blusa, sombra
            ['h'] = Hex(0xFFFFFF), // blusa, brillo
            ['R'] = Hex(0xD67BC4), // pelo
            ['r'] = Hex(0xA8559D), // pelo, sombra
            ['y'] = Hex(0xF3B4E3), // pelo, brillo
            ['i'] = Hex(0xEC88B6), // pelo, hacia las puntas
            ['z'] = Hex(0xF7AAC6), // pelo, las puntas
            ['S'] = Hex(0xFCE6D9), // piel
            ['s'] = Hex(0xEEC3AE), // piel, sombra
            ['b'] = Hex(0xF593A3), // rubor
            ['a'] = Hex(0x3B3AA6), // iris oscuro
            ['A'] = Hex(0x8DA2F8), // iris claro
            ['m'] = Hex(0x8E3550), // boca
            ['U'] = Hex(0x3D63DE), // cuello y estrella, azul
            ['H'] = Hex(0x2A43A6), // cuello, sombra
            ['x'] = Hex(0x8AB4F8), // estrella, celeste
            ['j'] = Hex(0x2DB36A), // estrella, verde
            ['Q'] = Hex(0x9A5ADC), // moño, violeta
            ['k'] = Hex(0xE86A8E), // moño, rosa
            ['I'] = Hex(0x2C3A92), // falda
            ['X'] = Hex(0x1F2A6E), // falda, tablas
        };

        // Mitad izquierda del cuerpo, 16 × 48: la cabeza más angosta, para que las coletas caigan separadas (Tails).
        public static readonly string[] Body =
        [
            "................", // 0  (las cejas van aparte: Brows)
            "................",
            "................",
            "................",
            "................",
            "...........KKKKK", // 5  coronilla
            ".........KKRRRRR",
            "...K...KKRRRRRyy", //    la estrella de Gemini que ata la coleta: azul arriba y verde abajo
            "..KUK.KRRRRRyyRR",
            ".KUUxKRRRRRRRRRR",
            "KUUWjjKRRRRRRRRR", // 10
            ".KxjjKRRRRRrRRRR",
            "..KjKKRRRRrRRRRR",
            "...KKRRRRRrRRRRR",
            "....KRRRRrRRRRRR",
            "...KRRRRrRRRRRRR", // 15
            "...KRRRrRSSSSRRR", //    frente: un mechón al medio
            "...KRRrRSSSSSSrR",
            "...KRRrSSSSSSSSr",
            "...KRRRrKKKKKSSS",
            "...KRRRrKaWWaSSS", // 20 ojos
            "...KRRRrSaWKaSSS",
            "...KRRRrSaKKaSSS",
            "...KRRRrSAKKASSS",
            "...KRRRrSSAASSSS",
            "...KRRRrbbbSSSSm", // 25 rubor y puchero
            "...KRRRRSSSSSSmS",
            "....KRRRRSSSSSSS",
            ".....KRrRRRKssss", //    mentón y cuello
            ".....KUUUUUKssss", //    cuello de marinera
            "....KUUWWWWUKsss", // 30
            "...KUUUUUUUUUKQK", //    moño
            "..KOOOKHUUUUKQkQ",
            "..KOOOKoOHUUKOKQ",
            ".KOOOKoOOOHHOOOK",
            ".KoooKoOOOOOOOOO", // 35 puño de la manga
            ".KSSSKoOOOOOOOOO", //    mano
            ".KSSKKIIIIIIIIII", //    falda tableada
            "..KKIIXIIIXIIIXI",
            "...KIIXIIIXIIIXI",
            "...KIIXIIIXIIIXI", // 40
            "...KKKKKKKKKKKKK",
            "...........KSSSK", //    piernas
            "...........KSSSK",
            "...........KWWWK", //    medias
            "...........KWWWK", // 45
            "..........KKKKK.", //    zapatos
            "..........KKKKK.",
        ];

        // Mitad izquierda de la cabeza chica, 8 × 16: las coletas altas a los costados.
        public static readonly string[] Head =
        [
            "........", // 0
            ".KK.....",
            "KRRK..KK",
            "KRRKKKRR",
            "KUjKRRRR", //    estrella
            "KRRKRRRy", // 5
            "KRRKRRRR",
            "KRiKRRRR",
            "KiiKRRrR",
            "KiiKRrRR",
            "KizKrSSS", // 10 frente
            "KzzKSWKS", //    ojos
            ".KzKSKKS",
            ".KzKbSSs", //    rubor
            "..K.KSSS",
            "....KKKK", // 15
        ];

        // Torso sin brazo (mitad izquierda, filas 31 a 38).
        public static readonly string[] Armless =
        [
            "...KUUUUUUUUUKQK",
            "....KOHUUUUUKQkQ",
            "....KOoOHUUKOKQK",
            "....KOoOOOHHOOOK",
            "....KOoOOOOOOOOO",
            "....KOoOOOOOOOOO",
            "...KKIIIIIIIIIII",
            "..KKIIXIIIXIIIXI",
        ];

        // La coleta izquierda (8 de ancho, desde 4 casillas a la izquierda del cuerpo y la fila 3): sube desde la
        // estrella, se abre hacia afuera y cae separada de la cabeza hasta la cadera, cada vez más rosa. La derecha es
        // la misma, espejada.
        static readonly string[] Tail =
        [
            "...KKK..", // 3
            "..KRRRK.",
            ".KRRyRRK", // 5
            ".KRyRRRK",
            "KRRRRRK.",
            "KRRRRK..",
            "KRRRK...",
            "KRRrK...", // 10
            "KRRrK...",
            "KRrRK...",
            "KRrRK...",
            "KRrRRK..",
            "KRRrRK..", // 15
            "KRRrRK..",
            ".KRrRK..",
            ".KRRrK..",
            ".KRRrK..",
            ".KiRrK..", // 20
            "KRiRRK..",
            "KRiRiK..",
            "KiRiiK..",
            "KiiRiK..",
            "KiiiiK..", // 25
            ".KiiiK..",
            ".KiiiK..",
            ".KizK...",
            "KiizK...",
            "KizzK...", // 30
            "KzizK...",
            ".KzzK...",
            ".KzzK...",
            ".KzK....",
            "..KzK...", // 35
            "..KzK...",
            "...K....",
        ];
        const int TailTop = 3;

        // La tapa de la laptop con la estrella de Gemini.
        public static readonly string[] Lid =
        [
            "KKKKKKKKKKKKKKKK",
            "KEEEEEEEEEEEEEEK",
            "KEEEEEEUEEEEEEEK",
            "KEEEEEUUxEEEEEEK",
            "KEEEUUUWjjjEEEEK",
            "KEEEEExjjEEEEEEK",
            "KEEEEEEjEEEEEEEK",
            "KEEEEEEEEEEEEEEK",
            "KeeeeeeeeeeeeeeK",
            "KKKKKKKKKKKKKKKK",
        ];

        public static void Tails(char[][] canvas, int x, int y)
        {
            Stamp(canvas, x - 4, y + TailTop, Tail);
            Stamp(canvas, x + 28, y + TailTop, [.. Tail.Select(Reverse)]);
        }

        /// <summary>Las cejas sobre la frente: fruncidas (tsundere), arriba al mirar hacia arriba y relajadas con los ojos cerrados.</summary>
        public static void Brows(char[][] g, (int X, int Y)? look)
        {
            string[] shape = look switch
            {
                null => ["....", "KKKK"],
                { Y: 0 } => ["KKKK", "...."],
                _ => ["KK..", "..KK"],
            };
            Stamp(g, 9, 17, shape);
            Stamp(g, 19, 17, [.. shape.Select(Reverse)]);
        }
    }
}
