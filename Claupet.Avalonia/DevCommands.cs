namespace Claupet;

/// <summary>
/// Los modos de línea de comandos para probar sin abrir ventanas, los mismos que la app WinForms: hacer una acción,
/// preguntarle a la mascota, repartir una pregunta como en Respuestas, crear un tema, el modelo local, la voz de Claupet y Whisper.
/// Escriben a la salida estándar y devuelven el código de salida; null si los argumentos no piden ninguno.
/// </summary>
static class DevCommands
{
    public static int? TryRun(string[] args)
    {
        // --accion <acción> <valor> [pregunta]: hace esa acción de la mascota, sin pasar por Claude ni pedir permiso, y
        // dice cómo le fue. Sin ventanas no hay recorte, ni portapapeles de texto en Linux y macOS, ni nada que esconder.
        if (Array.IndexOf(args, "--accion") is var a and >= 0 && a + 2 < args.Length)
        {
            SystemActions.Desktop = Headless();
            var question = a + 3 < args.Length && !args[a + 3].StartsWith("--") ? args[a + 3] : null;
            var outcome = SystemActions.RunAsync(new AssistantAction(args[a + 1], args[a + 2], question),
                palette => Console.WriteLine($"color de la app: {palette}")).GetAwaiter().GetResult();
            if (outcome.Problem is { } problem) { Console.WriteLine(problem); return 1; }
            if (outcome.Look is { } look)
            {
                Console.WriteLine($"para mirar: {look}");
                var settings = SettingsStore.Load();
                var seen = Assistant.AskAsync(new AssistantRequest
                {
                    Prompt = outcome.Question ?? "", Voice = AssistantVoice.Island, Model = settings.IslandModel,
                    ConfigDir = settings.AssistantAccount ?? settings.Selected, File = look,
                }).GetAwaiter().GetResult();
                Console.WriteLine(seen.Error ?? seen.Text);
                return seen.Failed ? 1 : 0;
            }
            Console.WriteLine(outcome.Say ?? outcome.Data ?? "listo");
            return 0;
        }

        // --preguntar <texto>: se lo pregunta a la mascota (con su cuenta y su modelo) y escribe la respuesta y la acción o
        // la tarea que pediría. Con --hacer, además hace la acción (sin pedir permiso) y, si trajo datos, le vuelve a
        // preguntar con ellos, como el chat. --modelo <id>: otro que el de la mascota.
        if (Array.IndexOf(args, "--preguntar") is var q and >= 0 && q + 1 < args.Length)
        {
            var settings = SettingsStore.Load();
            SystemActions.Desktop = Headless();
            var request = new AssistantRequest
            {
                Prompt = args[q + 1],
                Voice = AssistantVoice.Mascot,
                Model = Array.IndexOf(args, "--modelo") is var qm and >= 0 && qm + 1 < args.Length ? args[qm + 1] : settings.MascotModel,
                ConfigDir = settings.AssistantAccount ?? settings.Selected,
            };
            var result = Assistant.AskAsync(request).GetAwaiter().GetResult();
            if (result.Model is { } used) Console.WriteLine($"[MODELO] {used}");
            if (result.Action is { } action)
                Console.WriteLine($"[ACCION] {action.Kind} {action.Value}" + (action.Question is { } asked ? $" (pregunta: {asked})" : ""));
            if (result.Task is { } task) Console.WriteLine($"[TAREA] {task.Prompt}");
            Console.WriteLine(result.Error ?? result.Text);
            if (result.Action is not { } done || !args.Contains("--hacer")) return result.Failed ? 1 : 0;
            var outcome = SystemActions.RunAsync(done, palette => Console.WriteLine($"color de la app: {palette}")).GetAwaiter().GetResult();
            Console.WriteLine($"[HECHO] {outcome.Problem ?? outcome.Say ?? (outcome.Look is { } look ? $"para mirar: {look}" : "listo")}");
            if (outcome.Data is not { } data) return outcome.Problem is null ? 0 : 1;
            Console.WriteLine($"[DATOS] {data.ReplaceLineEndings(" | ")}");
            var follow = Assistant.AskAsync(request with { Data = data }).GetAwaiter().GetResult();
            Console.WriteLine(follow.Error ?? follow.Text);
            return follow.Failed ? 1 : 0;
        }

        // --modelo-local <id> [pregunta]: baja ese modelo local (y llama-server) como desde Ajustes, con el avance; con una
        // pregunta, se la hace como la mascota y dice cuánto tardó en arrancar y en responder. Por ahora, solo en Windows.
        if (Array.IndexOf(args, "--modelo-local") is var lm and >= 0 && lm + 1 < args.Length)
        {
            if (!OperatingSystem.IsWindows()) { Console.WriteLine("El modelo local todavía es solo de Windows."); return 1; }
            var choice = LocalModel.Find(args[lm + 1]);
            if (!LocalModel.Ready(choice))
            {
                Console.WriteLine($"descargando {choice.Name} ({LocalModel.Missing(choice) / 1_000_000} MB)");
                LocalModel.DownloadAsync(choice, Percent(), CancellationToken.None).GetAwaiter().GetResult();
            }
            Console.WriteLine(LocalModel.Ready(choice) ? $"{choice.Name}: listo" : $"{choice.Name}: incompleto");
            if (!LocalModel.Ready(choice) || lm + 2 >= args.Length) return LocalModel.Ready(choice) ? 0 : 1;
            using var server = new LocalModelServer();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            server.EnsureAsync(choice, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"arranque: {clock.ElapsedMilliseconds} ms");
            Assistant.Local = () => new LocalBrain(server, choice);
            clock.Restart();
            var answer = Assistant.AskAsync(new AssistantRequest { Prompt = args[lm + 2], Voice = AssistantVoice.Mascot, Model = AssistantModels.Local })
                .GetAwaiter().GetResult();
            Console.WriteLine($"respuesta en {clock.ElapsedMilliseconds} ms ({answer.Model}):");
            if (answer.Action is { } action) Console.WriteLine($"[ACCION] {action.Kind} {action.Value}");
            if (answer.Task is { } task) Console.WriteLine($"[TAREA] {task.Prompt}");
            Console.WriteLine(answer.Error ?? answer.Text);
            return answer.Failed ? 1 : 0;
        }

        // --responder <pregunta>: la reparte como la mascota (Ajustes › Respuestas) y dice quién respondió y qué; no hace la
        // acción. Si el modelo local está descargado (en Windows), lo usa.
        if (Array.IndexOf(args, "--responder") is var rs and >= 0 && rs + 1 < args.Length)
        {
            var settings = SettingsStore.Load();
            using var server = new LocalModelServer();
            var choice = LocalModel.Find(settings.LocalModel);
            if (OperatingSystem.IsWindows())
            {
                Assistant.Local = () => LocalModel.Ready(choice) ? new LocalBrain(server, choice) : null;
                WeatherService.WindowsLocation = WindowsLocation.GetAsync;
            }
            SystemActions.Desktop = Headless();
            Answers.OpenTarget = SystemActions.OpenTarget;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var answer = Answers.AskAsync(new AssistantRequest
            {
                Prompt = args[rs + 1], Voice = AssistantVoice.Mascot, Model = settings.MascotModel,
                ConfigDir = settings.AssistantAccount ?? settings.Selected,
            }, settings).GetAwaiter().GetResult();
            Console.WriteLine($"[{answer.By?.Label ?? "?"}] {clock.ElapsedMilliseconds} ms");
            if (answer.Action is { } action) Console.WriteLine($"[ACCION] {action.Kind} {action.Value}");
            if (answer.Task is { } task) Console.WriteLine($"[TAREA] {task.Prompt}");
            Console.WriteLine(answer.Error ?? answer.Text);
            return answer.Failed ? 1 : 0;
        }

        // --tema <prompt>: le pide a Claude un tema propio (con la cuenta y el modelo de la mascota), lo guarda en temas/ y
        // escribe el JSON ya validado y corregido. No lo elige: eso queda para Ajustes.
        if (Array.IndexOf(args, "--tema") is var th and >= 0 && th + 1 < args.Length)
        {
            var (theme, error) = CustomThemes.CreateAsync(args[th + 1], SettingsStore.Load()).GetAwaiter().GetResult();
            if (theme is null) { Console.WriteLine(error); return 1; }
            Console.WriteLine(CustomThemes.PathFor(theme.Id));
            Console.WriteLine(CustomThemes.ToJson(theme));
            return 0;
        }

        // --descargar-voz: baja la voz de Claupet como desde Ajustes, con el avance a la salida estándar.
        if (args.Contains("--descargar-voz"))
        {
            ClaupetVoiceModel.DownloadAsync(Percent(), CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine(ClaupetVoiceModel.Ready ? "lista" : "incompleta");
            return ClaupetVoiceModel.Ready ? 0 : 1;
        }

        // --decir <F4|M1> <texto>: lo dice con la voz de Claupet (ya descargada), como al leer una respuesta, y dice cuánto tardó.
        if (Array.IndexOf(args, "--decir") is var t and >= 0 && t + 2 < args.Length)
        {
            if (!ClaupetVoiceModel.Ready) { Console.WriteLine("Falta descargar la voz (Ajustes › Asistente)."); return 1; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var engine = new Supertonic(ClaupetVoiceModel.Folder);
            var voice = Supertonic.LoadVoice(ClaupetVoiceModel.VoiceFile(args[t + 1]));
            Console.WriteLine($"carga: {clock.ElapsedMilliseconds} ms");
            using var output = new AudioOut(engine.SampleRate);
            foreach (var chunk in Supertonic.Chunks(args[t + 2]))
            {
                clock.Restart();
                var samples = engine.Speak(chunk, voice);
                Console.WriteLine($"«{chunk}»: {samples.Length / (double)engine.SampleRate:0.0} s de audio en {clock.ElapsedMilliseconds} ms");
                output.Play(samples);
            }
            output.Drain(CancellationToken.None);
            AudioHost.Stop();
            return 0;
        }

        // --transcribir archivo.wav (16 kHz, mono, 16 bits): lo pasa a texto como el micrófono, con el modelo ya descargado.
        if (Array.IndexOf(args, "--transcribir") is var w and >= 0 && w + 1 < args.Length)
        {
            if (!VoiceModel.Ready) { Console.WriteLine("Falta descargar el modelo del micrófono (Ajustes › Asistente)."); return 1; }
            if (!File.Exists(args[w + 1])) { Console.WriteLine($"No encontré «{args[w + 1]}»."); return 1; }
            if (Transcriber.ReadWav(File.ReadAllBytes(args[w + 1])) is not { } pcm) { Console.WriteLine("El WAV no tiene audio."); return 1; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var transcriber = new Transcriber();
            var heard = transcriber.TranscribeAsync(pcm).GetAwaiter().GetResult();
            Console.WriteLine($"«{heard}» en {clock.ElapsedMilliseconds} ms");
            return heard.Length > 0 ? 0 : 1;
        }
        return null;
    }

    /// <summary>Las acciones sin ventanas: sin recorte, ni portapapeles de texto en Linux y macOS, ni nada que esconder.</summary>
    static IDesktop? Headless() =>
        OperatingSystem.IsWindows() ? new WindowsDesktop(WindowsUi.Create(work => work(), canCrop: false))
        : OperatingSystem.IsMacOS() ? new MacDesktop(null, work => work())
        : OperatingSystem.IsLinux() ? new LinuxDesktop(null, work => work())
        : null;

    /// <summary>El avance de una descarga, de a 10 %.</summary>
    static Progress<double> Percent()
    {
        var last = -1;
        return new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            if (percent / 10 != last / 10) Console.WriteLine($"{percent} %");
            last = percent;
        });
    }
}
