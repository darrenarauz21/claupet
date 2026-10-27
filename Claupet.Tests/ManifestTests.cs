namespace ClaudeUsageTray.Tests;

public class ManifestTests
{
    static readonly HashSet<string> Files = ["manifest.json", "bin/notas.cmd"];

    [Fact]
    public void ValidSkillParses()
    {
        var (m, errors) = ExtensionManifest.Parse(TestKit.SkillManifest(), Files);
        Assert.Empty(errors);
        Assert.NotNull(m);
        Assert.Equal("prueba.notas", m!.Id);
        Assert.Equal(new SemVersion(1, 0, 0, ""), m.Version);
        Assert.Equal("Notas", m.Name.Text);
        Assert.True(m.RunsCode);
        Assert.Equal("notas", Assert.Single(m.Commands).Name);
    }

    [Theory]
    [InlineData("Prueba.notas")]
    [InlineData("prueba")]
    [InlineData("prueba.notas.extra")]
    [InlineData("-prueba.notas")]
    [InlineData("prueba._notas")]
    public void BadIdsAreRejected(string id)
    {
        var (m, errors) = ExtensionManifest.Parse(TestKit.SkillManifest(id), Files);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("«id»"));
    }

    [Fact]
    public void NewerSchemaIsRejected()
    {
        var (m, errors) = ExtensionManifest.Parse(TestKit.SkillManifest().Replace("\"schema\": 1", "\"schema\": 2"), Files);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("más nueva"));
    }

    [Fact]
    public void UnknownSlotAndPermissionAreRejected()
    {
        var json = TestKit.SkillManifest().Replace("[\"skill\"]", "[\"skill\", \"root\"]").Replace("\"notices\"", "\"everything\"");
        var (m, errors) = ExtensionManifest.Parse(json, Files);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("root"));
        Assert.Contains(errors, e => e.Contains("everything"));
    }

    [Fact]
    public void MissingPackageFileIsRejected()
    {
        var (m, errors) = ExtensionManifest.Parse(TestKit.SkillManifest(), new HashSet<string> { "manifest.json" });
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("bin/notas.cmd"));
    }

    [Fact]
    public void PathEscapingThePackageIsRejected()
    {
        var json = TestKit.SkillManifest().Replace("bin/notas.cmd", "../notas.cmd");
        var (m, _) = ExtensionManifest.Parse(json, null);
        Assert.Null(m);
    }

    [Fact]
    public void SkillWithoutRuntimeIsRejected()
    {
        var json = TestKit.SkillManifest().Replace("\"runtime\": { \"type\": \"process\", \"command\": \"bin/notas.cmd\" },", "");
        var (m, errors) = ExtensionManifest.Parse(json, Files);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("runtime"));
    }

    [Fact]
    public void ProcessNeedsCommandOrProgramButNotBoth()
    {
        var json = TestKit.SkillManifest().Replace("\"command\": \"bin/notas.cmd\"", "\"command\": \"bin/notas.cmd\", \"program\": \"python\"");
        Assert.Null(ExtensionManifest.Parse(json, Files).Manifest);
        var program = TestKit.SkillManifest().Replace("\"command\": \"bin/notas.cmd\"", "\"program\": \"python\", \"args\": [\"main.py\"]");
        Assert.NotNull(ExtensionManifest.Parse(program, Files).Manifest);
        var path = TestKit.SkillManifest().Replace("\"command\": \"bin/notas.cmd\"", "\"program\": \"C:/Windows/evil.exe\"");
        Assert.Null(ExtensionManifest.Parse(path, Files).Manifest);
    }

    static string Brain(string url, string permissions, string auth = "") => $$"""
        {
          "schema": 1, "id": "prueba.cerebro", "name": "DeepSeek", "version": "1.0.0",
          "provides": ["assistant"],
          "runtime": { "type": "http", "format": "openai-chat", "url": "{{url}}", "model": "deepseek-chat"{{auth}} },
          "settings": [ { "key": "apiKey", "type": "secret", "label": "Clave" } ],
          "permissions": [{{permissions}}]
        }
        """;

    [Fact]
    public void RemoteHttpNeedsNetworkPermission()
    {
        Assert.Null(ExtensionManifest.Parse(Brain("https://api.deepseek.com", "")).Manifest);
        Assert.NotNull(ExtensionManifest.Parse(Brain("https://api.deepseek.com", "\"network:api.deepseek.com\"", ", \"auth\": \"bearer\", \"authSetting\": \"apiKey\"")).Manifest);
        Assert.NotNull(ExtensionManifest.Parse(Brain("https://api.deepseek.com", "\"network:*.deepseek.com\"")).Manifest);
    }

    [Fact]
    public void PlainHttpOnlyOnThisComputer()
    {
        Assert.NotNull(ExtensionManifest.Parse(Brain("http://localhost:11434/v1", "")).Manifest);
        Assert.NotNull(ExtensionManifest.Parse(Brain("http://127.0.0.1:1234/v1", "")).Manifest);
        Assert.Null(ExtensionManifest.Parse(Brain("http://api.deepseek.com", "\"network:api.deepseek.com\"")).Manifest);
    }

    [Fact]
    public void AuthNeedsSecretSetting()
    {
        var (m, errors) = ExtensionManifest.Parse(Brain("https://api.deepseek.com", "\"network\"", ", \"auth\": \"bearer\", \"authSetting\": \"nada\""));
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("authSetting"));
    }

    [Fact]
    public void FormatMustMatchSlot()
    {
        var json = Brain("https://api.x.com", "\"network\"").Replace("openai-chat", "openai-transcription");
        Assert.Null(ExtensionManifest.Parse(json).Manifest);
    }

    [Fact]
    public void PetNeedsIdle()
    {
        var json = """
            { "schema": 1, "id": "prueba.gato", "name": "Gato", "version": "1.0.0", "provides": ["pet"],
              "pet": { "name": "Gato", "width": 32, "height": 32, "animations": { "dance": { "file": "dance.png", "ms": 120 } } } }
            """;
        var (m, errors) = ExtensionManifest.Parse(json, new HashSet<string> { "manifest.json", "dance.png" });
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("idle"));
    }

    static string Voices(string runtime) => $$"""
        { "schema": 1, "id": "prueba.voces", "name": "Voces", "version": "1.0.0", "provides": ["voice-style"],
          "voices": [ { "id": "lucia", "name": "Lucía", "file": "voces/lucia.json", "language": "es" } ]{{runtime}} }
        """;

    [Fact]
    public void DataPackCannotHaveRuntime()
    {
        var files = new HashSet<string> { "manifest.json", "voces/lucia.json" };
        Assert.NotNull(ExtensionManifest.Parse(Voices(""), files).Manifest);
        Assert.Null(ExtensionManifest.Parse(Voices(", \"runtime\": { \"type\": \"process\", \"program\": \"python\" }"), files).Manifest);
    }

    [Fact]
    public void SemVersionOrder()
    {
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0"];
        for (var i = 1; i < ordered.Length; i++)
            Assert.True(SemVersion.Parse(ordered[i - 1])!.Value < SemVersion.Parse(ordered[i])!.Value, $"{ordered[i - 1]} < {ordered[i]}");
        Assert.Null(SemVersion.Parse("1.0"));
        Assert.Null(SemVersion.Parse("01.0.0"));
    }

    [Fact]
    public void LocalizedTextFallsBack()
    {
        var text = new LocalizedText(new Dictionary<string, string> { ["en"] = "Notes", ["fr"] = "Notes FR" });
        Assert.Equal("Notes", text.Text); // la app está en español en las pruebas: no hay «es», cae en inglés
    }
}
