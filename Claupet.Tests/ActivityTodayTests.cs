using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>Los tokens de hoy salen del recorrido de Actividad igual que de LocalUsage.ScanToday, que relee todo.</summary>
public sealed class ActivityTodayTests
{
    static string Line(string type, DateTime at, string? id = null, string? request = null, string model = "claude-sonnet-4-5",
        int input = 0, int output = 0, int write = 0, int read = 0) => (type == "assistant"
        ? new JsonObject
        {
            ["type"] = "assistant", ["timestamp"] = at.ToUniversalTime().ToString("O"), ["sessionId"] = "s1", ["requestId"] = request,
            ["message"] = new JsonObject
            {
                ["id"] = id, ["model"] = model, ["content"] = new JsonArray(),
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = input, ["output_tokens"] = output, ["cache_creation_input_tokens"] = write, ["cache_read_input_tokens"] = read,
                },
            },
        }
        : new JsonObject
        {
            ["type"] = "user", ["timestamp"] = at.ToUniversalTime().ToString("O"), ["sessionId"] = "s1", ["uuid"] = id,
            ["message"] = new JsonObject { ["content"] = "hola" },
        }).ToJsonString();

    [Fact]
    public void Hoy_da_lo_mismo_que_releer_las_conversaciones()
    {
        using var dir = new TempDir();
        var config = dir["cuenta"];
        var today = DateTime.Today.AddHours(10);
        var yesterday = today.AddDays(-1);
        var lines = new[]
        {
            Line("assistant", yesterday, "m0", "r0", input: 999, output: 999), // ayer: no cuenta
            Line("user", today, "u1"),
            Line("assistant", today, "m1", "r1", input: 10, output: 20, write: 30, read: 40),
            Line("assistant", today, "m1", "r1", input: 10, output: 20, write: 30, read: 40), // la misma respuesta en otra línea
            Line("assistant", today, "m2", "r2", model: "claude-opus-4-1", input: 1, output: 2, write: 3, read: 4),
            Line("assistant", today, "m3", "r3", model: "<synthetic>", input: 5, output: 5),
        };
        var project = Directory.CreateDirectory(Path.Combine(config, "projects", "proyecto")).FullName;
        File.WriteAllLines(Path.Combine(project, "uno.jsonl"), lines);
        // La misma respuesta copiada en otra conversación (una sesión reanudada) se cuenta una vez.
        File.WriteAllLines(Path.Combine(project, "dos.jsonl"), [lines[2], Line("assistant", today, "m4", "r4", input: 7, output: 8)]);

        var expected = LocalUsage.ScanToday([config]);
        var tracker = new ActivityTracker(dir["history.json"]);
        tracker.Scan([config]);
        var actual = tracker.Today([config]);

        Assert.Equal((expected.Input, expected.Output, expected.CacheWrite, expected.CacheRead, expected.Responses),
            (actual.Input, actual.Output, actual.CacheWrite, actual.CacheRead, actual.Responses));
        Assert.Equal(expected.OutputByModel.OrderBy(kv => kv.Key), actual.OutputByModel.OrderBy(kv => kv.Key));
        Assert.Equal((18L, 3), (actual.Input, actual.Responses));
    }

    [Fact]
    public void Lo_nuevo_se_suma_sin_releer()
    {
        using var dir = new TempDir();
        var config = dir["cuenta"];
        var project = Directory.CreateDirectory(Path.Combine(config, "projects", "p")).FullName;
        var file = Path.Combine(project, "c.jsonl");
        var today = DateTime.Today.AddHours(9);
        File.WriteAllLines(file, [Line("assistant", today, "a", "1", input: 1, output: 1)]);
        var tracker = new ActivityTracker(dir["history.json"]);
        tracker.Scan([config]);
        File.AppendAllLines(file, [Line("assistant", today, "b", "2", input: 2, output: 2)]);
        tracker.Scan([config]);
        var totals = tracker.Today([config]);
        Assert.Equal((3L, 3L, 2), (totals.Input, totals.Output, totals.Responses));
        Assert.Equal(LocalUsage.ScanToday([config]).Output, totals.Output);
    }
}
