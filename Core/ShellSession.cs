using System.Text;

namespace Claupet;

/// <summary>Los shells que puede abrir la Terminal: PowerShell, bash (Git Bash en Windows), bash en WSL y zsh (Linux y macOS).</summary>
enum ShellKind { PowerShell, Bash, Wsl, Zsh }

/// <summary>Un shell que hay en el equipo: cuál es, cómo se llama en la lista y su programa (y, en WSL, la distribución).</summary>
sealed record ShellChoice(ShellKind Kind, string Label, string Program, string? Distro = null)
{
    /// <summary>Lo que se guarda para recordar la elección («powershell», «bash», «wsl»).</summary>
    public string Id => Kind.ToString().ToLowerInvariant();
}

/// <summary>
/// Dónde están los shells y cómo se abren con las marcas de los bloques (OSC 133). PowerShell recibe un guion que
/// envuelve su prompt (D;código, la carpeta con OSC 7, A y B) y PSConsoleHostReadLine (C, apenas se acepta la línea);
/// bash, un --rcfile que carga lo de siempre y agrega PROMPT_COMMAND (D y OSC 7), PS1 (A y B) y PS0 (C).
/// </summary>
static class Shells
{
    static IReadOnlyList<ShellChoice>? _found;

    /// <summary>Los shells de este equipo (se buscan una vez). Sin ConPTY, igual se listan: al abrir avisan.</summary>
    public static IReadOnlyList<ShellChoice> Available() => _found ??= Find();

    static List<ShellChoice> Find()
    {
        var found = new List<ShellChoice>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if ((OnPath("pwsh.exe") ?? Existing(Path.Combine(ProgramFiles, "PowerShell", "7", "pwsh.exe"))) is { } pwsh)
                    found.Add(new ShellChoice(ShellKind.PowerShell, "PowerShell", pwsh));
                else if (Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) is { } powershell)
                    found.Add(new ShellChoice(ShellKind.PowerShell, "Windows PowerShell", powershell));
                if (GitBash() is { } bash) found.Add(new ShellChoice(ShellKind.Bash, "Git Bash", bash));
                if (WslDistro() is { } distro && Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe")) is { } wsl)
                    found.Add(new ShellChoice(ShellKind.Wsl, $"bash (WSL · {distro})", wsl, distro));
            }
            else
            {
                // Linux y macOS: el shell de la persona primero (en macOS suele ser zsh), después los otros que haya.
                if ((Existing("/bin/zsh") ?? OnPath("zsh")) is { } zsh) found.Add(new ShellChoice(ShellKind.Zsh, "zsh", zsh));
                if ((Existing("/bin/bash") ?? OnPath("bash")) is { } bash) found.Add(new ShellChoice(ShellKind.Bash, "bash", bash));
                if (OnPath("pwsh") is { } pwsh) found.Add(new ShellChoice(ShellKind.PowerShell, "PowerShell", pwsh));
                var mine = Path.GetFileName(Environment.GetEnvironmentVariable("SHELL") ?? "");
                found = [.. found.OrderBy(s => Path.GetFileName(s.Program) == mine ? 0 : 1)];
            }
        }
        catch (Exception) { }
        return found;
    }

    static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    static string? Existing(string path) => File.Exists(path) ? path : null;

    static string? OnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (Existing(Path.Combine(dir.Trim('"'), name)) is { } path) return path;
            }
            catch (Exception) { }
        }
        return null;
    }

    /// <summary>El bash de Git para Windows: junto a git.exe (en cmd, bin o mingw64\bin) o en las carpetas de siempre.</summary>
    static string? GitBash()
    {
        var roots = new List<string>();
        if (OnPath("git.exe") is { } git && Path.GetDirectoryName(git) is { } dir)
        {
            roots.Add(Path.GetDirectoryName(dir) ?? dir);
            if (Path.GetDirectoryName(Path.GetDirectoryName(dir)) is { } up) roots.Add(up);
        }
        roots.Add(Path.Combine(ProgramFiles, "Git"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git"));
        return roots.Select(r => Existing(Path.Combine(r, "bin", "bash.exe"))).FirstOrDefault(p => p is not null);
    }

    /// <summary>La distribución de WSL que se abre: la predeterminada, salvo las internas de Docker.</summary>
    static string? WslDistro()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var lxss = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss is null) return null;
        var names = new List<string>();
        string? preferred = null;
        var fallback = lxss.GetValue("DefaultDistribution") as string;
        foreach (var id in lxss.GetSubKeyNames())
        {
            using var key = lxss.OpenSubKey(id);
            if (key?.GetValue("DistributionName") is not string name || name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) continue;
            names.Add(name);
            if (string.Equals(id, fallback, StringComparison.OrdinalIgnoreCase)) preferred = name;
        }
        return preferred ?? names.FirstOrDefault();
    }

    /// <summary>Cómo se abre el shell con las marcas: su programa y argumentos (y la línea entera, en Windows) y sus variables.</summary>
    public static ShellLaunch Launch(ShellChoice shell, string folder)
    {
        var env = new Dictionary<string, string> { ["TERM_PROGRAM"] = "Claupet" };
        // less (el paginador de git) en la pantalla alterna, como en cualquier terminal: se ve en la pantalla completa
        // de la Terminal y, al salir, no deja la página en el bloque. F: si cabe, no pagina; R: con colores.
        if (Environment.GetEnvironmentVariable("LESS") is null) env["LESS"] = "FR";
        if (!OperatingSystem.IsWindows())
        {
            env["TERM"] = "xterm-256color";
            env["COLORTERM"] = "truecolor";
        }
        string[] args;
        switch (shell.Kind)
        {
            case ShellKind.PowerShell:
                args = ["-NoLogo", "-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellScript))];
                break;
            case ShellKind.Wsl:
                args = ["-d", shell.Distro ?? "", "--cd", folder, "-e", "bash", "--rcfile", WslPath(WriteRc()), "-i"];
                break;
            case ShellKind.Zsh:
                // zsh lee sus archivos de ZDOTDIR: los de la app cargan los de la persona y agregan las marcas. En macOS,
                // como en Terminal, como shell de entrada (.zprofile, donde suele estar Homebrew).
                env["CLAUPET_USER_ZDOTDIR"] = Environment.GetEnvironmentVariable("ZDOTDIR") is { Length: > 0 } own ? own
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                env["ZDOTDIR"] = WriteZsh();
                args = OperatingSystem.IsMacOS() ? ["-l", "-i"] : ["-i"];
                break;
            default:
                if (OperatingSystem.IsWindows())
                {
                    // CHERE_INVOKING: el /etc/profile de Git Bash no salta a la carpeta personal.
                    env["CHERE_INVOKING"] = "1";
                    env["TERM"] = "xterm-256color";
                }
                args = ["--rcfile", OperatingSystem.IsWindows() ? WriteRc().Replace('\\', '/') : WriteRc(), "-i"];
                break;
        }
        return new ShellLaunch(shell.Program, args, string.Join(" ", args.Prepend(shell.Program).Select(Quote)), env);
    }

    /// <summary>
    /// PowerShell (5.1 y 7): el prompt de siempre entre A y B, con D;código y la carpeta (OSC 7) antes. El código es 0 si
    /// lo último salió bien; si no, $LASTEXITCODE (un programa) o 1 (un cmdlet). C sale al aceptarse la línea, después del
    /// eco, y solo si trae un comando (con Ctrl+C, PSReadLine devuelve la línea vacía y no hay salida que empiece). Sin
    /// predicciones de PSReadLine: el comando llega entero desde la caja de la Terminal.
    /// </summary>
    const string PowerShellScript = """
        $global:__ClaupetPrompt = $function:prompt
        function global:prompt {
            $ok = $global:?
            $last = $global:LASTEXITCODE
            $code = if ($ok) { 0 } elseif ($last) { $last } else { 1 }
            $e = [char]27; $b = [char]7
            $here = $ExecutionContext.SessionState.Path.CurrentLocation
            $marks = "$e]133;D;$code$b"
            if ($here.Provider.Name -eq 'FileSystem') { $marks += "$e]7;file://localhost/" + [uri]::EscapeDataString($here.ProviderPath) + $b }
            $text = if ($global:__ClaupetPrompt) { & $global:__ClaupetPrompt } else { "PS $here> " }
            $global:LASTEXITCODE = $last
            "$marks$e]133;A$b$text$e]133;B$b"
        }
        if (Get-Command PSConsoleHostReadLine -ErrorAction SilentlyContinue) {
            $global:__ClaupetReadLine = $function:PSConsoleHostReadLine
            function global:PSConsoleHostReadLine {
                $line = & $global:__ClaupetReadLine
                if ("$line".Trim()) { [Console]::Write([char]27 + ']133;C' + [char]7) }
                $line
            }
            try { Set-PSReadLineOption -PredictionSource None -ErrorAction Stop } catch { }
        }
        """;

    /// <summary>bash: carga lo de siempre (el perfil de Git Bash y ~/.bashrc) y agrega las marcas. Con saltos de línea de Unix.</summary>
    const string BashRc = """
        # Claupet Terminal: las marcas de los bloques (OSC 133). Lo crea la app en cada sesión.
        if [ -n "$MSYSTEM" ] && [ -f /etc/profile ]; then . /etc/profile; fi
        if [ "$(uname)" = Darwin ]; then
          [ -f /etc/profile ] && . /etc/profile
          if [ -f ~/.bash_profile ]; then . ~/.bash_profile; elif [ -f ~/.bashrc ]; then . ~/.bashrc; fi
        elif [ -f ~/.bashrc ]; then . ~/.bashrc; fi
        __claupet_prompt() {
          local code=$?
          printf '\033]133;D;%s\007\033]7;file://%s%s\007' "$code" "${HOSTNAME:-localhost}" "$PWD"
          return $code
        }
        PROMPT_COMMAND="__claupet_prompt${PROMPT_COMMAND:+; $PROMPT_COMMAND}"
        PS1="\[\e]133;A\a\]${PS1:-\\w \$ }\[\e]133;B\a\]"
        PS0=$'\e]133;C\a'
        """;

    /// <summary>
    /// zsh (Linux y macOS): los archivos de ZDOTDIR de la app cargan los de la persona (desde CLAUPET_USER_ZDOTDIR) y
    /// agregan las marcas: D y la carpeta en precmd (el primero, para tener el código de lo último), C en preexec, y A y B
    /// alrededor del prompt (otra vez al final de precmd, por si un tema lo rearma). Después, ZDOTDIR vuelve a ser el
    /// de la persona (su historial y su .zlogin).
    /// </summary>
    static readonly (string Name, string Text)[] ZshFiles =
    [
        (".zshenv", """
            # Claupet Terminal: carga lo de siempre. Lo crea la app en cada sesión.
            __claupet_zdotdir="$ZDOTDIR"
            ZDOTDIR="${CLAUPET_USER_ZDOTDIR:-$HOME}"
            [[ -f "$ZDOTDIR/.zshenv" ]] && source "$ZDOTDIR/.zshenv"
            ZDOTDIR="$__claupet_zdotdir"
            """),
        (".zprofile", """
            ZDOTDIR="${CLAUPET_USER_ZDOTDIR:-$HOME}"
            [[ -f "$ZDOTDIR/.zprofile" ]] && source "$ZDOTDIR/.zprofile"
            ZDOTDIR="$__claupet_zdotdir"
            """),
        (".zshrc", """
            # Claupet Terminal: lo de siempre y las marcas de los bloques (OSC 133).
            ZDOTDIR="${CLAUPET_USER_ZDOTDIR:-$HOME}"
            unset __claupet_zdotdir
            [[ -f "$ZDOTDIR/.zshrc" ]] && source "$ZDOTDIR/.zshrc"
            __claupet_precmd() {
              local code=$?
              printf '\033]133;D;%s\007\033]7;file://%s%s\007' "$code" "${HOST:-localhost}" "$PWD"
            }
            __claupet_preexec() { printf '\033]133;C\007'; }
            __claupet_marks() {
              [[ "$PS1" == *'133;A'* ]] || PS1=$'%{\e]133;A\a%}'"$PS1"$'%{\e]133;B\a%}'
            }
            precmd_functions=(__claupet_precmd $precmd_functions __claupet_marks)
            preexec_functions+=(__claupet_preexec)
            """),
    ];

    /// <summary>Escribe los archivos de zsh en la carpeta temporal de la app y devuelve esa carpeta (el ZDOTDIR del shell).</summary>
    static string WriteZsh()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Claupet", "zsh");
        Directory.CreateDirectory(dir);
        foreach (var (name, text) in ZshFiles)
        {
            var path = Path.Combine(dir, name);
            var content = text.ReplaceLineEndings("\n") + "\n";
            if (!File.Exists(path) || File.ReadAllText(path) != content) File.WriteAllText(path, content, new UTF8Encoding(false));
        }
        return dir;
    }

    /// <summary>Escribe el rcfile de bash en la carpeta temporal de la app y devuelve su ruta.</summary>
    static string WriteRc()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Claupet");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "claupet-bashrc.sh");
        var text = BashRc.ReplaceLineEndings("\n") + "\n";
        if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    /// <summary>«C:\x\y» como la ve WSL (/mnt/c/x/y).</summary>
    public static string WslPath(string windows) =>
        windows.Length >= 2 && windows[1] == ':' ? $"/mnt/{char.ToLowerInvariant(windows[0])}{windows[2..].Replace('\\', '/')}" : windows.Replace('\\', '/');

    /// <summary>Un argumento con comillas para la línea de comandos de Windows (las comillas y barras de adentro, escapadas).</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var quoted = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            quoted.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            quoted.Append(c);
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
}

/// <summary>
/// Una terminal de comandos de la Terminal: el shell en una consola de mentira (ConPTY) y su salida en bloques. Lo que
/// llega se aplica con post, en el hilo de la vista (los bloques solo cambian ahí); si llega mucho de golpe, se junta.
/// </summary>
sealed class ShellSession : IDisposable
{
    /// <summary>El tamaño de la consola: ancho como para tablas, alto como una ventana común.</summary>
    const int Cols = 120, Rows = 40;

    readonly Action<Action> _post;
    readonly StringBuilder _pending = new();
    bool _posted;
    IPty? _console;
    int _cols = Cols, _rows = Rows;

    public ShellSession(ShellChoice shell, string folder, Action<Action> post)
    {
        Shell = shell;
        Folder = folder;
        _post = post;
        // Lo que el programa pregunta a la terminal (dónde está el cursor, qué terminal es) se contesta como cualquiera.
        Screen.DataReceived += (_, e) => _console?.Write(e.Data);
    }

    public ShellChoice Shell { get; }
    public string Folder { get; }
    public ShellTranscript Transcript { get; } = new(Cols, Rows);
    public bool Running { get; private set; }

    /// <summary>
    /// La pantalla tal como la pinta el shell, con colores (XTerm.NET). Los bloques salen de Transcript; esta se muestra
    /// cuando un programa usa la pantalla completa (la alterna: vim, less, claude, htop).
    /// </summary>
    public XTerm.Terminal Screen { get; } = new(new XTerm.Options.TerminalOptions { Cols = Cols, Rows = Rows, Scrollback = 100 });

    /// <summary>Un programa está usando la pantalla completa.</summary>
    public bool FullScreen { get; private set; }

    /// <summary>Entró o salió de la pantalla completa. Se avisa en el hilo de post.</summary>
    public event Action? FullScreenChanged;

    /// <summary>Cambió lo que se ve en la pantalla completa (hay que volver a dibujarla).</summary>
    public event Action? ScreenChanged;

    /// <summary>Cambiaron esos bloques (o el estado de la sesión). Se avisa en el hilo de post.</summary>
    public event Action<IReadOnlyList<ShellBlock>>? Changed;

    /// <summary>Abre el shell. Si no se puede (otro sistema, no abre), queda un aviso en la sesión.</summary>
    public void Start()
    {
        if (!Pty.IsSupported)
        {
            Notice(L.T("La terminal de comandos todavía no está disponible en este sistema. Por ahora, solo en Windows 10 (1809) o más nuevo."));
            return;
        }
        try
        {
            _console = Pty.Start(Shells.Launch(Shell, Folder), Folder, _cols, _rows);
        }
        catch (Exception ex)
        {
            Notice(L.T("No se pudo abrir {0}: {1}", Shell.Label, ex.Message));
            return;
        }
        Running = true;
        var console = _console;
        _ = Task.Run(() => Read(console));
        _ = WatchAsync(console);
    }

    /// <summary>
    /// Envía un comando (con Intro). Si hay uno corriendo, es lo que se le escribe a ese programa (una respuesta, una
    /// contraseña) y no abre un bloque.
    /// </summary>
    public void Send(string command)
    {
        if (_console is not { } console || !Running) return;
        if (!Transcript.Busy && command.Trim().Length > 0) Raise([Transcript.Begin(command)]);
        // Varias líneas van como varios Intro: el shell las ejecuta una tras otra (las siguientes, en sus bloques).
        console.Write(command.ReplaceLineEndings("\r") + "\r");
    }

    /// <summary>Ctrl+C: corta el comando que corre (o borra lo escrito en el prompt).</summary>
    public void Interrupt() => _console?.Write("\x03");

    /// <summary>Lo que se teclea en la pantalla completa, tal cual (las secuencias ya armadas).</summary>
    public void Input(string data)
    {
        if (Running && data.Length > 0) _console?.Write(data);
    }

    /// <summary>
    /// El tamaño de la consola: el del panel de la sesión, siempre (como cualquier terminal, el de su ventana). Así un
    /// programa de pantalla completa ya arranca con su tamaño: cambiarlo mientras pinta dejaba filas corridas, porque
    /// ConPTY y la pantalla de aquí reacomodan distinto lo que ya estaba.
    /// </summary>
    public void Resize(int cols, int rows)
    {
        cols = Math.Clamp(cols, 20, 400);
        rows = Math.Clamp(rows, 5, 200);
        if (cols == _cols && rows == _rows) return;
        // Lo que ya llegó se pinta con el tamaño de antes; después cambian las tres juntas: la de ConPTY, la de los
        // bloques y la de la pantalla completa.
        Flush();
        _cols = cols;
        _rows = rows;
        Transcript.Resize(cols, rows);
        Screen.Resize(cols, rows);
        _console?.Resize(cols, rows);
    }

    void Read(IPty console)
    {
        var decoder = new UTF8Encoding(false).GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[bytes.Length + 4];
        try
        {
            int n;
            while ((n = console.Output.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, n, chars, 0);
                lock (_pending)
                {
                    _pending.Append(chars, 0, count);
                    if (_posted) continue;
                    _posted = true;
                }
                _post(Flush);
            }
        }
        catch (Exception) { }
    }

    /// <summary>Aplica lo que llegó desde el último aviso, todo junto.</summary>
    void Flush()
    {
        string text;
        lock (_pending)
        {
            text = _pending.ToString();
            _pending.Clear();
            _posted = false;
        }
        if (text.Length == 0) return;
        Screen.Write(text);
        var changed = Transcript.Apply(text);
        if (Screen.IsAlternateBufferActive != FullScreen) SetFullScreen(Screen.IsAlternateBufferActive);
        else if (FullScreen) ScreenChanged?.Invoke();
        Raise(changed);
    }

    void SetFullScreen(bool on)
    {
        FullScreen = on;
        FullScreenChanged?.Invoke();
    }

    async Task WatchAsync(IPty console)
    {
        var code = await console.Exited;
        // Lo último que pintó llega un poco después del final del proceso.
        await Task.Delay(150);
        _post(() =>
        {
            if (!ReferenceEquals(_console, console)) return;
            Flush();
            Running = false;
            var changed = new List<ShellBlock>();
            if (Transcript.Stop() is { } stopped) changed.Add(stopped);
            changed.Add(Transcript.Notice(code == 0 ? L.T("El shell se cerró.") : L.T("El shell se cerró (código {0}).", code)));
            // La consola de mentira sigue abierta aunque el shell haya terminado: se cierra.
            _console = null;
            console.Dispose();
            if (FullScreen) SetFullScreen(false);
            Raise(changed);
        });
    }

    void Notice(string text) => Raise([Transcript.Notice(text)]);

    /// <summary>Avisa lo que cambió (el bloque ya cambió aunque nadie escuche: por eso no va dentro de Changed?.Invoke).</summary>
    void Raise(IReadOnlyList<ShellBlock> changed) => Changed?.Invoke(changed);

    public void Dispose()
    {
        var console = _console;
        _console = null;
        Running = false;
        console?.Dispose();
        Screen.Dispose();
    }
}
