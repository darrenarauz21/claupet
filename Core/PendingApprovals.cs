namespace Claupet;

/// <summary>
/// Sabe, por el registro de la sesión, si ya se respondió un permiso o una pregunta de Claude Code en otra parte (en la
/// terminal de la persona, por ejemplo): el hook no se entera, y matarlo no siempre cierra su pipe (en Windows, Claude
/// Code lo corre por un shell y el .exe puede quedar vivo). Se respondió si el paso que lo pidió (el tool_use de esa
/// herramienta, mejor si con el mismo pedido) ya tiene su resultado (también al negarlo o interrumpirlo, Claude Code
/// escribe uno), si la sesión pasó de esperar a trabajar, si el turno terminó o empezó otro, o si la sesión se cerró.
/// Sin herramienta (un aviso del hook Notification), mira el último paso que espera; si no hay ninguno, ya se respondió.
/// </summary>
sealed class AnswerWatch(string sessionId, string? tool = null, string? inputJson = null)
{
    /// <summary>Lecturas seguidas sin la sesión para darla por cerrada (una lectura puede fallar sola).</summary>
    const int GoneAfter = 2;

    readonly int _inputKey = inputJson is null ? 0 : Approvals.InputKey(inputJson);
    bool _started, _sawWaiting;
    int _turns, _missing, _idle;
    DateTimeOffset? _turnEnded;
    HashSet<string> _doneBefore = [];
    string? _step;

    public string SessionId => sessionId;

    /// <summary>La sesión apareció alguna vez entre las que se leen (si no, no se puede saber si se respondió).</summary>
    public bool Known => _started;

    /// <summary>Con cada lectura de las sesiones: true si ya se respondió (o la sesión siguió de largo, o se cerró).</summary>
    public bool Answered(IReadOnlyList<SessionInfo> sessions)
    {
        var s = sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (s is null) return _started && ++_missing >= GoneAfter;
        _missing = 0;
        if (!_started)
        {
            // La primera vez que se la ve: lo de ahora es la referencia. Lo que ya estaba hecho no es este pedido.
            _started = true;
            _turns = s.Turns;
            _turnEnded = s.TurnEndedAt;
            _doneBefore = s.Flow.Where(f => f.Done).Select(f => f.Id).ToHashSet();
        }
        else if (s.Turns != _turns || s.TurnEndedAt != _turnEnded) return true;

        if (s.State == SessionState.Waiting) _sawWaiting = true;
        else if (_sawWaiting && s.State == SessionState.Working) return true;

        if (_step is null)
        {
            var candidates = Candidates(s.Flow);
            if (candidates.FirstOrDefault(f => !f.Done) is { } waiting) _step = waiting.Id;
            // Entre dos lecturas se pidió, se respondió y ya terminó: el paso aparece hecho de una vez.
            else if (tool is not null && candidates.Any(f => f.Done && !_doneBefore.Contains(f.Id))) return true;
            // Sin herramienta (el hook Notification), el aviso llega cuando la terminal ya pregunta, a veces tarde: si
            // en dos lecturas no hay ningún paso esperando, ya se respondió antes de que el aviso saliera.
            else if (tool is null && ++_idle >= GoneAfter) return true;
            return false;
        }
        return s.Flow.FirstOrDefault(f => f.Id == _step) is not { Done: false };
    }

    /// <summary>Los pasos que pueden ser el del pedido: los de esa herramienta (los del mismo pedido, si hay); sin herramienta, el último que espera.</summary>
    IReadOnlyList<FlowStep> Candidates(IReadOnlyList<FlowStep> flow)
    {
        if (tool is null)
            return flow.LastOrDefault(f => !f.Done && f.Kind != ToolKind.Agent) is { } last ? [last] : [];
        var same = flow.Where(f => f.Tool == tool).ToList();
        var exact = same.Where(f => _inputKey != 0 && f.InputKey == _inputKey).ToList();
        return exact.Count > 0 ? exact : same;
    }
}

/// <summary>Un permiso o una pregunta de Claude Code que espera en la isla.</summary>
sealed class PendingApproval(ApprovalRequest request, string title)
{
    public ApprovalRequest Request => request;
    public string Title => title;

    /// <summary>
    /// Quien preguntó (el hook) ya no espera la respuesta: se le acabó el tiempo o Claude Code lo cortó. El pedido sigue
    /// en la terminal de la persona, así que la isla lo sigue mostrando, pero ya solo se responde allí.
    /// </summary>
    public bool Detached { get; internal set; }

    public AnswerWatch Watch { get; } = new(request.SessionId, request.Tool, request.InputJson);

    /// <summary>La respuesta para quien preguntó (la del hook, en JSON), o null si no la hubo.</summary>
    public Task<string?> Reply => _reply.Task;

    internal readonly TaskCompletionSource<string?> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Los permisos y las preguntas que esperan en la isla, en orden de llegada (la isla muestra el primero). No dependen de
/// la ventana de la isla: plegarla, esconderla o volver a crearla no quita nada. Cada uno se va solo cuando de verdad se
/// respondió: en la isla (Answer), en el bloque de Claupet Terminal (que cierra el pipe: Gone) o en la terminal de la
/// persona (lo dice el registro de la sesión: Sessions).
/// </summary>
sealed class PendingApprovals
{
    readonly List<PendingApproval> _items = new();

    public IReadOnlyList<PendingApproval> Items => _items;

    /// <summary>El que se muestra: el más viejo.</summary>
    public PendingApproval? Current => _items.Count > 0 ? _items[0] : null;

    public event Action? Changed;

    public PendingApproval Add(ApprovalRequest request, string title)
    {
        var item = new PendingApproval(request, title);
        _items.Add(item);
        Changed?.Invoke();
        return item;
    }

    /// <summary>Se respondió en la isla: la respuesta (o null, «responder en la terminal») va a quien preguntó.</summary>
    public void Answer(PendingApproval item, string? reply)
    {
        if (!_items.Remove(item)) return;
        item._reply.TrySetResult(reply);
        Changed?.Invoke();
    }

    /// <summary>
    /// Quien preguntó ya no espera (cerró el pipe o se le acabó el tiempo). Claupet Terminal lo cierra porque se respondió
    /// en su bloque: se quita. El hook puede haberse ido mientras Claude Code sigue preguntando en la terminal: queda,
    /// para responder allí, hasta que el registro diga que se respondió; si su sesión no se lee, no hay cómo saberlo y se quita.
    /// </summary>
    public void Gone(PendingApproval item)
    {
        if (!_items.Contains(item)) return;
        item._reply.TrySetResult(null);
        if (item.Request.FromTerminal || !item.Watch.Known) _items.Remove(item);
        else if (item.Detached) return;
        else item.Detached = true;
        Changed?.Invoke();
    }

    /// <summary>Con cada lectura de las sesiones: quita lo que ya se respondió en otra parte. true si algo cambió.</summary>
    public bool Sessions(IReadOnlyList<SessionInfo> sessions)
    {
        var answered = _items.Where(i => i.Watch.Answered(sessions)).ToList();
        foreach (var item in answered)
        {
            _items.Remove(item);
            item._reply.TrySetResult(null); // el hook no escribe nada: ya decidió la terminal
        }
        if (answered.Count == 0) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Sin isla (se apagó en Ajustes o la app se cierra): nada espera aquí y decide la terminal.</summary>
    public void Clear()
    {
        if (_items.Count == 0) return;
        foreach (var item in _items) item._reply.TrySetResult(null);
        _items.Clear();
        Changed?.Invoke();
    }
}
