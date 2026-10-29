namespace Claupet;

/// <summary>Un cuadro chico para escribir una línea (la ciudad del clima), con los colores de la app. null si se cancela.</summary>
static class TextPrompt
{
    public static string? Ask(Theme theme, string title, string label, string? initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            TopMost = true,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96),
            ClientSize = new Size(340, 132),
            BackColor = theme.Background,
            ForeColor = theme.Text,
            Font = new Font("Segoe UI", 9.5f),
        };
        var caption = new Label { Text = label, AutoSize = false, Location = new Point(16, 14), Size = new Size(308, 22), ForeColor = theme.Secondary };
        var box = new TextBox
        {
            Text = initial ?? "",
            Location = new Point(16, 40),
            Size = new Size(308, 26),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = theme.Raised,
            ForeColor = theme.Text,
        };
        Button Make(string text, int x, DialogResult result, bool primary)
        {
            var button = new Button
            {
                Text = text,
                DialogResult = result,
                Location = new Point(x, 86),
                Size = new Size(96, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? theme.Accent : theme.Raised,
                ForeColor = primary ? Color.White : theme.Text,
            };
            button.FlatAppearance.BorderColor = primary ? theme.Accent : theme.Border;
            return button;
        }
        var ok = Make(L.T("Guardar"), 228, DialogResult.OK, primary: true);
        var cancel = Make(L.T("Cancelar"), 124, DialogResult.Cancel, primary: false);
        form.Controls.AddRange([caption, box, cancel, ok]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { box.Focus(); box.SelectAll(); };
        return form.ShowDialog() == DialogResult.OK && box.Text.Trim() is { Length: > 0 } text ? text : null;
    }
}
