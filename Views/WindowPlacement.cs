using System.Text.Json;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace FlatOut4SaveEditor.Views;

// Where the window was when it last closed, kept in %LocalAppData%\FlatOut4SaveEditor\window.json.
// It is put back only while its title bar still lands on a connected display, so a monitor that
// has gone away can never make the editor open somewhere the user cannot see it.
public static class WindowPlacement
{
    // Physical pixels of title bar that must be on a display's work area to restore there.
    private const int MinVisibleWidth = 160;
    private const int MinVisibleHeight = 24;
    private const int TitleBarHeight = 40;

    private static RectInt32? lastRestored;

    // Snapshot runs point this at a scratch file (or leave placement alone entirely).
    public static string? StorePathOverride { get; set; }

    private static string StorePath => StorePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlatOut4SaveEditor", "window.json");

    // Tracks the last normal (not maximized or minimized) bounds, which is what a maximized
    // window should come back to when the user restores it next time.
    public static void Track(AppWindow window)
    {
        Remember(window);
        window.Changed += (sender, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            {
                Remember(sender);
            }
        };
    }

    public static bool TryRestore(AppWindow window)
    {
        if (!TryLoad(out RectInt32 bounds, out bool maximized) || !IsOnADisplay(bounds))
        {
            return false;
        }

        window.MoveAndResize(bounds);
        lastRestored = bounds;
        if (maximized && window.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        return true;
    }

    // The saved bounds, if there is a plausible saved placement (it may be off screen).
    public static bool TryLoad(out RectInt32 bounds, out bool maximized)
    {
        bounds = default;
        maximized = false;
        Placement? saved;
        try
        {
            saved = File.Exists(StorePath) ? JsonSerializer.Deserialize<Placement>(File.ReadAllText(StorePath)) : null;
        }
        catch
        {
            return false;
        }

        if (saved is null || saved.Width < 400 || saved.Height < 300)
        {
            return false;
        }

        bounds = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
        maximized = saved.Maximized;
        return true;
    }

    public static void Save(AppWindow window)
    {
        if (window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } && lastRestored is null)
        {
            return;
        }

        RectInt32 bounds = lastRestored ?? new RectInt32(window.Position.X, window.Position.Y, window.Size.Width, window.Size.Height);
        bool maximized = window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(new Placement(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized)));
        }
        catch
        {
            // A read-only profile folder only costs the remembered position.
        }
    }

    // True when enough of the title bar would be on some display's work area to grab it.
    public static bool IsOnADisplay(RectInt32 bounds)
    {
        var titleBar = new RectInt32(bounds.X, bounds.Y, bounds.Width, TitleBarHeight);
        DisplayArea? area = DisplayArea.GetFromRect(titleBar, DisplayAreaFallback.None);
        if (area is null)
        {
            return false;
        }

        RectInt32 work = area.WorkArea;
        int visibleWidth = Math.Min(titleBar.X + titleBar.Width, work.X + work.Width) - Math.Max(titleBar.X, work.X);
        int visibleHeight = Math.Min(titleBar.Y + titleBar.Height, work.Y + work.Height) - Math.Max(titleBar.Y, work.Y);
        return visibleWidth >= MinVisibleWidth && visibleHeight >= MinVisibleHeight;
    }

    private static void Remember(AppWindow window)
    {
        if (window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            lastRestored = new RectInt32(window.Position.X, window.Position.Y, window.Size.Width, window.Size.Height);
        }
    }

    private sealed record Placement(int X, int Y, int Width, int Height, bool Maximized);
}
