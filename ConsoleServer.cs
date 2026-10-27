using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

namespace Claupet;

/// <summary>
/// La punta de la bandeja en la tubería de Claupet Console: atiende a una Console a la vez y, cuando se cierra,
/// espera la siguiente. Lo que manda va a una cola: un Console trabado no traba la bandeja.
/// </summary>
sealed class ConsoleServer : IDisposable
{
    readonly CancellationTokenSource _stop = new();
    Channel<string>? _outbox;

    /// <summary>Llega un mensaje de la Console (en otro hilo).</summary>
    public event Action<ConsoleMessage>? Received;

    /// <summary>Se conectó una Console (en otro hilo): hay que mandarle el estado.</summary>
    public event Action? Connected;

    public bool IsConnected => _outbox is not null;

    public void Start() => _ = Task.Run(LoopAsync);

    public void Send(ConsoleMessage message) => _outbox?.Writer.TryWrite(ConsoleLink.Encode(message));

    async Task LoopAsync()
    {
        var encoding = new UTF8Encoding(false);
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                // CurrentUserOnly: la tubería solo la abre el mismo usuario de Windows.
                await using var pipe = new NamedPipeServerStream(ConsoleLink.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                var outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
                var sending = Task.Run(async () =>
                {
                    var writer = new StreamWriter(pipe, encoding) { NewLine = "\n" };
                    await foreach (var line in outbox.Reader.ReadAllAsync())
                    {
                        await writer.WriteLineAsync(line);
                        await writer.FlushAsync();
                    }
                });
                _outbox = outbox;
                Connected?.Invoke();
                try
                {
                    using var reader = new StreamReader(pipe, encoding);
                    while (await reader.ReadLineAsync(_stop.Token) is { } line)
                        if (ConsoleLink.Decode(line) is { } message) Received?.Invoke(message);
                }
                finally
                {
                    _outbox = null;
                    outbox.Writer.TryComplete();
                    try { await sending; }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // La Console se cerró a medio mensaje, u otra copia de Claupet (--preview) ya tiene la tubería.
                try { await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token); }
                catch (OperationCanceledException) { return; }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                try { await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
