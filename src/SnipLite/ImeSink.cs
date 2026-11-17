using System.Runtime.InteropServices;
using System.Text;

namespace SnipLite;

/// <summary>
/// A 1x1 edit control that exists only to receive keyboard and IME input for the
/// text tool. A normal TextBox has to paint an opaque background, which is what
/// made the old text tool show a dark box over the screenshot. Here the
/// composition string is taken away from the control and handed to the overlay,
/// which draws the glyphs, the caret and the composition inline.
/// </summary>
internal sealed class ImeSink : TextBox
{
    private const int WM_IME_STARTCOMPOSITION = 0x010D;
    private const int WM_IME_ENDCOMPOSITION = 0x010E;
    private const int WM_IME_COMPOSITION = 0x010F;
    private const int WM_IME_SETCONTEXT = 0x0281;

    private const int ISC_SHOWUICOMPOSITIONWINDOW = unchecked((int)0x80000000);

    private const int GCS_COMPSTR = 0x0008;
    private const int GCS_CURSORPOS = 0x0080;
    private const int GCS_RESULTSTR = 0x0800;

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr himc);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    private static extern int ImmGetCompositionStringW(IntPtr himc, int index, byte[]? buffer, int length);

    /// <summary>Text currently being composed by the IME (may be empty).</summary>
    public string Composition { get; private set; } = string.Empty;

    /// <summary>Caret offset inside <see cref="Composition"/>.</summary>
    public int CompositionCaret { get; private set; }

    public event Action? CompositionChanged;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_IME_SETCONTEXT:
                // Keep the IME from painting its own composition window; the
                // overlay renders the string inline at the caret instead.
                m.LParam = (IntPtr)(m.LParam.ToInt64() & ~(long)ISC_SHOWUICOMPOSITIONWINDOW);
                base.WndProc(ref m);
                return;

            case WM_IME_STARTCOMPOSITION:
            case WM_IME_ENDCOMPOSITION:
                SetComposition(string.Empty, 0);
                break;

            case WM_IME_COMPOSITION:
                var flags = (int)m.LParam;

                if ((flags & GCS_RESULTSTR) != 0)
                {
                    // Let the control insert the finished string, then clear.
                    SetComposition(string.Empty, 0);
                    break;
                }

                if ((flags & GCS_COMPSTR) != 0)
                {
                    var (text, caret) = ReadComposition();

                    if (text.Length > 0)
                    {
                        SetComposition(text, caret);
                        return; // swallow: the overlay draws it
                    }

                    SetComposition(string.Empty, 0);
                }

                break;
        }

        base.WndProc(ref m);
    }

    private void SetComposition(string text, int caret)
    {
        if (Composition == text && CompositionCaret == caret) return;

        Composition = text;
        CompositionCaret = caret;
        CompositionChanged?.Invoke();
    }

    private (string Text, int Caret) ReadComposition()
    {
        var himc = ImmGetContext(Handle);
        if (himc == IntPtr.Zero) return (string.Empty, 0);

        try
        {
            var length = ImmGetCompositionStringW(himc, GCS_COMPSTR, null, 0);
            if (length <= 0) return (string.Empty, 0);

            var buffer = new byte[length];
            if (ImmGetCompositionStringW(himc, GCS_COMPSTR, buffer, length) <= 0)
                return (string.Empty, 0);

            var text = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            var caret = ImmGetCompositionStringW(himc, GCS_CURSORPOS, null, 0) & 0xFFFF;

            return (text, caret);
        }
        finally
        {
            ImmReleaseContext(Handle, himc);
        }
    }
}
