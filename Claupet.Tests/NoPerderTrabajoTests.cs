using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Control = Avalonia.Controls.Control;
using Panel = Avalonia.Controls.Panel;
using Point = Avalonia.Point;
using TextBox = Avalonia.Controls.TextBox;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// Lo que se perdía sin avisar: cerrar un agente que trabaja, guardar encima de lo que él editó, escribir en una sesión
/// terminada. Con clics y teclas de verdad, fuera de la pantalla.
/// </summary>
public sealed class NoPerderTrabajoTests
{
    static Window Show(Control pane)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new Panel { Children = { pane } } };
        window.Show();
        Headless.Frame(window);
        return window;
    }

    static TextBlock? Find(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>()
        .FirstOrDefault(t => t.Text == text && t.IsEffectivelyVisible);

    static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
    }

    static void Click(Window window, string text)
    {
        var control = Find(window, text) ?? throw new Xunit.Sdk.XunitException($"No se ve «{text}»");
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }

    static List<TerminalPane.Tab> Tabs(TerminalPane pane) =>
        (List<TerminalPane.Tab>)typeof(TerminalPane).GetField("_tabs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pane)!;

    // ── Cerrar ──────────────────────────────────────────────────────────────────────────────

    static (TerminalPane Pane, Window Window, TerminalPane.Tab Tab) AgentTab(string folder, bool ended = false)
    {
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var window = Show(pane);
        var agent = new TerminalAgent(new AgentOptions(folder), _ => { }) { Ended = ended };
        var tab = new TerminalPane.Tab(agent, "cuenta");
        pane.Add(tab);
        Settle(window);
        return (pane, window, tab);
    }

    /// <summary>Un agente que trabaja de verdad: su proceso vive y hay un turno en curso.</summary>
    static Process MakeWorking(TerminalPane.Tab tab)
    {
        var process = Process.Start(new ProcessStartInfo("ping", "-n 120 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        typeof(TerminalAgent).GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(tab.Agent, process);
        tab.Agent.Transcript.Ask("haz algo");
        return process;
    }

    [Fact]
    public void CerrarUnaPestanaQueTrabajaPreguntaYSeguirTrabajandoNoLaToca() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, tab) = AgentTab(dir.Path);
        // Una segunda referencia: cerrar la pestaña libera la del agente.
        using var process = Process.GetProcessById(MakeWorking(tab).Id);
        try
        {
            Assert.Equal(1, pane.WorkingCount);
            pane.RequestClose(tab);
            Settle(window);
            Assert.Contains(tab, Tabs(pane));
            Click(window, L.T("Seguir trabajando"));
            Assert.Contains(tab, Tabs(pane));
            Assert.False(process.HasExited);
            Assert.Null(Find(window, L.T("Detener y cerrar")));

            pane.RequestClose(tab);
            Settle(window);
            Click(window, L.T("Detener y cerrar"));
            Assert.DoesNotContain(tab, Tabs(pane));
            Assert.True(process.WaitForExit(10_000), "El proceso sigue vivo después de detenerlo");
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void UnIntroOEscEnLaPreguntaNoDetienenNada() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, tab) = AgentTab(dir.Path);
        var process = MakeWorking(tab);
        try
        {
            pane.RequestClose(tab);
            Settle(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);
            Assert.Null(Find(window, L.T("Detener y cerrar")));
            Assert.Contains(tab, Tabs(pane));

            pane.RequestClose(tab);
            Settle(window);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Settle(window);
            Assert.Null(Find(window, L.T("Detener y cerrar")));
            Assert.Contains(tab, Tabs(pane));
            Assert.False(process.HasExited);
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            pane.CloseAll();
            window.Close();
        }
    });

    /// <summary>Tal como lo manda Claude Code 2.1.319.</summary>
    const string OneTask = """{"type":"system","subtype":"background_tasks_changed","tasks":[{"task_id":"bkki9pz5i","run_id":"0mw514bnx-6a4def96","task_type":"local_bash","description":"Ping"}],"session_id":"s"}""";
    const string NoTasks = """{"type":"system","subtype":"background_tasks_changed","tasks":[],"session_id":"s"}""";

    [Fact]
    public void LasTareasEnSegundoPlanoSeCuentanHastaQueTerminan()
    {
        var transcript = new AgentTranscript();
        transcript.Apply(OneTask);
        Assert.Equal(1, transcript.BackgroundTasks);
        Assert.False(transcript.Busy);
        transcript.Apply(NoTasks);
        Assert.Equal(0, transcript.BackgroundTasks);
    }

    [Fact]
    public void CerrarConUnaTareaEnSegundoPlanoPreguntaAunqueElTurnoHayaTerminado() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, tab) = AgentTab(dir.Path);
        var live = MakeWorking(tab);
        using var process = Process.GetProcessById(live.Id);
        try
        {
            tab.Agent.Transcript.Apply("""{"type":"result","subtype":"success","result":"listo"}""");
            Assert.False(tab.Agent.Transcript.Busy);
            Assert.False(TerminalPane.IsWorking(tab));

            tab.Agent.Transcript.Apply(OneTask);
            Assert.True(TerminalPane.IsWorking(tab));
            pane.RequestClose(tab);
            Settle(window);
            Assert.NotNull(Find(window, L.T("Dejó una tarea corriendo en segundo plano en «{0}». Si cierra la pestaña, se corta.", tab.Project)));
            Click(window, L.T("Seguir trabajando"));
            Assert.Contains(tab, Tabs(pane));

            tab.Agent.Transcript.Apply(NoTasks);
            Assert.False(TerminalPane.IsWorking(tab));
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void CerrarUnaPestanaSinTrabajoNoPregunta() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, tab) = AgentTab(dir.Path, ended: true);
        try
        {
            Assert.Equal(0, pane.WorkingCount);
            pane.RequestClose(tab);
            Settle(window);
            Assert.Null(Find(window, L.T("Detener y cerrar")));
            Assert.DoesNotContain(tab, Tabs(pane));
        }
        finally
        {
            pane.CloseAll();
            window.Close();
        }
    });

    // ── Sesión terminada ────────────────────────────────────────────────────────────────────

    static readonly string EndedText = "Esta sesión terminó: lo que escriba no llega hasta retomarla.";

    [Fact]
    public void UnaSesionVivaNoMuestraLaFranjaYUnaTerminadaSi() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, _) = AgentTab(dir.Path);
        var (pane2, window2, _) = AgentTab(dir.Path, ended: true);
        try
        {
            Assert.Null(Find(window, L.T(EndedText)));
            Assert.NotNull(Find(window2, L.T(EndedText)));
            Assert.NotNull(Find(window2, L.T("Abrir de nuevo")));
        }
        finally
        {
            pane.CloseAll();
            pane2.CloseAll();
            window.Close();
            window2.Close();
        }
    });

    [Fact]
    public void LaFranjaApareceCuandoTerminaConLaPestanaAbierta() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, tab) = AgentTab(dir.Path);
        try
        {
            Assert.Null(Find(window, L.T(EndedText)));
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            tab.Agent.Ended = true;
            typeof(TerminalPane).GetMethod("PaintStatus", flags)!.Invoke(pane, null);
            Settle(window);
            Assert.NotNull(Find(window, L.T(EndedText)));
        }
        finally
        {
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void IntroEnUnaSesionTerminadaConservaLoEscritoSiLaCarpetaYaNoEsta() => Headless.Run(() =>
    {
        var dir = new TempDir();
        var folder = dir.Path;
        var (pane, window, tab) = AgentTab(folder, ended: true);
        try
        {
            dir.Dispose();
            var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
            box.Text = "lo que escribí";
            box.Focus();
            Settle(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);
            Assert.NotNull(Find(window, L.T("La carpeta «{0}» ya no existe: no se puede abrir de nuevo aquí.", folder)));
            Assert.Equal("lo que escribí", box.Text);
            Assert.Contains(tab, Tabs(pane));
        }
        finally
        {
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void AlRetomarLoEscritoPasaALaPestanaNuevaSinEnviarse() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, ended) = AgentTab(dir.Path, ended: true);
        try
        {
            ended.Images.Add(new AgentImage("image/png", "AAAA"));
            var opened = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), _ => { }), "cuenta");
            pane.Add(opened);
            pane.ReplaceEnded(ended, opened, "lo que escribí");
            Settle(window);
            Assert.DoesNotContain(ended, Tabs(pane));
            var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
            Assert.Equal("lo que escribí", box.Text);
            Assert.Single(opened.Images);
            Assert.DoesNotContain(opened.Agent.Transcript.Blocks, b => b.Kind == AgentBlockKind.Ask);
        }
        finally
        {
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void LaPestanaRetomadaQuedaEnElLugarDeLaAnterior() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window, ended) = AgentTab(dir.Path, ended: true);
        try
        {
            var other = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), _ => { }), "otra");
            pane.Add(other);
            var opened = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), _ => { }), "retomada");
            pane.Add(opened);
            pane.ReplaceEnded(ended, opened, "");
            Settle(window);
            Assert.Equal([opened, other], Tabs(pane));
        }
        finally
        {
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void UnComandoLocalNoCambiaElModeloQueSeMuestra()
    {
        var transcript = new AgentTranscript();
        transcript.Apply("""{"type":"system","subtype":"init","model":"claude-opus-5-5"}""");
        transcript.Apply("""{"type":"assistant","message":{"model":"<synthetic>","content":[{"type":"text","text":"Adiós"}]}}""");
        Assert.Equal("claude-opus-5-5", transcript.Model);
        transcript.Apply("""{"type":"assistant","message":{"model":"claude-sonnet-5-5","content":[]}}""");
        Assert.Equal("claude-sonnet-5-5", transcript.Model);
    }

    // ── Editor ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Abre un archivo con «antes» en el editor; lo demás pasa en la prueba.</summary>
    static void WithEditor(Action<EditorPane, Window, string> test) => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var path = dir["nota.md"];
        File.WriteAllText(path, "antes");
        EditorRecents.FileForTests = dir["recientes.json"];
        var editor = new EditorPane();
        editor.SetInk(Ink.For(true));
        var window = Show(editor);
        editor.Open(path);
        Settle(window);
        try { test(editor, window, path); }
        finally
        {
            window.Close();
            EditorRecents.FileForTests = null;
        }
    });

    static void Check(EditorPane editor, Window window)
    {
        editor.CheckDisk();
        Settle(window);
    }

    [Fact]
    public void SinEdicionesSeRecargaSoloYCtrlZLoDevuelve() => WithEditor((editor, window, path) =>
    {
        File.WriteAllText(path, "lo que escribió el agente");
        Check(editor, window);
        var doc = editor.Current!;
        Assert.Equal("lo que escribió el agente", doc.Document.Text);
        Assert.False(doc.Dirty);
        Assert.Null(Find(window, L.T("Recargar")));
        doc.Document.UndoStack.Undo();
        Assert.Equal("antes", doc.Document.Text);
    });

    [Fact]
    public void ConEdicionesPreguntaYRecargarTraeLoDelDisco() => WithEditor((editor, window, path) =>
    {
        var doc = editor.Current!;
        doc.Document.Insert(0, "mío ");
        File.WriteAllText(path, "del agente");
        Check(editor, window);
        Assert.Equal("mío antes", doc.Document.Text);
        Click(window, L.T("Recargar"));
        Assert.Equal("del agente", doc.Document.Text);
        Assert.False(doc.Dirty);
    });

    [Fact]
    public void ConservarLoMioNoVuelveAPreguntarYGuardaSinTropiezo() => WithEditor((editor, window, path) =>
    {
        var doc = editor.Current!;
        doc.Document.Insert(0, "mío ");
        File.WriteAllText(path, "del agente");
        Check(editor, window);
        Click(window, L.T("Conservar lo mío"));
        Check(editor, window);
        Assert.Null(Find(window, L.T("Conservar lo mío")));
        Assert.True(doc.Dirty);
        Assert.True(editor.SaveAsync(doc).GetAwaiter().GetResult());
        Assert.Equal("mío antes", File.ReadAllText(path));
    });

    [Fact]
    public void GuardarNoPisaLoQueCambioAfueraSinPreguntar() => WithEditor((editor, window, path) =>
    {
        var doc = editor.Current!;
        doc.Document.Insert(0, "mío ");
        File.WriteAllText(path, "del agente");
        var saved = false;
        Assert.False(editor.SaveAsync(doc, saved: () => saved = true).GetAwaiter().GetResult());
        Settle(window);
        Assert.Equal("del agente", File.ReadAllText(path));
        Click(window, L.T("Sobrescribir"));
        Assert.Equal("mío antes", File.ReadAllText(path));
        Assert.True(saved);
        Assert.False(doc.Dirty);
    });

    [Fact]
    public void GuardarDosVecesNoSeConfundeConUnCambioDeAfuera() => WithEditor((editor, window, path) =>
    {
        var doc = editor.Current!;
        doc.Document.Insert(0, "uno ");
        Assert.True(editor.SaveAsync(doc).GetAwaiter().GetResult());
        doc.Document.Insert(0, "dos ");
        Check(editor, window);
        Assert.Null(Find(window, L.T("Recargar")));
        Assert.True(editor.SaveAsync(doc).GetAwaiter().GetResult());
        Assert.Equal("dos uno antes", File.ReadAllText(path));
    });

    [Fact]
    public void TocarElArchivoSinCambiarloNoCuenta() => WithEditor((editor, window, path) =>
    {
        var doc = editor.Current!;
        doc.Document.Insert(0, "mío ");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Check(editor, window);
        Assert.Null(Find(window, L.T("Recargar")));
        Assert.Equal("mío antes", doc.Document.Text);
    });

    [Fact]
    public void ReemplazarElArchivoNoSeConfundeConBorrarlo() => WithEditor((editor, window, path) =>
    {
        File.Delete(path);
        Check(editor, window);
        Assert.Null(Find(window, L.T("Conservar")));
        File.WriteAllText(path, "nuevo");
        Check(editor, window);
        Assert.Equal("nuevo", editor.Current!.Document.Text);
        Assert.Null(Find(window, L.T("Conservar")));
    });

    [Fact]
    public void SiLoBorranAvisaYConservarPermiteGuardarlo() => WithEditor((editor, window, path) =>
    {
        var doc = editor.Current!;
        doc.Document.Insert(0, "mío ");
        File.Delete(path);
        Check(editor, window);
        Check(editor, window);
        Assert.NotNull(Find(window, L.T("Cerrar sin guardar")));
        Click(window, L.T("Conservar"));
        Check(editor, window);
        Check(editor, window);
        Assert.Null(Find(window, L.T("Cerrar sin guardar")));
        Assert.Contains(doc, editor.Docs);
        Assert.True(editor.SaveAsync(doc).GetAwaiter().GetResult());
        Assert.Equal("mío antes", File.ReadAllText(path));
    });

    [Fact]
    public void SiLoBorranYSeCierraNoQuedaLaPregunta() => WithEditor((editor, window, path) =>
    {
        var doc = editor.Current!;
        File.Delete(path);
        Check(editor, window);
        Check(editor, window);
        Click(window, L.T("Cerrar"));
        Assert.DoesNotContain(doc, editor.Docs);
        Assert.Null(Find(window, L.T("Conservar")));
    });

    [Fact]
    public void ElQueNoSeVeSeRecargaSoloYElEditadoPreguntaAlElegirlo() => WithEditor((editor, window, path) =>
    {
        var first = editor.Current!;
        first.Document.Insert(0, "mío ");
        var other = Path.Combine(Path.GetDirectoryName(path)!, "otra.md");
        File.WriteAllText(other, "otra");
        editor.Open(other);
        Settle(window);
        var second = editor.Current!;
        File.WriteAllText(path, "del agente");
        Check(editor, window);
        // Editado y fuera de la vista: no interrumpe; se pregunta al volver a él.
        Assert.Null(Find(window, L.T("Recargar")));
        Assert.Equal("mío antes", first.Document.Text);
        File.WriteAllText(other, "otra nueva");
        Check(editor, window);
        Assert.Equal("otra nueva", second.Document.Text);
        editor.Select(first);
        Settle(window);
        Assert.NotNull(Find(window, L.T("Recargar")));
    });

    // ── Comparar con el disco ───────────────────────────────────────────────────────────────

    [Fact]
    public void CompararVeElCambioAunqueTengaElMismoTamano()
    {
        using var dir = new TempDir();
        var path = dir["a.txt"];
        File.WriteAllText(path, "aaaa");
        var known = TextFiles.StateOf(path);
        Assert.Equal(DiskChange.Same, TextFiles.Compare(path, known).Change);
        File.WriteAllText(path, "bbbb");
        File.SetLastWriteTimeUtc(path, known.Written.AddSeconds(5));
        var check = TextFiles.Compare(path, known);
        Assert.Equal(DiskChange.Changed, check.Change);
        Assert.Equal("bbbb", check.Text);
    }

    [Fact]
    public void CompararNoInventaNadaSiElArchivoEstaOcupadoOFalta()
    {
        using var dir = new TempDir();
        var path = dir["a.txt"];
        File.WriteAllText(path, "aaaa");
        var known = TextFiles.StateOf(path);
        File.WriteAllText(path, "otra cosa");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(DiskChange.Unreadable, TextFiles.Compare(path, known).Change);
        File.Delete(path);
        Assert.Equal(DiskChange.Missing, TextFiles.Compare(path, known).Change);
        Assert.Equal(DiskChange.Same, TextFiles.Compare(path, null).Change);
    }

    [Fact]
    public void RecargarRespetaLaCodificacionDelArchivo() => WithEditor((editor, window, path) =>
    {
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("añejo"));
        Check(editor, window);
        Assert.Equal("añejo", editor.Current!.Document.Text);
    });
}
