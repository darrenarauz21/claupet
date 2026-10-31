using System.Text;

namespace Claupet;

/// <summary>Qué es cada bloque de una terminal de comandos: un comando con su salida, texto suelto del shell o un aviso.</summary>
enum ShellBlockKind { Command, Output, Notice }

/// <summary>
/// Un bloque de la terminal de comandos: el comando, lo que mostró (solo las últimas líneas), cómo terminó y dónde corrió.
/// ShellTranscript lo cambia al llegar la salida; la vista lo vuelve a pintar.
/// </summary>
sealed class ShellBlock
{
    public required ShellBlockKind Kind { get; init; }
    /// <summary>El comando; en un aviso, el texto del aviso.</summary>
    public string Command { get; init; } = "";
    /// <summary>La carpeta en que corrió (la que avisó el shell antes del comando).</summary>
    public string? Cwd { get; init; }
    /// <summary>Las últimas líneas de la salida (hasta ShellTranscript.MaxLines), sin colores.</summary>
    public IReadOnlyList<string> Lines { get; set; } = [];
    /// <summary>Las líneas del principio que ya no se guardan.</summary>
    public int Hidden { get; set; }
    /// <summary>El código de salida; null mientras corre o si el shell no lo dijo.</summary>
    public int? ExitCode { get; set; }
    public bool Running { get; set; }
    public DateTime Started { get; init; } = DateTime.Now;
    public TimeSpan? Duration { get; set; }

    public string Output => string.Join("\n", Lines);
    /// <summary>Terminó con error. Cortado con Ctrl+C o con el paginador cerrado antes del final no cuenta (lo pidió la persona).</summary>
    public bool Failed => ExitCode is { } code && code != 0 && !Interrupted && !PagerClosed;
    /// <summary>Se cerró el paginador antes del final (q en less): git y otros terminan con 141 (SIGPIPE).</summary>
    public bool PagerClosed => ExitCode == 141;
    /// <summary>Lo cortó Ctrl+C: Windows le da STATUS_CONTROL_C_EXIT (0xC000013A).</summary>
    public bool Interrupted => ExitCode == unchecked((int)0xC000013A);
}

/// <summary>
/// La salida de un shell en una consola de mentira (ConPTY o una pty) vuelta bloques, como en Warp: el shell marca con
/// OSC 133 (o el 633 de VS Code) dónde empieza el prompt (A), el comando (B), su salida (C) y dónde termina (D;código).
/// Como ConPTY pinta una pantalla (mueve el cursor, borra, vuelve a pintar), aquí hay una pantalla chica: cada fila sabe
/// a qué bloque pertenece y, al salir por arriba, pasa a la salida de ese bloque. No abre procesos: se prueba con texto.
/// </summary>
sealed class ShellTranscript
{
    /// <summary>Cuántas líneas de la salida de un comando se guardan (las últimas).</summary>
    public const int MaxLines = 400;

    /// <summary>Una fila de la pantalla: su texto, a qué bloque pertenece y desde qué columna (antes está el comando).</summary>
    sealed class Row
    {
        public readonly StringBuilder Text = new();
        public ShellBlock? Owner;
        public int From;
        /// <summary>Siguió en la fila de abajo porque no cabía (no hubo un salto de línea).</summary>
        public bool Wrapped;

        public void Clear()
        {
            Text.Clear();
            Owner = null;
            From = 0;
            Wrapped = false;
        }
    }

    /// <summary>En qué parte de la vuelta del shell va: esperando, pintando el prompt, escribiendo el comando o su salida.</summary>
    enum Phase { Idle, Prompt, Input, Output }

    enum Parse { Ground, Escape, Charset, Csi, Osc, OscEscape, Ignore, IgnoreEscape }

    readonly List<ShellBlock> _blocks = [];
    readonly List<Row> _screen = [];
    readonly StringBuilder _sequence = new();
    /// <summary>Lo que ya salió por arriba de la pantalla, del bloque que corre.</summary>
    readonly List<string> _frozen = [];
    /// <summary>La última línea que salió sigue en la primera fila del bloque (no cabía).</summary>
    bool _frozenOpen;
    int _frozenDropped;
    Parse _parse;
    Phase _phase;
    readonly int _cols;
    int _row, _col;
    /// <summary>La fila donde empieza la salida del comando que corre: las de arriba (el prompt, el eco) no son de él.</summary>
    int _outputTop;
    /// <summary>Dónde empezó el comando (la marca B), para tomarlo del eco si no se envió desde la app.</summary>
    int _inputRow = -1, _inputCol;
    /// <summary>Se vio la marca B desde el último prompt: lo que se escribe ya es el comando.</summary>
    bool _inputMarked;
    /// <summary>
    /// El shell marca con C dónde empieza la salida (PowerShell y bash de la Terminal lo hacen). Desde entonces no se
    /// adivina por el salto de línea: ConPTY manda saltos sueltos para correr la pantalla cuando el eco de un comando
    /// largo se parte en la última fila, y la parte de abajo del eco quedaba como salida.
    /// </summary>
    bool _marksOutput;
    ShellBlock? _running;

    public ShellTranscript(int cols = 120, int rows = 40)
    {
        _cols = Math.Max(10, cols);
        for (var i = 0; i < Math.Max(2, rows); i++) _screen.Add(new Row());
    }

    public IReadOnlyList<ShellBlock> Blocks => _blocks;
    /// <summary>La carpeta actual del shell, si la avisó (OSC 7 o 633;P;Cwd=).</summary>
    public string? Cwd { get; private set; }
    /// <summary>El título que pidió el programa (OSC 0 o 2).</summary>
    public string? Title { get; private set; }
    /// <summary>Hay un comando corriendo.</summary>
    public bool Busy => _running is not null;
    /// <summary>El shell ya mostró su primer prompt con las marcas (la integración funciona).</summary>
    public bool Ready { get; private set; }
    /// <summary>Un programa pidió la pantalla alternativa (vim, htop…): lo que pinta ahí no se muestra.</summary>
    public bool AltScreen { get; private set; }

    /// <summary>Se envió un comando: su bloque, que corre hasta la marca D.</summary>
    public ShellBlock Begin(string command)
    {
        var block = new ShellBlock { Kind = ShellBlockKind.Command, Command = command, Cwd = Cwd, Running = true };
        _blocks.Add(block);
        StartRunning(block);
        // Lo que sigue es el eco del comando; la salida empieza con la marca C (o, sin ella, en la línea siguiente).
        _phase = Phase.Input;
        return block;
    }

    /// <summary>Un aviso de la app (la sesión terminó, no hay shell…).</summary>
    public ShellBlock Notice(string text)
    {
        var block = new ShellBlock { Kind = ShellBlockKind.Notice, Command = text };
        _blocks.Add(block);
        return block;
    }

    /// <summary>Se cerró el shell: el comando que corría queda como estaba, sin código. Devuelve ese bloque, si había uno.</summary>
    public ShellBlock? Stop()
    {
        if (_running is not { } block) return null;
        Finish(block, null);
        return block;
    }

    /// <summary>Lee un pedazo de la salida (puede cortar una secuencia o una marca a la mitad). Devuelve los bloques que cambiaron.</summary>
    public IReadOnlyList<ShellBlock> Apply(string chunk)
    {
        var changed = new List<ShellBlock>();
        foreach (var c in chunk) Feed(c, changed);
        if (_running is { } running && Refresh(running)) Touch(running, changed);
        return changed;
    }

    // ── Las secuencias ──────────────────────────────────────────────────────────────────────

    void Feed(char c, List<ShellBlock> changed)
    {
        switch (_parse)
        {
            case Parse.Ground:
                Ground(c);
                break;
            case Parse.Escape:
                _sequence.Clear();
                _parse = c switch
                {
                    '[' => Parse.Csi,
                    ']' => Parse.Osc,
                    // DCS, SOS, PM y APC: se saltan hasta su fin.
                    'P' or 'X' or '^' or '_' => Parse.Ignore,
                    // La elección de juegos de caracteres lleva una letra más.
                    '(' or ')' or '*' or '+' or '-' or '.' or '/' or '#' or '%' or ' ' => Parse.Charset,
                    _ => Parse.Ground,
                };
                if (c == 'M') ReverseIndex();
                break;
            case Parse.Charset:
                _parse = Parse.Ground;
                break;
            case Parse.Csi:
                if (c == '\x1b') _parse = Parse.Escape;
                else if (c is >= '@' and <= '~')
                {
                    _parse = Parse.Ground;
                    Csi(_sequence.ToString(), c, changed);
                }
                else if (_sequence.Length < 64) _sequence.Append(c);
                break;
            case Parse.Osc:
                if (c == '\a') EndOsc(changed);
                else if (c == '\x1b') _parse = Parse.OscEscape;
                else if (_sequence.Length < 4096) _sequence.Append(c);
                break;
            case Parse.OscEscape:
                // ESC \ (ST) la cierra; otra letra después de ESC empieza otra secuencia.
                if (c == '\\') EndOsc(changed);
                else
                {
                    _parse = Parse.Escape;
                    Feed(c, changed);
                }
                break;
            case Parse.Ignore:
                if (c == '\a') _parse = Parse.Ground;
                else if (c == '\x1b') _parse = Parse.IgnoreEscape;
                break;
            case Parse.IgnoreEscape:
                _parse = c == '\\' ? Parse.Ground : Parse.Ignore;
                break;
        }
    }

    void Ground(char c)
    {
        switch (c)
        {
            case '\x1b':
                _parse = Parse.Escape;
                break;
            case '\r':
                _col = 0;
                break;
            case '\n' or '\v' or '\f':
                LineFeed();
                break;
            case '\b':
                _col = Math.Max(0, Math.Min(_col, _cols - 1) - 1);
                break;
            case '\t':
                var next = Math.Min(_cols - 1, (_col / 8 + 1) * 8);
                while (_col < next) Put(' ');
                break;
            case < ' ' or '\x7f':
                break;
            default:
                Put(c);
                break;
        }
    }

    void Put(char c)
    {
        if (AltScreen) return;
        // Al llegar al borde, el cursor espera ahí: recién la letra siguiente pasa a la otra fila.
        if (_col >= _cols)
        {
            // No es un salto de línea: un eco largo que sigue abajo todavía es el comando.
            _screen[_row].Wrapped = true;
            NextRow();
            Claim();
            _col = 0;
        }
        var row = _screen[_row];
        if (_phase == Phase.Output && _running is { } running && row.Owner != running && _row >= _outputTop)
        {
            row.Owner = running;
            row.From = 0;
        }
        var text = row.Text;
        if (_col > text.Length) text.Append(' ', _col - text.Length);
        if (_col < text.Length) text[_col] = c;
        else text.Append(c);
        _col++;
    }

    void LineFeed()
    {
        if (AltScreen) return;
        NextRow();
        // Sin la marca C, la salida empieza después del eco del comando.
        if (!_marksOutput && _phase == Phase.Input && _inputMarked && _running is not null)
        {
            _phase = Phase.Output;
            _outputTop = _row;
        }
        Claim();
    }

    /// <summary>Baja una fila; en la última, la pantalla sube.</summary>
    void NextRow()
    {
        if (_row < _screen.Count - 1) _row++;
        else ScrollUp(1);
    }

    /// <summary>La fila a la que se llega mientras corre un comando ya es de su salida (aunque quede vacía).</summary>
    void Claim()
    {
        var row = _screen[_row];
        if (_phase != Phase.Output || _running is not { } running || row.Owner == running || _row < _outputTop || row.Text.Length > 0) return;
        row.Owner = running;
        row.From = 0;
    }

    void ReverseIndex()
    {
        if (_row > 0)
        {
            _row--;
            return;
        }
        var last = _screen[^1];
        _screen.RemoveAt(_screen.Count - 1);
        last.Clear();
        _screen.Insert(0, last);
    }

    /// <summary>Las filas de arriba salen de la pantalla: lo del comando que corre pasa a su salida.</summary>
    void ScrollUp(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var top = _screen[0];
            _screen.RemoveAt(0);
            if (_running is { } running && top.Owner == running) Freeze(Cut(top), top.Wrapped);
            _inputRow = _inputRow >= 0 ? _inputRow - 1 : -1;
            _outputTop = Math.Max(0, _outputTop - 1);
            top.Clear();
            _screen.Add(top);
        }
    }

    void Freeze(string text, bool wrapped)
    {
        if (_frozenOpen && _frozen.Count > 0) _frozen[^1] += text;
        else _frozen.Add(text);
        if (!wrapped) _frozen[^1] = _frozen[^1].TrimEnd();
        _frozenOpen = wrapped;
        if (_frozen.Count > MaxLines + 1)
        {
            var extra = _frozen.Count - MaxLines - 1;
            _frozen.RemoveRange(0, extra);
            _frozenDropped += extra;
        }
    }

    static string Cut(Row row) => row.From >= row.Text.Length ? "" : row.Text.ToString(row.From, row.Text.Length - row.From);

    void Csi(string p, char final, List<ShellBlock> changed)
    {
        var priv = p.StartsWith('?');
        var args = (priv ? p[1..] : p).Split(';').Select(a => int.TryParse(a, out var n) ? n : 0).ToArray();
        int Arg(int i) => i < args.Length && args[i] > 0 ? args[i] : 1;
        int Mode() => args.Length > 0 ? args[0] : 0;
        var rows = _screen.Count;
        switch (final)
        {
            case 'h' or 'l' when priv:
                if (args.Any(mode => mode is 1049 or 1047 or 47)) Alternate(final == 'h', changed);
                break;
            case 'H' or 'f':
                _row = Math.Clamp(Arg(0) - 1, 0, rows - 1);
                _col = Math.Clamp(Arg(1) - 1, 0, _cols - 1);
                Claim();
                break;
            case 'A':
                _row = Math.Max(0, _row - Arg(0));
                break;
            case 'B' or 'e':
                _row = Math.Min(rows - 1, _row + Arg(0));
                Claim();
                break;
            case 'C' or 'a':
                _col = Math.Min(_cols - 1, _col + Arg(0));
                break;
            case 'D':
                _col = Math.Max(0, Math.Min(_col, _cols - 1) - Arg(0));
                break;
            case 'E':
                _row = Math.Min(rows - 1, _row + Arg(0));
                _col = 0;
                Claim();
                break;
            case 'F':
                _row = Math.Max(0, _row - Arg(0));
                _col = 0;
                break;
            case 'G' or '`':
                _col = Math.Clamp(Arg(0) - 1, 0, _cols - 1);
                break;
            case 'd':
                _row = Math.Clamp(Arg(0) - 1, 0, rows - 1);
                Claim();
                break;
            case 'K':
                EraseLine(_screen[_row], Mode());
                break;
            case 'J':
                if (Mode() == 0)
                {
                    EraseLine(_screen[_row], 0);
                    for (var r = _row + 1; r < rows; r++) _screen[r].Clear();
                }
                else if (Mode() == 1)
                {
                    for (var r = 0; r < _row; r++) _screen[r].Clear();
                    EraseLine(_screen[_row], 1);
                }
                else foreach (var row in _screen) row.Clear();
                break;
            case 'X':
                var line = _screen[_row].Text;
                for (var i = _col; i < Math.Min(line.Length, _col + Arg(0)); i++) line[i] = ' ';
                break;
            case 'P':
                var del = _screen[_row].Text;
                if (_col < del.Length) del.Remove(_col, Math.Min(Arg(0), del.Length - _col));
                break;
            case '@':
                var ins = _screen[_row].Text;
                if (_col < ins.Length) ins.Insert(_col, new string(' ', Arg(0)));
                break;
            case 'S':
                ScrollUp(Math.Min(rows, Arg(0)));
                break;
            case 'T' or 'L':
                for (var i = 0; i < Math.Min(rows, Arg(0)); i++)
                {
                    var last = _screen[^1];
                    _screen.RemoveAt(_screen.Count - 1);
                    last.Clear();
                    _screen.Insert(final == 'T' ? 0 : _row, last);
                }
                break;
            case 'M':
                for (var i = 0; i < Math.Min(rows - _row, Arg(0)); i++)
                {
                    var gone = _screen[_row];
                    _screen.RemoveAt(_row);
                    gone.Clear();
                    _screen.Add(gone);
                }
                break;
        }
    }

    /// <summary>Borra en la fila del cursor: desde el cursor (0), hasta el cursor (1) o toda (2).</summary>
    void EraseLine(Row row, int mode)
    {
        var text = row.Text;
        switch (mode)
        {
            case 0:
                if (_col < text.Length) text.Length = _col;
                row.Wrapped = false;
                break;
            case 1:
                for (var i = 0; i <= Math.Min(_col, text.Length - 1); i++) text[i] = ' ';
                break;
            default:
                text.Clear();
                row.Wrapped = false;
                break;
        }
    }

    /// <summary>
    /// La pantalla alterna (vim, less, claude…): lo que se pinta ahí no va a los bloques. La muestra la pantalla
    /// completa de la Terminal (ShellSession.Screen); al salir, el bloque sigue con lo que quede en la normal.
    /// </summary>
    void Alternate(bool on, List<ShellBlock> changed)
    {
        if (on == AltScreen) return;
        AltScreen = on;
        if (_running is { } running) Touch(running, changed);
    }

    void EndOsc(List<ShellBlock> changed)
    {
        _parse = Parse.Ground;
        var osc = _sequence.ToString();
        _sequence.Clear();
        var semi = osc.IndexOf(';');
        var code = semi < 0 ? osc : osc[..semi];
        var rest = semi < 0 ? "" : osc[(semi + 1)..];
        switch (code)
        {
            case "133" or "633":
                Mark(rest, changed);
                break;
            case "7":
                if (PathOf(rest) is { } path) Cwd = path;
                break;
            case "0" or "2":
                Title = rest;
                break;
        }
    }

    /// <summary>Las marcas del shell: A (prompt), B (comando), C (salida), D;código (terminó) y, en 633, P;Cwd=.</summary>
    void Mark(string mark, List<ShellBlock> changed)
    {
        var parts = mark.Split(';');
        switch (parts[0])
        {
            case "A":
                if (!Ready)
                {
                    Ready = true;
                    Preamble(changed);
                }
                // Un prompt nuevo sin D: el comando terminó sin decir cómo.
                if (_running is { } open && _phase == Phase.Output)
                {
                    Finish(open, null);
                    Touch(open, changed);
                }
                _phase = Phase.Prompt;
                _inputMarked = false;
                break;
            case "B":
                _phase = Phase.Input;
                _inputMarked = true;
                _inputRow = _row;
                _inputCol = _col;
                break;
            case "C":
                _marksOutput = true;
                if (_phase == Phase.Output) break;
                if (_running is null)
                {
                    // Un comando que no se envió desde la app: se toma del eco, si todavía se ve. Un Ctrl+C en el prompt
                    // (el eco es «^C») o una línea vacía no son comandos.
                    var echo = _inputRow >= 0 ? Echo() : "";
                    if (echo.Length == 0 || echo == "^C")
                    {
                        _phase = Phase.Idle;
                        break;
                    }
                    var block = new ShellBlock { Kind = ShellBlockKind.Command, Command = echo, Cwd = Cwd, Running = true };
                    _blocks.Add(block);
                    StartRunning(block);
                    Touch(block, changed);
                }
                _phase = Phase.Output;
                _outputTop = _row;
                var row = _screen[_row];
                row.Owner = _running;
                row.From = Math.Min(_col, row.Text.Length);
                break;
            case "D":
                // La D del primer prompt (o de un Intro vacío) llega sin que haya empezado una salida: no cierra nada.
                if (_running is { } done && _phase == Phase.Output)
                {
                    Finish(done, parts.Length > 1 && int.TryParse(parts[1], out var exit) ? exit : null);
                    Touch(done, changed);
                }
                if (_phase != Phase.Input || _running is null) _phase = Phase.Idle;
                _inputMarked = false;
                break;
            case "P" when parts.Length > 1 && parts[1].StartsWith("Cwd=", StringComparison.Ordinal):
                Cwd = string.Join(";", parts.Skip(1))["Cwd=".Length..];
                break;
        }
    }

    /// <summary>Lo que el shell mostró antes de su primer prompt (errores del perfil, un aviso): un bloque sin comando.</summary>
    void Preamble(List<ShellBlock> changed)
    {
        var rows = _screen.Take(_row + 1).Select(r => (r.Text.ToString(), false)).ToList();
        var lines = Logical(rows);
        Trim(lines);
        if (lines.Count == 0) return;
        var block = new ShellBlock { Kind = ShellBlockKind.Output, Lines = lines };
        _blocks.Insert(0, block);
        Touch(block, changed);
    }

    string Echo()
    {
        var parts = new StringBuilder();
        for (var r = _inputRow; r <= _row && r < _screen.Count; r++)
        {
            var text = _screen[r].Text.ToString();
            var from = r == _inputRow ? Math.Min(_inputCol, text.Length) : 0;
            var to = r == _row ? Math.Min(_col, text.Length) : text.Length;
            if (to > from) parts.Append(text, from, to - from);
        }
        return parts.ToString().Trim();
    }

    void StartRunning(ShellBlock block)
    {
        if (_running is { } previous && previous != block) Finish(previous, null);
        _running = block;
        _frozen.Clear();
        _frozenOpen = false;
        _frozenDropped = 0;
    }

    void Finish(ShellBlock block, int? exit)
    {
        Refresh(block);
        block.Running = false;
        block.ExitCode = exit;
        block.Duration = DateTime.Now - block.Started;
        // La salida queda como está: lo que se vuelva a pintar en esas filas ya no es de este bloque.
        foreach (var row in _screen.Where(r => r.Owner == block)) row.Owner = null;
        if (_running == block) _running = null;
    }

    /// <summary>Arma la salida del bloque que corre: lo que salió por arriba y sus filas en la pantalla. True si cambió.</summary>
    bool Refresh(ShellBlock block)
    {
        var lines = new List<string>(_frozen);
        var screen = Logical(_screen.Where(r => r.Owner == block).Select(r => (Cut(r), r.Wrapped)).ToList());
        if (_frozenOpen && lines.Count > 0 && screen.Count > 0)
        {
            // La última línea que salió por arriba seguía en la primera fila que queda.
            lines[^1] = (lines[^1] + screen[0]).TrimEnd();
            screen.RemoveAt(0);
        }
        lines.AddRange(screen);
        var dropped = _frozenDropped;
        if (dropped == 0)
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > MaxLines)
        {
            dropped += lines.Count - MaxLines;
            lines.RemoveRange(0, lines.Count - MaxLines);
        }
        if (dropped == block.Hidden && lines.SequenceEqual(block.Lines)) return false;
        block.Lines = lines;
        block.Hidden = dropped;
        return true;
    }

    /// <summary>Las filas como líneas: las que siguieron abajo por no caber se juntan; sin espacios al final.</summary>
    static List<string> Logical(IReadOnlyList<(string Text, bool Wrapped)> rows)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        var open = false;
        foreach (var (text, wrapped) in rows)
        {
            current.Append(text);
            open = wrapped;
            if (wrapped) continue;
            lines.Add(current.ToString().TrimEnd());
            current.Clear();
        }
        if (open) lines.Add(current.ToString().TrimEnd());
        return lines;
    }

    static void Trim(List<string> lines)
    {
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
    }

    /// <summary>La carpeta de un OSC 7 (file://equipo/ruta): «C:\…» codificada (PowerShell) o «/home/…» (bash).</summary>
    public static string? PathOf(string url)
    {
        if (!url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return null;
        var slash = url.IndexOf('/', "file://".Length);
        if (slash < 0) return null;
        var raw = url[(slash + 1)..];
        string path;
        try { path = Uri.UnescapeDataString(raw); }
        catch (Exception) { path = raw; }
        // «C:\…» o «C:/…» es de Windows; si no, de Unix (la barra del principio era el separador).
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') return path.Replace('/', '\\');
        if (path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':') return path[1..].Replace('/', '\\');
        return "/" + path;
    }

    static void Touch(ShellBlock block, List<ShellBlock> changed)
    {
        if (!changed.Contains(block)) changed.Add(block);
    }
}
