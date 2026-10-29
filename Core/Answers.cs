using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Quién responde: la app sola (sin IA), el modelo local o la nube (Claude o la extensión del asistente).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Engine>))]
enum Engine { None, Local, Cloud }

/// <summary>Quién respondió y con qué («Sin IA · Open-Meteo», «Modelo local · Qwen3.5 2B», «Nube · Haiku 5.5»).</summary>
sealed record AnswerSource(Engine Engine, string Detail)
{
    public string Label => $"{Engines.Label(Engine)} · {Detail}";
}

/// <summary>Un modelo chico (el local): solo conversa, sin la lista de acciones; ellas las elige con un JSON cerrado.</summary>
interface ISmallBrain;

/// <summary>Un asistente que también sabe responder con un JSON que cumple un esquema (el modelo local).</summary>
interface IStructuredBrain
{
    Task<string?> JsonAsync(string system, string prompt, string schema, CancellationToken cancel);
}

static class Engines
{
    public static string Label(Engine engine) => engine switch
    {
        Engine.None => L.T("Sin IA"),
        Engine.Local => L.T("Modelo local"),
        _ => L.T("Nube"),
    };
}

/// <summary>
/// Los tipos de pregunta de Ajustes › Respuestas: quién responde primero cada uno. «Sin IA» solo en los que
/// la app sabe contestar sola; «Internet, archivos y tareas» siempre va a la nube.
/// </summary>
static class AnswerTopics
{
    public sealed record Topic(string Id, string Label, Engine Default, bool None = true, bool Fixed = false);

    public const string Time = "hora", Weather = "clima", Status = "estado", Usage = "uso", Math = "cuentas",
        Actions = "acciones", Open = "abrir", Notes = "notas", Chat = "charla", General = "generales", Internet = "internet";

    public static readonly Topic[] All =
    [
        new(Time, "Hora y fecha", Engine.None),
        new(Weather, "Clima", Engine.None),
        new(Status, "Estado del equipo", Engine.None),
        new(Usage, "Uso de Claude", Engine.None),
        new(Math, "Cuentas y conversiones", Engine.Cloud),
        new(Actions, "Acciones del equipo", Engine.Cloud),
        new(Open, "Abrir apps y sitios", Engine.Cloud),
        new(Notes, "Notas y recordatorios", Engine.Cloud),
        new(Chat, "Saludo y charla", Engine.Cloud),
        new(General, "Preguntas generales", Engine.Cloud, None: false),
        new(Internet, "Internet, archivos y tareas", Engine.Cloud, None: false, Fixed: true),
    ];

    public static Topic Find(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[^2];

    /// <summary>Quién responde primero ese tipo: lo elegido (si se puede) o el de siempre.</summary>
    public static Engine First(AppSettings settings, string id)
    {
        var topic = Find(id);
        if (topic.Fixed) return Engine.Cloud;
        return settings.AnswerEngines.TryGetValue(id, out var engine) && (engine != Engine.None || topic.None) ? engine : topic.Default;
    }

    /// <summary>Lo que se puede elegir para ese tipo.</summary>
    public static Engine[] Choices(Topic topic) =>
        topic.Fixed ? [Engine.Cloud] : topic.None ? [Engine.None, Engine.Local, Engine.Cloud] : [Engine.Local, Engine.Cloud];
}

/// <summary>
/// Los modos de Ajustes › Respuestas › Quién responde primero: un conjunto ya armado (quién responde cada tipo y hasta
/// dónde se pasa si no entiende) o «Personalizado», tipo por tipo. El modo no se guarda: es el que coincide con lo
/// elegido; solo «Personalizado» elegido a mano queda guardado (AnswerPreset), para que se vean las filas.
/// </summary>
static class AnswerPresets
{
    public const string Default = "defecto", Saving = "ahorro", Private = "local", Custom = "personalizado";

    static readonly string[] Ready = [Default, Saving, Private];

    public static IReadOnlyList<(string Id, string Label)> Choices() =>
        [(Default, L.T("Por defecto")), (Saving, L.T("Ahorro de tokens")), (Private, L.T("Solo en este equipo")), (Custom, L.T("Personalizado"))];

    public static string Label(string id) => Choices().FirstOrDefault(c => c.Id == id).Label ?? id;

    public static string Describe(string id) => id switch
    {
        Saving => L.T("Primero la app; si no sabe, el modelo local; la nube solo si ninguno entiende. Gasta menos tokens."),
        Private => L.T("Solo la app y el modelo local: nada va a la nube, salvo internet, archivos y tareas."),
        Custom => L.T("Usted elige quién responde primero cada tipo de pregunta."),
        _ => L.T("La app responde lo que sabe sola (hora, clima, estado del equipo, uso); lo demás, la nube."),
    };

    /// <summary>El modo de ahora: «Personalizado» si se eligió a mano; si no, el que coincide con lo elegido.</summary>
    public static string Current(AppSettings s) =>
        s.AnswerPreset == Custom ? Custom : Ready.FirstOrDefault(p => Matches(s, p)) ?? Custom;

    /// <summary>Si ese modo usa el modelo local (para descargarlo al elegirlo).</summary>
    public static bool UsesLocal(string id) => id is Saving or Private;

    public static void Apply(AppSettings s, string id)
    {
        if (id == Custom || !Ready.Contains(id))
        {
            s.AnswerPreset = Custom;
            return;
        }
        s.AnswerPreset = null;
        // Solo se guarda lo que no es el de siempre (como al elegir tipo por tipo).
        s.AnswerEngines = Engines(id).Where(e => e.Value != AnswerTopics.Find(e.Key).Default).ToDictionary(e => e.Key, e => e.Value);
        s.AnswerFallback = Fallback(id);
    }

    static bool Matches(AppSettings s, string id) =>
        s.AnswerFallback == Fallback(id) && Engines(id).All(e => AnswerTopics.First(s, e.Key) == e.Value);

    /// <summary>Quién responde primero cada tipo (los que se pueden elegir) en ese modo.</summary>
    static Dictionary<string, Engine> Engines(string id) => AnswerTopics.All.Where(t => !t.Fixed).ToDictionary(t => t.Id,
        t => id == Default ? t.Default : t.None ? Engine.None : Engine.Local);

    static Engine Fallback(string id) => id == Private ? Engine.Local : Engine.Cloud;
}

/// <summary>
/// Lo que se entendió de una pregunta: su tipo y, según cuál, la acción, el lugar, el día (0 hoy, 1 mañana,
/// 2 pasado mañana) o la respuesta ya lista (una cuenta, un saludo). Date: preguntaron la fecha, no la hora;
/// Rain: preguntaron si va a llover.
/// </summary>
sealed record Intent(string Topic, AssistantAction? Action = null, string? Place = null, int Day = 0, string? Reply = null,
    bool Date = false, bool Rain = false);

/// <summary>
/// Las reglas sin IA: frases cortas y claras que la app entiende sola. Son estrictas a propósito: la frase
/// entera tiene que ser una de estas (con un «oye» o un «por favor» alrededor); si sobra algo, no se
/// adivina y la pregunta pasa al siguiente.
/// </summary>
static class AnswerRules
{
    const string Before = @"^ (?:(?:oye|hola|hey|claupet|disculpa|porfa|por favor|please|ok|okay) )*";
    const string After = @"(?:(?:ahora|ahorita|now|por favor|porfa|please|claupet) )*$";
    const RegexOptions Options = RegexOptions.CultureInvariant;

    static Regex Rule(string body) => new(Before + "(?:" + body + ") " + After, Options);

    static readonly Regex Time = Rule(@"(?:que hora es|que horas son|dime la hora|me dices la hora|me das la hora|sabes que hora es|la hora|hora|what time is it|what s the time|tell me the time|the time)(?: (?:en|in) (?<lugar>\p{L}+(?: \p{L}+){0,3}?))?");
    static readonly Regex Date = Rule(@"(?:que dia es hoy|que dia es|que fecha es hoy|que fecha es|que fecha es hoy|a cuanto estamos(?: hoy)?|la fecha(?: de hoy)?|fecha de hoy|what day is it(?: today)?|what day is today|what s the date(?: today)?|what is the date(?: today)?|today s date)");
    static readonly Regex Weather = Rule(@"(?<rain>va a llover|llovera|esta lloviendo|lloviendo|hay lluvia|is it going to rain|will it rain|is it raining)?(?:(?<=\S) )?(?:(?:como (?:esta|estara|va a estar) el (?:clima|tiempo|dia)|que tal (?:esta |estara )?el (?:clima|tiempo|dia)|hace sol|hay sol|esta haciendo (?:calor|frio|sol)|esta (?:nublado|soleado)|que clima hace|que clima hara|que clima va a hacer|que tiempo hace|que tiempo hara|el clima|clima|el tiempo|que temperatura hace|cual es la temperatura|la temperatura|temperatura|cuanto calor hace|cuanto frio hace|hace (?:frio|calor)|pronostico(?: del tiempo)?|what s the weather(?: like)?|how s the weather|the weather|weather|temperature|forecast) ?)?(?:(?<dia>hoy|manana|pasado manana|today|tomorrow) ?)?(?:(?:en|in) (?<lugar>\p{L}+(?: \p{L}+){0,3}?) ?)?(?:(?<dia2>hoy|manana|pasado manana|today|tomorrow) ?)?");
    static readonly Regex Status = Rule(@"(?:(?<bateria>cuanta bateria (?:me )?(?:queda|tengo|tiene|hay)|como esta (?:la|mi) bateria|como va (?:la|mi) bateria|(?:el )?nivel de (?:la )?bateria|bateria|battery(?: level)?|how much battery(?: is left| do i have| left)?)|(?<disco>cuanto espacio (?:libre )?(?:me queda|queda|tengo|hay)(?: en (?:el )?disco)?|espacio (?:libre )?en (?:el )?disco|espacio libre|disk space|free space|how much (?:disk )?space(?: is left| do i have)?)|(?<memoria>cuanta (?:memoria|ram) (?:libre )?(?:me queda|queda|tengo|hay|uso|estoy usando)|memoria (?:libre|ram)|how much (?:ram|memory)(?: is free| am i using| is left)?)|(?<todo>como esta (?:el|mi) equipo|estado del equipo|como esta la (?:pc|computadora)|system status))");
    static readonly Regex Usage = Rule(@"(?:cuanto (?:uso de claude|limite|limite de claude) me queda|cuanto (?:me queda|llevo|he usado|uso)(?: del?| de mi)? (?:limite|plan|uso|claude|cuota)(?: de claude)?|como (?:va|esta|voy con) (?:mi |el )?(?:uso|limite|plan|cuota)(?: de claude)?|(?:mi )?uso de claude|(?:mi )?limite de claude|how much (?:claude )?usage(?: do i have left| is left| left)?|my (?:claude )?usage|claude usage)");
    static readonly Regex Greeting = Rule(@"(?<saludo>hola|holi|hey|buenas|buenos dias|buenas tardes|buenas noches|hello|hi|good morning|good afternoon|good evening)?(?: ?(?<como>como estas|como te va|que tal|how are you|what s up))?(?: ?(?<gracias>gracias|muchas gracias|mil gracias|thanks|thank you))?(?: ?(?<adios>adios|chao|chau|hasta luego|nos vemos|bye|goodbye|see you))?(?: ?(?<quien>quien eres|como te llamas|who are you|what s your name))?");

    // Acciones: valor fijo según la frase.
    static readonly (Regex Rule, string Kind, Func<Match, string?> Value)[] Commands =
    [
        (Rule(@"(?:sube|subele|aumenta|subir|turn up)(?: el| the)? (?:volumen|volume)|(?:volume|volumen) up|mas (?:volumen|alto)"), "volumen", _ => "subir"),
        (Rule(@"(?:baja|bajale|disminuye|bajar|turn down)(?: el| the)? (?:volumen|volume)|(?:volume|volumen) down|mas bajo"), "volumen", _ => "bajar"),
        (Rule(@"silencia(?:r)?(?: el (?:sonido|volumen|equipo))?|mute(?: the sound)?|(?:pon|ponlo) en silencio"), "volumen", _ => "silenciar"),
        (Rule(@"quita el silencio|unmute|activa el sonido"), "volumen", _ => "activar"),
        (Rule(@"(?:pon |set )?(?:el |the )?(?:volumen|volume)(?: (?:al|en|a|to))? (?<n>\d{1,3})(?: %| por ciento| percent)?"), "volumen", m => Percent(m)),
        (Rule(@"(?:pausa|pausar|deten|para|pause|stop)(?: la| the)? (?:musica|music|cancion|song)|pausa"), "musica", _ => "pausar"),
        (Rule(@"(?:reanuda|reproduce|continua|resume|play)(?: la| the)? (?:musica|music)|play"), "musica", _ => "reproducir"),
        (Rule(@"(?:pon |pasa a )?(?:la )?(?:siguiente cancion|cancion siguiente)|siguiente|next(?: song| track)?|skip(?: this song)?|salta(?: la)? cancion"), "musica", _ => "siguiente"),
        (Rule(@"(?:pon |vuelve a )?(?:la )?(?:cancion anterior|anterior cancion)|anterior|previous(?: song| track)?"), "musica", _ => "anterior"),
        (Rule(@"(?:sube|subele|aumenta|turn up)(?: el| the)? (?:brillo|brightness)|brightness up"), "brillo", _ => "subir"),
        (Rule(@"(?:baja|bajale|disminuye|turn down)(?: el| the)? (?:brillo|brightness)|brightness down"), "brillo", _ => "bajar"),
        (Rule(@"(?:pon |set )?(?:el |the )?(?:brillo|brightness)(?: (?:al|en|a|to))? (?<n>\d{1,3})(?: %| por ciento| percent)?"), "brillo", m => Percent(m)),
        (Rule(@"(?:pon |activa |cambia a |usa |switch to |turn on )?(?:el )?(?:modo|tema) (?<t>oscuro|claro)|(?<t>dark|light) (?:mode|theme)"), "tema", m => m.Groups["t"].Value is "oscuro" or "dark" ? "oscuro" : "claro"),
        (Rule(@"(?:muestra|muestrame|ve al|ir al|ensena)(?: el)? escritorio|minimiza todo|show(?: the| me the)? desktop"), "ventana", _ => "escritorio"),
        (Rule(@"bloquea (?:el equipo|la pantalla|la pc|la computadora)|lock(?: the)? (?:computer|screen|pc)"), "equipo", _ => "bloquear"),
        (Rule(@"(?:toma|haz|saca|hazme)(?: una)? captura(?: de pantalla)?|captura de pantalla|(?:take a )?screenshot"), "captura", _ => "pantalla"),
        (Rule(@"(?:toma|haz|saca|hazme)(?: un| una)? (?:recorte|captura de una parte)|recorte de pantalla"), "captura", _ => "recorte"),
    ];

    static readonly Regex Open = new(@"^(?:oye,?\s+|hey,?\s+)?(?:abre|ábreme|abreme|abrir|abrí|open|launch|inicia|ejecuta)\s+(?:el\s+|la\s+|mi\s+|the\s+|my\s+)?(?<x>[\p{L}\p{N} .+\-_:/]{2,60}?)[\s.!]*(?:,?\s*(?:por favor|porfa|please))?[\s.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex OpenNot = new(@"\b(?:archivo|archivos|proyecto|file|files|project|sesion|sesión|session|pestaña|pestana|tab|documento|document|enlace|link|correo|email)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Note = new(@"^(?:anota|apunta|toma nota(?: de)?|guarda (?:una )?nota(?: que)?|note(?: down)?|take a note(?: that)?)[:,]?\s+(?<t>\S.{1,500})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    static readonly Regex RemindIn = new(@"^(?:recuérdame|recuerdame|remind me(?: to)?)\s+(?<t>\S.{0,200}?)\s+(?:en|dentro de|in)\s+(?<n>\d{1,3}|un|una|media|an|a)\s+(?<u>minutos?|min|horas?|minutes?|hours?)[.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex RemindAt = new(@"^(?:recuérdame|recuerdame|remind me(?: to)?)\s+(?<t>\S.{0,200}?)\s+(?:a las|a la|at)\s+(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<p>a\.?\s?m\.?|p\.?\s?m\.?|de la mañana|de la manana|de la tarde|de la noche)?[.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex ReadNotes = Rule(@"(?:lee|leeme|muestrame|dime|cuales son|que dicen)?(?: ?(?:mis|las))? notas|read(?: me)? my notes|my notes");
    static readonly Regex ReadReminders = Rule(@"(?:que|cuales) recordatorios tengo|(?:mis|los) recordatorios(?: pendientes)?|recordatorios pendientes|what reminders do i have|my reminders");

    /// <summary>Lo que se entiende sin IA, o null si la frase no es una de las reglas.</summary>
    public static Intent? Match(string prompt)
    {
        var raw = prompt.Trim();
        if (raw.Length == 0 || raw.Length > 300) return null;
        var text = Assistant.Simple(raw);

        if (Date.IsMatch(text)) return new Intent(AnswerTopics.Time, Date: true);
        if (Time.Match(text) is { Success: true } time)
            return new Intent(AnswerTopics.Time, Place: Group(time, "lugar"));
        if (Status.Match(text) is { Success: true } status)
        {
            var value = new[] { "bateria", "disco", "memoria" }.FirstOrDefault(g => status.Groups[g].Success) ?? "todo";
            return new Intent(AnswerTopics.Status, new AssistantAction("estado", value));
        }
        if (Usage.IsMatch(text)) return new Intent(AnswerTopics.Usage);
        if (Weather.Match(text) is { Success: true } weather && IsWeather(weather))
        {
            var day = Group(weather, "dia") ?? Group(weather, "dia2");
            return new Intent(AnswerTopics.Weather, Place: Group(weather, "lugar"),
                Day: day is "manana" or "tomorrow" ? 1 : day == "pasado manana" ? 2 : 0, Rain: weather.Groups["rain"].Success);
        }
        foreach (var (rule, kind, value) in Commands)
            if (rule.Match(text) is { Success: true } command && value(command) is { } v)
                return new Intent(AnswerTopics.Actions, new AssistantAction(kind, v));
        if (ReadNotes.IsMatch(text)) return new Intent(AnswerTopics.Notes, new AssistantAction("notas", "leer"));
        if (ReadReminders.IsMatch(text)) return new Intent(AnswerTopics.Notes, new AssistantAction("recordatorios", "leer"));
        if (Note.Match(raw) is { Success: true } note)
        {
            var body = note.Groups["t"].Value.Trim();
            return new Intent(AnswerTopics.Notes, new AssistantAction("nota", JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["titulo"] = Title(body),
                ["texto"] = body,
            })));
        }
        if (Reminder(raw) is { } reminder) return new Intent(AnswerTopics.Notes, reminder);
        if (Open.Match(raw) is { Success: true } open && !OpenNot.IsMatch(open.Groups["x"].Value))
            return new Intent(AnswerTopics.Open, new AssistantAction("abrir", open.Groups["x"].Value.Trim()));
        if (Calculator.Answer(raw) is { } math) return new Intent(AnswerTopics.Math, Reply: math);
        if (Greeting.Match(text) is { Success: true } greeting && GreetingReply(greeting) is { } hello)
            return new Intent(AnswerTopics.Chat, Reply: hello);
        return null;
    }

    static string? Group(Match m, string name) => m.Groups[name].Success && m.Groups[name].Value.Trim() is { Length: > 0 } v ? v : null;

    /// <summary>Que la frase sea del clima: alguna palabra del clima o de la lluvia (no solo «hoy» o «en Panamá»).</summary>
    static bool IsWeather(Match m) => m.Value.Trim().Length > 0 && (m.Groups["rain"].Success
        || Regex.IsMatch(m.Value, @" (?:clima|tiempo|temperatura|calor|frio|sol|soleado|nublado|dia|pronostico|weather|temperature|forecast) ", Options));

    static string? Percent(Match m) => int.TryParse(m.Groups["n"].Value, out var n) && n is >= 0 and <= 100 ? n.ToString(CultureInfo.InvariantCulture) : null;

    static string Title(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5);
        var title = string.Join(' ', words).TrimEnd('.', ',', ':', ';');
        return title.Length > 40 ? title[..40] : title;
    }

    /// <summary>«recuérdame X en 20 minutos» o «a las 5 pm»: la acción recordar con la hora ya calculada.</summary>
    static AssistantAction? Reminder(string raw)
    {
        var now = DateTime.Now;
        DateTime when;
        Match m;
        if ((m = RemindIn.Match(raw)).Success)
        {
            var amount = m.Groups["n"].Value.ToLowerInvariant() switch
            {
                "un" or "una" or "an" or "a" => 1.0,
                "media" => 0.5,
                var n => double.Parse(n, CultureInfo.InvariantCulture),
            };
            var hours = m.Groups["u"].Value.StartsWith('h');
            when = now.AddMinutes(hours ? amount * 60 : amount);
        }
        else if ((m = RemindAt.Match(raw)).Success)
        {
            var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            var period = m.Groups["p"].Value.ToLowerInvariant().Replace(".", "").Replace(" ", "");
            if (hour > 23 || minute > 59) return null;
            if (period is "pm" or "delatarde" or "delanoche" && hour < 12) hour += 12;
            if (period is "am" or "delamañana" or "delamanana" && hour == 12) hour = 0;
            when = now.Date.AddHours(hour).AddMinutes(minute);
            if (when <= now) when = when.AddDays(1);
        }
        else return null;
        var text = m.Groups["t"].Value.Trim();
        return new AssistantAction("recordar", JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["texto"] = text,
            ["cuando"] = when.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
        }));
    }

    static string? GreetingReply(Match m)
    {
        if (m.Groups["quien"].Success) return L.T("Soy Claupet, tu mascota de escritorio. (^_^)");
        if (m.Groups["adios"].Success) return L.T("¡Hasta luego! (^_^)/");
        if (m.Groups["gracias"].Success) return L.T("¡De nada! (^_^)");
        if (m.Groups["como"].Success) return L.T("¡Muy bien, gracias! ¿Y tú? (^_^)");
        if (!m.Groups["saludo"].Success) return null;
        var hour = DateTime.Now.Hour;
        return hour < 12 ? L.T("¡Buenos días! ¿En qué te ayudo? (^_^)")
            : hour < 19 ? L.T("¡Buenas tardes! ¿En qué te ayudo? (^_^)")
            : L.T("¡Buenas noches! ¿En qué te ayudo? (^_^)");
    }
}

/// <summary>
/// Cuentas sin IA: + − × ÷, potencias, paréntesis y porcentajes («15 % de 200»), con números o con
/// «más», «menos», «por», «entre»; y unidades comunes («10 km a millas»). Lo que no entiende devuelve null.
/// </summary>
static class Calculator
{
    static readonly Regex Ask = new(@"^(?:cu[aá]nto (?:es|son|da)|calcula|calc[uú]lame|resuelve|what is|what's|how much is|calculate)?\s*(?<e>.+?)\s*[?=]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex PercentOf = new(@"^(?<p>\d+(?:[.,]\d+)?)\s*(?:%|por ?ciento|percent)\s*(?:de|of)\s*(?<n>\d+(?:[.,]\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Convert = new(@"^(?:convierte|pasa|convert)?\s*(?<n>-?\d+(?:[.,]\d+)?)\s*(?<from>[\p{L}°]+(?:\s[\p{L}]+)?)\s+(?:a|en|to|in)\s+(?<to>[\p{L}°]+(?:\s[\p{L}]+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Unidad → (magnitud, factor a la unidad base). Las temperaturas van aparte.
    static readonly Dictionary<string, (string Kind, double Factor, string Name)> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["km"] = ("l", 1000, "km"), ["kilometros"] = ("l", 1000, "km"), ["kilómetros"] = ("l", 1000, "km"), ["kilometers"] = ("l", 1000, "km"),
        ["m"] = ("l", 1, "m"), ["metros"] = ("l", 1, "m"), ["meters"] = ("l", 1, "m"),
        ["cm"] = ("l", 0.01, "cm"), ["centimetros"] = ("l", 0.01, "cm"), ["centímetros"] = ("l", 0.01, "cm"), ["centimeters"] = ("l", 0.01, "cm"),
        ["millas"] = ("l", 1609.344, "mi"), ["mi"] = ("l", 1609.344, "mi"), ["miles"] = ("l", 1609.344, "mi"),
        ["pies"] = ("l", 0.3048, "ft"), ["ft"] = ("l", 0.3048, "ft"), ["feet"] = ("l", 0.3048, "ft"),
        ["pulgadas"] = ("l", 0.0254, "in"), ["in"] = ("l", 0.0254, "in"), ["inches"] = ("l", 0.0254, "in"),
        ["kg"] = ("w", 1, "kg"), ["kilos"] = ("w", 1, "kg"), ["kilogramos"] = ("w", 1, "kg"), ["kilograms"] = ("w", 1, "kg"),
        ["g"] = ("w", 0.001, "g"), ["gramos"] = ("w", 0.001, "g"), ["grams"] = ("w", 0.001, "g"),
        ["libras"] = ("w", 0.45359237, "lb"), ["lb"] = ("w", 0.45359237, "lb"), ["pounds"] = ("w", 0.45359237, "lb"),
        ["onzas"] = ("w", 0.028349523125, "oz"), ["oz"] = ("w", 0.028349523125, "oz"), ["ounces"] = ("w", 0.028349523125, "oz"),
        ["litros"] = ("v", 1, "L"), ["l"] = ("v", 1, "L"), ["liters"] = ("v", 1, "L"),
        ["galones"] = ("v", 3.785411784, "gal"), ["gal"] = ("v", 3.785411784, "gal"), ["gallons"] = ("v", 3.785411784, "gal"),
        ["ml"] = ("v", 0.001, "ml"), ["mililitros"] = ("v", 0.001, "ml"),
    };

    static readonly HashSet<string> Celsius = new(StringComparer.OrdinalIgnoreCase) { "c", "°c", "celsius", "grados celsius", "centigrados", "centígrados" };
    static readonly HashSet<string> Fahrenheit = new(StringComparer.OrdinalIgnoreCase) { "f", "°f", "fahrenheit", "grados fahrenheit" };

    public static string? Answer(string prompt)
    {
        if (Ask.Match(prompt.Trim()) is not { Success: true } ask) return null;
        var expression = ask.Groups["e"].Value.Trim().TrimEnd('?', '.', '!').Trim();
        if (PercentOf.Match(expression) is { Success: true } p
            && Number(p.Groups["p"].Value) is { } percent && Number(p.Groups["n"].Value) is { } of)
            return L.T("El {0} % de {1} es {2}. (^_^)", Show(percent), Show(of), Show(percent * of / 100));
        if (Convert.Match(expression) is { Success: true } c && Number(c.Groups["n"].Value) is { } amount
            && Conversion(amount, c.Groups["from"].Value.Trim(), c.Groups["to"].Value.Trim()) is { } converted)
            return converted;
        var words = Regex.Replace(" " + expression.ToLowerInvariant() + " ", @"\s(?:dividido (?:entre|por)|divided by|entre)\s", " / ");
        words = Regex.Replace(words, @"\s(?:multiplicado por|por|times|x|×)\s", " * ");
        words = Regex.Replace(words, @"\s(?:más|mas|plus)\s", " + ");
        words = Regex.Replace(words, @"\s(?:menos|minus)\s", " - ");
        words = Regex.Replace(words, @"\s(?:elevado a(?: la)?|to the power of)\s", " ^ ");
        words = words.Replace('÷', '/').Replace('−', '-').Trim();
        // Solo números y operadores, con al menos un operador entre dos números: «2024» solo no es una cuenta.
        if (!Regex.IsMatch(words, @"^[\d\s.,+\-*/^%()]+$") || !Regex.IsMatch(words, @"\d\s*[+\-*/^%]\s*[\d(]")) return null;
        try
        {
            var parser = new Parser(words.Replace(" ", ""));
            var value = parser.Parse();
            if (double.IsNaN(value) || double.IsInfinity(value)) return null;
            return L.T("{0} = {1} (^_^)", expression.Trim(), Show(value));
        }
        catch (FormatException) { return null; }
    }

    static string? Conversion(double amount, string from, string to)
    {
        if (Celsius.Contains(from) && Fahrenheit.Contains(to))
            return L.T("{0} °C son {1} °F. (^_^)", Show(amount), Show(amount * 9 / 5 + 32));
        if (Fahrenheit.Contains(from) && Celsius.Contains(to))
            return L.T("{0} °F son {1} °C. (^_^)", Show(amount), Show((amount - 32) * 5 / 9));
        if (!Units.TryGetValue(from, out var a) || !Units.TryGetValue(to, out var b) || a.Kind != b.Kind) return null;
        return L.T("{0} {1} son {2} {3}. (^_^)", Show(amount), a.Name, Show(amount * a.Factor / b.Factor), b.Name);
    }

    static double? Number(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    static string Show(double value)
    {
        var rounded = Math.Round(value, Math.Abs(value) >= 100 ? 2 : 4);
        return rounded.ToString("#,0.####", CultureInfo.CurrentCulture);
    }

    /// <summary>Descenso recursivo: suma, producto, potencia (a la derecha), signo, porcentaje y paréntesis.</summary>
    sealed class Parser(string text)
    {
        int _at;

        public double Parse()
        {
            var value = Sum();
            if (_at != text.Length) throw new FormatException();
            return value;
        }

        double Sum()
        {
            var value = Product();
            while (_at < text.Length && text[_at] is '+' or '-')
                value = text[_at++] == '+' ? value + Product() : value - Product();
            return value;
        }

        double Product()
        {
            var value = Power();
            while (_at < text.Length && text[_at] is '*' or '/')
            {
                var times = text[_at++] == '*';
                var right = Power();
                if (!times && right == 0) throw new FormatException();
                value = times ? value * right : value / right;
            }
            return value;
        }

        double Power()
        {
            var value = Unary();
            if (_at < text.Length && text[_at] == '^')
            {
                _at++;
                value = System.Math.Pow(value, Power());
            }
            return value;
        }

        double Unary()
        {
            if (_at < text.Length && text[_at] == '-') { _at++; return -Unary(); }
            if (_at < text.Length && text[_at] == '+') { _at++; return Unary(); }
            var value = Atom();
            if (_at < text.Length && text[_at] == '%') { _at++; value /= 100; }
            return value;
        }

        double Atom()
        {
            if (_at < text.Length && text[_at] == '(')
            {
                _at++;
                var inner = Sum();
                if (_at >= text.Length || text[_at] != ')') throw new FormatException();
                _at++;
                return inner;
            }
            var start = _at;
            while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] is '.' or ',')) _at++;
            if (start == _at) throw new FormatException();
            var number = text[start.._at];
            // «1,5» es decimal; «1,000» y «1.000» son miles.
            if (Regex.IsMatch(number, @"^\d{1,3}([.,])\d{3}(\1\d{3})*$")) number = number.Replace(",", "").Replace(".", "");
            return double.Parse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Un lugar: su nombre (null: «donde estás»), sus coordenadas y su zona horaria (IANA).</summary>
sealed record Place(string? Name, double Latitude, double Longitude, string? TimeZone);

/// <summary>
/// El clima sin IA, de Open-Meteo (sin clave): el lugar se busca con su geocodificación, y si no lo nombran,
/// es el de Ajustes › Respuestas › Mi ciudad: por la IP (ipwho.is), con la ubicación de Windows o una ciudad
/// escrita. El pronóstico se guarda 15 minutos y la ubicación, 6 horas.
/// </summary>
static class WeatherService
{
    public const string ByIp = "ip", ByWindows = "windows", ByCity = "ciudad";

    /// <summary>La ubicación de Windows (la pone la app de Windows; null en Linux y macOS o si no hay permiso).</summary>
    public static Func<CancellationToken, Task<(double Lat, double Lon)?>>? WindowsLocation { get; set; }

    static readonly Dictionary<string, (DateTime At, string Json)> Forecasts = [];
    static (string Key, DateTime At, Place Place)? _here;

    static HttpClient Client() => SystemProxy.Client(TimeSpan.FromSeconds(10));

    /// <summary>El clima de hoy (ahora), mañana o pasado mañana, en una o dos frases; null si no se pudo.</summary>
    public static async Task<string?> DescribeAsync(AppSettings settings, string? placeName, int day, bool rain, CancellationToken cancel)
    {
        var place = placeName is not null ? await GeocodeAsync(placeName, cancel) : await HereAsync(settings, cancel);
        if (place is null) return null;
        var json = await ForecastAsync(place, cancel);
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = place.Name ?? L.T("donde estás");
        if (!root.TryGetProperty("daily", out var daily)) return null;
        double At(string field, int i) => daily.GetProperty(field)[i].GetDouble();
        var min = At("temperature_2m_min", day);
        var max = At("temperature_2m_max", day);
        var chance = daily.TryGetProperty("precipitation_probability_max", out var p) && p[day].ValueKind == JsonValueKind.Number ? p[day].GetInt32() : -1;
        var code = daily.GetProperty("weather_code")[day].GetInt32();
        var rainText = chance switch
        {
            < 0 => "",
            >= 70 => L.T(" Lluvia casi segura ({0} %): mejor lleva paraguas.", chance),
            >= 30 => L.T(" Puede llover ({0} % de probabilidad).", chance),
            _ => rain ? L.T(" No se espera lluvia ({0} %).", chance) : "",
        };
        if (day == 0 && root.TryGetProperty("current", out var current))
        {
            var now = current.GetProperty("temperature_2m").GetDouble();
            var feels = current.GetProperty("apparent_temperature").GetDouble();
            var sky = Sky(current.GetProperty("weather_code").GetInt32());
            var feel = Math.Abs(feels - now) >= 2 ? L.T(", se siente como {0:0} °C", feels) : "";
            return L.T("En {0} ahora: {1:0} °C y {2}{3}. Hoy, entre {4:0} y {5:0} °C.", name, now, sky, feel, min, max) + rainText + " (^_^)";
        }
        var when = day == 1 ? L.T("Mañana") : L.T("Pasado mañana");
        return L.T("{0} en {1}: {2}, entre {3:0} y {4:0} °C.", when, name, Sky(code), min, max) + rainText + " (^_^)";
    }

    /// <summary>La hora de ahora en una ciudad (por su zona horaria), o null si no se encontró.</summary>
    public static async Task<string?> TimeInAsync(string placeName, CancellationToken cancel)
    {
        if (await GeocodeAsync(placeName, cancel) is not { TimeZone: { } zone } place) return null;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var info)) return null;
        var there = TimeZoneInfo.ConvertTime(DateTime.Now, info);
        var day = there.Date == DateTime.Today ? "" : there.Date > DateTime.Today ? L.T(" (ya es {0:dddd})", there) : L.T(" (todavía es {0:dddd})", there);
        return L.T("En {0} son las {1:h:mm tt}{2}. (^_^)", place.Name, there, day);
    }

    /// <summary>El lugar de Mi ciudad, según cómo se eligió.</summary>
    public static async Task<Place?> HereAsync(AppSettings settings, CancellationToken cancel)
    {
        var key = settings.WeatherPlace + "|" + settings.WeatherCity;
        if (_here is { } cached && cached.Key == key && DateTime.UtcNow - cached.At < TimeSpan.FromHours(6)) return cached.Place;
        Place? place = null;
        if (settings.WeatherPlace == ByCity && !string.IsNullOrWhiteSpace(settings.WeatherCity))
            place = await GeocodeAsync(settings.WeatherCity, cancel);
        else if (settings.WeatherPlace == ByWindows && WindowsLocation is { } locate && await locate(cancel) is { } spot)
        {
            // El nombre lo da la IP (Open-Meteo no busca por coordenadas); si la IP dice otra cosa, sin nombre.
            var ip = await ByIpAsync(cancel);
            var near = ip is not null && Math.Abs(ip.Latitude - spot.Lat) < 0.3 && Math.Abs(ip.Longitude - spot.Lon) < 0.3;
            place = new Place(near ? ip!.Name : null, spot.Lat, spot.Lon, ip?.TimeZone);
        }
        place ??= await ByIpAsync(cancel);
        if (place is not null) _here = (key, DateTime.UtcNow, place);
        return place;
    }

    static async Task<Place?> ByIpAsync(CancellationToken cancel)
    {
        try
        {
            using var http = Client();
            var lang = L.IsSpanish ? "es" : "en";
            var json = await http.GetStringAsync($"https://ipwho.is/?fields=success,city,latitude,longitude,timezone&lang={lang}", cancel);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
            var zone = r.TryGetProperty("timezone", out var tz) ? Json.Str(tz, "id") : null;
            return new Place(Json.Str(r, "city"), r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), zone);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    static readonly Dictionary<string, Place?> Geocoded = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<Place?> GeocodeAsync(string name, CancellationToken cancel)
    {
        name = name.Trim();
        if (Geocoded.TryGetValue(name, out var known)) return known;
        try
        {
            using var http = Client();
            var lang = L.IsSpanish ? "es" : "en";
            var json = await http.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=1&language={lang}&format=json", cancel);
            using var doc = JsonDocument.Parse(json);
            Place? place = null;
            if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var r = results[0];
                place = new Place(Json.Str(r, "name") ?? name, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), Json.Str(r, "timezone"));
            }
            Geocoded[name] = place;
            return place;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    static async Task<string?> ForecastAsync(Place place, CancellationToken cancel)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{place.Latitude:0.00},{place.Longitude:0.00}");
        if (Forecasts.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(15)) return cached.Json;
        try
        {
            using var http = Client();
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude:0.####}&longitude={place.Longitude:0.####}&current=temperature_2m,apparent_temperature,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=3");
            var json = await http.GetStringAsync(url, cancel);
            Forecasts[key] = (DateTime.UtcNow, json);
            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>El código WMO de Open-Meteo, dicho con palabras.</summary>
    static string Sky(int code) => code switch
    {
        0 => L.T("despejado"),
        1 => L.T("casi despejado"),
        2 => L.T("parcialmente nublado"),
        3 => L.T("nublado"),
        45 or 48 => L.T("con niebla"),
        51 or 53 or 55 => L.T("con llovizna"),
        56 or 57 => L.T("con llovizna helada"),
        61 => L.T("con lluvia ligera"),
        63 => L.T("con lluvia"),
        65 => L.T("con lluvia fuerte"),
        66 or 67 => L.T("con lluvia helada"),
        71 or 73 or 75 or 77 => L.T("con nieve"),
        80 or 81 or 82 => L.T("con chubascos"),
        85 or 86 => L.T("con chubascos de nieve"),
        95 => L.T("con tormenta"),
        96 or 99 => L.T("con tormenta y granizo"),
        _ => L.T("variable"),
    };
}

/// <summary>Cuántas respuestas no gastaron la nube esta semana (desde el lunes): sin IA y con el modelo local.</summary>
static class AnswerStats
{
    sealed class Week
    {
        public DateOnly Monday { get; set; }
        public int None { get; set; }
        public int Local { get; set; }
    }

    static string FilePath => DataFolder.In("respuestas.json");
    static readonly object Gate = new();

    static DateOnly ThisMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
    }

    public static (int None, int Local) ThisWeek()
    {
        lock (Gate)
        {
            var week = Load();
            return week.Monday == ThisMonday() ? (week.None, week.Local) : (0, 0);
        }
    }

    public static void Add(Engine engine)
    {
        if (engine == Engine.Cloud) return;
        lock (Gate)
        {
            var week = Load();
            if (week.Monday != ThisMonday()) week = new Week { Monday = ThisMonday() };
            if (engine == Engine.None) week.None++;
            else week.Local++;
            try
            {
                Directory.CreateDirectory(DataFolder.Root);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(week));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    static Week Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Week>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
}
