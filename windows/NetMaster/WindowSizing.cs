using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace NetMaster;

/// <summary>Use a comfortable startup size while allowing smaller windows.</summary>
internal sealed class WindowSizing
{
    private const uint GetMinMaxInfo = 0x0024;
    private readonly nint hwnd;
    private readonly int width;
    private readonly int height;
    private readonly SubclassProc callback;

    private WindowSizing(Window window, int defaultWidth, int defaultHeight, int minWidth, int minHeight)
    {
        width = minWidth;
        height = minHeight;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        callback = WindowProc;
        if (!SetWindowSubclass(hwnd, callback, 1, 0))
            throw new InvalidOperationException("无法设置窗口最小尺寸。");
        window.Closed += (_, _) => RemoveWindowSubclass(hwnd, callback, 1);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(window.AppWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        window.AppWindow.Resize(new SizeInt32(
            Math.Min((int)Math.Ceiling(defaultWidth * scale), workArea.Width),
            Math.Min((int)Math.Ceiling(defaultHeight * scale), workArea.Height)));
    }

    public static void Attach(Window window, int defaultWidth, int defaultHeight, int minWidth, int minHeight) =>
        _ = new WindowSizing(window, defaultWidth, defaultHeight, minWidth, minHeight);

    private nint WindowProc(nint handle, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        nint result = DefSubclassProc(handle, message, wParam, lParam);
        if (message == GetMinMaxInfo)
        {
            var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            double scale = GetDpiForWindow(handle) / 96.0;
            bounds.MinTrackSize.X = Math.Max(bounds.MinTrackSize.X, (int)Math.Ceiling(width * scale));
            bounds.MinTrackSize.Y = Math.Max(bounds.MinTrackSize.Y, (int)Math.Ceiling(height * scale));
            Marshal.StructureToPtr(bounds, lParam, false);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
