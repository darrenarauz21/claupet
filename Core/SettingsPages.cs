namespace Claupet;

/// <summary>
/// Lo que una app sabe hacer en este sistema. Las filas de Ajustes de lo que falta no aparecen (en vez de quedar como
/// botones que no hacen nada); al llegar a Linux o macOS, aparecen solas.
/// </summary>
[Flags]
enum PlatformFeatures
{
    None = 0,
    /// <summary>El chat con la mascota (la nubecita).</summary>
    MascotChat = 1,
    /// <summary>Lo que hace en el equipo (captura, calendario, ventanas…), con sus permisos.</summary>
    Actions = 2,
    Mic = 4,
    ReadAloud = 8,
    Hotkeys = 16,
    Island = 32,
    Extensions = 64,
    LocalModel = 128,
    MascotSize = 256,
    Shortcuts = 512,
    /// <summary>Soltar archivos sobre la mascota: los analiza o los encarga.</summary>
    FileDrops = 1024,
    /// <summary>Buscar versiones nuevas en GitHub (Updates).</summary>
    Updates = 2048,
    All = MascotChat | Actions | Mic | ReadAloud | Hotkeys | Island | Extensions | LocalModel | MascotSize | Shortcuts | FileDrops | Updates,
}

/// <summary>Lo que muestra «Ajustes»; lo manda TrayApp cada vez que cambia algo. Los textos son los valores ya listos para mostrar.</summary>
sealed record SettingsState
{
    /// <summary>Lo que esta app sabe hacer en este sistema (Windows: todo).</summary>
    public PlatformFeatures Features { get; init; } = PlatformFeatures.All;
    public bool Has(PlatformFeatures feature) => (Features & feature) == feature;
    public bool Notifications { get; init; } = true;
    public bool Sound { get; init; } = true;
    /// <summary>Cómo va la creación de un tema propio («Creando…» o el error); null, nada que decir.</summary>
    public string? ThemeStatus { get; init; }
    /// <summary>Esta bandeja crea y borra temas propios (por ahora, la de Avalonia).</summary>
    public bool CustomThemesHere { get; init; }
    public string SoundPack { get; init; } = "";
    public string Palette { get; init; } = "";
    public string Mascot { get; init; } = "";
    public string MascotSize { get; init; } = "";
    public bool DesktopMascot { get; init; }
    public bool MascotAllDesktops { get; init; }
    public bool Speak { get; init; }
    public bool FlowGestures { get; init; }
    public bool DanceToMusic { get; init; }
    public bool NoticeGestures { get; init; }
    public bool GreetOnAppear { get; init; }
    public bool AngryOnClicks { get; init; }
    public bool SleepWithoutSessions { get; init; }
    public bool Chat { get; init; }
    public string MascotModel { get; init; } = "";
    public string AssistantAccount { get; init; } = "";
    public string TaskFolder { get; init; } = "";
    public bool AnalyzeDrops { get; init; }
    public bool Mic { get; init; }
    public string MicKey { get; init; } = "";
    public string ChatKey { get; init; } = "";
    public bool CopilotKey { get; init; }
    /// <summary>El modelo de voz: lo que dice la fila y su enlace (descargar, el avance o listo).</summary>
    public string VoiceStatus { get; init; } = "";
    public string VoiceAction { get; init; } = "";
    public bool ReadAloud { get; init; }
    /// <summary>La voz que lee y, si es de Claupet y falta su modelo, lo que dice su fila y su enlace (vacío: sin fila).</summary>
    public string ReadAloudVoice { get; init; } = "";
    public string SpeechStatus { get; init; } = "";
    public string SpeechAction { get; init; } = "";
    /// <summary>El modelo local: el elegido (nombre y RAM) y lo que dice su fila y su enlace (descargar, el avance, borrar).</summary>
    public string LocalModel { get; init; } = "";
    public string LocalStatus { get; init; } = "";
    public string LocalAction { get; init; } = "";
    public bool Island { get; init; }
    public string IslandModel { get; init; } = "";
    public bool IslandHover { get; init; }
    public bool IslandWhileBusy { get; init; }
    public bool WeeklyRecap { get; init; }
    /// <summary>El hook de permisos y preguntas: «Configurar…» o «Listo».</summary>
    public string Approvals { get; init; } = "";
    public bool StartWithWindows { get; init; }
    /// <summary>Los accesos directos que abren Claupet Console, en el menú Inicio y en el escritorio.</summary>
    public bool StartMenuShortcut { get; init; }
    public bool DesktopShortcut { get; init; }
    public int Accounts { get; init; }
    /// <summary>El idioma elegido, en su idioma («Español», «English»).</summary>
    public string Language { get; init; } = "";
    /// <summary>Cuántas extensiones hay y cuántas encendidas («Ninguna», «2 de 3 encendidas»).</summary>
    public string Extensions { get; init; } = "";
    /// <summary>Respuestas: el modelo local listo, cuándo suelta la memoria y el modelo de la nube («Claude · Haiku»).</summary>
    public bool LocalReady { get; init; }
    public string LocalIdle { get; init; } = "";
    public string CloudModel { get; init; } = "";
    /// <summary>El modelo local responde preguntas generales: se avisa que puede inventar.</summary>
    public bool GeneralLocal { get; init; }
    public string Fallback { get; init; } = "";
    public string FallbackHint { get; init; } = "";
    public bool ShowWho { get; init; }
    public string City { get; init; } = "";
    public string Saved { get; init; } = "";
    public string SavedDetail { get; init; } = "";
    /// <summary>El modo de «Quién responde primero» (Por defecto, Ahorro…), lo que hace y si es Personalizado (con las filas de cada tipo).</summary>
    public string AnswerPreset { get; init; } = "";
    public string AnswerPresetHint { get; init; } = "";
    public bool AnswerCustom { get; init; }
    /// <summary>Cada tipo de pregunta con su motor, para Claupet Console (en la bandeja van en un menú).</summary>
    public IReadOnlyList<TopicState> TopicEngines { get; init; } = [];
    /// <summary>Cada capacidad y si tiene «Permitir siempre», para Claupet Console (en la bandeja van en un menú).</summary>
    public IReadOnlyList<GrantState> Grants { get; init; } = [];
    /// <summary>Cada cuenta de Claude Code y su memoria (compartida con otra o propia), para Ajustes › Cuentas.</summary>
    public IReadOnlyList<AccountState> AccountList { get; init; } = [];
    /// <summary>Los otros agentes encontrados (Codex, Gemini, Copilot), si se siguen y qué se leyó de ellos.</summary>
    public IReadOnlyList<AgentState> AgentList { get; init; } = [];
    /// <summary>«Buscar actualizaciones» y la fila de la versión nueva (Updates.Row: lo que dice y su enlace; vacío, sin fila).</summary>
    public bool CheckUpdates { get; init; } = true;
    public string UpdateStatus { get; init; } = "";
    public string UpdateAction { get; init; } = "";
}

/// <summary>Un tipo de pregunta y quién lo responde primero, para elegirlo uno por uno en Claupet Console.</summary>
sealed record TopicState(string Id, string Label, string Engine, bool Fixed);

/// <summary>Una capacidad (captura, calendario, ventanas…) y si tiene «Permitir siempre».</summary>
sealed record GrantState(string Id, string Label, bool Allowed);

/// <summary>Una cuenta de Claude Code y lo que se dice de su memoria («Compartida con «trabajo»», «Propia»).</summary>
sealed record AccountState(string Label, string Memory);

/// <summary>Otro agente: su tipo, su nombre, si se sigue y lo que se dice de él (su carpeta, su última sesión).</summary>
sealed record AgentState(string Kind, string Label, bool Follow, string Detail);

/// <summary>
/// El tipo de una fila. Input: un cuadro para escribir (la acción lleva lo escrito en Value). Page: lleva a otra
/// página (Key es su id) sin pedirle nada a la bandeja, como el detalle de una extensión.
/// </summary>
enum RowKind { Caption, Switch, Choice, Info, Note, Link, Links, Input, Page }

/// <summary>El color de una nota o de un enlace: el de siempre, el apagado, el de cuidado, el acento o el de peligro.</summary>
enum RowTone { Normal, Muted, Warn, Accent, Danger }

/// <summary>Dónde va una fila: en los ajustes rápidos de la bandeja y en Claupet Console, o solo en la Console.</summary>
enum RowPlace { Both, Console }

sealed record SettingsLink(string Text, string Key);

/// <summary>
/// Una fila de Ajustes. Text es la etiqueta; Value, lo que se muestra a la derecha (la opción elegida, el dato
/// o el texto del enlace; en un cuadro para escribir, lo escrito); Key, la acción que se le pide a la bandeja al tocarla.
/// En un cuadro para escribir, Secret oculta lo escrito y Placeholder es lo que se ve mientras está vacío. En un
/// enlace, File (como «*.claupet») hace que la Console pida antes un archivo y mande su ruta en Value, y Copy hace que
/// la Console copie ese texto sin pedirle nada a la bandeja.
/// </summary>
sealed record SettingsRow(RowKind Kind, string Text, string Key = "", string Value = "", bool On = false,
    RowTone Tone = RowTone.Normal, bool Swatch = false, RowPlace Place = RowPlace.Both, IReadOnlyList<SettingsLink>? Links = null,
    bool Secret = false, string? Placeholder = null, string? File = null, string? Copy = null);

/// <summary>
/// Una sección de Ajustes. Tab es la pestaña de los ajustes rápidos donde van sus filas (las que no son solo de
/// la Console); null: la sección solo está en Claupet Console. Parent: es una página dentro de esa sección (el detalle
/// de una extensión): no va en la lista de la izquierda y tiene un enlace para volver.
/// </summary>
sealed record SettingsPage(string Id, string Title, string Description, string? Tab, IReadOnlyList<SettingsRow> Rows, string? Parent = null);

/// <summary>
/// Las secciones de Ajustes y sus filas, armadas una sola vez: con ellas se dibujan las pestañas de los ajustes
/// rápidos de la bandeja y las secciones de Claupet Console. Los rápidos llevan solo lo de todos los días; el
/// resto (RowPlace.Console) está en la Console. Cada fila lleva su acción; la bandeja es la única que cambia los ajustes.
/// </summary>
static class SettingsPages
{
    public static IReadOnlyList<SettingsPage> Build(SettingsState s) =>
    [
        GeneralPage(s), NoticesPage(s), LooksPage(s), MascotPage(s), AssistantPage(s), AnswersPage(s), ExtensionsPage(s),
        IslandPage(s), AccountsPage(s), MachinePage(s),
    ];

    internal static SettingsRow Caption(string text) => new(RowKind.Caption, text, Place: RowPlace.Console);
    internal static SettingsRow Switch(string text, string key, bool on) => new(RowKind.Switch, text, key, On: on);
    internal static SettingsRow Choice(string text, string key, string value) => new(RowKind.Choice, text, key, value);
    internal static SettingsRow Info(string text, string value) => new(RowKind.Info, text, Value: value);
    internal static SettingsRow Note(string text, RowTone tone = RowTone.Muted) => new(RowKind.Note, text, Tone: tone);
    internal static SettingsRow Link(string text, string link, string key, RowTone tone = RowTone.Accent) => new(RowKind.Link, text, key, link, Tone: tone);
    /// <summary>Una fila de enlaces juntos, sin etiqueta (como «Ver un aviso de ejemplo · Abrir carpeta de sonidos»).</summary>
    internal static SettingsRow Links(params SettingsLink[] links) => new(RowKind.Links, "", Links: links);

    /// <summary>Solo en Claupet Console: no es de todos los días.</summary>
    static SettingsRow Full(SettingsRow row) => row with { Place = RowPlace.Console };

    /// <summary>Lo que falta en este sistema: una nota en vez de filas que no hacen nada.</summary>
    static SettingsRow NotHere() => Note(L.T("Todavía no está disponible en este sistema."));

    static SettingsPage GeneralPage(SettingsState s)
    {
        List<SettingsRow> rows =
        [
            Choice(L.T("Idioma"), "language", s.Language),
            Switch(OperatingSystem.IsWindows() ? L.T("Iniciar con Windows") : L.T("Iniciar con el sistema"), "startup", s.StartWithWindows),
        ];
        if (s.Has(PlatformFeatures.Shortcuts))
            rows.AddRange(new[]
            {
                Caption(L.T("Accesos directos")),
                Switch(L.T("En el menú Inicio"), "startmenu", s.StartMenuShortcut),
                Switch(L.T("En el escritorio"), "desktoplink", s.DesktopShortcut),
                Note(L.T("Abren Claupet Console, como cualquier app; si Claupet no estaba abierto, lo abren también.")),
            }.Select(Full));
        return new("general", L.T("General"), L.T("El idioma, el inicio de Claupet y sus accesos directos."), "Sistema", rows);
    }

    static SettingsPage NoticesPage(SettingsState s) => new("avisos", L.T("Avisos"), L.T("Cómo le avisa Claupet cuando una sesión termina o necesita algo."), "Avisos",
    [
        Switch(L.T("Notificaciones"), "notify", s.Notifications),
        Switch(L.T("Sonido"), "sound", s.Sound),
        Choice(L.T("Estilo de sonido"), "pack", s.SoundPack),
        new(RowKind.Links, "", Links: [new(L.T("Ver un aviso de ejemplo"), "sample"), new(L.T("Abrir carpeta de sonidos"), "sounds")]),
    ]);

    static SettingsPage LooksPage(SettingsState s) => new("apariencia", L.T("Apariencia"), L.T("El color de la app y la mascota que lo acompaña."), "Apariencia",
    [
        new(RowKind.Choice, L.T("Color"), "palette", s.Palette, Swatch: true),
        // Los temas propios (CustomThemes): se crean con Claude a partir de un prompt y se borran aquí, solo en la Console
        // y donde la bandeja lo sepa hacer (CustomThemesHere).
        .. s.CustomThemesHere
            ? [new SettingsRow(RowKind.Input, L.T("Crear un tema…"), ThemeCreateKey, Place: RowPlace.Console, Placeholder: L.T("Por ejemplo, «otoñal, con una letra redonda»"))]
            : Array.Empty<SettingsRow>(),
        .. s.ThemeStatus is { } status ? [new SettingsRow(RowKind.Note, status, Tone: RowTone.Muted, Place: RowPlace.Console)] : Array.Empty<SettingsRow>(),
        .. s.CustomThemesHere && CustomThemes.Selected is { } custom
            ? [new SettingsRow(RowKind.Links, "", Place: RowPlace.Console, Links: [new(L.T("Borrar el tema «{0}»", custom.Name), ThemeDeleteKey)])]
            : Array.Empty<SettingsRow>(),
        Choice(L.T("Mascota"), "mascot", s.Mascot),
        .. s.Has(PlatformFeatures.MascotSize) ? [Choice(L.T("Tamaño de la mascota"), "size", s.MascotSize)] : Array.Empty<SettingsRow>(),
    ]);

    /// <summary>Las claves de las filas de los temas propios: el cuadro del prompt y el enlace para borrar el elegido.</summary>
    public const string ThemeCreateKey = "tema-crear", ThemeDeleteKey = "tema-borrar";

    static SettingsPage MascotPage(SettingsState s)
    {
        List<SettingsRow> rows = [Switch(L.T("Mascota en el escritorio"), "desk", s.DesktopMascot)];
        if (s.DesktopMascot)
            rows.AddRange(new[]
            {
                Switch(L.T("En todos los escritorios"), "alldesks", s.MascotAllDesktops),
                Note(L.T("Sigue a la vista al cambiar de escritorio virtual (en macOS, de Espacio).")),
                Switch(L.T("Gestos del flujo (lo que hace la sesión)"), "flowgest", s.FlowGestures),
                Switch(L.T("Bailar con música"), "music", s.DanceToMusic),
                Switch(L.T("Gesto de los avisos"), "noticegest", s.NoticeGestures),
                Switch(L.T("Saludar al aparecer"), "greet", s.GreetOnAppear),
                Switch(L.T("Enojarse con muchos clics"), "angry", s.AngryOnClicks),
                Switch(L.T("Dormir sin sesiones"), "sleep", s.SleepWithoutSessions),
                Switch(L.T("Hablar (los avisos en una nubecita)"), "speak", s.Speak),
            }.Select(Full));
        return new("mascota", L.T("Mascota"), L.T("Lo que hace la mascota en el escritorio."), "Apariencia", rows);
    }

    static SettingsPage AssistantPage(SettingsState s)
    {
        List<SettingsRow> rows = [];
        if (s.Has(PlatformFeatures.MascotChat)) rows.AddRange([Caption(L.T("Chat")), Switch(L.T("Chat con la mascota"), "chat", s.Chat)]);
        if (s.Chat && s.Has(PlatformFeatures.MascotChat))
        {
            rows.Add(Choice(L.T("Modelo de la mascota"), "mmodel", s.MascotModel));
            rows.AddRange(new[]
            {
                Choice(L.T("Cuenta que responde"), "account", s.AssistantAccount),
                Choice(L.T("Carpeta de las tareas"), "folder", s.TaskFolder),
            }.Select(Full));
            if (s.Has(PlatformFeatures.FileDrops)) rows.Add(Full(Switch(L.T("Analizar los archivos que le suelte"), "drops", s.AnalyzeDrops)));
        }
        // Lo que hace en el equipo (captura, calendario, ventanas…) se pide desde el chat o la isla: un interruptor por capacidad.
        if ((s.Chat || s.Island) && s.Has(PlatformFeatures.Actions))
        {
            rows.Add(Caption(L.T("Permisos")));
            rows.AddRange(s.Grants.Select(g => Full(Switch(g.Label, "grant:" + g.Id, g.Allowed))));
            rows.Add(Full(Note(L.T("Apagado: se pregunta la primera vez que lo use."))));
        }
        if (s.Has(PlatformFeatures.Mic) || s.Has(PlatformFeatures.ReadAloud)) rows.Add(Caption(L.T("Voz")));
        if (s.Has(PlatformFeatures.Mic)) rows.Add(Switch(L.T("Micrófono"), "mic", s.Mic));
        if (s.Mic && s.Has(PlatformFeatures.Mic))
        {
            rows.Add(Full(Choice(L.T("Tecla para hablar"), "mickey", s.MicKey)));
            // Mientras falta el modelo de voz (o baja), también en los rápidos: al encender el micrófono se ve que descarga.
            var voice = Link(s.VoiceStatus, s.VoiceAction, "voice");
            rows.Add(s.VoiceAction.Length > 0 ? voice : Full(voice));
        }
        // Aparte del micrófono: puede escribirle y que le responda hablando.
        if (s.Has(PlatformFeatures.ReadAloud)) rows.Add(Switch(L.T("Leer las respuestas en voz alta"), "readaloud", s.ReadAloud));
        if (s.ReadAloud && s.Has(PlatformFeatures.ReadAloud))
        {
            rows.Add(Full(Choice(L.T("Voz"), "readvoice", s.ReadAloudVoice)));
            if (s.SpeechStatus.Length > 0) rows.Add(Link(s.SpeechStatus, s.SpeechAction, "speech"));
        }
        if ((s.Chat || s.Island) && s.Has(PlatformFeatures.Hotkeys))
        {
            rows.Add(Full(Caption(L.T("Teclas"))));
            rows.Add(Full(Choice(L.T("Atajo del chat"), "chatkey", s.ChatKey)));
            // La tecla Copilot es de los teclados con Windows.
            if (OperatingSystem.IsWindows())
                rows.Add(Full(Switch(s.Mic ? L.T("Tecla Copilot: chat y voz") : L.T("Tecla Copilot: abre el chat"), "copilot", s.CopilotKey)));
            rows.Add(Full(Link(L.T("Atajos de teclado"), L.T("Ver…"), "shortcuts")));
        }
        if (rows.Count == 0) rows.Add(NotHere());
        return new("asistente", L.T("Asistente"), L.T("El chat con la mascota, la voz y las teclas."), "Asistente", rows);
    }

    static SettingsPage AnswersPage(SettingsState s)
    {
        List<SettingsRow> rows =
        [
            Caption(L.T("Motores")),
            Info(L.T("Sin IA"), L.T("Siempre activo · 0 tokens")),
        ];
        if (s.Has(PlatformFeatures.LocalModel))
        {
            rows.Add(Choice(L.T("Modelo local"), "localmodel", s.LocalModel));
            rows.Add(Link(s.LocalStatus, s.LocalAction, "local", s.LocalAction == L.T("Borrar") ? RowTone.Danger : RowTone.Accent));
            if (s.LocalReady) rows.Add(Choice(L.T("Liberar la memoria tras"), "localidle", s.LocalIdle));
        }
        rows.Add(Info(L.T("Nube"), s.CloudModel));
        rows.Add(Caption(L.T("Quién responde primero")));
        rows.Add(Choice(L.T("Modo"), "answerpreset", s.AnswerPreset));
        rows.Add(Note(s.AnswerPresetHint));
        // Tipo por tipo, solo en Personalizado; en los demás modos lo arma el modo.
        if (s.AnswerCustom)
            foreach (var topic in s.TopicEngines)
                rows.Add(topic.Fixed ? Info(topic.Label, topic.Engine) : Choice(topic.Label, "topic:" + topic.Id, topic.Engine));
        if (s.GeneralLocal) rows.Add(Note(L.T("El modelo local puede inventar datos en las preguntas generales."), RowTone.Warn));
        rows.AddRange(
        [
            Caption(L.T("Si no entiende")),
            Choice(L.T("Pasar al siguiente motor"), "fallback", s.Fallback),
            Note(s.FallbackHint),
            Switch(L.T("Mostrar quién respondió"), "who", s.ShowWho),
            Caption(L.T("Clima (Open-Meteo)")),
            Choice(L.T("Mi ciudad"), "city", s.City),
            Caption(L.T("Ahorro")),
            Info(L.T("Esta semana, sin la nube"), s.Saved),
        ]);
        if (s.SavedDetail.Length > 0) rows.Add(Note(s.SavedDetail));
        rows.Add(Note(L.T("Sin IA no gasta tokens ni sale del equipo, salvo el clima (Open-Meteo, y la ubicación por la IP con ipwho.is). Internet, archivos y tareas van siempre a la nube.")));
        return new("respuestas", L.T("Respuestas"), L.T("Quién responde cada pregunta: sin IA, el modelo local o la nube."), null, rows);
    }

    /// <summary>
    /// Sin extensiones en este sistema, la nota de siempre. Con ellas, Claupet Console la cambia por las páginas de
    /// ExtensionsPanel (la lista, el detalle de cada una, la revisión antes de instalar…); aquí queda solo el resumen.
    /// </summary>
    static SettingsPage ExtensionsPage(SettingsState s) => new(ExtensionsPanel.PageId, L.T("Extensiones"),
        L.T("Habilidades, voces, mascotas y modelos de otros. También puede soltar un .claupet sobre la mascota."), null,
        s.Has(PlatformFeatures.Extensions) ? [Info(L.T("Instaladas"), s.Extensions)] : [NotHere()]);

    static SettingsPage IslandPage(SettingsState s)
    {
        if (!s.Has(PlatformFeatures.Island))
            return new("isla", L.T("Isla"), L.T("La isla del borde de arriba: el chat, los permisos y el resumen de la semana."), "Isla", [NotHere()]);
        List<SettingsRow> rows = [Switch(L.T("Isla en el borde de arriba"), "island", s.Island)];
        if (s.Island)
        {
            rows.Add(Choice(L.T("Modelo de la isla"), "imodel", s.IslandModel));
            rows.Add(Switch(L.T("Abrir al pasar el cursor"), "hover", s.IslandHover));
            rows.AddRange(new[]
            {
                Switch(L.T("A la vista mientras algo trabaja"), "busy", s.IslandWhileBusy),
                Link(L.T("Permisos y preguntas en la isla"), s.Approvals, "approvals"),
                Switch(L.T("Resumen de la semana, los lunes"), "recap", s.WeeklyRecap),
                Link(L.T("El de esta semana"), L.T("Ver ahora"), "recapnow"),
            }.Select(Full));
        }
        return new("isla", L.T("Isla"), L.T("La isla del borde de arriba: el chat, los permisos y el resumen de la semana."), "Isla", rows);
    }

    static SettingsPage AccountsPage(SettingsState s)
    {
        List<SettingsRow> rows = [];
        if (s.AccountList.Count > 0 || s.AgentList.Count > 0) rows.Add(Caption(L.T("Claude Code")));
        foreach (var account in s.AccountList) rows.Add(Info(account.Label, account.Memory));
        rows.Add(Link(s.Accounts == 1 ? L.T("1 cuenta") : L.T("{0} cuentas", s.Accounts), L.T("Agregar cuenta…"), "add"));
        if (s.AccountList.Count > 0)
            rows.Add(Note(L.T("Al agregar una cuenta se elige su memoria: compartida (su carpeta projects se enlaza con la de otra cuenta; CLAUDE.md, skills, agentes, ajustes y credenciales siguen siendo propios) o separada.")));
        if (s.AgentList.Count > 0)
        {
            rows.Add(Caption(L.T("Otros agentes")));
            foreach (var agent in s.AgentList)
            {
                rows.Add(Switch(agent.Label, "agent:" + agent.Kind, agent.Follow));
                if (agent.Detail.Length > 0) rows.Add(Note(agent.Detail));
            }
            rows.Add(Note(L.T("Sus sesiones salen en Uso y Actividad. Solo se leen sus archivos en este equipo: no se usa su cuenta ni se gastan tokens."), RowTone.Normal));
        }
        return new("cuentas", L.T("Cuentas"), L.T("Las cuentas de Claude Code y los otros agentes que sigue Claupet."), null, rows);
    }

    static SettingsPage MachinePage(SettingsState s)
    {
        List<SettingsRow> rows =
        [
            Full(Link(L.T("Avisos de permisos (hook)"), L.T("Configurar…"), "setup")),
            Link(L.T("Si algo no funciona"), L.T("Copiar diagnóstico"), "diag"),
            Full(Link(OperatingSystem.IsWindows() ? L.T("Quitar hook e inicio con Windows") : L.T("Quitar hook e inicio con el sistema"),
                L.T("Quitar…"), "unsetup", RowTone.Danger)),
        ];
        if (s.Has(PlatformFeatures.Updates))
        {
            rows.Add(Caption(L.T("Actualizaciones")));
            rows.Add(Full(Info(L.T("Versión instalada"), SemVersion.App.ToString())));
            rows.Add(Full(Switch(L.T("Buscar actualizaciones"), "updates", s.CheckUpdates)));
            rows.Add(Full(Note(L.T("Una vez al día, en las versiones publicadas en GitHub. Nada se instala sin preguntarle."))));
            // Con una versión nueva (o bajándose), también en los rápidos: así se ve sin abrir la Console.
            if (s.UpdateStatus.Length > 0)
            {
                var row = Link(s.UpdateStatus, s.UpdateAction, "update");
                rows.Add(s.UpdateAction.Length > 0 ? row : Full(row));
            }
        }
        return new("sistema", L.T("Sistema"), L.T("El hook de permisos, el diagnóstico y las versiones nuevas."), "Sistema", rows);
    }
}
