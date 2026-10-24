namespace ClaudeUsageTray;

/// <summary>
/// ¿Hay internet? Una petición corta a la API de Anthropic por el proxy del momento: cualquier respuesta
/// (aunque sea un error) dice que sí; no llegar a ella, que no.
/// </summary>
static class Connectivity
{
    static readonly HttpClient Http = SystemProxy.Client(TimeSpan.FromSeconds(8));

    public static async Task<bool> IsOnlineAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://api.anthropic.com/");
            using var response = await Http.SendAsync(request);
            return true;
        }
        catch (Exception) { return false; }
    }
}
