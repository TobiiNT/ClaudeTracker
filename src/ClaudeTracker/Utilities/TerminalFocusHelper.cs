using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeTracker.Utilities;

/// <summary>
/// Finds and brings to foreground the terminal window matching a project directory.
/// Used by permission popup "Terminal" button and notification click handlers.
/// </summary>
public static class TerminalFocusHelper
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "cmd", "powershell", "pwsh",
        "Code",       // VS Code
        "Hyper", "Alacritty", "wezterm-gui", "ConEmu", "ConEmu64"
    };

    /// <summary>Bring a window to front by its handle (from HookBridge's GetConsoleWindow).</summary>
    public static bool BringToFront(long windowHandle)
    {
        var hWnd = new IntPtr(windowHandle);
        if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd)) return false;

        if (IsIconic(hWnd))
            ShowWindow(hWnd, 9); // SW_RESTORE
        return SetForegroundWindow(hWnd);
    }

    /// <summary>Bring a terminal window to front by matching cwd/project name in the title.</summary>
    public static bool BringToFront(string cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return false;

        var projectName = System.IO.Path.GetFileName(cwd) ?? cwd;
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                if (!TerminalProcesses.Contains(proc.ProcessName))
                    return true;
            }
            catch { return true; }

            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (title.Contains(cwd, StringComparison.OrdinalIgnoreCase) ||
                title.Contains(projectName, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            if (IsIconic(found))
                ShowWindow(found, 9); // SW_RESTORE
            SetForegroundWindow(found);
            return true;
        }

        return false;
    }
}
