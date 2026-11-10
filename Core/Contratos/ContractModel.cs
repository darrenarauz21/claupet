namespace Claupet;

/// <summary>
/// Contratos Claupet (docs/contratos/especificacion.md): las reglas viven en minicontratos Markdown con cabecera, en tres
/// capas. Este archivo es el modelo; lo usan el motor de la app y el comando cpt (Claupet.Cli), que solo enlaza
/// Core/Contratos, así que nada de aquí depende del resto de Core.
/// </summary>
enum ContractLayer { Claupet, Perfil, Proyecto }

/// <summary>Qué hace un contrato de una capa alta con el del mismo id de abajo.</summary>
enum ContractMode { Nuevo, Amplia, Reemplaza, Apaga }

enum ContractSeverity { Error, Aviso }

/// <summary>Una regla: id corto entre corchetes, el texto y, si se puede comprobar, sus checks.</summary>
sealed record ContractRule(string Id, string Text, IReadOnlyList<string> Checks)
{
    public ContractLayer Layer { get; init; }
    /// <summary>Apagada por una capa de arriba (apaga_reglas).</summary>
    public bool Off { get; init; }
}

/// <summary>Un problema de formato o de capas, con el archivo donde está.</summary>
sealed record ContractIssue(ContractSeverity Severity, string File, string Message);

/// <summary>Un minicontrato tal como está en su archivo, antes de juntar las capas.</summary>
sealed class ContractDoc
{
    public static readonly string[] Types = ["base", "calidad", "estilo", "capacidad", "mapa", "leccion", "perfil"];

    public required string Path { get; init; }
    public required ContractLayer Layer { get; init; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> AppliesTo { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    /// <summary>null: no lo dice (recomendada, o lo que traiga la capa de abajo).</summary>
    public bool? Required { get; set; }
    public ContractMode Mode { get; set; } = ContractMode.Nuevo;
    public List<string> OffRules { get; set; } = [];
    public string? Language { get; set; }
    public string? Updated { get; set; }
    public int? Format { get; set; }
    /// <summary>Lo demás de la cabecera (origen, visto_en de las lecciones…), tal cual.</summary>
    public Dictionary<string, string> Extra { get; } = new(StringComparer.Ordinal);
    public List<ContractRule> Rules { get; } = [];
    /// <summary>El Markdown sin la cabecera.</summary>
    public string Body { get; set; } = "";
    /// <summary>Lo que se avisó al leerlo (cabecera rota, campos que faltan…).</summary>
    public List<ContractIssue> Issues { get; } = [];
}

/// <summary>Un contrato ya resuelto por capas: lo que de verdad aplica.</summary>
sealed class ResolvedContract
{
    public required string Id { get; init; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> AppliesTo { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public bool Required { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>La capa que lo define (la de su modo nuevo o reemplaza).</summary>
    public ContractLayer Layer { get; set; }
    /// <summary>Por qué capa está apagado, si lo está.</summary>
    public ContractLayer? OffBy { get; set; }
    public List<ContractRule> Rules { get; set; } = [];
    public string Body { get; set; } = "";
    /// <summary>Cada archivo que aporta algo, de abajo hacia arriba.</summary>
    public List<(ContractLayer Layer, string Path, ContractMode Mode)> Sources { get; } = [];

    public IEnumerable<ContractRule> ActiveRules => Rules.Where(r => !r.Off);

    /// <summary>Una estimación de los tokens que ocupa leerlo (unos 4 caracteres por token).</summary>
    public int Tokens => (Title.Length + Summary.Length + Body.Length + Rules.Sum(r => r.Text.Length + r.Id.Length + 6)) / 4 + 20;
}

/// <summary>Todos los contratos resueltos de un proyecto, con los problemas encontrados.</summary>
sealed class ContractSet
{
    public required string? ProjectFolder { get; init; }
    public required string? ProfileFolder { get; init; }
    public List<ResolvedContract> Contracts { get; } = [];
    public List<ContractIssue> Issues { get; } = [];

    public ResolvedContract? Find(string id) => Contracts.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public bool HasErrors => Issues.Any(i => i.Severity == ContractSeverity.Error);
}
