using System.Diagnostics;
using System.IO;
using Velopack;

namespace ClaudeTracker;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack MUST run first. It handles installer hooks
        // (--velopack-install/update/uninstall) and exits the process.
        // For normal launches it returns immediately.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback((v) =>
            {
                // Remove hooks from ~/.claude/settings.json before files are deleted
                try
                {
                    var hookBridge = Path.Combine(AppContext.BaseDirectory, "ClaudeTracker.HookBridge.exe");
                    if (File.Exists(hookBridge))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = hookBridge,
                            Arguments = "uninstall",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        })?.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Best-effort — don't block uninstall
                }
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
