using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claupet;

/// <summary>Una cuenta es una carpeta de configuración de Claude Code (la de CLAUDE_CONFIG_DIR).</summary>
sealed class Account
{
    public string Label { get; set; } = "";
    public string ConfigDir { get; set; } = "";


    // La carpeta por defecto guarda su perfil fuera, en ~/.claude.json; las demás, dentro.
    [JsonIgnore]
    public string ProfilePath => SamePath(ConfigDir, DefaultDir)
        ? Path.Combine(Home, ".claude.json")
        : Path.Combine(ConfigDir, ".claude.json");

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string DefaultDir => Path.Combine(Home, ".claude");

    public static string LabelFor(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (name.Equals(".claude", StringComparison.OrdinalIgnoreCase)) return "principal";
        if (name.StartsWith(".claude-", StringComparison.OrdinalIgnoreCase)) return name[8..];
        return name.TrimStart('.');
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
}

sealed class AppSettings
{
    /// <summary>El idioma de la app (L.Choices); null: el del sistema.</summary>
    public string? Language { get; set; }
    public List<Account> Accounts { get; set; } = new();
    /// <summary>Carpetas quitadas a mano: la detección automática no las vuelve a agregar.</summary>
    public List<string> Ignored { get; set; } = new();
    public string? Selected { get; set; }
    public int RefreshMinutes { get; set; } = 5;
    public bool Notifications { get; set; } = true;
    public bool Sound { get; set; } = true;
    public string SoundPack { get; set; } = "clasico";
    /// <summary>Paleta del acento (Theme.Palettes). «claude» es como se llamaba antes «terracota».</summary>
    public string Palette { get => _palette; set => _palette = value == "claude" ? "terracota" : value; }
    string _palette = "terracota";
    /// <summary>Animación al actualizar (RefreshSprite.Styles). Se elige junto con Mascot, en la opción «Mascota» (Mascots).</summary>
    public string Animation { get; set; } = "mascota";
    /// <summary>Mascota de los avisos y de la animación «mascota»: Mascots.Classic o Mascots.Chibi.</summary>
    public string Mascot { get; set; } = Mascots.Classic;
    /// <summary>La mascota (el chibi o la clásica) en su propia ventana sobre el escritorio. Se enciende al elegir el chibi.</summary>
    public bool DesktopMascot { get; set; }
    /// <summary>«Hablar»: la mascota del escritorio cuenta los avisos en una nubecita, además del aviso de siempre.</summary>
    public bool Speak { get; set; } = true;
    /// <summary>La mascota del escritorio muestra lo que hace la sesión (leer, buscar, editar…); si no, baila mientras trabaja.</summary>
    public bool FlowGestures { get; set; } = true;
    /// <summary>Con algo sonando en el control multimedia: cabecea mientras trabaja y baila si no hay trabajo.</summary>
    public bool DanceToMusic { get; set; } = true;
    /// <summary>Festeja, se marea o baila chachachá cuando llega un aviso.</summary>
    public bool NoticeGestures { get; set; } = true;
    /// <summary>Saluda con la mano al aparecer en el escritorio.</summary>
    public bool GreetOnAppear { get; set; } = true;
    /// <summary>Puchero, brazos cruzados y furia con muchos clics seguidos; si no, siempre saluda.</summary>
    public bool AngryOnClicks { get; set; } = true;
    /// <summary>Se duerme sin sesiones abiertas; si no, se queda quieta.</summary>
    public bool SleepWithoutSessions { get; set; } = true;
    /// <summary>Dónde quedó la ventana de la mascota al arrastrarla, en pixeles de pantalla; null: junto a los avisos.</summary>
    /// <summary>La mascota sigue a la vista al cambiar de escritorio virtual (Windows) o de Espacio (macOS).</summary>
    public bool MascotAllDesktops { get; set; } = true;
    public int? DesktopMascotX { get; set; }
    public int? DesktopMascotY { get; set; }
    /// <summary>Tamaño de la mascota del escritorio (MascotSizes).</summary>
    public string MascotSize { get; set; } = MascotSizes.Normal;
    /// <summary>La última pestaña abierta de Ajustes.</summary>
    public string SettingsTab { get; set; } = "Avisos";

    /// <summary>Chat con la mascota: el clic del saludo abre un campo en la nubecita.</summary>
    public bool Chat { get; set; }
    /// <summary>Modelo de la mascota (alias de Claude Code): uno liviano, para respuestas cortas.</summary>
    public string MascotModel { get; set; } = "haiku";
    /// <summary>Modelo de la isla; null: el de sus sesiones (Claude Code usa el predeterminado de la cuenta).</summary>
    public string? IslandModel { get; set; }
    /// <summary>
    /// El modelo local (LocalModel.Choices): el que se descarga y responde cuando la mascota o la isla tienen
    /// «local» como modelo. null: ninguno elegido todavía.
    /// </summary>
    public string? LocalModel { get; set; }
    /// <summary>Minutos sin uso tras los que el modelo local suelta la memoria.</summary>
    public int LocalIdleMinutes { get; set; } = 5;

    /// <summary>Ajustes › Respuestas: quién responde primero cada tipo de pregunta (AnswerTopics); el que falta, el de siempre.</summary>
    public Dictionary<string, Engine> AnswerEngines { get; set; } = new();
    /// <summary>«personalizado» si se eligió a mano en Respuestas; si no (null), el modo es el que coincide (AnswerPresets).</summary>
    public string? AnswerPreset { get; set; }
    /// <summary>Si el primero no entiende, hasta dónde pasa: la nube, el modelo local o (None) a nadie.</summary>
    public Engine AnswerFallback { get; set; } = Engine.Cloud;
    /// <summary>Debajo de cada respuesta, quién respondió («Sin IA · al instante»).</summary>
    public bool ShowWhoAnswered { get; set; } = true;
    /// <summary>Mi ciudad, para el clima: «ip», «windows» (la ubicación de Windows) o «ciudad» (WeatherCity).</summary>
    public string WeatherPlace { get; set; } = WeatherService.ByIp;
    public string? WeatherCity { get; set; }
    /// <summary>Cuenta que responde el chat (su carpeta de configuración); null: la elegida en Uso.</summary>
    public string? AssistantAccount { get; set; }
    /// <summary>Carpeta donde se abren las tareas que se le piden a la mascota; null: la última de «Nueva sesión».</summary>
    public string? TaskFolder { get; set; }
    /// <summary>Un archivo soltado sobre la mascota o la isla se analiza.</summary>
    public bool AnalyzeDrops { get; set; } = true;
    /// <summary>Las capacidades con «Permitir siempre» (Capabilities): las demás se preguntan al usarlas.</summary>
    public List<string> Permissions { get; set; } = new();
    /// <summary>Hablarle con el micrófono (mantener MicKey).</summary>
    public bool Mic { get; set; }
    /// <summary>La tecla para hablar: una que se mantiene (nombre de System.Windows.Forms.Keys) o una combinación grabada (MicKeys).</summary>
    public string MicKey { get; set; } = "RControlKey";
    /// <summary>El atajo que abre el chat (KeyNames: «Ctrl+Alt+Space»).</summary>
    public string ChatKey { get; set; } = KeyNames.DefaultChat;
    /// <summary>La tecla Copilot (Win+Mayús+F23) abre el chat y escucha, en vez de abrir Copilot.</summary>
    public bool CopilotKey { get; set; } = true;
    /// <summary>Leer las respuestas en voz alta.</summary>
    public bool ReadAloud { get; set; }
    /// <summary>Con qué voz (ClaupetVoices): la femenina o la masculina de Claupet, o una de Windows.</summary>
    public string ReadAloudVoice { get; set; } = ClaupetVoices.Female;

    /// <summary>La isla del borde de arriba de la pantalla.</summary>
    public bool Island { get; set; }
    /// <summary>La isla se abre con el cursor encima (si no, con un clic).</summary>
    public bool IslandHover { get; set; } = true;
    /// <summary>La isla queda compacta a la vista mientras una sesión trabaja o espera.</summary>
    public bool IslandWhileBusy { get; set; } = true;
    /// <summary>El resumen de la semana, los lunes, en la isla.</summary>
    public bool WeeklyRecap { get; set; } = true;
    /// <summary>El lunes del último resumen semanal mostrado.</summary>
    public DateOnly? RecapShown { get; set; }
    /// <summary>El asistente de configuración ya se mostró una vez.</summary>
    public bool SetupDone { get; set; }
    /// <summary>El acceso directo «Claupet» del menú Inicio, que abre Claupet Console (en Windows).</summary>
    public bool StartMenuShortcut { get; set; } = true;
    /// <summary>El mismo acceso directo en el escritorio.</summary>
    public bool DesktopShortcut { get; set; }
    /// <summary>Lo último elegido en «Nueva sesión».</summary>
    public LaunchOptions Launch { get; set; } = new();

    /// <summary>Las extensiones: encendidas, sus permisos y ajustes, y quién ocupa cada ranura exclusiva.</summary>
    public ExtensionSettings Extensions { get; set; } = new();

    /// <summary>La mascota del escritorio de un paquete (su id); null: la elegida en Mascot. Los avisos siguen con la de Mascot.</summary>
    public string? CustomPet { get; set; }

    /// <summary>Buscar una versión nueva en las Releases de GitHub una vez al día (Ajustes › Sistema).</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Cuándo se buscó por última vez (null: nunca).</summary>
    public DateTimeOffset? UpdatesChecked { get; set; }
    /// <summary>La última versión nueva de la que se avisó: de cada una se avisa una sola vez.</summary>
    public string? UpdateNotified { get; set; }
}

/// <summary>
/// La carpeta de datos de la app: %APPDATA%\Claupet (en Linux y macOS, ~/.config/Claupet). Antes se
/// llamaba ClaudeUsageTray: mientras no se haya movido, se sigue usando esa. Las rutas se arman en cada
/// uso (no se guardan), para que después de Migrate todas apunten a la nueva.
/// </summary>
static class DataFolder
{
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string Current => Path.Combine(AppData, "Claupet");
    static string Before => Path.Combine(AppData, "ClaudeUsageTray");

    public static string Root => Directory.Exists(Current) || !Directory.Exists(Before) ? Current : Before;

    public static string In(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>
    /// Mueve la carpeta de antes, entera (ajustes, historial, sonidos, modelos), a la de Claupet. Lo hace
    /// solo la app al abrirse, no los hooks ni las herramientas de línea de comandos. Si no se puede (la
    /// tiene abierta la versión anterior), sigue con la de antes y lo intenta la próxima vez.
    /// </summary>
    public static void Migrate()
    {
        if (Directory.Exists(Current) || !Directory.Exists(Before)) return;
        try { Directory.Move(Before, Current); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

static class SettingsStore
{
    static string Dir => DataFolder.Root;
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch (Exception) { }
        return new();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception) { }
    }

    /// <summary>Agrega las carpetas ~/.claude* con credenciales (archivo o Llavero) que aún no están en la lista.</summary>
    public static bool DetectNew(AppSettings settings)
    {
        var changed = false;
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(Account.Home, ".claude*").OrderBy(d => d).ToList(); }
        catch (Exception) { return false; }

        foreach (var dir in dirs)
        {
            if (!Credentials.Exists(dir)) continue;
            if (settings.Accounts.Any(a => Account.SamePath(a.ConfigDir, dir))) continue;
            if (settings.Ignored.Any(i => Account.SamePath(i, dir))) continue;
            settings.Accounts.Add(new Account { Label = Account.LabelFor(dir), ConfigDir = dir });
            changed = true;
        }
        return changed;
    }
}
