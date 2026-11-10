using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Dónde están los contratos y cómo se juntan: los de Claupet (dentro del ejecutable), los del perfil y los del
/// proyecto (.claupet/contratos, buscado hacia arriba como git). Gana la capa más alta según su modo.
/// </summary>
static class ContractStore
{
    public const string ProjectSubfolder = ".claupet/contratos";
    public const string Generated = ".generado";
    /// <summary>Para las pruebas y para quien quiera otro perfil.</summary>
    public const string ProfileVariable = "CLAUPET_CONTRATOS_PERFIL";
    const string ResourcePrefix = "contratos/";

    /// <summary>Carpetas de un proyecto que no son contratos.</summary>
    static readonly string[] NotContracts = [Generated, "cambios", "propuestas", "feedback"];

    /// <summary>La carpeta .claupet/contratos más cercana subiendo desde «from», o null.</summary>
    public static string? FindProject(string from)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(from)); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".claupet", "contratos");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>La raíz del proyecto (donde está .claupet) a partir de la carpeta de contratos.</summary>
    public static string ProjectRoot(string contractsFolder) => Path.GetDirectoryName(Path.GetDirectoryName(contractsFolder)!)!;

    public static string ProfileFolder()
    {
        if (Environment.GetEnvironmentVariable(ProfileVariable) is { Length: > 0 } custom) return custom;
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claupet", "contratos");
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(config, "claupet", "contratos");
    }

    /// <summary>Los paquetes por defecto que vienen dentro del ejecutable (Core/Contratos/Paquetes).</summary>
    public static List<ContractDoc> LoadBuiltIn()
    {
        var assembly = typeof(ContractStore).Assembly;
        var docs = new List<ContractDoc>();
        foreach (var name in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            var normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !normalized.EndsWith(".md", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            docs.Add(ContractParser.Parse(reader.ReadToEnd(), "claupet:" + normalized[ResourcePrefix.Length..], ContractLayer.Claupet));
        }
        return docs;
    }

    /// <summary>Los .md de una carpeta de contratos (sin .generado, cambios, propuestas ni feedback).</summary>
    public static List<ContractDoc> LoadFolder(string? folder, ContractLayer layer)
    {
        var docs = new List<ContractDoc>();
        if (folder is null || !Directory.Exists(folder)) return docs;
        foreach (var file in Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (NotContracts.Any(n => relative.StartsWith(n + "/", StringComparison.Ordinal))) continue;
            if (relative.StartsWith('.') || relative.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
            // Las traducciones (escritura.en.md) no son contratos aparte.
            if (Path.GetFileNameWithoutExtension(file).Contains('.')) continue;
            docs.Add(ContractParser.Parse(File.ReadAllText(file), file, layer));
        }
        return docs;
    }

    /// <summary>Carga y junta las tres capas a partir de una carpeta de trabajo.</summary>
    public static ContractSet Load(string workingFolder)
    {
        var project = FindProject(workingFolder);
        var profile = ProfileFolder();
        return Resolve(LoadBuiltIn(), LoadFolder(profile, ContractLayer.Perfil), LoadFolder(project, ContractLayer.Proyecto), project, profile);
    }

    /// <summary>Junta las capas: cada una, de abajo hacia arriba, crea, amplía, reemplaza o apaga por id.</summary>
    public static ContractSet Resolve(IEnumerable<ContractDoc> builtIn, IEnumerable<ContractDoc> profile, IEnumerable<ContractDoc> project, string? projectFolder, string? profileFolder)
    {
        var set = new ContractSet { ProjectFolder = projectFolder, ProfileFolder = profileFolder };
        var byId = new Dictionary<string, ResolvedContract>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var layer in new[] { builtIn, profile, project })
        {
            var seenInLayer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in layer)
            {
                set.Issues.AddRange(doc.Issues);
                // Sin cabecera o sin id no hay con qué juntarlo: ya quedó el problema anotado.
                if (doc.Id.Length == 0) continue;
                if (!seenInLayer.Add(doc.Id))
                {
                    set.Issues.Add(new(ContractSeverity.Error, doc.Path, $"El id «{doc.Id}» ya está en otro archivo de la misma capa."));
                    continue;
                }
                byId.TryGetValue(doc.Id, out var existing);
                switch (doc.Mode)
                {
                    case ContractMode.Nuevo when existing is not null:
                        set.Issues.Add(new(ContractSeverity.Error, doc.Path,
                            $"El id «{doc.Id}» ya existe en la capa {Name(existing.Layer)}: usa «modo: amplia», «reemplaza» o «apaga», o cambia el id."));
                        break;
                    case ContractMode.Nuevo:
                    case ContractMode.Reemplaza:
                        if (existing is null && doc.Mode == ContractMode.Reemplaza)
                            set.Issues.Add(new(ContractSeverity.Aviso, doc.Path, $"«modo: reemplaza» pero no hay un «{doc.Id}» debajo: queda como nuevo."));
                        var fresh = FromDoc(doc);
                        if (existing is not null) fresh.Sources.InsertRange(0, existing.Sources);
                        if (existing is null) order.Add(doc.Id);
                        byId[doc.Id] = fresh;
                        break;
                    case ContractMode.Amplia when existing is null:
                    case ContractMode.Apaga when existing is null:
                        set.Issues.Add(new(ContractSeverity.Aviso, doc.Path, $"«modo: {ModeName(doc.Mode)}» pero no hay un «{doc.Id}» debajo: no hace nada."));
                        break;
                    case ContractMode.Amplia:
                        Extend(existing!, doc);
                        break;
                    case ContractMode.Apaga:
                        existing!.Enabled = false;
                        existing.OffBy = doc.Layer;
                        existing.Sources.Add((doc.Layer, doc.Path, doc.Mode));
                        break;
                }
                if (byId.TryGetValue(doc.Id, out var target) && doc.OffRules.Count > 0) TurnOffRules(target, doc, set.Issues);
            }
        }
        foreach (var id in order) set.Contracts.Add(byId[id]);
        set.Contracts.Sort((a, b) => TypeOrder(a.Type) != TypeOrder(b.Type) ? TypeOrder(a.Type) - TypeOrder(b.Type) : string.CompareOrdinal(a.Id, b.Id));
        return set;
    }

    static ResolvedContract FromDoc(ContractDoc doc)
    {
        var c = new ResolvedContract
        {
            Id = doc.Id,
            Type = doc.Type,
            Title = doc.Title,
            Summary = doc.Summary,
            AppliesTo = [.. doc.AppliesTo],
            Tags = [.. doc.Tags],
            Required = doc.Required ?? false,
            Layer = doc.Layer,
            Rules = [.. doc.Rules],
            Body = doc.Body,
        };
        c.Sources.Add((doc.Layer, doc.Path, doc.Mode));
        return c;
    }

    /// <summary>Suma reglas (una con el mismo id cambia la de abajo) y cambia los campos que traiga.</summary>
    static void Extend(ResolvedContract target, ContractDoc doc)
    {
        if (doc.Title.Length > 0) target.Title = doc.Title;
        if (doc.Summary.Length > 0) target.Summary = doc.Summary;
        if (doc.AppliesTo.Count > 0) target.AppliesTo = [.. doc.AppliesTo];
        foreach (var tag in doc.Tags) if (!target.Tags.Contains(tag)) target.Tags.Add(tag);
        if (doc.Required is { } required) target.Required = required;
        foreach (var rule in doc.Rules)
        {
            var index = target.Rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0) target.Rules[index] = rule;
            else target.Rules.Add(rule);
        }
        if (doc.Body.Length > 0) target.Body = (target.Body + "\n\n" + doc.Body).Trim();
        target.Sources.Add((doc.Layer, doc.Path, doc.Mode));
    }

    static void TurnOffRules(ResolvedContract target, ContractDoc doc, List<ContractIssue> issues)
    {
        foreach (var id in doc.OffRules)
        {
            var index = target.Rules.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                issues.Add(new(ContractSeverity.Aviso, doc.Path, $"«apaga_reglas» nombra «{id}», que no está en «{target.Id}»."));
                continue;
            }
            target.Rules[index] = target.Rules[index] with { Off = true };
        }
    }

    static int TypeOrder(string type) => Array.IndexOf(ContractDoc.Types, type) is var i and >= 0 ? i : ContractDoc.Types.Length;

    public static string Name(ContractLayer layer) => layer switch
    {
        ContractLayer.Claupet => "Claupet",
        ContractLayer.Perfil => "perfil",
        _ => "proyecto",
    };

    public static string ModeName(ContractMode mode) => mode switch
    {
        ContractMode.Amplia => "amplia",
        ContractMode.Reemplaza => "reemplaza",
        ContractMode.Apaga => "apaga",
        _ => "nuevo",
    };

    /// <summary>Si una ruta relativa (con «/») cae en alguno de los patrones de «aplica».</summary>
    public static bool Applies(ResolvedContract contract, string relativePath) =>
        contract.AppliesTo.Any(pattern => ContractGlob.IsMatch(pattern, relativePath));
}

/// <summary>Patrones de rutas: «**» cualquier cantidad de carpetas, «*» dentro de un nombre, «?» un carácter.</summary>
static class ContractGlob
{
    static readonly Dictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string pattern, string path)
    {
        path = path.Replace('\\', '/').TrimStart('.', '/');
        Regex regex;
        lock (Cache)
            if (!Cache.TryGetValue(pattern, out regex!))
                Cache[pattern] = regex = new Regex(ToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return regex.IsMatch(path);
    }

    internal static string ToRegex(string pattern)
    {
        pattern = pattern.Replace('\\', '/').TrimStart('.', '/');
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                var slash = i + 2 < pattern.Length && pattern[i + 2] == '/';
                sb.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.Append('$').ToString();
    }
}
