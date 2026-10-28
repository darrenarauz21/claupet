using System.Text.Json;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// El reparto de Ajustes › Respuestas: cada pregunta la responde primero quien se eligió para su tipo y, si
/// no la entiende o no está disponible, pasa al siguiente (sin IA → modelo local → nube) hasta donde diga «Si
/// no entiende». El tipo lo dan las reglas sin IA (AnswerRules); si ninguna sirve y el modelo local está
/// descargado, lo entiende él con un JSON de esquema cerrado: solo puede elegir tipos y acciones de la lista.
/// Con «Local» como modelo de la mascota o la isla, la nube no se usa: lo que no responde la app, lo
/// responde el modelo local. Un archivo soltado no se reparte.
/// </summary>
static class Answers
{
    /// <summary>El uso de Claude dicho en una o dos frases (lo pone la app; null si todavía no lo sabe).</summary>
    public static Func<string?>? Usage { get; set; }

    /// <summary>Lo que se abriría con «abrir» (una app del menú Inicio, una URL o una carpeta); null si no existe.</summary>
    public static Func<string, string?>? OpenTarget { get; set; }

    const string Instant = "al instante";

    public static async Task<AssistantResult> AskAsync(AssistantRequest request, AppSettings settings, Action<string>? onText = null,
        Action<AssistantStage>? onStage = null, CancellationToken cancel = default)
    {
        var onlyLocal = request.Model == AssistantModels.Local;
        if (request.File is not null)
        {
            if (!onlyLocal) return await CloudAsync(request, onText, onStage, cancel);
            var read = await Assistant.AskAsync(request, onText, onStage, cancel);
            return read with { By = new AnswerSource(Engine.Local, read.Model ?? L.T("Modelo local")) };
        }
        if (request.Data is not null) return await WithDataAsync(request, onText, onStage, cancel);

        var intent = AnswerRules.Match(request.Prompt);
        var local = Assistant.Local?.Invoke();
        var understood = false;
        if (intent is null && local is IStructuredBrain structured)
        {
            onStage?.Invoke(AssistantStage.Opening);
            intent = await ClassifyAsync(structured, request.Prompt, cancel);
            understood = intent is not null;
        }
        var topic = intent?.Topic ?? AnswerTopics.General;
        var first = AnswerTopics.First(settings, topic);
        var last = AnswerTopics.Find(topic).Fixed ? Engine.Cloud : (Engine)Math.Max((int)first, (int)settings.AnswerFallback);
        if (onlyLocal) (first, last) = (first == Engine.None ? Engine.None : Engine.Local, Engine.Local);

        AssistantResult? result = null, failed = null;
        var localMissing = false;
        for (var engine = first; engine <= last && result is null; engine++)
        {
            if (cancel.IsCancellationRequested) return new AssistantResult("", null, null, null, "Cancelado.");
            if (engine == Engine.Local && local is null) { localMissing = true; continue; }
            result = engine switch
            {
                Engine.None => await NoAiAsync(request, settings, intent, cancel),
                Engine.Local => await LocalAsync(local!, request, settings, intent, understood, onlyLocal, onText, onStage, cancel),
                _ => await CloudAsync(request, onText, onStage, cancel),
            };
            // Si el modelo local falla (no arrancó), pasa al siguiente; si no hay siguiente, se dice qué pasó.
            if (engine == Engine.Local && result is { Failed: true }) (failed, result) = (result, null);
        }
        if (result is null)
            return failed ?? (localMissing
                ? new AssistantResult("", null, null, null, L.T("El modelo local no está descargado: descárguelo en Claupet Console › Respuestas."))
                : new AssistantResult(L.T("No te entendí bien. ¿Me lo dices de otra forma? (・_・?)"), null, null, null, null,
                    By: new AnswerSource(Engine.None, L.T(Instant))));
        // Lo entendió el modelo local aunque responda la app: cuenta como suyo.
        if (understood && result.By is { Engine: Engine.None } && local is not null)
            result = result with { By = new AnswerSource(Engine.Local, local.Name) };
        if (!result.Failed && result.By is { } by) AnswerStats.Add(by.Engine);
        return result;
    }

    /// <summary>La segunda vuelta de una acción que trajo datos (la batería, las notas): responde quien la pidió.</summary>
    static async Task<AssistantResult> WithDataAsync(AssistantRequest request, Action<string>? onText, Action<AssistantStage>? onStage, CancellationToken cancel)
    {
        if (request.Engine == Engine.None)
        {
            var text = request.Data!.Trim() + " (^_^)";
            return new AssistantResult(text, null, null, null, null, Spoken: text, By: new AnswerSource(Engine.None, L.T(Instant)));
        }
        if ((request.Engine == Engine.Local || request.Model == AssistantModels.Local) && Assistant.Local?.Invoke() is { } local)
        {
            var reply = await Assistant.AskBrainAsync(local, request, onText, onStage, cancel);
            if (!reply.Failed) return reply with { By = new AnswerSource(Engine.Local, local.Name) };
        }
        return await CloudAsync(request, onText, onStage, cancel);
    }

    static async Task<AssistantResult> CloudAsync(AssistantRequest request, Action<string>? onText, Action<AssistantStage>? onStage, CancellationToken cancel)
    {
        // En «Automático» el chip dice también el esfuerzo que se eligió («Opus 5.5 · esfuerzo alto»).
        AutoChoice? auto = null;
        if (request.Model == AutoModel.Id)
        {
            auto = AutoModel.Pick(request.Prompt, request.History, request.File is not null);
            request = request with { Model = auto.Model, Effort = auto.Effort };
        }
        var result = await Assistant.AskAsync(request, onText, onStage, cancel);
        var label = result.Model is { } model ? Format.Model(model) : "Claude";
        if (auto is not null) label += " · " + AutoModel.EffortLabel(auto.Level);
        return result with { By = new AnswerSource(Engine.Cloud, label) };
    }

    /// <summary>La app sola: lo que entendieron las reglas (o el modelo local), sin preguntarle a nadie. null si no puede.</summary>
    static async Task<AssistantResult?> NoAiAsync(AssistantRequest request, AppSettings settings, Intent? intent, CancellationToken cancel)
    {
        if (intent is null) return null;
        if (intent.Action is { } action)
        {
            if (Checked(action) is not { } ready) return null;
            return new AssistantResult("", null, null, null, null, ready, By: new AnswerSource(Engine.None, L.T(Instant)));
        }
        var (text, source) = await DataAsync(settings, intent, cancel);
        return text is null ? null : new AssistantResult(text, null, null, null, null, Spoken: text, By: new AnswerSource(Engine.None, source));
    }

    /// <summary>La respuesta de la app para los tipos que solo necesitan datos (hora, clima, uso, cuentas, saludo) y de dónde salió.</summary>
    static async Task<(string? Text, string Source)> DataAsync(AppSettings settings, Intent intent, CancellationToken cancel)
    {
        switch (intent.Topic)
        {
            case AnswerTopics.Time when intent.Date:
                return (Assistant.DateText(), L.T(Instant));
            case AnswerTopics.Time when intent.Place is { } place:
                return (await WeatherService.TimeInAsync(place, cancel), "Open-Meteo");
            case AnswerTopics.Time:
                return (Assistant.TimeText(), L.T(Instant));
            case AnswerTopics.Weather:
                return (await WeatherService.DescribeAsync(settings, intent.Place, intent.Day, intent.Rain, cancel), "Open-Meteo");
            case AnswerTopics.Usage:
                return (Usage?.Invoke(), L.T(Instant));
            case AnswerTopics.Math or AnswerTopics.Chat:
                return (intent.Reply, L.T(Instant));
            default:
                return (null, "");
        }
    }

    /// <summary>La acción lista para hacer: «abrir» solo si existe lo que se abre (si no, null: que la entienda otro).</summary>
    static AssistantAction? Checked(AssistantAction action)
    {
        if (action.Kind != "abrir") return action;
        return OpenTarget?.Invoke(action.Value) is { } target ? action with { Value = target } : null;
    }

    /// <summary>
    /// El modelo local: con los datos de la app (la hora, el clima, una cuenta ya hecha), los dice él; las
    /// acciones las elige con el JSON cerrado; lo demás (charla, preguntas generales) lo conversa. Internet,
    /// archivos y tareas, nunca (salvo que sea el modelo elegido: entonces conversa). Una acción que las reglas
    /// ya entendieron se hace sin preguntarle: él solo tardaría en llegar a lo mismo.
    /// </summary>
    static async Task<AssistantResult?> LocalAsync(IAssistantBrain local, AssistantRequest request, AppSettings settings, Intent? intent,
        bool understood, bool onlyLocal, Action<string>? onText, Action<AssistantStage>? onStage, CancellationToken cancel)
    {
        var topic = intent?.Topic ?? AnswerTopics.General;
        var by = new AnswerSource(Engine.Local, local.Name);
        switch (topic)
        {
            case AnswerTopics.Internet when !onlyLocal:
                return null;
            case AnswerTopics.Time or AnswerTopics.Weather or AnswerTopics.Usage:
            case AnswerTopics.Math when intent?.Reply is not null:
            {
                var (data, _) = await DataAsync(settings, intent!, cancel);
                if (data is null) return onlyLocal ? await ChatAsync() : null;
                var reply = await Assistant.AskBrainAsync(local, request with { Data = data, Engine = Engine.Local }, onText, onStage, cancel);
                return reply with { By = by };
            }
            case AnswerTopics.Status or AnswerTopics.Actions or AnswerTopics.Open or AnswerTopics.Notes:
            {
                if (!understood && intent?.Action is { } ruled)
                    return Checked(ruled) is { } exact
                        ? new AssistantResult("", null, null, null, null, exact, By: new AnswerSource(Engine.None, L.T(Instant)))
                        : onlyLocal ? await ChatAsync() : null;
                if (intent?.Action is null || Checked(intent.Action) is not { } action) return onlyLocal ? await ChatAsync() : null;
                return new AssistantResult("", null, null, null, null, action, By: by);
            }
            default:
                return await ChatAsync();
        }

        async Task<AssistantResult> ChatAsync() => (await Assistant.AskBrainAsync(local, request, onText, onStage, cancel)) with { By = by };
    }

    // ── El modelo local entiende la pregunta ────────────────────────────────────────────────

    /// <summary>Las acciones que el modelo local puede elegir, por tipo (las que no necesitan armar un JSON).</summary>
    static readonly Dictionary<string, string[]> LocalActions = new()
    {
        [AnswerTopics.Status] = ["estado"],
        [AnswerTopics.Actions] = ["volumen", "musica", "brillo", "tema", "ventana", "captura"],
        [AnswerTopics.Open] = ["abrir", "carpeta"],
        [AnswerTopics.Notes] = ["notas", "recordatorios"],
    };

    static readonly string Schema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["tema"] = new { type = "string", @enum = AnswerTopics.All.Select(t => t.Id).ToArray() },
            ["accion"] = new { type = "string", @enum = new[] { "ninguna" }.Concat(LocalActions.Values.SelectMany(a => a)).ToArray() },
            ["valor"] = new { type = "string", maxLength = 80 },
            ["lugar"] = new { type = "string", maxLength = 60 },
            ["dia"] = new { type = "string", @enum = new[] { "hoy", "manana", "pasado" } },
        },
        required = new[] { "tema", "accion", "valor", "lugar", "dia" },
        additionalProperties = false,
    });

    const string ClassifierPrompt = """
        Clasificas lo que le dicen a una mascota de escritorio. Responde solo el JSON.
        tema:
        - hora: la hora o la fecha (también en otra ciudad).
        - clima: el clima, la temperatura o si va a llover.
        - estado: la batería, el disco o la memoria de este equipo.
        - uso: cuánto uso o límite de Claude le queda.
        - cuentas: un cálculo o una conversión de unidades.
        - acciones: volumen, música, brillo, modo claro u oscuro, mostrar el escritorio o una captura de pantalla.
        - abrir: abrir una app, un sitio web o una carpeta.
        - notas: leer sus notas o sus recordatorios, o guardar una nota o un recordatorio.
        - charla: saludos, gracias, cómo estás, conversación casual.
        - generales: una pregunta de conocimiento (historia, ciencia, definiciones, consejos).
        - internet: noticias, precios, resultados, buscar en internet, archivos, programar o cualquier tarea en el equipo que no esté arriba.
        accion y valor, según el tema (si no aplica: accion "ninguna" y valor ""):
        - estado: accion "estado", valor "bateria", "disco", "memoria" o "todo".
        - volumen (qué tan alto suena todo, también «bájale a la música»): "subir", "bajar", "silenciar", "activar" o un número de 0 a 100.
        - musica: "reproducir", "pausar", "siguiente" o "anterior".
        - brillo (solo la luz de la pantalla): "subir", "bajar" o un número de 0 a 100.
        - tema: "claro" u "oscuro".
        - ventana: "escritorio".
        - captura: "pantalla" o "recorte".
        - abrir: el nombre de la app o la URL. carpeta: "descargas", "documentos", "escritorio", "imagenes", "musica" o "videos".
        - notas o recordatorios: "leer" (guardar uno nuevo: accion "ninguna").
        lugar: la ciudad que nombran, tal cual, o "". dia: "hoy", "manana" o "pasado".
        Si dudas entre dos temas, elige generales o internet.
        Ejemplos:
        «súbele que no se oye» → {"tema":"acciones","accion":"volumen","valor":"subir","lugar":"","dia":"hoy"}
        «bájale un poco a la música» → {"tema":"acciones","accion":"volumen","valor":"bajar","lugar":"","dia":"hoy"}
        «la pantalla está muy oscura» → {"tema":"acciones","accion":"brillo","valor":"subir","lugar":"","dia":"hoy"}
        «¿necesito paraguas mañana?» → {"tema":"clima","accion":"ninguna","valor":"","lugar":"","dia":"manana"}
        «¿qué tal el tiempo en Madrid?» → {"tema":"clima","accion":"ninguna","valor":"","lugar":"Madrid","dia":"hoy"}
        «ponme algo de Bad Bunny» → {"tema":"internet","accion":"ninguna","valor":"","lugar":"","dia":"hoy"}
        «¿cuál es el río más largo?» → {"tema":"generales","accion":"ninguna","valor":"","lugar":"","dia":"hoy"}
        """;

    static readonly Regex Here = new(@"^\s*(?:afuera|fuera|aqui|aca|aqui afuera|donde estoy|mi ciudad|mi casa|here|outside|home)?\s*$");

    /// <summary>Lo que entendió el modelo local, o null si no respondió o no tiene sentido.</summary>
    static async Task<Intent?> ClassifyAsync(IStructuredBrain brain, string prompt, CancellationToken cancel)
    {
        try
        {
            var json = await brain.JsonAsync(ClassifierPrompt, prompt, Schema, cancel);
            if (json is null) return null;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var topic = Json.Str(r, "tema");
            if (topic is null || AnswerTopics.All.All(t => t.Id != topic)) return null;
            var kind = Json.Str(r, "accion") ?? "ninguna";
            var value = (Json.Str(r, "valor") ?? "").Trim();
            var action = LocalActions.TryGetValue(topic, out var allowed) && allowed.Contains(kind) && value.Length > 0
                ? new AssistantAction(kind, value) : null;
            var place = (Json.Str(r, "lugar") ?? "").Trim();
            if (Here.IsMatch(" " + Assistant.Simple(place) + " ")) place = ""; // «afuera», «aquí»: donde está la persona
            var day = Json.Str(r, "dia") switch { "manana" => 1, "pasado" => 2, _ => 0 };
            var simple = Assistant.Simple(prompt);
            return new Intent(topic, action, place.Length > 0 ? place : null, day,
                Date: topic == AnswerTopics.Time && Regex.IsMatch(simple, " (?:fecha|dia|date|day) "),
                Rain: Regex.IsMatch(simple, "llov|lluvi|rain"));
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException or ExtensionUnavailableException or InvalidOperationException)
        {
            return null;
        }
    }
}
