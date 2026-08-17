using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PressHistory.Services;

public sealed class ClipboardMonitor : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x5048;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyH = 0x48;

    private HwndSource? _source;
    private nint _windowHandle;
    private bool _listenerRegistered;
    private bool _hotkeyRegistered;

    public event EventHandler<uint>? ClipboardUpdated;

    public event EventHandler? ShowRequested;

    public bool IsHotkeyRegistered => _hotkeyRegistered;

    public static uint CurrentSequenceNumber => GetClipboardSequenceNumber();

    public void Initialize(nint windowHandle)
    {
        if (_source is not null)
        {
            throw new InvalidOperationException("Le moniteur du presse-papiers est déjà initialisé.");
        }

        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle)
                  ?? throw new InvalidOperationException("La fenêtre Windows n’est pas encore disponible.");
        _source.AddHook(WindowProcedure);

        if (!AddClipboardFormatListener(windowHandle))
        {
            _source.RemoveHook(WindowProcedure);
            _source = null;
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Impossible d’écouter les changements du presse-papiers.");
        }

        _listenerRegistered = true;
        _hotkeyRegistered = RegisterHotKey(
            windowHandle,
            HotkeyId,
            ModControl | ModAlt | ModNoRepeat,
            VirtualKeyH);
    }

    public void Dispose()
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _hotkeyRegistered = false;
        }

        if (_listenerRegistered)
        {
            RemoveClipboardFormatListener(_windowHandle);
            _listenerRegistered = false;
        }

        if (_source is not null)
        {
            _source.RemoveHook(WindowProcedure);
            _source = null;
        }

        _windowHandle = 0;
    }

    private nint WindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmClipboardUpdate)
        {
            ClipboardUpdated?.Invoke(this, CurrentSequenceNumber);
        }
        else if (message == WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
