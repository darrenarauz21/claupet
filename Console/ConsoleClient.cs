namespace Claupet;

using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using Avalonia.Threading;

/// <summary>
/// La punta de la Console en la tubería con la bandeja. Si Claupet no está abierto, o se cierra, reintenta
/// cada poco; los mensajes llegan en el hilo de la interfaz.
/// </summary>
sealed class ConsoleClient : IDisposable
{
    readonly CancellationTokenSource _stop = new();
    Channel<string>? _outbox;

    public event Action<ConsoleMessage>? Received;

    /// <summary>Se conectó (true) o se perdió la conexión (false).</summary>
    public event Action<bool>? ConnectedChanged;

    public bool IsConnected => _outbox is not null;

    public void Start() => _ = Task.Run(LoopAsync);

    public void Send(ConsoleMessage message)
    {
        // Lo que pida puede abrir una ventana de Claupet (grabar una tecla, la ciudad…): que pueda venir al frente.
        Native.AllowForeground();
        _outbox?.Writer.TryWrite(ConsoleLink.Encode(message));
    }

    async Task LoopAsync()
    {
        var encoding = new UTF8Encoding(false);
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", ConsoleLink.PipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(TimeSpan.FromSeconds(2), _stop.Token);
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
                Dispatcher.UIThread.Post(() => ConnectedChanged?.Invoke(true));
                try
                {
                    using var reader = new StreamReader(pipe, encoding);
                    while (await reader.ReadLineAsync(_stop.Token) is { } line)
                        if (ConsoleLink.Decode(line) is { } message) Dispatcher.UIThread.Post(() => Received?.Invoke(message));
                }
                finally
                {
                    _outbox = null;
                    outbox.Writer.TryComplete();
                    try { await sending; }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
                    Dispatcher.UIThread.Post(() => ConnectedChanged?.Invoke(false));
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException) { }
            try { await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
