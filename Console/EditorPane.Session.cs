namespace Claupet;

using AvaloniaEdit.Document;

/// <summary>
/// Volver a abrir lo que había, como en Notepad++: las pestañas, los grupos, el zoom y lo que no se guardó (también lo
/// «Sin título»). Se guarda cada poco mientras algo cambia y al cerrar.
/// </summary>
sealed partial class EditorPane
{
    /// <summary>La sesión está activa (la ventana la pidió): se guarda. Las pruebas no la activan y no tocan la de la persona.</summary>
    bool _sessionOn;
    bool _sessionChanged;

    /// <summary>Abre lo de la última vez. Lo llama la ventana al abrirse, antes de los archivos que se pidieron.</summary>
    public void RestoreSession()
    {
        _sessionOn = true;
        if (EditorSession.Load() is not { } state) return;
        _zoom = state.Zoom is >= 50 and <= 300 ? state.Zoom : 100;
        _wrap = state.Wrap;
        _spaces = state.Spaces;
        foreach (var group in state.Groups ?? [])
            if (_groups.All(g => g.Id != group.Id)) _groups.Add(new EditorGroup { Id = group.Id, Name = group.Name, Color = group.Color });
        var languages = EditorLanguages.All;
        EditorDoc? current = null;
        var index = 0;
        foreach (var saved in state.Docs ?? [])
        {
            var at = index++;
            if (_docs.Any(d => saved.Path is not null && string.Equals(d.Path, saved.Path, StringComparison.OrdinalIgnoreCase))) continue;
            if (Restore(saved) is not { } doc) continue;
            if (languages.Contains(saved.Language)) doc.Language = saved.Language;
            if (saved.Group is { } group && _groups.Any(g => g.Id == group)) doc.Group = group;
            doc.Caret = Math.Clamp(saved.Caret, 0, doc.Document.TextLength);
            _docs.Add(doc);
            Watch(doc);
            if (at == state.Current) current = doc;
        }
        _groups.RemoveAll(g => _docs.All(d => d.Group != g.Id));
        if ((current ?? _docs.FirstOrDefault()) is { } shown) Select(shown);
        else Build();
        _sessionChanged = false;
    }

    /// <summary>Un archivo de la sesión: el del disco, con lo que no se guardó encima (Ctrl+Z vuelve a lo del disco).</summary>
    EditorDoc? Restore(EditorSession.Doc saved)
    {
        var backup = saved.Backup is { } id ? EditorSession.ReadBackup(id) : null;
        EditorDoc doc;
        if (saved.Path is { } path && File.Exists(path))
        {
            if (Read(path) is not { } read) return null;
            doc = read;
            if (backup is not null && backup != doc.Document.Text) doc.Document.Replace(0, doc.Document.TextLength, backup);
        }
        else if (backup is not null)
        {
            // Un «Sin título», o un archivo que ya no está pero tenía cambios: lo único que queda es el respaldo.
            doc = new EditorDoc
            {
                Path = saved.Path,
                Document = new TextDocument(),
                Number = Math.Max(1, saved.Number),
                Language = EditorLanguages.ForPath(saved.Path),
            };
            doc.Document.UndoStack.MarkAsOriginalFile();
            doc.Document.Insert(0, backup);
            doc.DetectFormat();
        }
        else return null;
        if (saved.Backup is { } backupId) doc.BackupId = backupId;
        return doc;
    }

    /// <summary>Guarda la sesión ahora. true si se pudo (o si no hay sesión que guardar).</summary>
    public bool SaveSession()
    {
        if (!_sessionOn) return true;
        Remember();
        var backups = new Dictionary<string, string>();
        var docs = new List<EditorSession.Doc>();
        var current = 0;
        foreach (var doc in _docs)
        {
            string? backup = null;
            if (doc.Dirty)
            {
                backups[doc.BackupId] = doc.Document.Text;
                backup = doc.BackupId;
            }
            // Un «Sin título» vacío no se guarda: no hay nada que recuperar.
            if (doc.Path is null && (backup is null || doc.Document.TextLength == 0)) continue;
            if (doc == _current) current = docs.Count;
            docs.Add(new(doc.Path, backup, doc.Language, doc.Group, doc.Caret, doc.Number));
        }
        var groups = _groups.Where(g => _docs.Any(d => d.Group == g.Id)).Select(g => new EditorSession.Group(g.Id, g.Name, g.Color)).ToList();
        var ok = EditorSession.Save(new(docs, groups, current, _zoom, _wrap, _spaces), backups);
        if (ok) _sessionChanged = false;
        return ok;
    }

    void SaveSessionIfChanged()
    {
        if (_sessionOn && _sessionChanged) SaveSession();
    }
}
