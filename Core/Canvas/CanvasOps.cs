using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Una operación sobre un canvas. Las IA no reescriben el documento entero: dicen qué cambia («inserta una sección de
/// código después de s2», «en s3 cambia este párrafo por este otro»), y así se editan a la vez sin pisarse.
/// - insert: una sección nueva (Kind, Language, Title, Content) después de After («start»: al principio; sin After: al final).
/// - replace: el contenido entero de Id (y, si vienen, Title, Language o Kind).
/// - edit: en Id, cambia Find por Replace. Find tiene que estar una sola vez (o All, para todas).
/// - append: agrega Content al final de Id (lo que se va escribiendo por partes).
/// - delete: borra Id (y sus comentarios).
/// - move: pone Id después de After («start»: al principio).
/// - title: cambia el título del canvas.
/// - comment: un comentario (Text) sobre Section (Quote, si es sobre un pedazo; Parent, si responde a otro).
/// - resolve: marca el comentario Id como resuelto.
/// - plan: liga el canvas a un plan (Content: la ruta de un *.plan.json; relativa, a la carpeta del canvas). Vacío: lo desliga.
/// </summary>
sealed record CanvasOp(
    string Op, string? Id = null, string? After = null, string? Kind = null, string? Language = null, string? Title = null,
    string? Content = null, string? Find = null, string? Replace = null, bool All = false, string? Section = null,
    string? Quote = null, string? Text = null, string? Parent = null)
{
    public static readonly string[] Names = ["insert", "replace", "edit", "append", "delete", "move", "title", "comment", "resolve", "plan"];

    /// <summary>Lee una operación del JSON de un modelo (los nombres en snake_case o camelCase dan igual).</summary>
    public static CanvasOp Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new CanvasException(L.T("Cada operación es un objeto JSON."));
        string? S(params string[] names)
        {
            foreach (var name in names)
                if (e.TryGetProperty(name, out var v))
                    return v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString(),
                        JsonValueKind.Null => null,
                        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToString(),
                        // Datos que vienen como objeto (un JSON dentro de una sección de datos): se guardan como texto.
                        _ => v.GetRawText(),
                    };
            return null;
        }
        var all = e.TryGetProperty("all", out var a) && a.ValueKind == JsonValueKind.True;
        return new CanvasOp(S("op", "type") ?? "", S("id", "section_id", "sectionId"), S("after"), S("kind"), S("language", "lang"),
            S("title"), S("content"), S("find", "old", "old_text"), S("replace", "new", "new_text"), all, S("section", "section_id", "sectionId"),
            S("quote"), S("text"), S("parent", "parent_id", "parentId"));
    }

    public static List<CanvasOp> ParseAll(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? [.. e.EnumerateArray().Select(Parse)] : [Parse(e)];
}

/// <summary>Una operación que no se puede hacer, con un mensaje que le sirve al modelo para corregirla.</summary>
sealed class CanvasException(string message) : Exception(message);

/// <summary>El resultado de aplicar operaciones: el canvas nuevo y lo que cambió.</summary>
sealed record CanvasApplied(CanvasDoc Doc, IReadOnlyList<string> Sections, string Summary);

static class CanvasEngine
{
    public const int MaxSections = 400;
    public const int MaxContent = 4 << 20;
    public const int MaxTitle = 200;

    /// <summary>
    /// Aplica las operaciones, todas o ninguna, sobre una copia: si una falla, el canvas queda como estaba y el error
    /// dice cuál fue (para que el modelo la corrija). No sube la versión: eso lo hace CanvasStore al guardar.
    /// </summary>
    public static CanvasApplied Apply(CanvasDoc original, IReadOnlyList<CanvasOp> ops, string? author, DateTimeOffset now)
    {
        if (ops.Count == 0) throw new CanvasException(L.T("No vino ninguna operación."));
        var doc = original.Clone();
        var touched = new List<string>();
        var summary = new List<string>();
        for (var i = 0; i < ops.Count; i++)
        {
            try { summary.Add(Apply(doc, ops[i], author, now, touched)); }
            catch (CanvasException ex) when (ops.Count > 1)
            {
                throw new CanvasException(L.T("Operación {0} ({1}): {2}", i + 1, ops[i].Op, ex.Message));
            }
        }
        return new(doc, touched.Distinct().ToList(), string.Join("; ", summary.Distinct()));
    }

    static string Apply(CanvasDoc doc, CanvasOp op, string? author, DateTimeOffset now, List<string> touched)
    {
        switch (op.Op.ToLowerInvariant())
        {
            case "insert":
            {
                if (doc.Sections.Count >= MaxSections) throw new CanvasException(L.T("El canvas ya tiene el máximo de {0} secciones.", MaxSections));
                var kind = op.Kind ?? CanvasKinds.Markdown;
                if (!CanvasKinds.IsValid(kind)) throw new CanvasException(L.T("Tipo «{0}» no válido; puede ser: {1}.", kind, string.Join(", ", CanvasKinds.All)));
                var section = new CanvasSection
                {
                    Id = NextId(doc.Sections.Select(s => s.Id), "s"),
                    Kind = kind,
                    Language = Clean(op.Language),
                    Title = Title(op.Title),
                    Content = Content(op.Content ?? ""),
                    Author = author,
                    Updated = now,
                };
                doc.Sections.Insert(Position(doc, op.After), section);
                touched.Add(section.Id);
                return L.T("agregó {0}", section.Id);
            }
            case "replace":
            {
                var section = Find(doc, op.Id);
                if (op.Content is null && op.Title is null && op.Language is null && op.Kind is null)
                    throw new CanvasException(L.T("replace necesita content (o title, language, kind)."));
                if (op.Kind is { } kind)
                {
                    if (!CanvasKinds.IsValid(kind)) throw new CanvasException(L.T("Tipo «{0}» no válido; puede ser: {1}.", kind, string.Join(", ", CanvasKinds.All)));
                    section.Kind = kind;
                }
                if (op.Content is { } content) section.Content = Content(content);
                if (op.Title is not null) section.Title = Title(op.Title);
                if (op.Language is not null) section.Language = Clean(op.Language);
                return Touch(section, author, now, touched, L.T("reescribió {0}", section.Id));
            }
            case "edit":
            {
                var section = Find(doc, op.Id);
                if (string.IsNullOrEmpty(op.Find)) throw new CanvasException(L.T("edit necesita find (el texto exacto que se cambia)."));
                var replace = op.Replace ?? "";
                var count = Count(section.Content, op.Find);
                // Los modelos a veces copian el texto con otros saltos de línea: se prueba también con los del documento.
                var find = op.Find;
                if (count == 0 && find.Contains('\n'))
                {
                    var alt = section.Content.Contains("\r\n") ? find.ReplaceLineEndings("\r\n") : find.ReplaceLineEndings("\n");
                    if ((count = Count(section.Content, alt)) > 0) find = alt;
                }
                if (count == 0) throw new CanvasException(L.T("No encontré ese texto en {0}. Lea la sección otra vez y copie el texto exacto.", section.Id));
                if (count > 1 && !op.All)
                    throw new CanvasException(L.T("Ese texto está {0} veces en {1}: agregue más contexto para que sea único, o all: true.", count, section.Id));
                section.Content = Content(op.All ? section.Content.Replace(find, replace, StringComparison.Ordinal) : ReplaceOnce(section.Content, find, replace));
                return Touch(section, author, now, touched, L.T("editó {0}", section.Id));
            }
            case "append":
            {
                var section = Find(doc, op.Id);
                section.Content = Content(section.Content + (op.Content ?? ""));
                return Touch(section, author, now, touched, L.T("editó {0}", section.Id));
            }
            case "delete":
            {
                var section = Find(doc, op.Id);
                doc.Sections.Remove(section);
                doc.Comments.RemoveAll(c => c.SectionId == section.Id);
                touched.Add(section.Id);
                return L.T("borró {0}", section.Id);
            }
            case "move":
            {
                var section = Find(doc, op.Id);
                if (op.After == section.Id) throw new CanvasException(L.T("Una sección no puede ir después de sí misma."));
                doc.Sections.Remove(section);
                doc.Sections.Insert(Position(doc, op.After), section);
                touched.Add(section.Id);
                return L.T("movió {0}", section.Id);
            }
            case "title":
            {
                var title = Title(op.Title ?? op.Content ?? op.Text);
                if (string.IsNullOrWhiteSpace(title)) throw new CanvasException(L.T("title necesita el título nuevo."));
                doc.Title = title;
                return L.T("cambió el título");
            }
            case "comment":
            {
                var sectionId = op.Section ?? op.Id;
                var section = Find(doc, sectionId);
                if (string.IsNullOrWhiteSpace(op.Text)) throw new CanvasException(L.T("comment necesita text."));
                if (op.Parent is { } parent && doc.Comments.All(c => c.Id != parent))
                    throw new CanvasException(L.T("No hay un comentario {0}.", parent));
                var comment = new CanvasComment
                {
                    Id = NextId(doc.Comments.Select(c => c.Id), "c"),
                    SectionId = section.Id,
                    ParentId = op.Parent,
                    Quote = Clean(op.Quote),
                    Text = op.Text.Trim(),
                    Author = author,
                    Created = now,
                };
                doc.Comments.Add(comment);
                touched.Add(section.Id);
                return L.T("comentó en {0}", section.Id);
            }
            case "resolve":
            {
                var comment = doc.Comments.FirstOrDefault(c => c.Id == op.Id) ?? throw new CanvasException(L.T("No hay un comentario {0}.", op.Id ?? "?"));
                comment.Resolved = true;
                foreach (var reply in doc.Comments.Where(c => c.ParentId == comment.Id)) reply.Resolved = true;
                touched.Add(comment.SectionId);
                return L.T("resolvió {0}", comment.Id);
            }
            case "plan":
            {
                if (string.IsNullOrWhiteSpace(op.Content))
                {
                    doc.Plan = null;
                    return L.T("quitó el plan");
                }
                var path = op.Content.Trim();
                if (!System.IO.Path.IsPathFullyQualified(path))
                {
                    if (doc.Folder is null) throw new CanvasException(L.T("plan necesita la ruta completa del archivo."));
                    path = System.IO.Path.GetFullPath(path, doc.Folder);
                }
                if (!Plan.IsPlanFile(path)) throw new CanvasException(L.T("Un plan es un archivo {0}.", Plan.Extension));
                if (!File.Exists(path)) throw new CanvasException(L.T("No está el plan {0}.", path));
                doc.Plan = path;
                return L.T("ligó el plan {0}", System.IO.Path.GetFileName(path));
            }
            default:
                throw new CanvasException(L.T("Operación «{0}» desconocida; puede ser: {1}.", op.Op, string.Join(", ", CanvasOp.Names)));
        }
    }

    static string Touch(CanvasSection section, string? author, DateTimeOffset now, List<string> touched, string summary)
    {
        section.Author = author;
        section.Updated = now;
        touched.Add(section.Id);
        return summary;
    }

    static CanvasSection Find(CanvasDoc doc, string? id)
    {
        if (string.IsNullOrEmpty(id)) throw new CanvasException(L.T("Falta id (la sección: s1, s2…)."));
        return doc.Section(id) ?? throw new CanvasException(L.T("No hay una sección {0}. Las que hay: {1}.", id,
            doc.Sections.Count == 0 ? L.T("ninguna") : string.Join(", ", doc.Sections.Select(s => s.Id))));
    }

    static int Position(CanvasDoc doc, string? after)
    {
        if (after is null or "" or "end") return doc.Sections.Count;
        if (after == "start") return 0;
        var index = doc.Sections.FindIndex(s => s.Id == after);
        if (index < 0) throw new CanvasException(L.T("No hay una sección {0} para poner algo después.", after));
        return index + 1;
    }

    /// <summary>El que sigue al mayor: «s7» después de «s6», aunque se haya borrado alguno (un id no se reutiliza en el canvas).</summary>
    public static string NextId(IEnumerable<string> ids, string prefix)
    {
        var max = 0;
        foreach (var id in ids)
            if (id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), out var n) && n > max) max = n;
        return prefix + (max + 1);
    }

    static int Count(string text, string find)
    {
        var count = 0;
        for (var i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + find.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    static string ReplaceOnce(string text, string find, string replace)
    {
        var i = text.IndexOf(find, StringComparison.Ordinal);
        return text[..i] + replace + text[(i + find.Length)..];
    }

    static string Content(string content) =>
        content.Length > MaxContent ? throw new CanvasException(L.T("La sección pasa del máximo de {0} MB.", MaxContent >> 20)) : content;

    static string? Title(string? title) => Clean(title) is { } t && t.Length > MaxTitle ? t[..MaxTitle] : Clean(title);

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Lo que se le muestra al modelo de una operación hecha: para el resultado de canvas_edit.</summary>
    public static JsonObject Describe(CanvasApplied applied) => new()
    {
        ["version"] = applied.Doc.Version,
        ["summary"] = applied.Summary,
        ["sections"] = new JsonArray([.. applied.Sections.Select(s => (JsonNode)s)]),
    };
}
