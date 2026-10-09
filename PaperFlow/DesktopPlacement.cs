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
    private const int ExtendedStyle = -20, TopmostStyle = 0x00000008;

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
        var name = new StringBuilder(256);
        GetClassName(foreground, name, name.Capacity);
        bool desktop = name.ToString() is "Progman" or "WorkerW";
        bool minimized = IsIconic(handle);
        if (desktop)
        {
            if (minimized) ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
            bool shellTopmost = IsTopmost(foreground);
            if (!shellTopmost && IsTopmost(handle)) Place(new IntPtr(-2));
            // Stay immediately above the shell, below windows already above it.
            // Show Desktop may raise either a regular or a topmost shell window;
            // focusing an exposed desktop beside an app must not raise us over it.
            IntPtr preceding = GetWindow(foreground, 3); // GW_HWNDPREV
            if (preceding != handle)
            {
                if (shellTopmost) Place(preceding == IntPtr.Zero ? new IntPtr(-1) : preceding);
                else Place(preceding == IntPtr.Zero || IsTopmost(preceding) ? IntPtr.Zero : preceding);
            }
        }
        // Activating our widget or an owned dialog can also raise its native window.
        // Lower the widget without changing focus or the dialog's own window order.
        else if (!minimized && (foreground != lastForeground || IsTopmost(handle)))
        {
            Place(new IntPtr(1)); // HWND_BOTTOM: ordinary applications cover us
        }
        lastForeground = foreground;
    }

    private static bool IsTopmost(IntPtr window) => (GetWindowLong(window, ExtendedStyle) & TopmostStyle) != 0;

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
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
