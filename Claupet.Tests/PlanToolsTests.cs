using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>Las herramientas del plan por MCP: un agente crea, lee y cambia un *.plan.json solo dentro de la carpeta del proyecto.</summary>
public sealed class PlanToolsTests : IDisposable
{
    readonly TempDir _project = new();
    readonly TempDir _data = new();
    readonly CanvasMcp.Server _server;

    public PlanToolsTests() => _server = new CanvasMcp.Server(new CanvasStore(_data.Path), "Codex", _project.Path);

    public void Dispose()
    {
        _project.Dispose();
        _data.Dispose();
    }

    string Call(string tool, object arguments, bool error = false)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = JsonSerializer.SerializeToNode(arguments) },
        };
        var result = JsonDocument.Parse(_server.Handle(request.ToJsonString())!).RootElement.GetProperty("result");
        Assert.Equal(error, result.GetProperty("isError").GetBoolean());
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    static readonly object Sample = new
    {
        titulo = "Lanzar 2.4",
        inicio = "2026-11-03",
        etapas = new[] { new { id = "e1", nombre = "Correcciones" } },
        tareas = new object[]
        {
            new { id = "t1", titulo = "Historial", etapa = "e1", dias = 2 },
            new { id = "t2", titulo = "Tarjeta", etapa = "e1", dias = 3, depende = new[] { "t1" } },
        },
    };

    [Fact]
    public void CreaUnPlanConElNombreDelTituloYLoDescribeConLasFechasCalculadas()
    {
        var text = Call("plan_create", new { plan = Sample });
        var file = Path.Combine(_project.Path, "planes", "lanzar-2-4.plan.json");
        Assert.True(File.Exists(file));
        Assert.Contains("planes" + Path.DirectorySeparatorChar + "lanzar-2-4.plan.json", text);
        Assert.Contains("task t2 [por-hacer] \"Tarjeta\" stage=e1 2026-11-05→2026-11-07 (3d) after=t1", text);
        Assert.Contains("\"claupet\": \"plan/1\"", File.ReadAllText(file));
        Assert.Contains("lanzar-2-4.plan.json", Call("plan_list", new { }));
        Assert.Equal(text.Split('\n', 2)[1].TrimStart(), Call("plan_read", new { path = "planes/lanzar-2-4.plan.json" }));
    }

    [Fact]
    public void UpdateCambiaSoloLoPedidoYSiAlgoFallaNoSeGuardaNada()
    {
        Call("plan_create", new { path = "plan.plan.json", plan = Sample });
        var before = File.ReadAllText(_project["plan.plan.json"]);

        var ok = Call("plan_update", new
        {
            path = "plan.plan.json",
            ops = new object[]
            {
                new { op = "task", id = "t1", estado = "hecho", horasReales = 2 },
                new { op = "task", id = "t3", titulo = "Publicar", depende = new[] { "t2" }, dias = 1 },
                new { op = "milestone", id = "h1", titulo = "Salida", depende = new[] { "t3" } },
            },
        });
        Assert.Contains("added task t3", ok);
        Assert.Contains("task t1 [hecho]", ok);
        Assert.Contains("milestone h1 \"Salida\" 2026-11-09", ok);

        var failed = Call("plan_update", new
        {
            path = "plan.plan.json",
            ops = new object[] { new { op = "task", id = "t1", estado = "en-curso" }, new { op = "task", id = "t9", colorcito = "rojo" } },
        }, error: true);
        Assert.Contains("Operation 2", failed);
        Assert.Contains("Nothing was changed", failed);
        Assert.Contains("\"estado\": \"hecho\"", File.ReadAllText(_project["plan.plan.json"])); // t1 sigue hecha
        Assert.NotEqual(before, File.ReadAllText(_project["plan.plan.json"]));
    }

    [Fact]
    public void UnaTareaCambiadaPorUnaIaQuedaConSuNombre()
    {
        Call("plan_create", new { path = "plan.plan.json", plan = Sample });
        var text = Call("plan_update", new { path = "plan.plan.json", ops = new object[] { new { op = "task", id = "t1", estado = "hecho" } } });
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("by=Codex", lines.Single(l => l.StartsWith("task t1")));
        Assert.DoesNotContain("by=", lines.Single(l => l.StartsWith("task t2"))); // solo la que cambió
        Assert.Contains("\"por\": \"Codex\"", File.ReadAllText(_project["plan.plan.json"]));
        Assert.Equal("Codex", Plan.Parse(File.ReadAllText(_project["plan.plan.json"])).Tasks[0].Editor);
    }

    [Fact]
    public void BorrarUnaTareaLaQuitaDeLasDependencias()
    {
        Call("plan_create", new { path = "plan.plan.json", plan = Sample });
        var text = Call("plan_update", new { path = "plan.plan.json", ops = new object[] { new { op = "delete", kind = "task", id = "t1" } } });
        var plan = text[(text.IndexOf("\n\n", StringComparison.Ordinal) + 2)..]; // lo que sigue al «OK: …»
        Assert.DoesNotContain("task t1", plan);
        Assert.DoesNotContain("after=t1", plan);
        Assert.DoesNotContain("warning", plan);
    }

    [Theory]
    [InlineData("../fuera.plan.json")]
    [InlineData("planes/../../fuera.plan.json")]
    [InlineData("notas.md")]
    [InlineData("package.json")]
    public void SoloEscribeArchivosDePlanDentroDeLaCarpetaDelProyecto(string path)
    {
        var error = Call("plan_create", new { path, plan = Sample }, error: true);
        Assert.True(error.Contains("inside the project folder") || error.Contains(".plan.json"), error);
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(_project.Path, path))));
    }

    [Fact]
    public void NoPisaUnPlanQueYaExisteYUnPlanMalEscritoVuelveComoError()
    {
        Call("plan_create", new { path = "plan.plan.json", plan = Sample });
        Assert.Contains("already exists", Call("plan_create", new { path = "plan.plan.json", plan = Sample }, error: true));
        Assert.Contains("No plan at", Call("plan_read", new { path = "otro.plan.json" }, error: true));
        _project.Write("roto.plan.json", "esto no es json");
        Assert.Contains("JSON", Call("plan_read", new { path = "roto.plan.json" }, error: true));
        Assert.Contains("unreadable", Call("plan_list", new { }));
    }

    [Fact]
    public void LasHerramientasDelPlanSeUsanSinPreguntarYElServidorLasExplica()
    {
        foreach (var name in PlanTools.Names) Assert.Contains(name, CanvasMcp.SafeTools);
        var init = JsonDocument.Parse(_server.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")!).RootElement;
        Assert.Contains("plan_create", init.GetProperty("result").GetProperty("instructions").GetString());
    }
}
