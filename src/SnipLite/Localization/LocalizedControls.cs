using System.Runtime.CompilerServices;

namespace SnipLite.Localization;

internal static class LocalizedControls
{
    private sealed class SourceText(string text)
    {
        public string Source = text;
        public string Last = text;
    }
    private static readonly ConditionalWeakTable<object, SourceText> Originals = new();
    private static readonly ConditionalWeakTable<object, SourceText> AccessibleOriginals = new();
    private static string Translate(object owner, string text, string? language, ConditionalWeakTable<object, SourceText>? originals = null)
    {
        var original = (originals ?? Originals).GetValue(owner, _ => new SourceText(text));
        if (original.Last != text) original.Source = text;
        return original.Last = LanguageService.Text(original.Source, language);
    }
    public static void Apply(Control root, string? language = null)
    {
        if (!string.IsNullOrEmpty(root.AccessibleName))
            root.AccessibleName = Translate(root, root.AccessibleName, language, AccessibleOriginals);
        // Editable values, paths, recognition results and hotkey bindings are data.
        if (root is not TextBoxBase and not UpDownBase && !Equals(root.Tag, "localization:native")) root.Text = Translate(root, root.Text, language);
        if (root is ToolStrip strip) foreach (ToolStripItem item in strip.Items) Apply(item, language);
        foreach (Control child in root.Controls) Apply(child, language);
    }
    public static void Apply(ToolStripItem item, string? language = null)
    {
        if (!Equals(item.Tag, "localization:native")) item.Text = Translate(item, item.Text ?? "", language);
        if (item is ToolStripDropDownItem parent) foreach (ToolStripItem child in parent.DropDownItems) Apply(child, language);
    }
}
