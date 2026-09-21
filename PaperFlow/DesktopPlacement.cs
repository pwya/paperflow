using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PaperFlow;

// Keep a regular, interactive WPF window. Never reparent it into Explorer or forward
// mouse input to another process. Only reposition our own window, without activation.
internal sealed class DesktopPlacement : IDisposable
{
    private readonly Window window;
    private readonly IntPtr handle;
    private readonly Action<string> report;
    private readonly DispatcherTimer timer;
    private readonly WinEventCallback foregroundChanged;
    private readonly IntPtr foregroundHook;
    private IntPtr lastForeground;
    private string mode = "window";
    private bool failed;
    private const uint NoMoveSizeActivate = 0x0001 | 0x0002 | 0x0010 | 0x0200;

    public DesktopPlacement(Window window, Action<string> report)
    {
        this.window = window; this.report = report;
        handle = new WindowInteropHelper(window).Handle;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => Refresh();
        foregroundChanged = (_, _, _, _, _, _, _) =>
        {
            if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Send);
        };
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundChanged, 0, 0, 0);
        timer.Start();
    }

    public void Apply(string requested)
    {
        requested = WidgetLayout.NormalizeMode(requested);
        if (mode == requested && !failed) return;
        mode = requested; failed = false; lastForeground = IntPtr.Zero;
        window.Topmost = mode == "topmost";
        // WPF's property may already be false while Show Desktop temporarily raised
        // the native window; explicitly remove that temporary native topmost state.
        if (mode != "topmost") Place(new IntPtr(-2));
        Refresh();
    }

    private void Refresh()
    {
        if (failed || mode != "desktop" || !window.IsVisible) return;
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;
        if (Owns(foreground)) { lastForeground = foreground; return; }
        var name = new StringBuilder(256);
        GetClassName(foreground, name, name.Capacity);
        bool desktop = name.ToString() is "Progman" or "WorkerW";
        bool minimized = IsIconic(handle);
        // Explorer may raise its desktop instead of minimizing tool windows. Check
        // both cases, including a second Show Desktop while Explorer keeps focus.
        bool obscured = desktop && Behind(handle, foreground);
        if (desktop && (minimized || obscured || foreground != lastForeground))
        {
            if (minimized) ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
            // Show Desktop can raise the shell above the entire ordinary-window band.
            // Raise only while the desktop is foreground; demote on the next foreign
            // foreground event, not by repeatedly stealing focus from applications.
            Place(new IntPtr(-1));
        }
        else if (!desktop && foreground != lastForeground && !minimized)
        {
            Place(new IntPtr(1)); // HWND_BOTTOM: ordinary applications cover us
        }
        lastForeground = foreground;
    }

    private bool Owns(IntPtr foreground)
    {
        int remaining = 64;
        for (var cursor = GetAncestor(foreground, 2); cursor != IntPtr.Zero && remaining-- > 0; cursor = GetWindow(cursor, 4))
            if (cursor == handle) return true;
        return false;
    }

    private static bool Behind(IntPtr own, IntPtr other)
    {
        // Bounded walk over top-level windows; no titles or application data are read.
        int remaining = 2048;
        for (IntPtr cursor = GetWindow(own, 3); cursor != IntPtr.Zero && remaining-- > 0; cursor = GetWindow(cursor, 3))
        {
            if (cursor == other) return true;
        }
        return false;
    }

    private void Place(IntPtr after)
    {
        if (SetWindowPos(handle, after, 0, 0, 0, 0, NoMoveSizeActivate)) return;
        failed = true;
        report(new Win32Exception(Marshal.GetLastWin32Error()).Message);
    }

    public void Dispose() { timer.Stop(); if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook); }
    private delegate void WinEventCallback(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
