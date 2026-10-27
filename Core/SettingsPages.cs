namespace Claupet;

/// <summary>Lo que muestra «Ajustes»; lo manda TrayApp cada vez que cambia algo. Los textos son los valores ya listos para mostrar.</summary>
sealed record SettingsState
{
    public bool Notifications { get; init; } = true;
    public bool Sound { get; init; } = true;
    public string SoundPack { get; init; } = "";
    public string Palette { get; init; } = "";
    public string Mascot { get; init; } = "";
    public string MascotSize { get; init; } = "";
    public bool DesktopMascot { get; init; }
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
    /// <summary>Cuántas capacidades tienen «Permitir siempre» («3 de 10 permitidas»).</summary>
    public string Permissions { get; init; } = "";
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
    public int Accounts { get; init; }
    /// <summary>El idioma elegido, en su idioma («Español», «English»).</summary>
    public string Language { get; init; } = "";
    /// <summary>Cuántas extensiones hay y cuántas encendidas («Ninguna», «2 de 3 encendidas»).</summary>
    public string Extensions { get; init; } = "";
    /// <summary>Respuestas: el modelo local listo, cuándo suelta la memoria y el modelo de la nube («Claude · Haiku»).</summary>
    public bool LocalReady { get; init; }
    public string LocalIdle { get; init; } = "";
    public string CloudModel { get; init; } = "";
    /// <summary>Quién responde primero, en resumen («4 sin IA · 7 en la nube»); el detalle se elige en su menú.</summary>
    public string Topics { get; init; } = "";
    /// <summary>El modelo local responde preguntas generales: se avisa que puede inventar.</summary>
    public bool GeneralLocal { get; init; }
    public string Fallback { get; init; } = "";
    public string FallbackHint { get; init; } = "";
    public bool ShowWho { get; init; }
    public string City { get; init; } = "";
    public string Saved { get; init; } = "";
    public string SavedDetail { get; init; } = "";
    /// <summary>Cada tipo de pregunta con su motor, para Claupet Console (en la bandeja van en un menú).</summary>
    public IReadOnlyList<TopicState> TopicEngines { get; init; } = [];
    /// <summary>Cada capacidad y si tiene «Permitir siempre», para Claupet Console (en la bandeja van en un menú).</summary>
    public IReadOnlyList<GrantState> Grants { get; init; } = [];
}

/// <summary>Un tipo de pregunta y quién lo responde primero, para elegirlo uno por uno en Claupet Console.</summary>
sealed record TopicState(string Id, string Label, string Engine, bool Fixed);

/// <summary>Una capacidad (captura, calendario, ventanas…) y si tiene «Permitir siempre».</summary>
sealed record GrantState(string Id, string Label, bool Allowed);

enum RowKind { Caption, Switch, Choice, Info, Note, Link, Links }

/// <summary>El color de una nota o de un enlace: el de siempre, el apagado, el de cuidado, el acento o el de peligro.</summary>
enum RowTone { Normal, Muted, Warn, Accent, Danger }

/// <summary>Dónde va una fila: en las dos, solo en los ajustes rápidos de la bandeja o solo en Claupet Console.</summary>
enum RowPlace { Both, Tray, Console }

sealed record SettingsLink(string Text, string Key);

/// <summary>
/// Una fila de Ajustes. Text es la etiqueta; Value, lo que se muestra a la derecha (la opción elegida, el dato
/// o el texto del enlace); Key, la acción que se le pide a la bandeja al tocarla.
/// </summary>
sealed record SettingsRow(RowKind Kind, string Text, string Key = "", string Value = "", bool On = false,
    RowTone Tone = RowTone.Normal, bool Swatch = false, RowPlace Place = RowPlace.Both, IReadOnlyList<SettingsLink>? Links = null);

/// <summary>Una sección de Ajustes. Tab es la pestaña de los ajustes rápidos donde van sus filas.</summary>
sealed record SettingsPage(string Id, string Title, string Description, string Tab, IReadOnlyList<SettingsRow> Rows);

/// <summary>
/// Las secciones de Ajustes y sus filas, armadas una sola vez: con ellas se dibujan las pestañas de la bandeja
/// y las secciones de Claupet Console. Cada fila lleva su acción; la bandeja es la única que cambia los ajustes.
/// </summary>
static class SettingsPages
{
    public static IReadOnlyList<SettingsPage> Build(SettingsState s) =>
        [GeneralPage(s), NoticesPage(s), LooksPage(s), MascotPage(s), AssistantPage(s), AnswersPage(s), IslandPage(s), AccountsPage(s), MachinePage(s)];

    static SettingsRow Caption(string text, RowPlace place = RowPlace.Both) => new(RowKind.Caption, text, Place: place);
    static SettingsRow Switch(string text, string key, bool on) => new(RowKind.Switch, text, key, On: on);
    static SettingsRow Choice(string text, string key, string value, RowPlace place = RowPlace.Both) => new(RowKind.Choice, text, key, value, Place: place);
    static SettingsRow Info(string text, string value) => new(RowKind.Info, text, Value: value);
    static SettingsRow Note(string text, RowTone tone = RowTone.Muted) => new(RowKind.Note, text, Tone: tone);
    static SettingsRow Link(string text, string link, string key, RowTone tone = RowTone.Accent) => new(RowKind.Link, text, key, link, Tone: tone);

    static SettingsPage GeneralPage(SettingsState s) => new("general", L.T("General"), L.T("El idioma y el inicio de Claupet."), "Sistema",
    [
        Choice(L.T("Idioma"), "language", s.Language),
        Switch(L.T("Iniciar con Windows"), "startup", s.StartWithWindows),
    ]);

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
        Choice(L.T("Mascota"), "mascot", s.Mascot),
        Choice(L.T("Tamaño de la mascota"), "size", s.MascotSize),
    ]);

    static SettingsPage MascotPage(SettingsState s)
    {
        List<SettingsRow> rows = [Switch(L.T("Mascota en el escritorio"), "desk", s.DesktopMascot)];
        if (s.DesktopMascot)
            rows.AddRange(
            [
                Switch(L.T("Gestos del flujo (lo que hace la sesión)"), "flowgest", s.FlowGestures),
                Switch(L.T("Bailar con música"), "music", s.DanceToMusic),
                Switch(L.T("Gesto de los avisos"), "noticegest", s.NoticeGestures),
                Switch(L.T("Saludar al aparecer"), "greet", s.GreetOnAppear),
                Switch(L.T("Enojarse con muchos clics"), "angry", s.AngryOnClicks),
                Switch(L.T("Dormir sin sesiones"), "sleep", s.SleepWithoutSessions),
                Switch(L.T("Hablar (los avisos en una nubecita)"), "speak", s.Speak),
            ]);
        return new("mascota", L.T("Mascota"), L.T("Lo que hace la mascota en el escritorio."), "Apariencia", rows);
    }

    static SettingsPage AssistantPage(SettingsState s)
    {
        List<SettingsRow> rows = [Caption(L.T("Chat")), Switch(L.T("Chat con la mascota"), "chat", s.Chat)];
        if (s.Chat)
            rows.AddRange(
            [
                Choice(L.T("Modelo de la mascota"), "mmodel", s.MascotModel),
                Choice(L.T("Cuenta que responde"), "account", s.AssistantAccount),
                Choice(L.T("Carpeta de las tareas"), "folder", s.TaskFolder),
                Switch(L.T("Analizar los archivos que le suelte"), "drops", s.AnalyzeDrops),
            ]);
        // Lo que hace en el equipo (captura, calendario, ventanas…) se pide desde el chat o la isla.
        if (s.Chat || s.Island)
        {
            // En la bandeja, un menú con las capacidades; en la Console, un interruptor por cada una.
            rows.Add(Choice(L.T("Permisos"), "grants", s.Permissions, RowPlace.Tray));
            rows.Add(Caption(L.T("Permisos"), RowPlace.Console));
            rows.AddRange(s.Grants.Select(g => Switch(g.Label, "grant:" + g.Id, g.Allowed) with { Place = RowPlace.Console }));
            rows.Add(Note(L.T("Apagado: se pregunta la primera vez que lo use.")) with { Place = RowPlace.Console });
        }
        rows.Add(Caption(L.T("Voz")));
        rows.Add(Switch(L.T("Micrófono"), "mic", s.Mic));
        if (s.Mic)
        {
            rows.Add(Choice(L.T("Tecla para hablar"), "mickey", s.MicKey));
            rows.Add(Link(s.VoiceStatus, s.VoiceAction, "voice"));
        }
        // Aparte del micrófono: puede escribirle y que le responda hablando.
        rows.Add(Switch(L.T("Leer las respuestas en voz alta"), "readaloud", s.ReadAloud));
        if (s.ReadAloud)
        {
            rows.Add(Choice(L.T("Voz"), "readvoice", s.ReadAloudVoice));
            if (s.SpeechStatus.Length > 0) rows.Add(Link(s.SpeechStatus, s.SpeechAction, "speech"));
        }
        if (s.Chat || s.Island)
            rows.AddRange(
            [
                Caption(L.T("Teclas")),
                Choice(L.T("Atajo del chat"), "chatkey", s.ChatKey),
                Switch(s.Mic ? L.T("Tecla Copilot: chat y voz") : L.T("Tecla Copilot: abre el chat"), "copilot", s.CopilotKey),
                Link(L.T("Atajos de teclado"), L.T("Ver…"), "shortcuts"),
            ]);
        return new("asistente", L.T("Asistente"), L.T("El chat con la mascota, la voz y las teclas."), "Asistente", rows);
    }

    static SettingsPage AnswersPage(SettingsState s)
    {
        List<SettingsRow> rows =
        [
            Caption(L.T("Motores")),
            Info(L.T("Sin IA"), L.T("Siempre activo · 0 tokens")),
            Choice(L.T("Modelo local"), "localmodel", s.LocalModel),
            Link(s.LocalStatus, s.LocalAction, "local", s.LocalAction == L.T("Borrar") ? RowTone.Danger : RowTone.Accent),
        ];
        if (s.LocalReady) rows.Add(Choice(L.T("Liberar la memoria tras"), "localidle", s.LocalIdle));
        rows.Add(Info(L.T("Nube"), s.CloudModel));
        rows.Add(Caption(L.T("Quién responde primero")));
        // En la bandeja, un menú con los tipos; en la Console, una fila por tipo.
        rows.Add(Choice(L.T("Por tipo de pregunta"), "topics", s.Topics, RowPlace.Tray));
        foreach (var topic in s.TopicEngines)
            rows.Add(topic.Fixed ? Info(topic.Label, topic.Engine) with { Place = RowPlace.Console }
                : Choice(topic.Label, "topic:" + topic.Id, topic.Engine, RowPlace.Console));
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
        return new("respuestas", L.T("Respuestas"), L.T("Quién responde cada pregunta: sin IA, el modelo local o la nube."), "Respuestas", rows);
    }

    static SettingsPage IslandPage(SettingsState s)
    {
        List<SettingsRow> rows = [Switch(L.T("Isla en el borde de arriba"), "island", s.Island)];
        if (s.Island)
            rows.AddRange(
            [
                Choice(L.T("Modelo de la isla"), "imodel", s.IslandModel),
                Switch(L.T("Abrir al pasar el cursor"), "hover", s.IslandHover),
                Switch(L.T("A la vista mientras algo trabaja"), "busy", s.IslandWhileBusy),
                Link(L.T("Permisos y preguntas en la isla"), s.Approvals, "approvals"),
                Switch(L.T("Resumen de la semana, los lunes"), "recap", s.WeeklyRecap),
                Link(L.T("El de esta semana"), L.T("Ver ahora"), "recapnow"),
            ]);
        return new("isla", L.T("Isla"), L.T("La isla del borde de arriba: el chat, los permisos y el resumen de la semana."), "Isla", rows);
    }

    static SettingsPage AccountsPage(SettingsState s) => new("cuentas", L.T("Cuentas"), L.T("Las cuentas de Claude Code que sigue Claupet."), "Sistema",
    [
        Link(s.Accounts == 1 ? L.T("1 cuenta") : L.T("{0} cuentas", s.Accounts), L.T("Agregar cuenta…"), "add"),
    ]);

    static SettingsPage MachinePage(SettingsState s) => new("sistema", L.T("Sistema"), L.T("Las extensiones, el hook de permisos y el diagnóstico."), "Sistema",
    [
        Choice(L.T("Extensiones"), "extensions", s.Extensions),
        Link(L.T("Un paquete .claupet (o suéltelo en la mascota)"), L.T("Instalar…"), "extinstall"),
        Link(L.T("Avisos de permisos (hook)"), L.T("Configurar…"), "setup"),
        Link(L.T("Si algo no funciona"), L.T("Copiar diagnóstico"), "diag"),
        Link(L.T("Quitar hook e inicio con Windows"), L.T("Quitar…"), "unsetup", RowTone.Danger),
    ]);
}
