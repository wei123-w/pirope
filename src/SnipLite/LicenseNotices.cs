namespace SnipLite;

internal static class LicenseNotices
{
    public static void Show(IWin32Window owner)
    {
        const string prefix = "SnipLite.Notices.";
        var assembly = typeof(LicenseNotices).Assembly;
        var names = assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix))
            .OrderBy(name => name == prefix + "LICENSE" ? "" : name, StringComparer.Ordinal).ToArray();
        using var window = new Form
        {
            Text = "SnipLite · MIT / Third-party licenses", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(820, 560), MinimumSize = new Size(600, 400), ShowInTaskbar = false
        };
        using var font = new Font("Consolas", 10);
        var text = new TextBox { Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill, Font = font, BackColor = Color.White };
        var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        picker.Items.AddRange(names.Select(name => name[prefix.Length..].Replace('\\', '/')).ToArray());
        picker.SelectedIndexChanged += (_, _) =>
        {
            using var stream = assembly.GetManifestResourceStream(names[picker.SelectedIndex])!;
            using var reader = new StreamReader(stream);
            text.Text = reader.ReadToEnd(); text.Select(0, 0);
        };
        window.Controls.Add(text); window.Controls.Add(picker);
        if (names.Length > 0) picker.SelectedIndex = 0;
        void ApplyTheme() { AppTheme.Apply(window); AppTheme.ApplyWindow(window); }
        ApplyTheme();
        window.HandleCreated += (_, _) => AppTheme.ApplyWindow(window);
        AppTheme.Changed += ApplyTheme;
        try { window.ShowDialog(owner); }
        finally { AppTheme.Changed -= ApplyTheme; }
    }
}
