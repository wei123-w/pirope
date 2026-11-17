using System.Diagnostics;
using System.Runtime.InteropServices;
using UIA = Interop.UIAutomationClient;

namespace SnipLite;

/// <summary>Queries the OS UIA COM service without loading the WPF automation client.</summary>
internal static class NativeUiAutomation
{
    // Global, not per overlay: slow providers must not accumulate threads across F1/Esc.
    private static readonly SemaphoreSlim Slots = new(2, 2);

    public static Rectangle Bounds(IntPtr handle, Point point, Rectangle window)
    {
        // Capture queries use the MTA thread pool. A direct call from the UI thread
        // must never start a cross-process provider call that could block that UI.
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA || !Slots.Wait(0))
            return Rectangle.Empty;

        var owned = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var release = new List<object>();
        T Keep<T>(T value) where T : class
        {
            if (value is not null && owned.Add(value)) release.Add(value);
            return value!;
        }

        try
        {
            // Generated, versioned Windows COM declarations; no handwritten vtables.
            var automation = Keep((UIA.IUIAutomation2)new UIA.CUIAutomation8());
            automation.ConnectionTimeout = 200;
            automation.TransactionTimeout = 150;
            var root = Keep(automation.ElementFromHandle(handle));
            if (root is null) return Rectangle.Empty;
            var walker = Keep(automation.RawViewWalker);
            var cache = Keep(automation.CreateCacheRequest());
            cache.AddProperty(30001); // UIA_BoundingRectanglePropertyId
            var best = window;
            var timer = Stopwatch.StartNew();
            var budget = 800;
            void Walk(UIA.IUIAutomationElement current, int depth)
            {
                if (depth >= 30 || budget <= 0 || timer.ElapsedMilliseconds >= 350) return;
                for (var child = Keep(walker.GetFirstChildElementBuildCache(current, cache));
                    child is not null && budget-- > 0 && timer.ElapsedMilliseconds < 350;
                    child = Keep(walker.GetNextSiblingElementBuildCache(child, cache)))
                {
                    var rect = child.CachedBoundingRectangle;
                    // Virtual containers may have no own rectangle but contain
                    // visible descendants. Match the previous raw-tree behavior.
                    if (rect.right <= rect.left || rect.bottom <= rect.top)
                    {
                        Walk(child, depth + 1);
                        continue;
                    }
                    var bounds = Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
                    if (!bounds.Contains(point)) continue;
                    var candidate = Rectangle.Intersect(window, bounds);
                    if (candidate.Width > 1 && candidate.Height > 1
                        && (long)candidate.Width * candidate.Height <= (long)best.Width * best.Height)
                        best = candidate;
                    // Descend at once, before enumerating unrelated siblings.
                    Walk(child, depth + 1);
                }
            }
            Walk(root, 0);
            return best;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // Provider unavailable / window closed: Win32 and MSAA remain usable.
            return Rectangle.Empty;
        }
        finally
        {
            // Objects never escape this MTA call. Release each RCW once, including
            // nodes visited before the traversal budget expires.
            for (var i = release.Count - 1; i >= 0; i--)
                if (Marshal.IsComObject(release[i])) Marshal.ReleaseComObject(release[i]);
            Slots.Release();
        }
    }
}
