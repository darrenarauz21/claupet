namespace Claupet;

/// <summary>
/// El modelo de la voz de Claupet (Supertonic 3, unos 400 MB): solo los archivos que se usan, de una
/// versión fija de Hugging Face y con su SHA-256. Se descarga una vez, al elegir una voz de Claupet, a
/// la carpeta de la app; después todo funciona sin internet.
/// </summary>
static class ClaupetVoiceModel
{
    const string Base = "https://huggingface.co/supertone-oss-archive/supertonic-3/resolve/aafc6e32416a594460b32413efc49d7fe4ce6d46/";

    static readonly (string Path, long Size, string Sha256)[] Files =
    [
        ("onnx/duration_predictor.onnx", 3700147, "c3eb91414d5ff8a7a239b7fe9e34e7e2bf8a8140d8375ffb14718b1c639325db"),
        ("onnx/text_encoder.onnx", 36416150, "c7befd5ea8c3119769e8a6c1486c4edc6a3bc8365c67621c881bbb774b9902ff"),
        ("onnx/vector_estimator.onnx", 256534781, "883ac868ea0275ef0e991524dc64f16b3c0376efd7c320af6b53f5b780d7c61c"),
        ("onnx/vocoder.onnx", 101424195, "085de76dd8e8d5836d6ca66826601f615939218f90e519f70ee8a36ed2a4c4ba"),
        ("onnx/tts.json", 8253, "42078d3aef1cd43ab43021f3c54f47d2d75ceb4e75f627f118890128b06a0d09"),
        ("onnx/unicode_indexer.json", 277676, "9bf7346e43883a81f8645c81224f786d43c5b57f3641f6e7671a7d6c493cb24f"),
        ("voice_styles/F4.json", 291808, "c2fa764c1225a76dfc3e2c73e8aa4f70d9ee48793860eb34c295fff01c2e032b"),
        ("voice_styles/M1.json", 291748, "e35604687f5d23694b8e91593a93eec0e4eca6c0b02bb8ed69139ab2ea6b0a5b"),
        ("LICENSE", 15007, "0d944a9110fed9a9602d60e0423a272903e7bd21ab060490774efc77c2275e9f"),
    ];

    public static string Folder => DataFolder.In("voz", "supertonic-3");

    public static long Size => Files.Sum(f => f.Size);

    static string Local(string path) => Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Están todos los archivos (y no a medias).</summary>
    public static bool Ready => Files.All(f => File.Exists(Local(f.Path)) && new FileInfo(Local(f.Path)).Length == f.Size);

    public static string VoiceFile(string voice) => Local($"voice_styles/{voice}.json");

    /// <summary>Baja los que falten, uno tras otro, y avisa el avance de 0 a 1 sobre el total.</summary>
    public static async Task DownloadAsync(IProgress<double> progress, CancellationToken cancel)
    {
        long done = 0;
        foreach (var (path, size, sha256) in Files)
        {
            var local = Local(path);
            if (!File.Exists(local) || new FileInfo(local).Length != size)
            {
                var before = done;
                await ModelDownload.FetchAsync(Base + path, local, size, sha256,
                    new Progress<long>(have => progress.Report((double)(before + have) / Size)), cancel);
            }
            done += size;
            progress.Report((double)done / Size);
        }
    }
}
