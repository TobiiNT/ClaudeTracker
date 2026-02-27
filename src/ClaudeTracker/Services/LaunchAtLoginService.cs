using Microsoft.Win32;

namespace ClaudeTracker.Services;

public class LaunchAtLoginService
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ClaudeTracker";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            return key?.GetValue(AppName) != null;
        }
        set
        {
            if (value)
                Enable();
            else
                Disable();
        }
    }

    private void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            key?.SetValue(AppName, $"\"{exePath}\"");
            LoggingService.Instance.Log("Launch at login enabled");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to enable launch at login", ex);
        }
    }

    private void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            LoggingService.Instance.Log("Launch at login disabled");
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Failed to disable launch at login", ex);
        }
    }
}
