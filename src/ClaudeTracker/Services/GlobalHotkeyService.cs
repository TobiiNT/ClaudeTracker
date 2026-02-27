using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ClaudeTracker.Services;

public class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_C = 0x43;

    private HwndSource? _hwndSource;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public void Register()
    {
        if (_registered) return;

        // Create a hidden message-only window to receive WM_HOTKEY
        var parameters = new HwndSourceParameters("ClaudeTrackerHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0x800000 // WS_POPUP (not visible)
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        _registered = RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_C);

        if (!_registered)
        {
            LoggingService.Instance.Log("Failed to register global hotkey Ctrl+Shift+C (may already be in use)");
        }
        else
        {
            LoggingService.Instance.Log("Global hotkey Ctrl+Shift+C registered");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered && _hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _registered = false;
        }

        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _hwndSource = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
