using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TextBlock = Avalonia.Controls.TextBlock;
using TextBox = Avalonia.Controls.TextBox;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>Especialistas (experimental): se guardan, vienen de extensiones y cada agente recibe sus instrucciones y solo sus herramientas.</summary>
public sealed class SpecialistsTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly string _folder = Specialists.Folder;
    readonly Func<IReadOnlyList<Specialist>> _extensions = Specialists.FromExtensions;

    public SpecialistsTests()
    {
        Specialists.Folder = _dir["especialistas"];
        Specialists.FromExtensions = () => [];
    }

    public void Dispose()
    {
        Specialists.Folder = _folder;
        Specialists.FromExtensions = _extensions;
        _dir.Dispose();
    }

    static Specialist Reviewer() => new()
    {
        Id = Specialists.NewId(),
        Name = "Revisor de PRs",
        Role = "Revisa los cambios",
        Instructions = "Agrupe los comentarios por archivo.",
        Model = "opus",
        Effort = "high",
        Tools = new()
        {
            [Specialist.CanvasTool] = new SpecialistTool { On = true, Ask = false },
            ["notas"] = new SpecialistTool { On = true, Ask = false },
            ["github"] = new SpecialistTool { On = true, Ask = true },
        },
    };

    static readonly McpServer Notas = new("notas", McpServerSpec.Stdio, @"C:\node\node.exe", ["server.js"],
        new Dictionary<string, string>(), null, new Dictionary<string, string>(), "prueba.notas");
    static readonly McpServer GitHub = new("github", McpServerSpec.Http, null, [], new Dictionary<string, string>(),
        "https://api.example.com/mcp", new Dictionary<string, string>(), "prueba.github");
    static readonly McpServer Otro = new("otro", McpServerSpec.Stdio, @"C:\otro.exe", [],
        new Dictionary<string, string>(), null, new Dictionary<string, string>(), "prueba.otro");

    [Fact]
    public void Se_guardan_se_leen_y_se_borran()
    {
        var s = Reviewer();
        Specialists.Save(s);
        var read = Assert.Single(Specialists.Mine());
        Assert.Equal(s.Id, read.Id);
        Assert.Equal("Revisor de PRs", read.Name);
        Assert.True(read.Uses("notas"));
        Assert.False(read.AsksFor("notas"));
        Assert.True(read.AsksFor("github"));
        Assert.False(read.Uses("nada"));

        Specialists.Delete(read);
        Assert.Empty(Specialists.Mine());
    }

    [Fact]
    public void Lo_que_no_sirve_se_arregla_al_guardar()
    {
        var s = new Specialist { Name = new string('x', 90), Color = "#000000", Glyph = "nada", Agent = "otro", Model = "opus", Folder = "" };
        Specialists.Save(s);
        var read = Assert.Single(Specialists.Mine());
        Assert.Equal(Specialist.MaxName, read.Name.Length);
        Assert.Equal(Specialist.Colors[0], read.Color);
        Assert.Equal(Specialist.Glyphs[0], read.Glyph);
        Assert.Equal(AgentKinds.Claude, read.Agent);
        Assert.Null(read.Folder);

        // Con otro agente, el modelo y el esfuerzo de Claude Code no se guardan.
        read.Agent = AgentKinds.Codex;
        Specialists.Save(read);
        Assert.Null(Assert.Single(Specialists.Mine()).Model);

        // OpenCode es uno más de los agentes que puede llevar.
        read.Agent = AgentKinds.OpenCode;
        Specialists.Save(read);
        Assert.Equal(AgentKinds.OpenCode, Assert.Single(Specialists.Mine()).Agent);
    }

    [Fact]
    public void Un_archivo_roto_se_salta()
    {
        Directory.CreateDirectory(Specialists.Folder);
        File.WriteAllText(Path.Combine(Specialists.Folder, "abcdef123456.json"), "{ esto no es json");
        Specialists.Save(Reviewer());
        Assert.Single(Specialists.Mine());
    }

    [Fact]
    public void Las_instrucciones_dicen_quien_es_y_como_trabaja()
    {
        var prompt = Reviewer().Prompt();
        Assert.Contains("«Revisor de PRs»", prompt);
        Assert.Contains("Revisa los cambios", prompt);
        Assert.EndsWith("Agrupe los comentarios por archivo.", prompt);
    }

    [Fact]
    public void Claude_Code_recibe_sus_herramientas_y_sus_instrucciones()
    {
        var s = Reviewer();
        var args = CanvasMcp.ClaudeArguments(@"C:\p", s, [Notas, GitHub, Otro]).ToList();

        var config = JsonNode.Parse(File.ReadAllText(args[args.IndexOf("--mcp-config") + 1]))!["mcpServers"]!.AsObject();
        Assert.Equal([CanvasMcp.Name, "notas", "github"], config.Select(p => p.Key));
        var allowed = args[args.IndexOf("--allowedTools") + 1].Split(',');
        Assert.Contains("mcp__notas", allowed);
        Assert.DoesNotContain("mcp__github", allowed);
        Assert.Contains(allowed, a => a.StartsWith($"mcp__{CanvasMcp.Name}__", StringComparison.Ordinal));
        var prompt = args[args.IndexOf("--append-system-prompt-file") + 1];
        Assert.Equal(s.Prompt(), File.ReadAllText(prompt));

        // Sin el canvas y sin herramientas: ni configuración ni herramientas permitidas, solo las instrucciones.
        s.Tools = new() { [Specialist.CanvasTool] = new SpecialistTool { On = false } };
        var bare = CanvasMcp.ClaudeArguments(@"C:\p", s, [Notas]).ToList();
        Assert.DoesNotContain("--mcp-config", bare);
        Assert.DoesNotContain("--allowedTools", bare);
        Assert.Contains("--append-system-prompt-file", bare);

        // Sin especialista, todo como siempre: el canvas y todos los servidores.
        var plain = CanvasMcp.ClaudeArguments(@"C:\p", null, [Notas, Otro]).ToList();
        var all = JsonNode.Parse(File.ReadAllText(plain[plain.IndexOf("--mcp-config") + 1]))!["mcpServers"]!.AsObject();
        Assert.Equal([CanvasMcp.Name, "notas", "otro"], all.Select(p => p.Key));
        Assert.DoesNotContain("--append-system-prompt-file", plain);
    }

    [Fact]
    public void Codex_y_ACP_reciben_solo_sus_herramientas()
    {
        var s = Reviewer();
        s.Tools[Specialist.CanvasTool].On = false;
        var codex = CanvasMcp.CodexArguments(null, Specialists.ServersFor(s, [Notas, Otro]), Specialists.UsesCanvas(s)).ToList();
        Assert.DoesNotContain(codex, a => a.Contains("claupet_canvas"));
        Assert.Contains(codex, a => a.StartsWith("mcp_servers.notas.", StringComparison.Ordinal));
        Assert.DoesNotContain(codex, a => a.Contains("mcp_servers.otro"));

        var acp = CanvasMcp.AcpServers("Gemini CLI", null, false, Specialists.ServersFor(s, [Notas, Otro]), Specialists.UsesCanvas(s));
        Assert.Equal(["notas"], acp.Select(x => x!["name"]!.GetValue<string>()));
    }

    static string Manifest(string agents, string provides = "\"mcp\", \"agent\"") => $$"""
        {
          "schema": 1,
          "id": "prueba.revisor",
          "name": "Revisor",
          "version": "1.0.0",
          "provides": [{{provides}}],
          "mcp": { "notas": { "program": "node", "args": ["{package}/server.js"] } },
          "agents": {{agents}}
        }
        """;

    static readonly HashSet<string> Files = ["manifest.json", "server.js"];

    [Fact]
    public void Una_extension_trae_especialistas_de_solo_lectura()
    {
        var (m, errors) = ExtensionManifest.Parse(Manifest("""
            [ { "id": "revisor", "name": { "es": "Revisor", "en": "Reviewer" }, "role": "Revisa", "instructions": "Sea breve.",
                "agent": "claude", "model": "opus", "effort": "high", "color": "#5ba3e0", "icon": "bug", "tools": ["canvas", "notas"] } ]
            """), Files);
        Assert.Empty(errors);
        var ext = new InstalledExtension(m!, @"C:\ext\p", @"C:\ext\d", new InstallRecord());
        var s = Assert.Single(Specialists.FromManifest(ext));
        Assert.True(s.ReadOnly);
        Assert.Equal("prueba.revisor/revisor", s.Id);
        Assert.Equal("#5BA3E0", s.Color);
        Assert.Equal("bug", s.Glyph);
        Assert.True(s.Uses("notas"));
        Assert.True(s.AsksFor("notas"));

        // Uno de solo lectura no se guarda ni se borra; su copia sí es de la persona.
        Specialists.Save(s);
        Assert.Empty(Specialists.Mine());
        var copy = s.Copy("Mi revisor");
        Assert.False(copy.ReadOnly);
        Specialists.Save(copy);
        Assert.Equal("Mi revisor", Assert.Single(Specialists.Mine()).Name);
    }

    [Theory]
    [InlineData("""[ { "id": "r", "name": "R", "tools": ["github"] } ]""", "«github»")]
    [InlineData("""[ { "id": "r", "name": "R", "agent": "otro" } ]""", "«agent»")]
    [InlineData("""[ { "id": "r", "name": "R", "agent": "codex", "model": "o3" } ]""", "«model»")]
    [InlineData("""[ { "id": "r", "name": "R", "color": "#123456" } ]""", "«color»")]
    [InlineData("""[ { "id": "r", "name": "R", "icon": "pato" } ]""", "«icon»")]
    [InlineData("""[ { "id": "r-1", "name": "R" } ]""", "«id»")]
    [InlineData("""[]""", "«agent» en «provides»")]
    public void Lo_que_no_sirve_en_el_manifiesto_se_rechaza(string agents, string problem)
    {
        var (m, errors) = ExtensionManifest.Parse(Manifest(agents), Files);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains(problem));
    }

    // ── La vista ────────────────────────────────────────────────────────────────────────────

    sealed class Scene : IDisposable
    {
        public SpecialistsPane Pane { get; } = new();
        public Window Window { get; }

        public Scene()
        {
            Pane.SetInk(Ink.For(true));
            var list = new Border { Width = 272, Child = Pane.List };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("272,*"), Children = { list } };
            Grid.SetColumn(Pane, 1);
            grid.Children.Add(Pane);
            Window = new Window { Width = 1100, Height = 760, Content = grid };
            Window.Show();
            Pane.Reload();
            Headless.Frame(Window);
        }

        public IEnumerable<string> Texts(Avalonia.Visual under) =>
            under.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

        public void Dispose() => Window.Close();
    }

    [Fact]
    public void Sin_especialistas_invita_a_crear_uno_y_dice_que_es_experimental() => Headless.Run(() =>
    {
        using var scene = new Scene();
        // Sin texto de más: el ícono y el botón de uno nuevo.
        Assert.Empty(scene.Texts(scene.Pane).Where(t => t.Length > 0));
        Assert.Contains(scene.Pane.GetVisualDescendants().OfType<Avalonia.Controls.Control>(), c => Avalonia.Automation.AutomationProperties.GetName(c) == L.T("Especialista nuevo"));
        // «Experimental» va en el tooltip del frasco, junto al título.
        var badge = SpecialistsPane.Badge(Ink.For(true));
        Assert.Equal(L.T("Experimental"), Avalonia.Automation.AutomationProperties.GetName(badge));
        Assert.Equal(L.T("Experimental: puede cambiar o fallar"), Avalonia.Controls.ToolTip.GetTip(badge));
    });

    /// <summary>Con CLAUPET_SPECIALISTS_SHOT, guarda capturas de la vista (claro y oscuro) para compararla con el prototipo.</summary>
    [Fact]
    public void Captura_de_la_vista() => Headless.Run(() =>
    {
        if (Environment.GetEnvironmentVariable("CLAUPET_SPECIALISTS_SHOT") is not { Length: > 0 } shot) return;
        Specialists.Save(Reviewer());
        var ext = Reviewer();
        ext.Id = "prueba.x/analista";
        ext.Name = "Investigador";
        ext.Color = Specialist.Colors[3];
        ext.Glyph = "globe";
        ext.Extension = "Investigación";
        Specialists.FromExtensions = () => [ext];
        foreach (var dark in new[] { false, true })
        {
            using var scene = new Scene();
            scene.Pane.SetInk(Ink.For(dark));
            scene.Window.Background = Ink.For(dark).Window;
            for (var i = 0; i < 8; i++)
            {
                Thread.Sleep(80);
                Headless.Frame(scene.Window);
            }
            Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(scene.Window)?.Save(shot + (dark ? ".oscuro.png" : ".claro.png"));
        }
    });

    [Fact]
    public void La_busqueda_filtra_la_lista_por_nombre_o_rol() => Headless.Run(() =>
    {
        Specialists.Save(Reviewer());
        var other = Reviewer();
        other.Id = Specialists.NewId();
        other.Name = "Datos";
        other.Role = "Consultas y gráficos";
        Specialists.Save(other);
        using var scene = new Scene();
        var search = scene.Pane.List.GetVisualDescendants().OfType<TextBox>().First();

        search.Text = "gráf";
        Headless.Frame(scene.Window);
        var list = scene.Texts(scene.Pane.List).ToList();
        Assert.Contains("Datos", list);
        Assert.DoesNotContain("Revisor de PRs", list);

        search.Text = "zzz";
        Headless.Frame(scene.Window);
        Assert.Contains(L.T("Sin resultados"), scene.Texts(scene.Pane.List));
    });

    [Fact]
    public void Lista_los_suyos_y_aparte_los_de_extensiones_y_crea_uno_nuevo() => Headless.Run(() =>
    {
        Specialists.Save(Reviewer());
        var ext = Reviewer();
        ext.Id = "prueba.x/analista";
        ext.Name = "Analista";
        ext.Extension = "prueba.x · Datos";
        Specialists.FromExtensions = () => [ext];
        using var scene = new Scene();

        var list = scene.Texts(scene.Pane.List).ToList();
        Assert.Equal(["Revisor de PRs", L.T("De extensiones"), "Analista"], list.Where(t => t is "Revisor de PRs" or "Analista" || t == L.T("De extensiones")));
        // El primero se ve en el panel, con su nombre para editar en el lugar.
        Assert.Contains(scene.Pane.GetVisualDescendants().OfType<TextBox>(), b => b.Text == "Revisor de PRs");

        scene.Pane.New();
        Headless.Frame(scene.Window);
        Assert.Equal(2, Specialists.Mine().Count);
        Assert.Contains(L.T("Especialista nuevo"), scene.Texts(scene.Pane.List));
    });

    [Fact]
    public void Al_escribir_el_nombre_cambia_la_lista_y_se_guarda() => Headless.Run(() =>
    {
        Specialists.Save(Reviewer());
        using var scene = new Scene();
        var name = scene.Pane.GetVisualDescendants().OfType<TextBox>().First(b => b.Text == "Revisor de PRs");
        name.Text = "Revisor estricto";
        Headless.Frame(scene.Window);
        Assert.Contains("Revisor estricto", scene.Texts(scene.Pane.List));
        scene.Pane.Flush();
        Assert.Equal("Revisor estricto", Assert.Single(Specialists.Mine()).Name);
    });
}
