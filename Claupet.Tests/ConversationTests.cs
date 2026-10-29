namespace Claupet.Tests;

/// <summary>El chat de Claupet Console: las conversaciones guardadas, cómo se parte una respuesta y el canal.</summary>
public class ConversationTests
{
    [Fact]
    public void Una_conversacion_nueva_no_se_guarda_hasta_su_primer_mensaje()
    {
        using var dir = new TempDir();
        var store = new ConversationStore(dir.Path);
        var conversation = store.Create();
        store.Save(conversation);
        Assert.Empty(store.List());

        var chat = new SavedChat(conversation, store);
        chat.Add(new ChatTurn(true, "¿Cómo leo un JSON en C# sin crear clases?"));
        chat.Add(new ChatTurn(false, "Con `JsonDocument`.", "Claude · Haiku"));

        var saved = Assert.Single(store.List());
        Assert.Equal(conversation.Id, saved.Id);
        Assert.Equal("¿Cómo leo un JSON en C# sin crear clases?", saved.Title);
        Assert.Equal(2, saved.Turns.Count);
        Assert.Equal("Claude · Haiku", store.Load(conversation.Id)!.Turns[1].Who);
    }

    [Fact]
    public void La_lista_va_de_la_mas_reciente_a_la_mas_vieja_y_se_puede_borrar()
    {
        using var dir = new TempDir();
        var store = new ConversationStore(dir.Path);
        var old = store.Create();
        old.Turns.Add(new ChatTurn(true, "vieja"));
        old.Updated = DateTimeOffset.Now.AddDays(-3);
        store.Save(old);
        var recent = store.Create();
        recent.Turns.Add(new ChatTurn(true, "nueva"));
        recent.Updated = DateTimeOffset.Now;
        store.Save(recent);

        Assert.Equal([recent.Id, old.Id], store.List().Select(c => c.Id));
        store.Delete(old.Id);
        Assert.Equal([recent.Id], store.List().Select(c => c.Id));
    }

    [Fact]
    public void El_id_que_manda_la_Console_se_usa_solo_si_sirve_para_un_nombre_de_archivo()
    {
        using var dir = new TempDir();
        var store = new ConversationStore(dir.Path);
        Assert.Equal("abc123def456", store.Create("abc123def456").Id);
        foreach (var bad in new[] { "..\\fuera", "a/b", "", new string('a', 41) })
        {
            var created = store.Create(bad);
            Assert.NotEqual(bad, created.Id);
            Assert.All(created.Id, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
        Assert.Null(store.Load("..\\settings"));
    }

    [Fact]
    public void El_titulo_es_el_comienzo_de_la_primera_pregunta_en_una_linea()
    {
        Assert.Equal("Hola mundo", Conversations.TitleFor("  Hola\n   mundo  "));
        var title = Conversations.TitleFor(new string('x', 80));
        Assert.Equal(48, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Los_chats_se_agrupan_por_cuando_se_usaron()
    {
        L.Use("es");
        var now = new DateTime(2026, 10, 28, 15, 0, 0);
        Assert.Equal("Hoy", Conversations.GroupOf(new DateTimeOffset(now.AddHours(-2)), now));
        Assert.Equal("Ayer", Conversations.GroupOf(new DateTimeOffset(now.AddDays(-1)), now));
        Assert.Equal("Esta semana", Conversations.GroupOf(new DateTimeOffset(now.AddDays(-4)), now));
        Assert.Equal("Antes", Conversations.GroupOf(new DateTimeOffset(now.AddDays(-9)), now));
    }

    [Fact]
    public void Una_respuesta_se_parte_en_texto_y_bloques_de_codigo()
    {
        var segments = Conversations.Split("Con esto:\n```csharp\nvar x = 1;\n```\nY listo.");
        Assert.Equal(3, segments.Count);
        Assert.Equal(new ChatSegment("Con esto:"), segments[0]);
        Assert.Equal(new ChatSegment("var x = 1;", Code: true, "csharp"), segments[1]);
        Assert.Equal(new ChatSegment("Y listo."), segments[2]);
    }

    [Fact]
    public void Un_bloque_que_todavia_esta_llegando_llega_hasta_el_final()
    {
        var segments = Conversations.Split("Mire:\n```py\nprint('a')\nprint(");
        Assert.Equal(new ChatSegment("print('a')\nprint(", Code: true, "py"), segments[^1]);
        // Recién abierto, sin salto de línea todavía: un bloque vacío con lo que se sabe del lenguaje.
        Assert.Equal(new ChatSegment("", Code: true, "js"), Conversations.Split("Va:\n```js")[^1]);
    }

    [Fact]
    public void El_codigo_en_linea_se_separa_y_una_comilla_sola_queda_como_esta()
    {
        Assert.Equal([new ChatSpan("Use "), new ChatSpan("JsonDocument", Code: true), new ChatSpan(" y ya.")],
            Conversations.Spans("Use `JsonDocument` y ya."));
        Assert.Equal([new ChatSpan("una "), new ChatSpan("`sola")], Conversations.Spans("una `sola"));
    }

    [Fact]
    public void El_codigo_se_colorea_por_palabras_clave_textos_y_comentarios()
    {
        var tokens = CodeColors.Tokens("var nombre = \"Ana\"; // saludo\nreturn nombre;");
        Assert.Contains(("var", CodeColors.Kind.Keyword), tokens);
        Assert.Contains(("\"Ana\"", CodeColors.Kind.String), tokens);
        Assert.Contains(("// saludo", CodeColors.Kind.Comment), tokens);
        Assert.Contains(("return", CodeColors.Kind.Keyword), tokens);
        Assert.Equal("var nombre = \"Ana\"; // saludo\nreturn nombre;", string.Concat(tokens.Select(t => t.Text)));
        // Una comilla sin cerrar llega hasta el final de la línea, sin llevarse el salto.
        var open = CodeColors.Tokens("x = 'abc\ny");
        Assert.Contains(("'abc", CodeColors.Kind.String), open);
        Assert.Equal("x = 'abc\ny", string.Concat(open.Select(t => t.Text)));
    }

    [Fact]
    public void La_conversacion_y_la_lista_pasan_enteras_por_el_canal()
    {
        var view = new ChatView("rapida", "Conversación rápida",
            [new ChatTurn(true, "hola"), new ChatTurn(false, "¡Hola! (^_^)", "Sin IA · 0 tokens")], "escrib", true, "Claude · Haiku", ReadAloud: true,
            About: "Para preguntas sueltas.");
        var back = ConsoleLink.Decode(ConsoleLink.Encode(new ConsoleMessage { Type = ConsoleLink.Chat, Chat = view }))!.Chat!;
        Assert.Equal(view.Id, back.Id);
        Assert.Equal(view.Turns, back.Turns);
        Assert.Equal("escrib", back.Streaming);
        Assert.True(back.Asking);
        Assert.True(back.ReadAloud);
        Assert.Equal("Para preguntas sueltas.", back.About); // lo dice la bandeja: cambia según el sistema
        // Una guardada no lo lleva: la Console pone el suyo.
        var saved = ConsoleLink.Decode(ConsoleLink.Encode(new ConsoleMessage { Type = ConsoleLink.Chat, Chat = view with { Id = "abc", About = null } }))!;
        Assert.Null(saved.Chat!.About);

        var list = new List<ChatSummary> { new("rapida", "Conversación rápida", "hola", DateTimeOffset.Now, Quick: true) };
        var chats = ConsoleLink.Decode(ConsoleLink.Encode(new ConsoleMessage { Type = ConsoleLink.Chats, Chats = list }))!.Chats!;
        Assert.True(Assert.Single(chats).Quick);
    }
}
