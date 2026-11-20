using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Una herramienta de un especialista: si la tiene y si el agente pide permiso antes de usarla.</summary>
sealed class SpecialistTool
{
    public bool On { get; set; }
    public bool Ask { get; set; } = true;
}

/// <summary>
/// Hasta dónde llega un especialista en una tarea que le delegan: los minutos (pasados, se detiene) y los turnos del agente
/// (donde el agente deja ponerles tope).
/// </summary>
sealed class SpecialistLimits
{
    public const int MaxMinutes = 120, MaxTurns = 200;
    public int Minutes { get; set; } = 15;
    public int Turns { get; set; } = 40;
}

/// <summary>
/// Un especialista (experimental): un agente con nombre, rol, instrucciones y herramientas. Se abre en la Terminal como
/// una sesión propia o, como los «dots», trabaja en una tarea que le delega la persona (@nombre) u otro agente
/// (SpecialistRunner), y devuelve el resultado. Las herramientas son el canvas de Claupet («canvas») y los servidores MCP
/// de las extensiones, por su nombre. Los de la persona se guardan como .json en la carpeta «especialistas»; los de una
/// extensión (la ranura «agent») son de solo lectura.
/// </summary>
sealed class Specialist
{
    public const string CanvasTool = "canvas";
    public const int MaxName = 60, MaxRole = 120, MaxWhen = 300, MaxInstructions = 8000;

    /// <summary>Los colores (los de las paletas de Claupet) y los íconos que se pueden elegir.</summary>
    public static readonly string[] Colors = ["#D97757", "#5BA3E0", "#6DB283", "#A48BE0", "#E07CA4", "#E0A84B"];
    public static readonly string[] Glyphs = ["search", "pen", "bug", "chart", "code", "book", "rocket", "flask", "shield", "spark", "globe", "heart"];

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Color { get; set; } = Colors[0];
    public string Glyph { get; set; } = Glyphs[0];
    public string Agent { get; set; } = AgentKinds.Claude;
    public string? Model { get; set; }
    public string? Effort { get; set; }
    /// <summary>La carpeta donde abre; null: la del proyecto que se esté usando.</summary>
    public string? Folder { get; set; }
    public string Instructions { get; set; } = "";
    public Dictionary<string, SpecialistTool> Tools { get; set; } = new() { [CanvasTool] = new SpecialistTool { On = true, Ask = false } };

    /// <summary>
    /// Cuándo usarlo: la frase con la que un agente decide delegarle una tarea («cuando haya que revisar un diff»). Vacía,
    /// se usa el rol.
    /// </summary>
    public string When { get; set; } = "";

    /// <summary>Edita archivos: al delegarle una tarea trabaja en su propio worktree (si la carpeta es un repositorio).</summary>
    public bool EditsFiles { get; set; }

    /// <summary>La cuenta de Claude Code con la que trabaja (su carpeta de configuración); null: la de quien lo llama.</summary>
    public string? Account { get; set; }

    public SpecialistLimits Limits { get; set; } = new();

    /// <summary>Lo que se le dice a un agente para que sepa cuándo delegarle: «cuándo usarlo» o, si no se escribió, el rol.</summary>
    public string Purpose => When.Trim() is { Length: > 0 } when ? when : Role.Trim();

    /// <summary>La extensión que lo trae («id · nombre»); null si es de la persona.</summary>
    [JsonIgnore] public string? Extension { get; set; }
    [JsonIgnore] public bool ReadOnly => Extension is not null;

    public bool Uses(string tool) => Tools.TryGetValue(tool, out var t) ? t.On : false;
    public bool AsksFor(string tool) => !Tools.TryGetValue(tool, out var t) || t.Ask;

    /// <summary>Lo que se le agrega al agente como instrucciones de sistema: quién es y cómo trabaja.</summary>
    public string Prompt()
    {
        var text = new StringBuilder();
        text.Append(L.T("Usted es el especialista «{0}».", Name.Trim()));
        if (Role.Trim() is { Length: > 0 } role) text.Append(' ').Append(L.T("Su rol: {0}", role));
        if (Instructions.Trim() is { Length: > 0 } how) text.Append("\n\n").Append(how);
        return text.ToString();
    }

    /// <summary>Una copia de la persona (para duplicar uno, también uno de una extensión).</summary>
    public Specialist Copy(string name) => new()
    {
        Id = Specialists.NewId(), Name = name, Role = Role, Color = Color, Glyph = Glyph, Agent = Agent, Model = Model, Effort = Effort,
        Folder = Folder, Instructions = Instructions, When = When, EditsFiles = EditsFiles, Account = Account,
        Limits = new SpecialistLimits { Minutes = Limits.Minutes, Turns = Limits.Turns },
        Tools = Tools.ToDictionary(t => t.Key, t => new SpecialistTool { On = t.Value.On, Ask = t.Value.Ask }),
    };

    /// <summary>Lo que se guarda, arreglado: textos cortados a su largo, color e ícono de la lista, un agente que existe.</summary>
    public void Normalize()
    {
        static string Clip(string? text, int max) => (text ?? "").Length <= max ? text ?? "" : text![..max];
        Name = Clip(Name?.Trim(), MaxName);
        Role = Clip(Role?.Replace('\n', ' ').Trim(), MaxRole);
        When = Clip(When?.Replace('\n', ' ').Trim(), MaxWhen);
        Instructions = Clip(Instructions, MaxInstructions);
        Limits ??= new();
        Limits.Minutes = Math.Clamp(Limits.Minutes, 1, SpecialistLimits.MaxMinutes);
        Limits.Turns = Math.Clamp(Limits.Turns, 1, SpecialistLimits.MaxTurns);
        if (Account is { Length: 0 }) Account = null;
        if (!Colors.Contains(Color)) Color = Colors[0];
        if (!Glyphs.Contains(Glyph)) Glyph = Glyphs[0];
        if (Agent is not (AgentKinds.Claude or AgentKinds.Codex or AgentKinds.Gemini or AgentKinds.Copilot or AgentKinds.OpenCode)) Agent = AgentKinds.Claude;
        if (Agent != AgentKinds.Claude) Model = Effort = null;
        if (Folder is { Length: 0 }) Folder = null;
        Tools ??= new();
    }
}

static class Specialists
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly Regex IdPattern = new("^[a-z0-9]{8,32}$", RegexOptions.CultureInvariant);

    /// <summary>Dónde se guardan (las pruebas ponen otra).</summary>
    public static string Folder { get; set; } = DataFolder.In("especialistas");

    /// <summary>Los de las extensiones encendidas; en la app, leídos del disco (las pruebas ponen otros).</summary>
    public static Func<IReadOnlyList<Specialist>> FromExtensions { get; set; } = ReadExtensions;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Los de la persona, por nombre. Un archivo que no se entiende se salta.</summary>
    public static List<Specialist> Mine()
    {
        var list = new List<Specialist>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Specialist>(File.ReadAllText(file), Options) is not { } s) continue;
                s.Id = Path.GetFileNameWithoutExtension(file);
                if (!IdPattern.IsMatch(s.Id)) continue;
                s.Normalize();
                list.Add(s);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { ErrorLog.Write(ex); }
        }
        return [.. list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Los de la persona y, después, los de las extensiones.</summary>
    public static List<Specialist> All()
    {
        List<Specialist> extensions;
        try { extensions = [.. FromExtensions()]; }
        catch (Exception ex) { ErrorLog.Write(ex); extensions = []; }
        return [.. Mine(), .. extensions];
    }

    public static void Save(Specialist s)
    {
        if (s.ReadOnly) return;
        if (!IdPattern.IsMatch(s.Id)) s.Id = NewId();
        s.Normalize();
        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, s.Id + ".json");
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(s, Options), new UTF8Encoding(false));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    public static void Delete(Specialist s)
    {
        if (s.ReadOnly || !IdPattern.IsMatch(s.Id)) return;
        try { File.Delete(Path.Combine(Folder, s.Id + ".json")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    /// <summary>El especialista como .json, para compartirlo.</summary>
    public static string Export(Specialist s) => JsonSerializer.Serialize(s, Options);

    // ── Para los agentes ────────────────────────────────────────────────────────────────────

    /// <summary>Los servidores de las extensiones que ese especialista usa (sin especialista, todos).</summary>
    public static IReadOnlyList<McpServer> ServersFor(Specialist? s, IReadOnlyList<McpServer> all) =>
        s is null ? all : [.. all.Where(x => s.Uses(x.Name))];

    /// <summary>El canvas de Claupet va, salvo que el especialista lo haya quitado.</summary>
    public static bool UsesCanvas(Specialist? s) => s is null || s.Uses(Specialist.CanvasTool);

    /// <summary>Las instrucciones en un archivo, para «--append-system-prompt-file»: el texto no sobrevive a cmd.exe. Null si no se pudo.</summary>
    public static string? PromptFile(Specialist s)
    {
        try
        {
            var dir = Path.Combine(Folder, "sesiones");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, (IdPattern.IsMatch(s.Id) ? s.Id : "ext-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s.Id)))[..10].ToLowerInvariant()) + ".md");
            File.WriteAllText(file, s.Prompt(), new UTF8Encoding(false));
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); return null; }
    }

    // ── Los de las extensiones ──────────────────────────────────────────────────────────────

    static IReadOnlyList<Specialist> ReadExtensions() =>
        [.. ExtensionsOnDisk.Enabled(ExtensionSlots.Agent).SelectMany(e => FromManifest(e.Extension))];

    /// <summary>Los especialistas que trae una extensión, de solo lectura.</summary>
    public static IEnumerable<Specialist> FromManifest(InstalledExtension ext) => ext.Manifest.Agents.Select(a => new Specialist
    {
        Id = $"{ext.Id}/{a.Id}",
        Name = a.Name.Text,
        Role = a.Role.Text,
        Color = a.Color ?? Specialist.Colors[0],
        Glyph = a.Glyph ?? Specialist.Glyphs[0],
        Agent = a.Agent,
        Model = a.Model,
        Effort = a.Effort,
        Instructions = a.Instructions,
        When = a.When?.Text ?? "",
        Tools = a.Tools.ToDictionary(t => t, _ => new SpecialistTool { On = true, Ask = true }),
        Extension = $"{ext.Id} · {ext.Manifest.Name.Text}",
    });
}
