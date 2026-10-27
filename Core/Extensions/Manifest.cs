using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeUsageTray;

/// <summary>
/// Lo que puede ocupar una extensión. Las exclusivas (voz a texto, texto a voz y el asistente) tienen un
/// solo proveedor activo: al ocuparlas una extensión, el integrado (Whisper, Supertonic o la voz de
/// Windows, claude -p) no se carga. Las demás se suman: habilidades, mascotas, estilos de voz y sonidos.
/// Los Id son del formato del manifiesto: no cambian con el idioma.
/// </summary>
static class ExtensionSlots
{
    public const string SpeechToText = "speech-to-text", TextToSpeech = "text-to-speech", Assistant = "assistant",
        Skill = "skill", Pet = "pet", VoiceStyle = "voice-style", Sounds = "sounds";

    public static readonly string[] Exclusive = [SpeechToText, TextToSpeech, Assistant];

    public static readonly string[] All = [SpeechToText, TextToSpeech, Assistant, Skill, Pet, VoiceStyle, Sounds];

    public static bool IsExclusive(string slot) => Exclusive.Contains(slot);

    /// <summary>Necesita código corriendo (un runtime); las otras son solo datos.</summary>
    public static bool NeedsRuntime(string slot) => slot is SpeechToText or TextToSpeech or Assistant or Skill;

    public static string Label(string slot) => slot switch
    {
        SpeechToText => L.T("Voz a texto"),
        TextToSpeech => L.T("Texto a voz"),
        Assistant => L.T("Asistente"),
        Skill => L.T("Habilidad"),
        Pet => L.T("Mascota"),
        VoiceStyle => L.T("Voces"),
        Sounds => L.T("Sonidos"),
        _ => slot,
    };

    /// <summary>Lo que trae Claupet en esa ranura, para decir qué se apaga al ocuparla.</summary>
    public static string BuiltIn(string slot) => slot switch
    {
        SpeechToText => "Whisper",
        TextToSpeech => L.T("la voz de Claupet (Supertonic) y la de Windows"),
        Assistant => "Claude Code",
        _ => "",
    };
}

/// <summary>
/// Los permisos que una extensión puede pedir. Claupet hace cumplir los de su API (la mascota, los
/// avisos, el micrófono, Claude…); «network» y «files» los declara el autor y se muestran al instalar,
/// pero un programa o una DLL corre con los permisos de la persona y Claupet no puede encerrarlo.
/// </summary>
static class ExtensionPermissions
{
    public const string Pet = "pet", Notices = "notices", Island = "island", Voice = "voice", Microphone = "microphone",
        SystemAudio = "audio:system", Claude = "claude", Sessions = "sessions", Clipboard = "clipboard",
        Network = "network", Files = "files";

    static readonly string[] Plain = [Pet, Notices, Island, Voice, Microphone, SystemAudio, Claude, Sessions, Clipboard, Network];

    static readonly Regex Host = new(@"^(\*\.)?[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*(:\d{1,5})?$", RegexOptions.CultureInvariant);

    /// <summary>Uno de la lista, «network:&lt;host&gt;» o «files:&lt;ruta&gt;».</summary>
    public static bool IsValid(string permission)
    {
        if (Plain.Contains(permission)) return true;
        if (permission.StartsWith("network:", StringComparison.Ordinal)) return Host.IsMatch(permission[8..]);
        if (permission.StartsWith("files:", StringComparison.Ordinal)) return permission.Length > 6 && permission.Length <= 260 && !permission.Contains('\0');
        return false;
    }

    /// <summary>Los que Claupet controla en su API (los demás solo se declaran).</summary>
    public static bool Enforced(string permission) => permission is not (Network or Files)
        && !permission.StartsWith("network:", StringComparison.Ordinal) && !permission.StartsWith("files:", StringComparison.Ordinal);

    /// <summary>La red a ese host está declarada: «network» a secas o «network:host» (o «network:*.dominio»).</summary>
    public static bool AllowsHost(IEnumerable<string> permissions, string host) => permissions.Any(p =>
        p == Network || (p.StartsWith("network:", StringComparison.Ordinal) && HostMatches(p[8..], host)));

    static bool HostMatches(string pattern, string host)
    {
        var bare = pattern.Split(':')[0];
        return bare.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(bare[1..], StringComparison.OrdinalIgnoreCase)
            : host.Equals(bare, StringComparison.OrdinalIgnoreCase);
    }

    public static string Label(string permission) => permission switch
    {
        Pet => L.T("Mover y hacer hablar a la mascota"),
        Notices => L.T("Mostrar avisos"),
        Island => L.T("Mostrar tarjetas en la isla"),
        Voice => L.T("Hablar con la voz de Claupet"),
        Microphone => L.T("Escuchar el micrófono"),
        SystemAudio => L.T("Escuchar lo que suena en el equipo"),
        Claude => L.T("Preguntarle a Claude con su cuenta (gasta de su uso)"),
        Sessions => L.T("Ver sus sesiones de Claude Code"),
        Clipboard => L.T("Leer y escribir el portapapeles"),
        Network => L.T("Conectarse a internet"),
        _ when permission.StartsWith("network:", StringComparison.Ordinal) => L.T("Conectarse a {0}", permission[8..]),
        _ when permission.StartsWith("files:", StringComparison.Ordinal) => L.T("Usar archivos en {0}", permission[6..]),
        _ => permission,
    };
}

/// <summary>Una versión semántica (1.2.3 o 1.2.3-beta.1): las de las extensiones y la de Claupet.</summary>
readonly record struct SemVersion(int Major, int Minor, int Patch, string Pre) : IComparable<SemVersion>
{
    static readonly Regex Pattern = new(@"^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant);

    public static SemVersion? Parse(string? text)
    {
        if (text is null || Pattern.Match(text) is not { Success: true } m) return null;
        return new SemVersion(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value);
    }

    /// <summary>La de esta app (la del ensamblado: Version del .csproj).</summary>
    public static SemVersion App
    {
        get
        {
            var v = typeof(SemVersion).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new SemVersion(v.Major, v.Minor, Math.Max(0, v.Build), "");
        }
    }

    public int CompareTo(SemVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        // Sin «-algo» es mayor que con él; entre dos, por partes (numéricas como números).
        if (Pre.Length == 0 || other.Pre.Length == 0) return Pre.Length == other.Pre.Length ? 0 : Pre.Length == 0 ? 1 : -1;
        var a = Pre.Split('.');
        var b = other.Pre.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var an = int.TryParse(a[i], out var x);
            var bn = int.TryParse(b[i], out var y);
            c = an && bn ? x.CompareTo(y) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => Pre.Length == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Pre}";
}

/// <summary>Un texto del manifiesto: uno solo o uno por idioma ({"es": "…", "en": "…"}).</summary>
sealed record LocalizedText(IReadOnlyDictionary<string, string> ByLanguage)
{
    public static readonly LocalizedText Empty = new(new Dictionary<string, string>());

    /// <summary>En el idioma de la app; si no está, en inglés; si no, el primero.</summary>
    public string Text => ByLanguage.TryGetValue(L.Language, out var t) ? t
        : ByLanguage.TryGetValue("en", out var en) ? en
        : ByLanguage.Values.FirstOrDefault() ?? "";

    public override string ToString() => Text;
}

/// <summary>El sistema y la arquitectura de este proceso, como en el manifiesto: «win-x64», «osx-arm64»…</summary>
static class ExtensionPlatform
{
    public static readonly string[] Known = ["win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];

    public static string Current
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                _ => "x64",
            };
            return $"{os}-{arch}";
        }
    }
}

/// <summary>
/// Cómo corre el código de la extensión. Type: «process» (un programa por stdio), «native» (una DLL,
/// .so o .dylib con la ABI de C, cargada en un proceso aparte), «http» (un servidor; Format dice cuál) o
/// ninguno para los paquetes de solo datos. Targets: el archivo del paquete para cada plataforma.
/// Program: un programa del sistema (python, node, codex…) en vez de uno del paquete.
/// </summary>
sealed record RuntimeSpec
{
    public const string Process = "process", Native = "native", Http = "http";

    /// <summary>Los formatos de un runtime http: los de OpenAI los entienden DeepSeek, Ollama, LM Studio, OpenRouter, Groq…</summary>
    public const string OpenAiChat = "openai-chat", OpenAiTranscription = "openai-transcription",
        OpenAiSpeech = "openai-speech", ClaupetRpc = "claupet-rpc";

    public static readonly string[] Formats = [OpenAiChat, OpenAiTranscription, OpenAiSpeech, ClaupetRpc];

    public required string Type { get; init; }
    public IReadOnlyDictionary<string, string> Targets { get; init; } = new Dictionary<string, string>();
    public string? Program { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? Url { get; init; }
    public string? Format { get; init; }
    public string? Model { get; init; }
    /// <summary>La clave de la API: «bearer» o «header:&lt;Nombre&gt;», con el valor del ajuste AuthSetting.</summary>
    public string Auth { get; init; } = "none";
    public string? AuthSetting { get; init; }
    /// <summary>Una DLL «single»: Claupet no la llama desde dos hilos a la vez.</summary>
    public bool SingleThreaded { get; init; }

    public const string JsonRpc = "jsonrpc", Text = "text";

    /// <summary>
    /// Cómo habla un runtime «process»: JSON-RPC (lo normal) o «text», para un asistente que es un programa
    /// de consola cualquiera (codex exec, ollama run, llm…): Claupet le escribe la conversación por stdin y
    /// lee la respuesta de stdout, o del archivo {output} si los argumentos lo nombran. Uno por pregunta.
    /// </summary>
    public string Protocol { get; init; } = JsonRpc;

    /// <summary>El archivo del paquete para esta plataforma; null si no trae uno.</summary>
    public string? TargetFor(string platform) => Targets.TryGetValue(platform, out var t) ? t
        : Targets.TryGetValue("any", out var any) ? any : null;
}

sealed record CommandSpec(string Name, LocalizedText Description);

sealed record VoiceSpec(string Id, LocalizedText Name, string File, string? Language);

/// <summary>Una animación de la mascota: una tira PNG de cuadros del mismo tamaño, de izquierda a derecha.</summary>
sealed record PetAnimation(string File, int Ms);

sealed record PetSpec(LocalizedText Name, int Width, int Height, IReadOnlyDictionary<string, PetAnimation> Animations);

sealed record SettingSpec(string Key, string Type, LocalizedText Label, string? Default, IReadOnlyList<string> Choices)
{
    public const string Text = "string", Secret = "secret", Bool = "bool", Number = "number", Choice = "choice";
    public static readonly string[] Types = [Text, Secret, Bool, Number, Choice];
}

/// <summary>
/// El manifest.json de una extensión. Se valida entero al instalar y al cargar: un campo que no se
/// entiende en la lista cerrada (ranuras, permisos, formatos, eventos) lo rechaza, porque de eso depende
/// lo que se le deja hacer. Las claves desconocidas se ignoran, para que un manifiesto más nuevo con
/// campos opcionales siga sirviendo.
/// </summary>
sealed record ExtensionManifest
{
    /// <summary>La versión del formato del manifiesto que entiende esta app.</summary>
    public const int SchemaVersion = 1;

    /// <summary>La versión del protocolo con las extensiones (JSON-RPC, la API del anfitrión y la ABI de C).</summary>
    public const int ApiVersion = 1;

    public static readonly string[] EventNames = ["notice", "toolUse", "session", "drop", "chat"];

    /// <summary>Los nombres de los sonidos de un aviso (como en Sounds).</summary>
    public static readonly string[] SoundNames = ["terminado", "pregunta", "permiso", "fallo", "uso", "aviso"];

    static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9-]{0,38}\.[a-z0-9][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant);
    static readonly Regex KeyPattern = new(@"^[a-zA-Z][a-zA-Z0-9_]{0,31}$", RegexOptions.CultureInvariant);
    static readonly Regex CommandPattern = new(@"^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant);
    static readonly Regex ProgramPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    static readonly Regex AnimationPattern = new(@"^[a-z][a-zA-Z]{0,31}$", RegexOptions.CultureInvariant);

    public required string Id { get; init; }
    public required SemVersion Version { get; init; }
    public required LocalizedText Name { get; init; }
    public LocalizedText Description { get; init; } = LocalizedText.Empty;
    public string? Author { get; init; }
    public string? License { get; init; }
    public string? Homepage { get; init; }
    public SemVersion? MinClaupet { get; init; }
    public int Api { get; init; } = ApiVersion;
    public required IReadOnlyList<string> Provides { get; init; }
    public RuntimeSpec? Runtime { get; init; }
    public IReadOnlyList<CommandSpec> Commands { get; init; } = [];
    public IReadOnlyList<string> Events { get; init; } = [];
    public IReadOnlyList<VoiceSpec> Voices { get; init; } = [];
    public PetSpec? Pet { get; init; }
    public IReadOnlyDictionary<string, string> Sounds { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<SettingSpec> Settings { get; init; } = [];

    /// <summary>Corre código en el equipo (un programa o una DLL), no solo datos o un servidor.</summary>
    public bool RunsCode => Runtime?.Type is RuntimeSpec.Process or RuntimeSpec.Native;

    public static bool ValidId(string id) => IdPattern.IsMatch(id);

    /// <summary>
    /// Lee y valida. files: las rutas del paquete (con «/»), para comprobar que lo que nombra existe; null
    /// no lo comprueba. Devuelve el manifiesto o, si no sirve, los problemas, en el idioma de la app.
    /// </summary>
    public static (ExtensionManifest? Manifest, IReadOnlyList<string> Errors) Parse(string json, IReadOnlySet<string>? files = null)
    {
        var errors = new List<string>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { return (null, [L.T("El manifest.json no es un JSON válido: {0}", ex.Message)]); }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return (null, [L.T("El manifest.json debe ser un objeto.")]);

            var schema = r.TryGetProperty("schema", out var s) && s.TryGetInt32(out var sv) ? sv : 0;
            if (schema != SchemaVersion)
                return (null, [schema > SchemaVersion
                    ? L.T("Esta extensión necesita una versión más nueva de Claupet.")
                    : L.T("Falta «schema»: {0}.", SchemaVersion)]);

            var id = Json.Str(r, "id") ?? "";
            if (!IdPattern.IsMatch(id)) errors.Add(L.T("«id» debe ser «autor.nombre», en minúsculas, números y guiones."));
            var version = SemVersion.Parse(Json.Str(r, "version"));
            if (version is null) errors.Add(L.T("«version» debe ser como 1.0.0."));
            var name = Text(r, "name", errors, required: true, max: 60);
            var description = Text(r, "description", errors, required: false, max: 400);
            var author = Short(r, "author", 80, errors);
            var license = Short(r, "license", 80, errors);
            var homepage = Json.Str(r, "homepage");
            if (homepage is not null && !(Uri.TryCreate(homepage, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps))
                errors.Add(L.T("«homepage» debe ser una dirección https."));

            SemVersion? min = null;
            if (Json.Str(r, "claupet") is { } wanted)
            {
                min = SemVersion.Parse(wanted.TrimStart('>', '='));
                if (min is null) errors.Add(L.T("«claupet» debe ser la versión mínima, como >=2.3.0."));
            }
            var api = r.TryGetProperty("api", out var a) && a.TryGetInt32(out var av) ? av : ApiVersion;
            if (api > ApiVersion) errors.Add(L.T("Esta extensión usa una versión más nueva del protocolo ({0}); esta app entiende la {1}.", api, ApiVersion));
            else if (api < 1) errors.Add(L.T("«api» debe ser 1 o más."));

            var provides = Strings(r, "provides", errors);
            if (provides.Count == 0) errors.Add(L.T("«provides» debe decir qué trae la extensión."));
            foreach (var p in provides.Where(p => !ExtensionSlots.All.Contains(p)))
                errors.Add(L.T("«provides» no conoce «{0}».", p));
            if (provides.Count != provides.Distinct().Count()) errors.Add(L.T("«provides» repite una ranura."));

            var permissions = Strings(r, "permissions", errors);
            foreach (var p in permissions.Where(p => !ExtensionPermissions.IsValid(p)))
                errors.Add(L.T("Permiso desconocido: «{0}».", p));

            var settings = SettingsOf(r, errors);
            var runtime = RuntimeOf(r, errors, files, settings);
            if (provides.Any(ExtensionSlots.NeedsRuntime) && runtime is null)
                errors.Add(L.T("Una extensión con «{0}» necesita «runtime».", provides.First(ExtensionSlots.NeedsRuntime)));
            if (runtime is not null && !provides.Any(ExtensionSlots.NeedsRuntime))
                errors.Add(L.T("«runtime» sobra: lo que trae son solo datos."));
            if (runtime?.Type == RuntimeSpec.Http) CheckHttp(runtime, provides, permissions, errors);
            if (runtime?.Protocol == RuntimeSpec.Text && !provides.SequenceEqual([ExtensionSlots.Assistant]))
                errors.Add(L.T("«protocol»: «text» es solo para un asistente."));

            var commands = CommandsOf(r, errors);
            if (commands.Count > 0 && !provides.Contains(ExtensionSlots.Skill)) errors.Add(L.T("«commands» es de una extensión con «skill»."));
            var events = Strings(r, "events", errors);
            foreach (var e in events.Where(e => !EventNames.Contains(e))) errors.Add(L.T("Evento desconocido: «{0}».", e));
            if (events.Count > 0 && !provides.Contains(ExtensionSlots.Skill)) errors.Add(L.T("«events» es de una extensión con «skill»."));

            var voices = VoicesOf(r, errors, files);
            if (provides.Contains(ExtensionSlots.VoiceStyle) != (voices.Count > 0))
                errors.Add(L.T("«voice-style» va con al menos una voz en «voices»."));
            var pet = PetOf(r, errors, files);
            if (provides.Contains(ExtensionSlots.Pet) != (pet is not null))
                errors.Add(L.T("«pet» en «provides» va con su objeto «pet»."));
            var sounds = SoundsOf(r, errors, files);
            if (provides.Contains(ExtensionSlots.Sounds) != (sounds.Count > 0))
                errors.Add(L.T("«sounds» en «provides» va con sus sonidos."));

            if (errors.Count > 0) return (null, errors);
            return (new ExtensionManifest
            {
                Id = id, Version = version!.Value, Name = name, Description = description, Author = author, License = license,
                Homepage = homepage, MinClaupet = min, Api = api, Provides = provides, Runtime = runtime, Commands = commands,
                Events = events, Voices = voices, Pet = pet, Sounds = sounds, Permissions = permissions, Settings = settings,
            }, errors);
        }
    }

    /// <summary>Una ruta del paquete que el manifiesto nombra: relativa, con «/», y que exista (si se sabe).</summary>
    static bool FileOk(string? path, IReadOnlySet<string>? files, List<string> errors, string field, params string[] extensions)
    {
        if (string.IsNullOrEmpty(path) || PackagePaths.Normalize(path) != path)
        {
            errors.Add(L.T("«{0}» debe ser una ruta dentro del paquete, con «/».", field));
            return false;
        }
        if (extensions.Length > 0 && !extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(L.T("«{0}» debe ser un archivo {1}.", field, string.Join(" o ", extensions)));
            return false;
        }
        if (files is not null && !files.Contains(path))
        {
            errors.Add(L.T("«{0}» nombra «{1}», que no está en el paquete.", field, path));
            return false;
        }
        return true;
    }

    static LocalizedText Text(JsonElement r, string name, List<string> errors, bool required, int max)
    {
        if (!r.TryGetProperty(name, out var v))
        {
            if (required) errors.Add(L.T("Falta «{0}».", name));
            return LocalizedText.Empty;
        }
        var map = new Dictionary<string, string>();
        if (v.ValueKind == JsonValueKind.String) map[L.Language] = v.GetString()!.Trim();
        else if (v.ValueKind == JsonValueKind.Object)
            foreach (var p in v.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String || p.Name.Length is < 2 or > 8)
                {
                    errors.Add(L.T("«{0}» por idioma lleva textos, como {{\"es\": \"…\"}}.", name));
                    return LocalizedText.Empty;
                }
                map[p.Name] = p.Value.GetString()!.Trim();
            }
        if (map.Count == 0 || map.Values.Any(t => t.Length == 0 && required))
        {
            errors.Add(L.T("«{0}» está vacío.", name));
            return LocalizedText.Empty;
        }
        if (map.Values.Any(t => t.Length > max || t.Any(char.IsControl)))
            errors.Add(L.T("«{0}» es demasiado largo o trae caracteres de control (máximo {1}).", name, max));
        // Un texto suelto vale para cualquier idioma.
        if (v.ValueKind == JsonValueKind.String) map = new() { ["en"] = map[L.Language], [L.Language] = map[L.Language] };
        return new LocalizedText(map);
    }

    static string? Short(JsonElement r, string name, int max, List<string> errors)
    {
        var text = Json.Str(r, name)?.Trim();
        if (text is not null && (text.Length > max || text.Any(char.IsControl))) errors.Add(L.T("«{0}» es demasiado largo o trae caracteres de control (máximo {1}).", name, max));
        return text is { Length: > 0 } ? text : null;
    }

    static List<string> Strings(JsonElement r, string name, List<string> errors)
    {
        var list = new List<string>();
        if (!r.TryGetProperty(name, out var v)) return list;
        if (v.ValueKind != JsonValueKind.Array)
        {
            errors.Add(L.T("«{0}» debe ser una lista.", name));
            return list;
        }
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) { errors.Add(L.T("«{0}» lleva solo textos.", name)); continue; }
            list.Add(item.GetString()!);
        }
        if (list.Count > 64) errors.Add(L.T("«{0}» trae demasiados elementos.", name));
        return list;
    }

    static List<SettingSpec> SettingsOf(JsonElement r, List<string> errors)
    {
        var list = new List<SettingSpec>();
        if (!r.TryGetProperty("settings", out var v)) return list;
        if (v.ValueKind != JsonValueKind.Array) { errors.Add(L.T("«{0}» debe ser una lista.", "settings")); return list; }
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { errors.Add(L.T("Cada ajuste de «settings» es un objeto.")); continue; }
            var key = Json.Str(item, "key") ?? "";
            var type = Json.Str(item, "type") ?? SettingSpec.Text;
            if (!KeyPattern.IsMatch(key)) errors.Add(L.T("Ajuste con «key» inválida: «{0}».", key));
            if (!SettingSpec.Types.Contains(type)) errors.Add(L.T("Ajuste «{0}»: tipo desconocido «{1}».", key, type));
            var label = Text(item, "label", errors, required: true, max: 80);
            var choices = Strings(item, "choices", errors);
            if (type == SettingSpec.Choice && choices.Count == 0) errors.Add(L.T("Ajuste «{0}»: «choice» necesita «choices».", key));
            string? def = item.TryGetProperty("default", out var d) ? d.ValueKind switch
            {
                JsonValueKind.String => d.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => d.GetRawText(),
                _ => null,
            } : null;
            if (type == SettingSpec.Secret && def is not null) errors.Add(L.T("Ajuste «{0}»: una clave no lleva valor por defecto.", key));
            if (list.Any(x => x.Key == key)) errors.Add(L.T("Ajuste repetido: «{0}».", key));
            list.Add(new SettingSpec(key, type, label, def, choices));
        }
        if (list.Count > 32) errors.Add(L.T("«{0}» trae demasiados elementos.", "settings"));
        return list;
    }

    static RuntimeSpec? RuntimeOf(JsonElement r, List<string> errors, IReadOnlySet<string>? files, List<SettingSpec> settings)
    {
        if (!r.TryGetProperty("runtime", out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Object) { errors.Add(L.T("«runtime» debe ser un objeto.")); return null; }
        var type = Json.Str(v, "type") ?? "";
        var args = Strings(v, "args", errors);
        if (args.Any(x => x.Contains('\0') || x.Length > 1000)) errors.Add(L.T("«args» trae un argumento inválido."));
        var targets = new Dictionary<string, string>();
        var field = type == RuntimeSpec.Native ? "library" : "command";
        if (v.TryGetProperty(field, out var t))
        {
            if (t.ValueKind == JsonValueKind.String) targets["any"] = t.GetString()!;
            else if (t.ValueKind == JsonValueKind.Object)
                foreach (var p in t.EnumerateObject())
                {
                    if (!ExtensionPlatform.Known.Contains(p.Name) && p.Name != "any") errors.Add(L.T("Plataforma desconocida en «{0}»: «{1}».", field, p.Name));
                    else if (p.Value.ValueKind == JsonValueKind.String) targets[p.Name] = p.Value.GetString()!;
                }
            else errors.Add(L.T("«{0}» es una ruta o un objeto por plataforma.", field));
        }
        foreach (var (platform, path) in targets)
        {
            // Una biblioteca nativa, con la extensión de su sistema; un programa, cualquier archivo del paquete.
            string[] kinds = type != RuntimeSpec.Native ? []
                : platform.StartsWith("win") ? [".dll"]
                : platform.StartsWith("osx") ? [".dylib"]
                : platform.StartsWith("linux") ? [".so"]
                : [".dll", ".so", ".dylib"];
            FileOk(path, files, errors, $"runtime.{field}", kinds);
        }
        var program = Json.Str(v, "program");
        var auth = Json.Str(v, "auth") ?? "none";
        var authSetting = Json.Str(v, "authSetting");
        var spec = new RuntimeSpec
        {
            Type = type, Targets = targets, Program = program, Args = args, Url = Json.Str(v, "url"), Format = Json.Str(v, "format"),
            Model = Json.Str(v, "model"), Auth = auth, AuthSetting = authSetting,
            SingleThreaded = Json.Str(v, "threading") == "single",
            Protocol = Json.Str(v, "protocol") ?? RuntimeSpec.JsonRpc,
        };
        switch (type)
        {
            case RuntimeSpec.Process:
                if ((targets.Count > 0) == (program is not null)) errors.Add(L.T("Un runtime «process» lleva «command» (del paquete) o «program» (del sistema), uno de los dos."));
                if (program is not null && !ProgramPattern.IsMatch(program)) errors.Add(L.T("«program» es solo el nombre de un programa del sistema, sin ruta."));
                if (spec.Protocol is not (RuntimeSpec.JsonRpc or RuntimeSpec.Text)) errors.Add(L.T("«protocol» es «jsonrpc» o «text»."));
                break;
            case RuntimeSpec.Native:
                if (targets.Count == 0) errors.Add(L.T("Un runtime «native» lleva «library»."));
                if (Json.Str(v, "threading") is { } threading && threading is not ("single" or "multi")) errors.Add(L.T("«threading» es «single» o «multi»."));
                break;
            case RuntimeSpec.Http:
                if (spec.Format is null || !RuntimeSpec.Formats.Contains(spec.Format)) errors.Add(L.T("«format» debe ser uno de: {0}.", string.Join(", ", RuntimeSpec.Formats)));
                break;
            default:
                errors.Add(L.T("«runtime.type» debe ser «process», «native» o «http»."));
                break;
        }
        if (auth != "none" && auth != "bearer" && !(auth.StartsWith("header:", StringComparison.Ordinal) && Regex.IsMatch(auth[7..], "^[A-Za-z0-9-]{1,64}$")))
            errors.Add(L.T("«auth» es «none», «bearer» o «header:<Nombre>»."));
        if (auth != "none" && !settings.Any(x => x.Key == authSetting && x.Type == SettingSpec.Secret))
            errors.Add(L.T("«authSetting» debe nombrar un ajuste de tipo «secret»."));
        return spec;
    }

    /// <summary>Un servidor http: https (o http solo en este equipo), el formato que va con la ranura y la red declarada.</summary>
    static void CheckHttp(RuntimeSpec runtime, List<string> provides, List<string> permissions, List<string> errors)
    {
        var url = runtime.Url ?? "";
        // Los {setting:clave} se reemplazan al usarla: aquí se comprueba con un valor de muestra.
        var sample = Regex.Replace(url, @"\{setting:[A-Za-z0-9_]+\}", "x");
        var templated = sample != url;
        // Toda la dirección puede venir de un ajuste ({setting:baseUrl}): se prueba como https://localhost.
        var probe = Regex.Replace(url, @"^\{setting:[A-Za-z0-9_]+\}", "https://localhost");
        probe = Regex.Replace(probe, @"\{setting:[A-Za-z0-9_]+\}", "localhost");
        if (!Uri.TryCreate(templated ? probe : url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
        {
            errors.Add(L.T("«url» debe ser una dirección https."));
            return;
        }
        if (uri.Scheme == "http" && !templated && !uri.IsLoopback) errors.Add(L.T("Por http solo se permite este equipo (localhost); afuera, https."));
        if (templated ? !permissions.Contains(ExtensionPermissions.Network) && !permissions.Any(p => p.StartsWith("network:"))
            : !uri.IsLoopback && !ExtensionPermissions.AllowsHost(permissions, uri.Host))
            errors.Add(L.T("«url» va a {0}: declare «network:{0}» en «permissions».", templated ? "…" : uri.Host));
        var ok = runtime.Format switch
        {
            RuntimeSpec.OpenAiChat => provides.SequenceEqual([ExtensionSlots.Assistant]),
            RuntimeSpec.OpenAiTranscription => provides.SequenceEqual([ExtensionSlots.SpeechToText]),
            RuntimeSpec.OpenAiSpeech => provides.All(p => p is ExtensionSlots.TextToSpeech or ExtensionSlots.VoiceStyle) && provides.Contains(ExtensionSlots.TextToSpeech),
            _ => true,
        };
        if (!ok) errors.Add(L.T("El formato «{0}» no sirve para lo que dice «provides».", runtime.Format ?? ""));
    }

    static List<CommandSpec> CommandsOf(JsonElement r, List<string> errors)
    {
        var list = new List<CommandSpec>();
        if (!r.TryGetProperty("commands", out var v)) return list;
        if (v.ValueKind != JsonValueKind.Array) { errors.Add(L.T("«{0}» debe ser una lista.", "commands")); return list; }
        foreach (var item in v.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.Object ? Json.Str(item, "name") ?? "" : "";
            if (!CommandPattern.IsMatch(name)) { errors.Add(L.T("Comando con nombre inválido: «{0}».", name)); continue; }
            if (list.Any(c => c.Name == name)) errors.Add(L.T("Comando repetido: «{0}».", name));
            list.Add(new CommandSpec(name, Text(item, "description", errors, required: true, max: 120)));
        }
        if (list.Count > 32) errors.Add(L.T("«{0}» trae demasiados elementos.", "commands"));
        return list;
    }

    static List<VoiceSpec> VoicesOf(JsonElement r, List<string> errors, IReadOnlySet<string>? files)
    {
        var list = new List<VoiceSpec>();
        if (!r.TryGetProperty("voices", out var v)) return list;
        if (v.ValueKind != JsonValueKind.Array) { errors.Add(L.T("«{0}» debe ser una lista.", "voices")); return list; }
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { errors.Add(L.T("Cada voz de «voices» es un objeto.")); continue; }
            var id = Json.Str(item, "id") ?? "";
            if (!KeyPattern.IsMatch(id)) errors.Add(L.T("Voz con «id» inválido: «{0}».", id));
            if (list.Any(x => x.Id == id)) errors.Add(L.T("Voz repetida: «{0}».", id));
            var file = Json.Str(item, "file");
            FileOk(file, files, errors, "voices.file", ".json");
            var language = Json.Str(item, "language");
            if (language is not null && !Regex.IsMatch(language, "^[a-z]{2,3}(-[A-Za-z]{2,4})?$")) errors.Add(L.T("Voz «{0}»: «language» debe ser como «es» o «en».", id));
            list.Add(new VoiceSpec(id, Text(item, "name", errors, required: true, max: 40), file ?? "", language));
        }
        if (list.Count > 32) errors.Add(L.T("«{0}» trae demasiados elementos.", "voices"));
        return list;
    }

    static PetSpec? PetOf(JsonElement r, List<string> errors, IReadOnlySet<string>? files)
    {
        if (!r.TryGetProperty("pet", out var v)) return null;
        if (v.ValueKind != JsonValueKind.Object) { errors.Add(L.T("«pet» debe ser un objeto.")); return null; }
        var width = v.TryGetProperty("width", out var w) && w.TryGetInt32(out var wv) ? wv : 0;
        var height = v.TryGetProperty("height", out var h) && h.TryGetInt32(out var hv) ? hv : 0;
        if (width is < 8 or > 128 || height is < 8 or > 128) errors.Add(L.T("«pet.width» y «pet.height» van de 8 a 128 pixeles."));
        var animations = new Dictionary<string, PetAnimation>();
        if (v.TryGetProperty("animations", out var a) && a.ValueKind == JsonValueKind.Object)
            foreach (var p in a.EnumerateObject())
            {
                if (!AnimationPattern.IsMatch(p.Name)) { errors.Add(L.T("Animación con nombre inválido: «{0}».", p.Name)); continue; }
                var file = p.Value.ValueKind == JsonValueKind.Object ? Json.Str(p.Value, "file") : null;
                var ms = p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("ms", out var m) && m.TryGetInt32(out var mv) ? mv : 200;
                if (ms is < 30 or > 5000) errors.Add(L.T("Animación «{0}»: «ms» va de 30 a 5000.", p.Name));
                FileOk(file, files, errors, $"pet.animations.{p.Name}.file", ".png");
                animations[p.Name] = new PetAnimation(file ?? "", ms);
            }
        if (!animations.ContainsKey("idle")) errors.Add(L.T("La mascota necesita al menos la animación «idle»."));
        if (animations.Count > 64) errors.Add(L.T("«{0}» trae demasiados elementos.", "pet.animations"));
        return new PetSpec(Text(v, "name", errors, required: true, max: 40), width, height, animations);
    }

    static Dictionary<string, string> SoundsOf(JsonElement r, List<string> errors, IReadOnlySet<string>? files)
    {
        var map = new Dictionary<string, string>();
        if (!r.TryGetProperty("sounds", out var v)) return map;
        if (v.ValueKind != JsonValueKind.Object) { errors.Add(L.T("«sounds» debe ser un objeto.")); return map; }
        foreach (var p in v.EnumerateObject())
        {
            if (!SoundNames.Contains(p.Name)) { errors.Add(L.T("Sonido desconocido: «{0}» (son: {1}).", p.Name, string.Join(", ", SoundNames))); continue; }
            var file = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            if (FileOk(file, files, errors, $"sounds.{p.Name}", ".wav")) map[p.Name] = file!;
        }
        return map;
    }
}
