using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// Contenido de la ventana, dibujado a mano igual que en Windows: «Uso», «Actividad» (las sesiones con sus carriles),
/// «Historial» (el mapa de calor de 6 meses) y «Ajustes» desde el pie.
/// Un solo recorrido (Compose) sirve para medir la altura y para dibujar.
/// </summary>
sealed partial class UsageView : Control
{
    public event Action? RefreshRequested;
    public event Action? AddRequested;
    public event Action<Account>? SelectedChanged;
    public event Action<Account>? RemoveRequested;
    public event Action<Account>? RenameRequested;
    public event Action<SessionInfo>? NewSessionRequested;
    /// <summary>Una acción de «Ajustes» (notify, sound, pack:id, startup, sample, sounds, add, setup, diag, unsetup).</summary>
    public event Action<string>? SettingRequested;

    public const double ViewWidth = 360;

    /// <summary>Activity son las sesiones, cada una con su carril (antes «Sesiones» y «Flujos»); History, el mapa de calor.</summary>
    enum View { Usage, Activity, History, Settings }
    enum HitKind { Refresh, Account, Add, View, IdleToggle, Day, Gear, CloseSettings, Setting, SettingsTab, FlowSession, FlowBack, SessionAction }
    sealed record Hit(string Key, Rect Bounds, HitKind Kind, Account? Account = null, SessionInfo? Session = null, View View = View.Usage, DateOnly Day = default);

    /// <summary>Semanas completas del cuadro del historial, más la actual.</summary>
    const int HeatmapWeeks = 26;

    static readonly string[] SpinnerFrames = ["·", "✢", "✳", "✶", "✻", "✽", "✻", "✶", "✳", "✢"];

    readonly List<Hit> _hits = new();
    readonly Dictionary<string, Rect> _rowBounds = new();
    readonly DispatcherTimer _animation = new() { Interval = TimeSpan.FromMilliseconds(50) };

    Theme _t = Claupet.Theme.Light;
    View _view = View.Usage;
    IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    IReadOnlyDictionary<string, UsageSnapshot> _snapshots = new Dictionary<string, UsageSnapshot>();
    IReadOnlyDictionary<string, Profile> _profiles = new Dictionary<string, Profile>();
    IReadOnlyList<SessionInfo> _sessions = Array.Empty<SessionInfo>();
    ActivitySummary? _activity;
    RefreshFrame? _refresh;
    string _refreshStyle = RefreshSprite.Styles[0].Id;
    LocalTotals? _local;
    DateTime? _updated;
    string? _selected;
    bool _busy, _showIdle;
    string? _hoverKey;

    public UsageView()
    {
        Width = ViewWidth;
        ClipToBounds = true;
        _animation.Tick += (_, _) =>
        {
            InvalidateVisual();
            UpdateAnimation(); // se apaga sola cuando ya no hay nada que mover
        };
    }

    public Theme Palette
    {
        get => _t;
        set { _t = value; Relayout(); }
    }

    public void SetBusy(bool busy) { _busy = busy; UpdateAnimation(); InvalidateVisual(); }

    public void Render(IReadOnlyList<Account> accounts, string? selectedDir,
        IReadOnlyDictionary<string, UsageSnapshot> snapshots, IReadOnlyDictionary<string, Profile> profiles,
        LocalTotals? local, DateTime? updated)
    {
        _accounts = accounts.ToList();
        _selected = selectedDir;
        _snapshots = snapshots;
        _profiles = profiles;
        _local = local;
        _updated = updated;
        Relayout();
    }

    public void ShowActivity() => _view = View.Activity;

    public void ShowSettings()
    {
        if (_view != View.Settings) _beforeSettings = _view;
        _view = View.Settings;
        Relayout();
    }

    /// <summary>Abre la Actividad en el detalle de esa sesión (al hacer clic en un aviso).</summary>
    public void FocusSession(string sessionId)
    {
        _view = View.Activity;
        _flowSession = sessionId;
        if (_sessions.Any(s => s.SessionId == sessionId && s.State == SessionState.Idle)) _showIdle = true;
        Relayout();
    }

    public void FocusAccount(string configDir)
    {
        _view = View.Usage;
        _selected = configDir;
        Relayout();
    }

    /// <summary>El cuadro de la animación al actualizar (null: no está corriendo) y su estilo.</summary>
    public void SetRefreshFrame(RefreshFrame? frame, string style) { _refresh = frame; _refreshStyle = style; InvalidateVisual(); }

    public void SetActivity(ActivitySummary activity)
    {
        _activity = activity;
        if (_view == View.History) Relayout();
    }

    public void SetSessions(IReadOnlyList<SessionInfo> sessions)
    {
        _sessions = sessions;
        _arrivals.Update(sessions);
        UpdateAnimation();
        Relayout();
    }

    /// <summary>Corre con la ventana abierta y algo en marcha: una sesión activa, un paso que entra o una actualización.</summary>
    public void UpdateAnimation()
    {
        var active = IsEffectivelyVisible && (_busy || Environment.TickCount64 < _arrivals.BusyUntil
            || _sessions.Any(s => s.State is SessionState.Working or SessionState.Waiting));
        if (active && !_animation.IsEnabled) _animation.Start();
        else if (!active && _animation.IsEnabled) _animation.Stop();
    }

    void Relayout()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(ViewWidth, Compose(null));

    public override void Render(DrawingContext context) => Compose(context);

    Account? SelectedAccount =>
        _accounts.FirstOrDefault(a => _selected is not null && Account.SamePath(a.ConfigDir, _selected)) ?? _accounts.FirstOrDefault();

    const double Pad = 20;
    static double Inner => ViewWidth - 2 * Pad;

    double Compose(DrawingContext? dc)
    {
        _hits.Clear();
        dc?.FillRectangle(Gfx.B(_t.Background), new Rect(0, 0, ViewWidth, Math.Max(Bounds.Height, 1)));
        var y = _view == View.Settings ? ComposeSettingsHeader(dc, 16) : ComposeHeader(dc, 16);
        y = _view switch
        {
            // Debajo de lo de Claude Code, los otros agentes que se siguen (UsageView.Agents.cs).
            View.Usage => ComposeAgentUsage(dc, ComposeUsage(dc, y)),
            View.History => ComposeHistory(dc, y),
            View.Settings => ComposeSettings(dc, y),
            _ => ComposeAgentActivity(dc, ComposeActivity(dc, y)),
        };
        return ComposeFooter(dc, y);
    }

    // ── Encabezado: las pestañas y actualizar ─────────────────────────────────────────────────

    double ComposeHeader(DrawingContext? dc, double y)
    {
        var height = Gfx.Title.LineHeight;
        var x = Pad;
        foreach (var view in Tabs)
        {
            var label = ViewLabel(view);
            var width = Gfx.Measure(label, Gfx.Title).Width;
            var badge = view == View.Activity ? BadgeWidth() : 0;
            var key = "view:" + view;
            AddHit(key, new Rect(x - 4, y - 4, width + badge + 8, height + 8), HitKind.View, view: view);
            if (dc is not null)
            {
                var color = _view == view ? _t.Text : _hoverKey == key ? _t.Secondary : _t.Tertiary;
                Gfx.Write(dc, label, Gfx.Title, color, new Rect(x, y, width + 2, height));
                if (badge > 0) DrawBadge(dc, x + width, y, height);
            }
            x += width + badge + 14;
        }

        var refresh = new Rect(ViewWidth - Pad - 24, y + (height - 30) / 2, 30, 30);
        AddHit("refresh", refresh, HitKind.Refresh);
        if (dc is not null)
        {
            if (_hoverKey == "refresh" && !_busy) Gfx.Fill(dc, _t.Hover, refresh, 8);
            if (_busy)
            {
                // Gira mientras actualiza.
                var angle = Environment.TickCount64 % 900 / 900.0 * 2 * Math.PI;
                var c = refresh.Center;
                using (dc.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(c.X, c.Y)))
                    Gfx.Icon(dc, Glyph.Refresh, _t.Accent, refresh);
            }
            else Gfx.Icon(dc, Glyph.Refresh, _t.Secondary, refresh);
        }
        return y + height + 16;
    }

    double BadgeWidth()
    {
        var count = _sessions.Count(s => s.State is SessionState.Working or SessionState.Waiting);
        return count == 0 ? 0 : 8 + 14 + 2 + Gfx.Measure(count.ToString(), Gfx.Small).Width;
    }

    void DrawBadge(DrawingContext dc, double x, double y, double height)
    {
        var working = _sessions.Count(s => s.State == SessionState.Working);
        var waiting = _sessions.Count(s => s.State == SessionState.Waiting);
        var box = new Rect(x + 8, y, 14, height);
        if (working > 0) DrawSpinner(dc, box);
        else DrawPulse(dc, box, _t.Warn);
        Gfx.Write(dc, (working + waiting).ToString(), Gfx.Small, _t.Tertiary, new Rect(box.Right + 2, y, 30, height), center: true);
    }

    // ── Uso ─────────────────────────────────────────────────────────────────────────────────

    double ComposeUsage(DrawingContext? dc, double y)
    {
        const double pillHeight = 30, gap = 6;
        var x = Pad;
        var selected = SelectedAccount;

        foreach (var account in _accounts)
        {
            _snapshots.TryGetValue(account.ConfigDir, out var snap);
            var percent = snap?.Session is { } s ? L.T("{0:0} %", s.Percent) : "";
            var labelWidth = Gfx.Measure(account.Label, Gfx.Body).Width;
            var percentWidth = percent.Length == 0 ? 0 : 6 + Gfx.Measure(percent, Gfx.Small).Width;
            var pillWidth = 12 + labelWidth + percentWidth + 12;
            if (x + pillWidth > ViewWidth - Pad && x > Pad) { x = Pad; y += pillHeight + gap; }

            var pill = new Rect(x, y, pillWidth, pillHeight);
            var key = "acct:" + account.ConfigDir;
            AddHit(key, pill, HitKind.Account, account);
            if (dc is not null)
            {
                var isSelected = account == selected;
                if (isSelected)
                {
                    Gfx.Fill(dc, _t.Raised, pill, 8);
                    Gfx.Outline(dc, _t.Border, pill, 8);
                }
                else if (_hoverKey == key) Gfx.Fill(dc, _t.Hover, pill, 8);
                Gfx.Write(dc, account.Label, Gfx.Body, isSelected ? _t.Text : _t.Secondary,
                    new Rect(pill.X + 12, pill.Y, labelWidth + 2, pillHeight), center: true);
                if (percent.Length > 0)
                    Gfx.Write(dc, percent, Gfx.Small, snap!.Error is null ? _t.Tertiary : _t.Danger,
                        new Rect(pill.X + 12 + labelWidth + 6, pill.Y, percentWidth + 2, pillHeight), center: true);
            }
            x += pillWidth + gap;
        }
        if (x + pillHeight > ViewWidth - Pad && x > Pad) { x = Pad; y += pillHeight + gap; }
        var add = new Rect(x, y, pillHeight, pillHeight);
        AddHit("add", add, HitKind.Add);
        if (dc is not null)
        {
            if (_hoverKey == "add") Gfx.Fill(dc, _t.Hover, add, 8);
            Gfx.Icon(dc, Glyph.Add, _t.Secondary, add, 14);
        }
        y += pillHeight + 20;

        var body = Gfx.Body.LineHeight;
        var small = Gfx.Small.LineHeight;
        if (selected is null)
        {
            Gfx.Write(dc, L.T("Agregue una cuenta con +"), Gfx.Body, _t.Secondary, new Rect(Pad, y, Inner, body));
            return y + body + 20;
        }

        _profiles.TryGetValue(selected.ConfigDir, out var profile);
        _snapshots.TryGetValue(selected.ConfigDir, out var snapshot);

        var plan = profile?.Plan ?? "";
        var planWidth = plan.Length == 0 ? 0 : Gfx.Measure(plan, Gfx.Small).Width;
        Gfx.Write(dc, profile?.Email ?? selected.ConfigDir, Gfx.Small, _t.Secondary, new Rect(Pad, y, Inner - planWidth - 12, small));
        if (plan.Length > 0) Gfx.Write(dc, plan, Gfx.Small, _t.Tertiary, new Rect(ViewWidth - Pad - planWidth, y, planWidth + 1, small));
        y += small + 18;

        if (snapshot is null)
        {
            Gfx.Write(dc, L.T("Consultando…"), Gfx.Body, _t.Tertiary, new Rect(Pad, y, Inner, body));
            return y + body + 20;
        }

        if (snapshot.Error is not null)
        {
            var h = Gfx.MeasureWrapped(snapshot.Error, Gfx.Small, Inner, 4);
            Gfx.Write(dc, snapshot.Error, Gfx.Small, _t.Danger, new Rect(Pad, y, Inner, h), maxLines: 4);
            y += h + 16;
        }

        foreach (var limit in snapshot.Limits)
        {
            Gfx.Write(dc, limit.Label, Gfx.Body, _t.Text, new Rect(Pad, y, Inner - 60, body));
            Gfx.Write(dc, L.T("{0:0} %", limit.Percent), Gfx.BodyStrong, _t.Text, new Rect(ViewWidth - Pad - 60, y, 60, body), TextAlignment.Right);
            y += body + 8;

            if (dc is not null)
            {
                var bar = new Rect(Pad, y, Inner, 6);
                Gfx.Fill(dc, _t.Track, bar, 3);
                var fill = bar.Width * Math.Clamp(limit.Percent, 0, 100) / 100;
                if (fill > 0) Gfx.Fill(dc, _t.For(limit.Percent, limit.Severity), new Rect(bar.X, bar.Y, Math.Max(fill, 6), 6), 3);
            }
            y += 6 + 8;

            Gfx.Write(dc, Format.Reset(limit.ResetsAt), Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
            y += small + 20;
        }
        return y;
    }

    // ── Historial ───────────────────────────────────────────────────────────────────────────

    double ComposeHistory(DrawingContext? dc, double y)
    {
        var small = Gfx.Small.LineHeight;
        if (_activity is null)
        {
            Gfx.Write(dc, L.T("Calculando…"), Gfx.Body, _t.Tertiary, new Rect(Pad, y, Inner, Gfx.Body.LineHeight));
            return y + Gfx.Body.LineHeight + 24;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var start = monday.AddDays(-7 * HeatmapWeeks);
        const int columns = HeatmapWeeks + 1;
        const double labelWidth = 28, gap = 3;
        var cell = Math.Floor((Inner - labelWidth - gap * (columns - 1)) / columns);
        var gridX = Pad + Inner - (cell * columns + gap * (columns - 1));

        var lastLabelRight = double.MinValue;
        for (var c = 0; c < columns; c++)
        {
            var sunday = start.AddDays(7 * c + 6);
            if (c > 0 && sunday.Month == start.AddDays(7 * (c - 1) + 6).Month) continue;
            var x = gridX + c * (cell + gap);
            var text = sunday.ToString("MMM").TrimEnd('.');
            var width = Gfx.Measure(text, Gfx.Small).Width;
            if (x < lastLabelRight + 6) continue;
            Gfx.Write(dc, text, Gfx.Small, _t.Tertiary, new Rect(x, y, width + 2, small));
            lastLabelRight = x + width;
        }
        y += small + 6;

        var values = Enumerable.Range(0, columns * 7).Select(i => start.AddDays(i))
            .Where(d => d <= today).Select(d => _activity.Get(d)?.Tokens ?? 0).Where(v => v > 0).OrderBy(v => v).ToList();
        long Quartile(double q) => values.Count == 0 ? 0 : values[(int)Math.Min(values.Count - 1, Math.Floor(q * values.Count))];
        var (q1, q2, q3) = (Quartile(0.25), Quartile(0.5), Quartile(0.75));
        int Level(long v) => v <= 0 ? 0 : v <= q1 ? 1 : v <= q2 ? 2 : v <= q3 ? 3 : 4;

        string[] dayLabels = [L.T("lun"), "", L.T("mié"), "", L.T("vie"), "", ""];
        for (var r = 0; r < 7; r++)
            if (dayLabels[r].Length > 0)
                Gfx.Write(dc, dayLabels[r], Gfx.Small, _t.Tertiary, new Rect(Pad, y + r * (cell + gap), labelWidth, cell), center: true);

        DateOnly? hovered = null;
        for (var c = 0; c < columns; c++)
            for (var r = 0; r < 7; r++)
            {
                var day = start.AddDays(7 * c + r);
                if (day > today) break;
                var rect = new Rect(gridX + c * (cell + gap), y + r * (cell + gap), cell, cell);
                var key = "day:" + day.DayNumber;
                AddHit(key, rect.Inflate(gap / 2), HitKind.Day, day: day);
                if (_hoverKey == key) hovered = day;
                if (dc is null) continue;
                Gfx.Fill(dc, HeatColor(Level(_activity.Get(day)?.Tokens ?? 0)), rect, 2);
                if (_hoverKey == key || day == today) Gfx.Outline(dc, _hoverKey == key ? _t.Text : _t.Tertiary, rect, 2);
            }
        y += 7 * cell + 6 * gap + 12;

        const double legendBox = 9;
        var less = Gfx.Measure(L.T("Menos"), Gfx.Small).Width;
        var more = Gfx.Measure(L.T("Más"), Gfx.Small).Width;
        var legendWidth = less + 5 + 5 * (legendBox + 3) + 2 + more;
        if (dc is not null)
        {
            string detail;
            if (hovered is { } d)
            {
                var record = _activity.Get(d);
                detail = L.T("{0:ddd d MMM}", d).Replace(".", "") + (record is null || record.Tokens == 0
                    ? L.T(" · sin actividad")
                    : L.T(" · {0} tokens · {1} respuestas", Format.Tokens(record.Tokens), record.Responses));
            }
            else detail = _activity.FirstDay is { } first && first > start ? L.T("Desde el {0:d 'de' MMMM}", first) : L.T("Últimos 6 meses");
            Gfx.Write(dc, detail, Gfx.Small, hovered is null ? _t.Tertiary : _t.Secondary, new Rect(Pad, y, Inner - legendWidth - 10, small));

            var x = Pad + Inner - legendWidth;
            Gfx.Write(dc, L.T("Menos"), Gfx.Small, _t.Tertiary, new Rect(x, y, less + 2, small));
            x += less + 5;
            for (var level = 0; level <= 4; level++)
            {
                Gfx.Fill(dc, HeatColor(level), new Rect(x, y + (small - legendBox) / 2, legendBox, legendBox), 2);
                x += legendBox + 3;
            }
            Gfx.Write(dc, L.T("Más"), Gfx.Small, _t.Tertiary, new Rect(x + 2, y, more + 2, small));
        }
        y += small + 22;

        var stats = ActivityStats.Compute(_activity, start, today);
        static string Days(int n) => n == 1 ? L.T("1 día") : L.T("{0} días", n);
        var items = new (string Value, string Label)[]
        {
            (Format.Tokens(stats.Tokens), L.T("tokens")),
            (stats.ActiveDays.ToString(), L.T("días activos")),
            (Days(stats.CurrentStreak), L.T("racha actual")),
            (Days(stats.LongestStreak), L.T("racha más larga")),
            (stats.BusiestDay is { } busiest ? L.T("{0:d MMM}", busiest).Replace(".", "") : "—", L.T("día más activo")),
            (stats.FavoriteModel ?? "—", L.T("modelo favorito")),
        };
        var columnWidth = Inner / 2;
        var rowHeight = Gfx.BodyStrong.LineHeight + 2 + small + 14;
        for (var i = 0; i < items.Length && dc is not null; i++)
        {
            var x = Pad + i % 2 * columnWidth;
            var rowTop = y + i / 2 * rowHeight;
            Gfx.Write(dc, items[i].Value, Gfx.BodyStrong, _t.Text, new Rect(x, rowTop, columnWidth - 8, Gfx.BodyStrong.LineHeight));
            Gfx.Write(dc, items[i].Label, Gfx.Small, _t.Tertiary, new Rect(x, rowTop + Gfx.BodyStrong.LineHeight + 2, columnWidth - 8, small));
        }
        return y + (items.Length + 1) / 2 * rowHeight + 6;
    }

    SD.Color HeatColor(int level) => level switch
    {
        0 => _t.Track,
        1 => Gfx.Blend(_t.Background, _t.Accent, 0.3),
        2 => Gfx.Blend(_t.Background, _t.Accent, 0.52),
        3 => Gfx.Blend(_t.Background, _t.Accent, 0.76),
        _ => _t.Accent,
    };

    // ── Lo que se dice de una sesión ────────────────────────────────────────────────────────

    (string Text, SD.Color Color) StatusLine(SessionInfo s) => s.State switch
    {
        SessionState.Working => (s.Action ?? L.T("Pensando…"), _t.Secondary),
        SessionState.Waiting => (s.AwaitingAnswer ? L.T("Esperando su respuesta") : L.T("Necesita su atención"), _t.Warn),
        SessionState.Failed => (L.T("Falló · {0}", s.ErrorText ?? L.T("error de la API")), _t.Danger),
        SessionState.Done => (L.T("Terminó {0}", Format.Ago(s.TurnEndedAt ?? s.StatusAt))
            + (s.LastTurn is { } d ? $" · {Format.Duration(d)}" : ""), _t.Success),
        _ => (L.T("Inactiva · {0}", Format.Ago(s.TurnEndedAt ?? s.StatusAt)), _t.Tertiary),
    };

    static IEnumerable<(string Label, string Value, int Lines)> Details(SessionInfo s)
    {
        if (!string.IsNullOrWhiteSpace(s.LastPrompt)) yield return (L.T("Pedido"), s.LastPrompt.ReplaceLineEndings(" "), 3);
        if (s.Cwd.Length > 0) yield return (L.T("Carpeta"), s.Cwd.Replace(Account.Home, "~"), 1);
        if (Format.ModelLine(s) is { Length: > 0 } model) yield return (L.T("Modelo"), model, 1);
        var turns = s.Turns == 1 ? L.T("1 interacción") : L.T("{0} interacciones", s.Turns);
        var started = s.StartedAt is { } at ? L.T(" · desde {0:t}", at.LocalDateTime) : "";
        yield return (L.T("Actividad"), turns + started, 1);
    }

    // ── Animaciones ─────────────────────────────────────────────────────────────────────────

    void DrawStateIcon(DrawingContext dc, SessionState state, Rect box)
    {
        switch (state)
        {
            case SessionState.Working: DrawSpinner(dc, box); break;
            case SessionState.Waiting: DrawPulse(dc, box, _t.Warn); break;
            case SessionState.Failed: Gfx.Icon(dc, Glyph.Close, _t.Danger, box, 13); break;
            case SessionState.Done: Gfx.Icon(dc, Glyph.Check, _t.Success, box, 14); break;
            default: dc.DrawEllipse(Gfx.B(_t.Tertiary), null, box.Center, 3, 3); break;
        }
    }

    void DrawSpinner(DrawingContext dc, Rect box)
    {
        var frame = SpinnerFrames[(int)(Environment.TickCount64 / 110 % SpinnerFrames.Length)];
        var ft = Gfx.Text(frame, Gfx.Spinner, Gfx.B(_t.Accent));
        dc.DrawText(ft, new Point(box.X + (box.Width - ft.Width) / 2, box.Y + (box.Height - ft.Height) / 2));
    }

    void DrawPulse(DrawingContext dc, Rect box, SD.Color color)
    {
        var wave = 0.5 + 0.5 * Math.Sin(Environment.TickCount64 % 1400 / 1400.0 * 2 * Math.PI);
        var halo = 4 + 3 * wave;
        dc.DrawEllipse(Gfx.B(SD.Color.FromArgb((int)(70 * (1 - wave)) + 20, color)), null, box.Center, halo, halo);
        dc.DrawEllipse(Gfx.B(color), null, box.Center, 4, 4);
    }

    // ── Pie ─────────────────────────────────────────────────────────────────────────────────

    double ComposeFooter(DrawingContext? dc, double y)
    {
        var small = Gfx.Small.LineHeight;
        dc?.DrawLine(Gfx.P(_t.Border), new Point(Pad, y), new Point(ViewWidth - Pad, y));
        y += 14;
        var gear = new Rect(ViewWidth - Pad - 26, y + (small - 28) / 2, 28, 28);
        AddHit("gear", gear, HitKind.Gear);
        if (dc is not null)
        {
            // Unos 20 de alto sea cual sea la cuadrícula del estilo (la de la cabeza del chibi es más fina).
            var rows = RefreshSprite.Rows(_refreshStyle);
            var unit = Math.Max(1, Math.Round(20.0 / rows));
            var right = gear.X - 8;
            var when = _refresh is not null ? "" : _busy ? L.T("Actualizando…") : _updated is { } u ? $"{u:t}" : "";
            var whenWidth = _refresh is not null ? RefreshSprite.Columns(_refreshStyle) * unit : Gfx.Measure(when, Gfx.Small).Width;
            Gfx.Write(dc, Format.Local(_local), Gfx.Small, _t.Tertiary, new Rect(Pad, y, right - Pad - whenWidth - 12, small));
            if (_refresh is { } frame)
                Gfx.Pixels(dc, right - whenWidth, y + (small - rows * unit) / 2, unit,
                    fill => RefreshSprite.Paint(fill, _refreshStyle, frame, _t.Accent));
            else
                Gfx.Write(dc, when, Gfx.Small, _t.Tertiary, new Rect(right - whenWidth, y, whenWidth + 1, small));

            var active = _view == View.Settings;
            if (active) Gfx.Fill(dc, _t.Accent, gear, 8);
            else if (_hoverKey == "gear") Gfx.Fill(dc, _t.Hover, gear, 8);
            Gfx.Icon(dc, Glyph.Settings, active ? _t.Background : _t.Secondary, gear, 15);
        }
        return y + small + 16;
    }

    // ── Interacción ─────────────────────────────────────────────────────────────────────────

    void AddHit(string key, Rect bounds, HitKind kind, Account? account = null, SessionInfo? session = null, View view = View.Usage, DateOnly day = default) =>
        _hits.Add(new Hit(key, bounds, kind, account, session, view, day));

    Hit? HitAt(Point p) => _hits.LastOrDefault(h => h.Bounds.Contains(p));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitAt(e.GetPosition(this));
        Cursor = hit is null || hit.Kind == HitKind.Day ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (hit?.Key == _hoverKey) return;
        _hoverKey = hit?.Key;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverKey is null) return;
        _hoverKey = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (HitAt(e.GetPosition(this)) is not { } hit) return;

        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            if (hit.Account is { } account)
                ShowMenu((L.T("Renombrar"), () => RenameRequested?.Invoke(account)),
                    (L.T("Abrir carpeta"), () => Gfx.OpenFolder(account.ConfigDir)),
                    (L.T("Quitar"), () => RemoveRequested?.Invoke(account)));
            else if (hit.Session is { } session)
                ShowMenu((L.T("Nueva sesión en esta carpeta…"), () => NewSessionRequested?.Invoke(session)),
                    (L.T("Abrir carpeta del proyecto"), () => Gfx.OpenFolder(session.Cwd)),
                    (L.T("Copiar ID de la sesión"), () => _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(session.SessionId)));
            return;
        }
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        switch (hit.Kind)
        {
            case HitKind.Refresh when !_busy: RefreshRequested?.Invoke(); break;
            case HitKind.Add: AddRequested?.Invoke(); break;
            case HitKind.View: OpenView(hit.View); break;
            case HitKind.Gear:
                if (_view == View.Settings) OpenView(_beforeSettings);
                else ShowSettings();
                break;
            case HitKind.CloseSettings: OpenView(_beforeSettings); break;
            case HitKind.Setting: SettingRequested?.Invoke(hit.Key[4..]); break;
            case HitKind.SettingsTab: SelectSettingsTab(Enum.Parse<SettingsTab>(hit.Key["stab:".Length..])); break;
            case HitKind.FlowSession when hit.Session is { } flow: _flowSession = flow.SessionId; Relayout(); break;
            case HitKind.FlowBack: _flowSession = null; Relayout(); break;
            case HitKind.SessionAction when hit.Session is { } session: RunSessionAction(hit.Key, session); break;
            case HitKind.IdleToggle: _showIdle = !_showIdle; Relayout(); break;
            case HitKind.Account when hit.Account is { } account:
                _selected = account.ConfigDir;
                SelectedChanged?.Invoke(account);
                Relayout();
                break;
        }
    }

    void ShowMenu(params (string Header, Action Action)[] items)
    {
        var menu = new ContextMenu();
        foreach (var (header, action) in items)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        menu.Open(this);
    }
}
