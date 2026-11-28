namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

/// <summary>
/// Ajustes › Consumo: lo que gastaron todos los agentes en este equipo (Claude Code, Codex, Gemini CLI, OpenCode y
/// Copilot CLI), por modelo. Arriba, el periodo y una franja con los filtros de agente y los totales; a la izquierda, la
/// tendencia en áreas apiladas y la tabla por modelo con su minilínea; a la derecha, el reparto en una dona, la actividad
/// por hora, los límites, los proyectos y lo que no se puede medir. Se lee de los archivos de los agentes (UsageLedger),
/// aparte del hilo de la interfaz y cada minuto mientras se ve. El costo solo aparece si el agente lo escribe.
/// </summary>
sealed class ConsumptionPane : Border
{
    const double WideFrom = 980;

    static readonly object LedgerLock = new();
    static UsageLedger? _ledger;
    static UsageLedger Ledger { get { lock (LedgerLock) return _ledger ??= new UsageLedger(); } }
    /// <summary>Lo último leído: al volver a la página (o con otro tema) se ve enseguida, mientras se lee lo nuevo.</summary>
    static (List<UsageRecord> Records, List<UsageLimit> Limits, DateTimeOffset At)? _last;

    readonly Ink _ink;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    readonly TextBlock _status;
    List<UsageRecord> _records = [];
    List<UsageLimit> _limits = [];
    List<string> _accounts = [];
    bool _loaded, _busy, _wide = true;
    DateTimeOffset? _updated;

    UsagePeriod _period = UsagePeriod.Days7;
    UsageMetric _metric = UsageMetric.Output;
    bool _trendByModel, _splitByAgent, _notesOpen;
    readonly HashSet<string> _off = [];
    string? _account;
    string _sort = "output";
    bool _ascending;

    /// <summary>Para las pruebas: lo que muestra ahora.</summary>
    internal UsageReport? Report { get; private set; }

    /// <param name="read">Leer los archivos de los agentes al mostrarse y cada minuto (las pruebas usan Show).</param>
    public ConsumptionPane(Ink ink, bool read = true)
    {
        _ink = ink;
        _status = Ui.Text("", 11.5, ink.Tertiary);
        if (read && _last is { } last) (_records, _limits, _updated, _loaded) = (last.Records, last.Limits, last.At, true);
        if (read)
        {
            _timer.Tick += (_, _) => Reload();
            AttachedToVisualTree += (_, _) =>
            {
                _timer.Start();
                Reload();
            };
            DetachedFromVisualTree += (_, _) => _timer.Stop();
        }
        SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= WideFrom;
            if (wide == _wide) return;
            _wide = wide;
            Draw();
        };
        Draw();
    }

    /// <summary>Para las pruebas: muestra estos registros sin leer nada.</summary>
    internal void Show(List<UsageRecord> records, List<UsageLimit> limits)
    {
        (_records, _limits, _loaded, _updated) = (records, limits, true, DateTimeOffset.Now);
        Draw();
    }

    /// <summary>Lee lo nuevo (lo de los últimos 31 días: así cambiar de periodo no vuelve a leer) y redibuja.</summary>
    void Reload()
    {
        if (_busy) return;
        _busy = true;
        var settings = SettingsStore.Load();
        _accounts = settings.Accounts.Select(a => a.Label).ToList();
        var sources = UsageSources.From(settings);
        var now = DateTimeOffset.Now;
        var since = new[] { UsageReport.Range(UsagePeriod.Days30, now).From, UsageReport.Range(UsagePeriod.Month, now).From }.Min();
        var shown = -1;
        _ = Task.Run(() =>
        {
            try
            {
                var records = Ledger.Refresh(sources, since, default, part =>
                {
                    var percent = (int)(part * 100);
                    if (_loaded || percent == shown) return;
                    shown = percent;
                    Dispatcher.UIThread.Post(() => _status.Text = L.T("Leyendo los registros de los agentes… {0} %", percent));
                });
                var limits = UsageLimits.Read(settings, now);
                Ledger.Save();
                Dispatcher.UIThread.Post(() =>
                {
                    (_records, _limits, _loaded, _updated, _busy) = (records, limits, true, DateTimeOffset.Now, false);
                    _last = (records, limits, _updated.Value);
                    Draw();
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                Dispatcher.UIThread.Post(() =>
                {
                    _busy = false;
                    _status.Text = L.T("No se pudo leer el consumo: {0}", ex.Message);
                });
            }
        });
    }

    // ── La página ───────────────────────────────────────────────────────────────────────────

    void Draw()
    {
        var report = UsageReport.Build(_records, _period, DateTimeOffset.Now, _off, _account);
        Report = report;
        var page = new StackPanel { Spacing = 12, Margin = new Thickness(28, 22, 28, 28) };
        page.Children.Add(Header());
        if (!_loaded)
        {
            _status.Text = L.T("Leyendo los registros de los agentes…");
            Child = page;
            return;
        }
        _status.Text = L.T("Se actualiza cada minuto; solo vuelve a leer los archivos que cambiaron.");
        page.Children.Add(Strip(report));
        Control[] left = [Trend(report), ModelTable(report)];
        Control[] right = [Donut(report), Heat(report), Limits(), Projects(report), Notes()];
        if (_wide)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,12,*") };
            var a = new StackPanel { Spacing = 12 };
            foreach (var c in left) a.Children.Add(c);
            var b = new StackPanel { Spacing = 12 };
            foreach (var c in right) b.Children.Add(c);
            Grid.SetColumn(b, 2);
            grid.Children.Add(a);
            grid.Children.Add(b);
            page.Children.Add(grid);
        }
        else foreach (var c in left.Concat(right)) page.Children.Add(c);
        page.Children.Add(Footer(report));
        Child = page;
    }

    Control Header()
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock { Text = L.T("Consumo"), FontFamily = Ink.Display, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = _ink.Text });
        var about = Ui.Text(L.T("Lo que gastaron los agentes en este equipo, por modelo. Se lee de sus archivos: no usa sus cuentas ni gasta tokens."), 12.5, _ink.Tertiary);
        about.TextWrapping = TextWrapping.Wrap;
        title.Children.Add(about);
        // La misma línea de estado en cada dibujo (la lectura la va cambiando): se suelta del anterior.
        if (_status.Parent is Panel old) old.Children.Remove(_status);
        title.Children.Add(_status);
        head.Children.Add(title);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        tools.Children.Add(Segmented([(nameof(UsagePeriod.Today), L.T("Hoy")), (nameof(UsagePeriod.Days7), L.T("7 días")), (nameof(UsagePeriod.Days30), L.T("30 días")), (nameof(UsagePeriod.Month), L.T("Este mes"))],
            _period.ToString(), id => { _period = Enum.Parse<UsagePeriod>(id); Draw(); }));
        if (_accounts.Count > 1)
        {
            Border? pick = null;
            pick = Pill(_account ?? L.T("Todas las cuentas de Claude"), () => DropList.Show(pick!, _ink,
                [new DropList.Entry("", L.T("Todas las cuentas de Claude"), _account is null), .. _accounts.Select(a => new DropList.Entry(a, a, a == _account))], picked =>
                {
                    if (picked is null) return;
                    _account = picked.Length == 0 ? null : picked;
                    Draw();
                }));
            ToolTip.SetTip(pick, L.T("Filtra lo de Claude Code por cuenta; los otros agentes no tienen cuentas de Claude."));
            tools.Children.Add(pick);
        }
        Grid.SetColumn(tools, 1);
        head.Children.Add(tools);
        return head;
    }

    /// <summary>Los filtros de agente y los cinco totales, en una franja.</summary>
    Control Strip(UsageReport r)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*,*,*,*") };
        var chips = new WrapPanel { MaxWidth = 270, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 8) };
        var agents = UsageAgents.All.Where(a => r.AgentsSeen.Contains(a) || _off.Contains(a)).ToList();
        if (agents.Count == 0) chips.Children.Add(Ui.Text(L.T("Sin consumo en este periodo"), 12, _ink.Tertiary));
        foreach (var agent in agents)
        {
            var off = _off.Contains(agent);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            row.Children.Add(Dot(ChartDraw.Brush(UsageAgents.Color(agent))));
            row.Children.Add(Ui.Text(UsageAgents.Name(agent), 11.5, _ink.Secondary));
            var chip = Ui.Press(row, () =>
            {
                if (!_off.Remove(agent)) _off.Add(agent);
                Draw();
            }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, UsageAgents.Name(agent));
            chip.Padding = new Thickness(8, 2);
            chip.Margin = new Thickness(0, 2, 4, 2);
            chip.CornerRadius = new CornerRadius(999);
            chip.BorderBrush = _ink.Hairline;
            chip.BorderThickness = new Thickness(1);
            chip.Opacity = off ? 0.38 : 1;
            ToolTip.SetTip(chip, off ? L.T("Mostrar {0}", UsageAgents.Name(agent)) : L.T("Ocultar {0}", UsageAgents.Name(agent)));
            chips.Children.Add(chip);
        }
        grid.Children.Add(chips);

        var t = r.Totals;
        string cost, costNote;
        if (r.HasCost || r.HasPremium)
        {
            cost = string.Join(" · ", new[] { r.HasCost ? Usd(t.Cost!.Value) : null, r.HasPremium ? L.T("{0:0.##} premium", t.Premium) : null }.OfType<string>());
            costNote = r.HasCost && r.HasPremium ? L.T("OpenCode en dólares; Copilot en solicitudes") : r.HasCost ? L.T("el que escribe OpenCode") : L.T("solicitudes premium de Copilot");
        }
        else (cost, costNote) = ("—", L.T("ninguno de estos agentes lo escribe"));
        (string Label, string Value, string Note)[] kpis =
        [
            (L.T("Respuestas"), t.Responses.ToString("N0"), L.T("{0} modelos · {1} sesiones", r.Models.Count, r.Sessions)),
            (L.T("Salida"), Format.Tokens(t.Output), L.T("tokens que generaron")),
            (L.T("Entrada"), Format.Tokens(t.AllInput), L.T("{0} de caché", Format.Tokens(t.CacheRead))),
            (L.T("Razonamiento"), t.Reasoning > 0 ? Format.Tokens(t.Reasoning) : "—", L.T("Codex, Gemini y OpenCode")),
            (L.T("Costo"), cost, costNote),
        ];
        for (var i = 0; i < kpis.Length; i++)
        {
            var box = new StackPanel { Spacing = 1, Margin = new Thickness(12, 7) };
            box.Children.Add(Ui.Text(kpis[i].Label, 11, _ink.Tertiary));
            var value = Ui.Text(kpis[i].Value, 17, _ink.Text);
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            box.Children.Add(value);
            var note = Ui.Text(kpis[i].Note, 11, _ink.Secondary);
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            box.Children.Add(note);
            var cell = new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1, 0, 0, 0), Child = box };
            Grid.SetColumn(cell, i + 1);
            grid.Children.Add(cell);
        }
        return Card(grid, 0);
    }

    // ── Izquierda ───────────────────────────────────────────────────────────────────────────

    Control Trend(UsageReport r)
    {
        var hours = r.Period == UsagePeriod.Today;
        var title = (_metric switch { UsageMetric.Input => L.T("Tokens de entrada"), UsageMetric.Responses => L.T("Respuestas"), _ => L.T("Tokens de salida") })
            + " " + (hours ? L.T("por hora") : L.T("por día"));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tools.Children.Add(Segmented([(nameof(UsageMetric.Output), L.T("Salida")), (nameof(UsageMetric.Input), L.T("Entrada")), (nameof(UsageMetric.Responses), L.T("Respuestas"))],
            _metric.ToString(), id => { _metric = Enum.Parse<UsageMetric>(id); Draw(); }));
        tools.Children.Add(Segmented([("agent", L.T("Agente")), ("model", L.T("Modelo"))], _trendByModel ? "model" : "agent",
            id => { _trendByModel = id == "model"; Draw(); }));

        var count = r.Slots.Count;
        var series = new List<ChartSeries>();
        if (_trendByModel)
        {
            var colors = ModelColors(r);
            var top = r.Models.Take(7).ToList();
            foreach (var m in top)
                series.Add(new ChartSeries(UsageAgents.ModelName(m.Agent, m.Model), colors[m], Values(r.ByModelSlot.GetValueOrDefault(m.Agent + "|" + m.Model), count)));
            if (r.Models.Count > top.Count)
            {
                var rest = new double[count];
                foreach (var m in r.Models.Skip(top.Count))
                {
                    var v = Values(r.ByModelSlot.GetValueOrDefault(m.Agent + "|" + m.Model), count);
                    for (var i = 0; i < count; i++) rest[i] += v[i];
                }
                series.Add(new ChartSeries(L.T("Otros"), _ink.Tertiary, rest));
            }
        }
        else
            foreach (var a in r.Agents)
                series.Add(new ChartSeries(UsageAgents.Name(a.Agent), ChartDraw.Brush(UsageAgents.Color(a.Agent)), Values(r.ByAgentSlot.GetValueOrDefault(a.Agent), count)));

        var labels = new string[count];
        var titles = new string[count];
        for (var i = 0; i < count; i++)
        {
            var at = r.Slots[i].ToLocalTime();
            if (hours)
            {
                labels[i] = i % 6 == 0 || i == count - 1 ? $"{i}:00" : "";
                titles[i] = $"{i}:00";
            }
            else
            {
                titles[i] = i == count - 1 ? L.T("Hoy") : i == count - 2 ? L.T("Ayer") : at.ToString("ddd d MMM");
                labels[i] = i == count - 1 ? L.T("Hoy") : i == 0 || i == count / 2 ? at.ToString("d MMM") : "";
            }
        }
        Func<double, string> format = _metric == UsageMetric.Responses ? v => Math.Round(v).ToString("N0") : v => Format.Tokens((long)Math.Round(v));
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(CardHead(title, tools));
        if (series.Count == 0 || series.All(s => s.Values.All(v => v <= 0))) body.Children.Add(Empty());
        else
        {
            body.Children.Add(new UsageAreaChart(series, labels, titles, format, _ink));
            var legend = new WrapPanel();
            foreach (var s in series) legend.Children.Add(LegendItem(s.Color, s.Name));
            body.Children.Add(legend);
        }
        return Card(body);
    }

    double[] Values(UsageSums[]? slots, int count) => [.. Enumerable.Range(0, count).Select(i => slots is null || i >= slots.Length ? 0 : slots[i].Of(_metric))];

    Control ModelTable(UsageReport r)
    {
        var colors = ModelColors(r);
        var total = Math.Max(1, r.Totals.Output);
        (string Id, string Label)[] columns =
        [
            ("model", L.T("Modelo")), ("agent", L.T("Agente · proveedor")), ("responses", L.T("Resp.")), ("input", L.T("Entrada")), ("cache", L.T("Caché")),
            ("output", L.T("Salida")), ("reasoning", L.T("Razon.")), ("trend", L.T("Tendencia")), ("share", "%"), ("cost", L.T("Costo")),
        ];
        Func<UsageModelRow, IComparable> key = _sort switch
        {
            "model" => m => UsageAgents.ModelName(m.Agent, m.Model),
            "agent" => m => UsageAgents.Name(m.Agent) + (m.Provider ?? ""),
            "responses" => m => m.Sums.Responses,
            "input" => m => m.Sums.AllInput,
            "cache" => m => m.Sums.CacheRead,
            "reasoning" => m => m.Sums.Reasoning,
            "trend" => m => m.Trend[^1] - m.Trend[0],
            "cost" => m => m.Sums.Cost ?? m.Sums.Premium ?? -1,
            _ => m => m.Sums.Output,
        };
        var rows = _ascending ? r.Models.OrderBy(key).ToList() : r.Models.OrderByDescending(key).ToList();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,1.5*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < columns.Length; c++)
        {
            var (id, label) = columns[c];
            var on = _sort == id;
            var text = Ui.Text(label + (on ? (_ascending ? " ↑" : " ↓") : ""), 11, on ? _ink.Text : _ink.Tertiary);
            text.HorizontalAlignment = c < 2 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            var head = Ui.Press(text, () =>
            {
                _ascending = _sort == id ? !_ascending : id is "model" or "agent";
                _sort = id;
                Draw();
            }, (b, hover) => text.Foreground = hover || on ? _ink.Text : _ink.Tertiary, L.T("Ordenar por {0}", label));
            head.Padding = new Thickness(6, 3);
            Place(grid, head, 0, c);
        }
        grid.Children.Add(Line(0, columns.Length));

        for (var i = 0; i < rows.Count; i++)
        {
            var m = rows[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            // En una grilla y no en una fila sin fin: un nombre largo se corta con «…» en vez de montarse sobre la columna siguiente.
            var name = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            name.Children.Add(Dot(colors[m]));
            var modelText = Ui.Text(UsageAgents.ModelName(m.Agent, m.Model), 12, _ink.Text);
            modelText.TextTrimming = TextTrimming.CharacterEllipsis;
            modelText.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(modelText, 1);
            name.Children.Add(modelText);
            ToolTip.SetTip(name, m.Model);
            Place(grid, name, row, 0);
            var agent = new TextBlock { FontFamily = Ink.Sans, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            agent.Inlines!.Add(new Avalonia.Controls.Documents.Run(UsageAgents.Name(m.Agent)) { Foreground = _ink.Secondary });
            if (m.Provider is { } provider) agent.Inlines.Add(new Avalonia.Controls.Documents.Run(" · " + provider) { Foreground = _ink.Tertiary });
            Place(grid, agent, row, 1);
            Place(grid, Number(m.Sums.Responses.ToString("N0")), row, 2);
            var input = Number(Format.Tokens(m.Sums.AllInput));
            ToolTip.SetTip(input, L.T("Nueva: {0} · leída de caché: {1} · escrita en caché: {2}", Format.Tokens(m.Sums.Input), Format.Tokens(m.Sums.CacheRead), Format.Tokens(m.Sums.CacheWrite)));
            Place(grid, input, row, 3);
            Place(grid, Number(Format.Tokens(m.Sums.CacheRead)), row, 4);
            Place(grid, Number(Format.Tokens(m.Sums.Output)), row, 5);
            Place(grid, Number(m.Sums.Reasoning > 0 ? Format.Tokens(m.Sums.Reasoning) : "—", m.Sums.Reasoning > 0 ? _ink.Text : _ink.Tertiary), row, 6);
            var spark = new UsageSpark(m.Trend, colors[m]) { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(6, 0) };
            Place(grid, spark, row, 7);
            var share = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right };
            share.Children.Add(Ui.Text(L.T("{0:0} %", 100.0 * m.Sums.Output / total), 12, _ink.Text));
            share.Children.Add(new UsageBar([((double)m.Sums.Output, _ink.Accent)], _ink.Track, total) { Width = 40, Height = 4, VerticalAlignment = VerticalAlignment.Center });
            Place(grid, share, row, 8);
            Control cost = m.Sums.Cost is { } usd ? Number(Usd(usd))
                : m.Sums.Premium is { } premium ? Number(L.T("{0:0.##} premium", premium))
                : Number("—", _ink.Tertiary);
            if (m.Sums.Cost is null && m.Sums.Premium is null) ToolTip.SetTip(cost, L.T("Este agente no escribe su costo"));
            Place(grid, cost, row, 9);
            if (i < rows.Count - 1) grid.Children.Add(Line(row, columns.Length));
        }
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(CardHead(L.T("Por modelo"), Ui.Text(L.T("Toque una columna para ordenar · la línea es su tendencia"), 11, _ink.Tertiary)));
        body.Children.Add(rows.Count == 0 ? Empty() : grid);
        return Card(body);
    }

    // ── Derecha ─────────────────────────────────────────────────────────────────────────────

    Control Donut(UsageReport r)
    {
        var colors = ModelColors(r);
        var parts = _splitByAgent
            ? r.Agents.Select(a => (UsageAgents.Name(a.Agent), ChartDraw.Brush(UsageAgents.Color(a.Agent)), (double)a.Sums.Output)).ToList()
            : r.Models.Select(m => (UsageAgents.ModelName(m.Agent, m.Model), colors[m], (double)m.Sums.Output)).ToList();
        if (parts.Count > 6)
            parts = [.. parts.Take(5), (L.T("Otros"), _ink.Tertiary, parts.Skip(5).Sum(p => p.Item3))];
        var total = parts.Sum(p => p.Item3);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(CardHead(L.T("Reparto de la salida"), Segmented([("model", L.T("Modelo")), ("agent", L.T("Agente"))], _splitByAgent ? "agent" : "model",
            id => { _splitByAgent = id == "agent"; Draw(); })));
        if (total <= 0)
        {
            body.Children.Add(Empty());
            return Card(body);
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        row.Children.Add(new UsageDonut(parts, Format.Tokens((long)total), L.T("tokens de salida"), _ink) { Width = 120, Height = 120 });
        var legend = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        foreach (var (name, color, value) in parts)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            line.Children.Add(Dot(color));
            var text = Ui.Text(name, 12, _ink.Text);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Margin = new Thickness(6, 0);
            Grid.SetColumn(text, 1);
            line.Children.Add(text);
            var percent = Ui.Text(L.T("{0:0} %", 100 * value / total), 12, _ink.Tertiary);
            Grid.SetColumn(percent, 2);
            line.Children.Add(percent);
            legend.Children.Add(line);
        }
        Grid.SetColumn(legend, 1);
        row.Children.Add(legend);
        body.Children.Add(row);
        return Card(body);
    }

    Control Heat(UsageReport r)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(CardHead(L.T("Actividad por hora"), Ui.Text(L.T("respuestas de los últimos 7 días"), 11, _ink.Tertiary)));
        string[] days = [L.T("Lun"), L.T("Mar"), L.T("Mié"), L.T("Jue"), L.T("Vie"), L.T("Sáb"), L.T("Dom")];
        body.Children.Add(new UsageHeat(r.Heat, days, _ink, 7));
        return Card(body);
    }

    Control Limits()
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(CardHead(L.T("Límites"), Ui.Text(L.T("de quien los escribe"), 11, _ink.Tertiary)));
        var shown = _limits.Where(l => !_off.Contains(l.Agent) && (_account is null || l.Agent != UsageAgents.Claude || l.Account == _account)).ToList();
        if (shown.Count == 0) body.Children.Add(Ui.Text(L.T("Ninguno de los agentes elegidos escribe límites."), 12, _ink.Tertiary));
        var many = _accounts.Count > 1;
        foreach (var l in shown)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,40") };
            var name = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            name.Children.Add(Dot(ChartDraw.Brush(UsageAgents.Color(l.Agent))));
            var who = l.Agent == UsageAgents.Claude && many ? $"{UsageAgents.Name(l.Agent)} ({l.Account})" : UsageAgents.Name(l.Agent);
            var label = Ui.Text($"{who} · {l.Label}", 12, _ink.Text);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.Margin = new Thickness(6, 0, 8, 0);
            Grid.SetColumn(label, 1);
            name.Children.Add(label);
            line.Children.Add(name);
            var bar = new UsageBar([(l.Percent, _ink.Accent)], _ink.Track, 100) { Height = 5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1);
            line.Children.Add(bar);
            var percent = Ui.Text(L.T("{0:0} %", l.Percent), 12, _ink.Text, FontWeight.SemiBold);
            percent.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(percent, 2);
            line.Children.Add(percent);
            var item = new StackPanel { Spacing = 1 };
            item.Children.Add(line);
            if (l.ResetsAt is not null) item.Children.Add(Ui.Text(Format.Reset(l.ResetsAt), 11, _ink.Tertiary));
            body.Children.Add(item);
        }
        return Card(body);
    }

    Control Projects(UsageReport r)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(CardHead(L.T("Por proyecto"), null));
        var list = r.Projects.Where(p => p.Sums.Output > 0).ToList();
        if (list.Count == 0)
        {
            body.Children.Add(Empty());
            return Card(body);
        }
        var parts = list.Take(5).Select(p => (Name: p.Project.Length == 0 ? L.T("Sin proyecto") : p.Project, Value: (double)p.Sums.Output)).ToList();
        if (list.Count > 5) parts.Add((L.T("Otros"), list.Skip(5).Sum(p => (double)p.Sums.Output)));
        var accent = _ink.Accent is ISolidColorBrush solid ? solid.Color : ChartDraw.ColorOf("#7EA6E8");
        var colors = parts.Select((_, i) => ChartDraw.Shade(accent, i)).ToList();
        body.Children.Add(new UsageBar(parts.Select((p, i) => (p.Value, colors[i])).ToList(), _ink.Track) { Height = 8 });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        for (var i = 0; i < parts.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 1) };
            line.Children.Add(Dot(colors[i]));
            var name = Ui.Text(parts[i].Name, 12, _ink.Text);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(6, 0);
            Grid.SetColumn(name, 1);
            line.Children.Add(name);
            var value = Ui.Text(Format.Tokens((long)parts[i].Value), 12, _ink.Tertiary);
            Grid.SetColumn(value, 2);
            line.Children.Add(value);
            Grid.SetRow(line, i / 2);
            Grid.SetColumn(line, i % 2 == 0 ? 0 : 2);
            grid.Children.Add(line);
        }
        body.Children.Add(grid);
        return Card(body);
    }

    Control Notes()
    {
        var body = new StackPanel { Spacing = 6 };
        var chevron = Ui.Text(_notesOpen ? "▾" : "▸", 12, _ink.Tertiary);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(chevron);
        head.Children.Add(Ui.Text(L.T("Lo que no se puede medir"), 12.5, _ink.Text, FontWeight.SemiBold));
        var toggle = Ui.Press(head, () => { _notesOpen = !_notesOpen; Draw(); }, null, L.T("Lo que no se puede medir"));
        body.Children.Add(toggle);
        if (_notesOpen)
            foreach (var note in new[]
            {
                L.T("Solo lo hecho en este equipo: claude.ai, el móvil u otras computadoras no aparecen aquí (sí en los límites de Claude)."),
                L.T("Costo: Claude Code, Codex y Gemini CLI no lo escriben. OpenCode lo escribe en dólares; Copilot, en solicitudes premium."),
                L.T("Límites: Gemini CLI, OpenCode y Copilot CLI no los escriben; se ve lo que hicieron."),
                L.T("Las sesiones comprimidas de Codex (.zst) no se leen."),
                L.T("Solo los agentes que se siguen en Ajustes › Cuentas."),
            })
            {
                var text = Ui.Text("• " + note, 12, _ink.Secondary);
                text.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(text);
            }
        return Card(body);
    }

    Control Footer(UsageReport r)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var label = Ui.Text(L.T("Exportar CSV"), 12, _ink.Accent);
        var export = Ui.Press(label, () => _ = ExportAsync(r), (b, on) => label.TextDecorations = on ? TextDecorations.Underline : null, L.T("Exportar CSV"));
        ToolTip.SetTip(export, L.T("La tabla por modelo de este periodo, para abrirla en una hoja de cálculo"));
        row.Children.Add(export);
        return row;
    }

    async Task ExportAsync(UsageReport r)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Exportar CSV"),
            SuggestedFileName = $"consumo-{DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true));
            await writer.WriteAsync(UsageCsv.Of(r));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = L.T("No se pudo guardar: {0}", ex.Message); }
    }

    // ── Piezas ──────────────────────────────────────────────────────────────────────────────

    /// <summary>El color de cada modelo: el de su agente, más claro u oscuro según su lugar entre los de ese agente.</summary>
    static Dictionary<UsageModelRow, IBrush> ModelColors(UsageReport r)
    {
        var colors = new Dictionary<UsageModelRow, IBrush>();
        foreach (var group in r.Models.GroupBy(m => m.Agent))
        {
            var i = 0;
            foreach (var m in group.OrderByDescending(m => m.Sums.Output)) colors[m] = ChartDraw.Shade(UsageAgents.Color(m.Agent), i++);
        }
        return colors;
    }

    static string Usd(double value) => L.T("US$ {0:0.00}", value);

    Border Card(Control child, double padding = 12) => new()
    {
        Background = _ink.Raised,
        BorderBrush = _ink.Hairline,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(padding, padding * 0.85),
        Child = child,
    };

    Control CardHead(string title, Control? tools)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = Ui.Text(title, 12.5, _ink.Text, FontWeight.SemiBold);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(text);
        if (tools is not null)
        {
            tools.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tools, 1);
            head.Children.Add(tools);
        }
        return head;
    }

    Control Empty() => new TextBlock { Text = L.T("Sin consumo en este periodo."), FontFamily = Ink.Sans, FontSize = 12, Foreground = _ink.Tertiary, Margin = new Thickness(0, 6) };

    static Border Dot(IBrush color) => new() { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = color, VerticalAlignment = VerticalAlignment.Center };

    Control LegendItem(IBrush color, string name)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 12, 2) };
        row.Children.Add(Dot(color));
        row.Children.Add(Ui.Text(name, 11.5, _ink.Secondary));
        return row;
    }

    TextBlock Number(string text, IBrush? brush = null)
    {
        var t = Ui.Text(text, 12, brush ?? _ink.Text);
        t.HorizontalAlignment = HorizontalAlignment.Right;
        return t;
    }

    static void Place(Grid grid, Control control, int row, int column)
    {
        if (control.Margin == default) control.Margin = new Thickness(6, 4);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    Border Line(int row, int columns)
    {
        var line = new Border { Height = 1, Background = _ink.Hairline, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        Grid.SetRow(line, row);
        Grid.SetColumnSpan(line, columns);
        return line;
    }

    Border Pill(string text, Action onClick)
    {
        var label = Ui.Text(text + " ▾", 12, _ink.Secondary);
        var pill = Ui.Press(label, onClick, (b, on) => label.Foreground = on ? _ink.Text : _ink.Secondary, text);
        pill.Height = 30;
        pill.Padding = new Thickness(10, 0);
        pill.CornerRadius = new CornerRadius(9);
        pill.Background = _ink.Pill;
        label.VerticalAlignment = VerticalAlignment.Center;
        return pill;
    }

    /// <summary>Botones segmentados, como los de Ajustes › Terminal: el elegido, en relieve.</summary>
    Control Segmented(IReadOnlyList<(string Id, string Label)> options, string selected, Action<string> chosen)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, label) in options)
        {
            var on = id == selected;
            var text = Ui.Text(label, 12, on ? _ink.Text : _ink.Secondary, on ? FontWeight.Medium : FontWeight.Normal);
            text.VerticalAlignment = VerticalAlignment.Center;
            var button = Ui.Press(text, () => { if (!on) chosen(id); }, on ? null : (b, hover) => text.Foreground = hover ? _ink.Text : _ink.Secondary, label);
            button.Height = 24;
            button.Padding = new Thickness(9, 0);
            button.CornerRadius = new CornerRadius(7);
            if (on)
            {
                button.Background = _ink.Raised;
                button.BoxShadow = BoxShadows.Parse("0 1 2 0 #29000000");
            }
            row.Children.Add(button);
        }
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = row };
    }
}
