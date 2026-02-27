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
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
