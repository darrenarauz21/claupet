using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claupet;

/// <summary>
/// Los tipos de sección de un canvas. Cada uno es texto (lo que un modelo de cualquier proveedor sabe escribir y leer);
/// lo que cambia es cómo se muestra y cómo se exporta.
/// </summary>
static class CanvasKinds
{
    /// <summary>Markdown: texto con títulos, listas, tablas, enlaces.</summary>
    public const string Markdown = "markdown";
    /// <summary>Código en cualquier lenguaje (Language: «python», «csharp»…).</summary>
    public const string Code = "code";
    /// <summary>Una página o app en HTML (con su CSS y JavaScript): se ve en el navegador, aislada.</summary>
    public const string Html = "html";
    /// <summary>Un diagrama en texto (Language: «mermaid», por ahora el único que se dibuja).</summary>
    public const string Diagram = "diagram";
    /// <summary>Un gráfico vectorial (SVG).</summary>
    public const string Svg = "svg";
    /// <summary>Datos (Language: «json» o «csv»): tablas, configuraciones, resultados.</summary>
    public const string Data = "data";
    /// <summary>Una imagen: una URL https, una ruta de archivo o un «data:image/…;base64,…».</summary>
    public const string Image = "image";

    /// <summary>Un plan de proyecto (JSON con etapas, tareas e hitos, el formato de Core/Plan.cs): se ve como resumen, tablero y cronograma.</summary>
    public const string Plan = "plan";

    public static readonly string[] All = [Markdown, Code, Html, Diagram, Svg, Data, Image, Plan];

    public static bool IsValid(string? kind) => kind is not null && All.Contains(kind);

    /// <summary>El nombre que se muestra.</summary>
    public static string Label(string kind) => kind switch
    {
        Markdown => L.T("Texto"),
        Code => L.T("Código"),
        Html => "HTML",
        Diagram => L.T("Diagrama"),
        Svg => "SVG",
        Data => L.T("Datos"),
        Image => L.T("Imagen"),
        Plan => L.T("Plan"),
        _ => kind,
    };
}

/// <summary>Un bloque del canvas: un pedazo con su tipo, que se edita y se versiona por separado.</summary>
sealed class CanvasSection
{
    /// <summary>Corto y estable («s1», «s2»…): es lo que los modelos usan para decir qué sección cambian.</summary>
    public string Id { get; set; } = "";
    public string Kind { get; set; } = CanvasKinds.Markdown;
    public string? Language { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = "";
    /// <summary>Quién la cambió por última vez («user» es la persona; si no, el agente: «Claude Code», «Codex»…).</summary>
    public string? Author { get; set; }
    public DateTimeOffset Updated { get; set; }
}

/// <summary>Un comentario sobre una sección (o una respuesta a otro, con ParentId), de la persona o de un agente.</summary>
sealed class CanvasComment
{
    public string Id { get; set; } = "";
    public string SectionId { get; set; } = "";
    public string? ParentId { get; set; }
    /// <summary>El pedazo de texto al que se refiere, si es uno en particular.</summary>
    public string? Quote { get; set; }
    public string Text { get; set; } = "";
    public string? Author { get; set; }
    public DateTimeOffset Created { get; set; }
    public bool Resolved { get; set; }
}

/// <summary>Un cambio guardado: la versión a la que llevó, quién, cuándo y qué tocó.</summary>
sealed class CanvasChange
{
    public int Version { get; set; }
    public string? Author { get; set; }
    public DateTimeOffset Time { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Sections { get; set; } = [];
}

/// <summary>
/// Un canvas: un documento vivo, hecho de secciones tipadas, que la persona y cualquier agente editan a la vez. Se
/// guarda como JSON (CanvasStore); cada cambio sube Version y deja una copia de la anterior en el historial.
/// </summary>
sealed class CanvasDoc
{
    /// <summary>La versión del formato del archivo (no la del contenido): para leer bien los de mañana.</summary>
    public int Format { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>La carpeta del proyecto con el que se hizo, si se hizo desde uno.</summary>
    public string? Folder { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
    public int Version { get; set; }
    public List<CanvasSection> Sections { get; set; } = [];
    public List<CanvasComment> Comments { get; set; } = [];
    /// <summary>Los últimos cambios (hasta MaxChanges), el más nuevo al final.</summary>
    public List<CanvasChange> Changes { get; set; } = [];

    public const int MaxChanges = 100;

    public CanvasSection? Section(string id) => Sections.FirstOrDefault(s => s.Id == id);

    public CanvasDoc Clone() => JsonSerializer.Deserialize<CanvasDoc>(JsonSerializer.Serialize(this, CanvasJson.Options), CanvasJson.Options)!;
}

static class CanvasJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>Quién es «user» al mostrarlo.</summary>
    public const string User = "user";

    public static string AuthorLabel(string? author) => author is null or User ? L.T("Usted") : author;
}
