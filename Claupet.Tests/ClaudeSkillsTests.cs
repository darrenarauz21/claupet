using System.IO.Compression;
using Avalonia.LogicalTree;

namespace Claupet.Tests;

/// <summary>Ajustes › Skills: las skills de Claude Code de cada cuenta, la de Claupet y las que se agregan desde un archivo.</summary>
public sealed class ClaudeSkillsTests
{
    const string Skill = "---\nname: Notas Rápidas\ndescription: >\n  Toma notas\n  cortas.\n---\n\n# Notas\n";

    [Fact]
    public void LeeElEncabezadoDelSkillMd()
    {
        var front = ClaudeSkills.Front(Skill);
        Assert.Equal("Notas Rápidas", front["name"]);
        Assert.Equal("Toma notas cortas.", front["description"]);
        Assert.Equal("Dice \"hola\"", ClaudeSkills.Front("---\r\ndescription: 'Dice \"hola\"'\r\n---\r\n")["description"]);
        Assert.Empty(ClaudeSkills.Front("# Sin encabezado"));
        Assert.Equal("notas-rapidas", ClaudeSkills.Slug("Notas Rápidas"));
    }

    [Fact]
    public void LaDeClaupetVaDentroDelEjecutableYSeInstalaEnTodasLasCuentas()
    {
        using var dir = new TempDir();
        string[] accounts = [dir["a"], dir["b"]];
        var text = ClaudeSkills.BundledText("claupet-plan");
        Assert.NotNull(text);
        Assert.Contains("plan_update", text);
        Assert.Equal("claupet-plan", ClaudeSkills.Front(text!)["name"]);

        Assert.Null(ClaudeSkills.InstallBundled("claupet-plan", accounts));
        Assert.All(accounts, a => Assert.True(File.Exists(Path.Combine(a, "skills", "claupet-plan", "SKILL.md"))));
        var listed = Assert.Single(ClaudeSkills.List(accounts));
        Assert.True(listed.Bundled);

        // Se pone al día donde está; donde la quitaron no vuelve.
        File.WriteAllText(Path.Combine(accounts[0], "skills", "claupet-plan", "SKILL.md"), "vieja");
        Directory.Delete(Path.Combine(accounts[1], "skills", "claupet-plan"), recursive: true);
        ClaudeSkills.RefreshBundled(accounts);
        Assert.Equal(text, File.ReadAllText(Path.Combine(accounts[0], "skills", "claupet-plan", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(accounts[1], "skills", "claupet-plan")));

        Assert.Null(ClaudeSkills.Remove("claupet-plan", accounts));
        Assert.Empty(ClaudeSkills.List(accounts));
    }

    [Fact]
    public void UnSkillMdLlegaConSuCarpeta()
    {
        using var dir = new TempDir();
        var file = dir.Write("descargas/notas/SKILL.md", Skill);
        dir.Write("descargas/notas/scripts/hacer.py", "print(1)");
        string[] accounts = [dir["cuenta"]];

        var (name, problem) = ClaudeSkills.Add(file, accounts);
        Assert.Null(problem);
        Assert.Equal("notas-rapidas", name);
        Assert.True(File.Exists(Path.Combine(accounts[0], "skills", "notas-rapidas", "scripts", "hacer.py")));
        var skill = Assert.Single(ClaudeSkills.List(accounts));
        Assert.Equal("Toma notas cortas.", skill.Description);
        Assert.False(skill.Bundled);

        // Elegir la que ya está instalada no la borra.
        Assert.Null(ClaudeSkills.Add(Path.Combine(accounts[0], "skills", "notas-rapidas", "SKILL.md"), accounts).Problem);
        Assert.True(File.Exists(Path.Combine(accounts[0], "skills", "notas-rapidas", "scripts", "hacer.py")));
    }

    [Fact]
    public void OtroMdPasaASerElSkillMdDeUnaCarpetaNueva()
    {
        using var dir = new TempDir();
        var file = dir.Write("revisar-pr.md", "---\ndescription: Revisa un PR.\n---\nPasos.");
        string[] accounts = [dir["cuenta"]];
        Assert.Equal("revisar-pr", ClaudeSkills.Add(file, accounts).Name);
        Assert.Equal("---\ndescription: Revisa un PR.\n---\nPasos.", File.ReadAllText(Path.Combine(accounts[0], "skills", "revisar-pr", "SKILL.md")));
    }

    [Fact]
    public void SinDescripcionNoSeAgrega()
    {
        using var dir = new TempDir();
        var file = dir.Write("vacia.md", "# Nada");
        Assert.NotNull(ClaudeSkills.Add(file, [dir["cuenta"]]).Problem);
        Assert.False(Directory.Exists(dir["cuenta/skills"]));
    }

    [Fact]
    public void UnZipConSuCarpetaSeAbreYNadaSaleDeElla()
    {
        using var dir = new TempDir();
        var zip = dir["notas.skill"];
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "notas/SKILL.md", Skill);
            Write(archive, "notas/ref/guia.md", "guía");
        }
        string[] accounts = [dir["cuenta"]];
        Assert.Equal("notas-rapidas", ClaudeSkills.Add(zip, accounts).Name);
        Assert.True(File.Exists(Path.Combine(accounts[0], "skills", "notas-rapidas", "ref", "guia.md")));

        var bad = dir["mala.zip"];
        using (var archive = ZipFile.Open(bad, ZipArchiveMode.Create))
        {
            Write(archive, "SKILL.md", "---\nname: mala\ndescription: x\n---\n");
            Write(archive, "../fuera.txt", "no");
        }
        Assert.NotNull(ClaudeSkills.Add(bad, accounts).Problem);
        Assert.False(File.Exists(Path.Combine(accounts[0], "skills", "fuera.txt")));
    }

    static void Write(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(text);
    }

    [Fact]
    public void LaPaginaDeSkillsEstaSoloEnLaConsoleYSistemaVaAlFinal()
    {
        var pages = SettingsPages.Build(new SettingsState { Skills = [new("claupet-plan", "Planes", true), new("notas", "Toma notas.", false)] });
        Assert.Equal("sistema", pages[^1].Id);
        var skills = pages.Single(p => p.Id == "skills");
        Assert.Null(skills.Tab);
        Assert.Contains(skills.Rows, r => r is { Kind: RowKind.Switch, Key: SettingsPages.SkillBundledKey + "claupet-plan", On: true });
        var notas = skills.Rows.Single(r => r.Text == "notas");
        Assert.Equal([SettingsPages.SkillOpenKey + "notas", SettingsPages.SkillRemoveKey + "notas"], notas.Icons!.Select(i => i.Key));
        var add = skills.Rows.Single(r => r.Icons?.Any(i => i.Key == SettingsPages.SkillAddKey) == true).Icons!.Single();
        Assert.Equal(RowIconKind.Add, add.Icon);
        Assert.Equal(ClaudeSkills.FilePattern, add.File);

        var off = SettingsPages.Build(new SettingsState()).Single(p => p.Id == "skills");
        Assert.Contains(off.Rows, r => r is { Kind: RowKind.Switch, On: false });
    }
}

/// <summary>Los botones de ícono de las filas de Ajustes: se ven con su descripción y mandan su acción.</summary>
public sealed class RowIconsTests
{
    [Fact]
    public void RefrescarYAgregarSonIconosConSuDescripcion() => Headless.Run(() =>
    {
        var pages = SettingsPages.Build(new SettingsState());
        var view = new SettingsView();
        var acts = new List<string>();
        view.Action += (key, _) => acts.Add(key);
        view.Show(pages.Single(p => p.Id == "sistema"), Ink.For(true), rebuild: true);
        var refresh = view.GetLogicalDescendants().OfType<Avalonia.Controls.Border>()
            .Single(c => Avalonia.Automation.AutomationProperties.GetName(c) == L.T("Buscar ahora"));
        Assert.Equal(L.T("Buscar ahora"), Avalonia.Controls.ToolTip.GetTip(refresh));
        Assert.IsType<Avalonia.Controls.Viewbox>(refresh.Child);

        view.Show(pages.Single(p => p.Id == "skills"), Ink.For(false), rebuild: true);
        Assert.Contains(view.GetLogicalDescendants().OfType<Avalonia.Controls.Border>(),
            c => Avalonia.Automation.AutomationProperties.GetName(c) == L.T("Elegir su SKILL.md, .zip o .skill"));
    });
}
