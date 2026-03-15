using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeTracker.HookBridge;

internal static class Program
{
    // --- Constants ---
    private const int ConnectionTimeoutMs = 3000;
    private const int ResponseTimeoutMs = 310_000;

    private static string PipeName => $"ClaudeTracker-Hooks-{Environment.UserName}";

    private static string ClaudeSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    private static readonly string[] AllEvents =
    {
        "PreToolUse", "PostToolUse", "PostToolUseFailure",
        "PermissionRequest", "Notification", "Stop",
        "SessionStart", "SessionEnd", "UserPromptSubmit",
        "SubagentStart", "SubagentStop",
        "PreCompact", "PostCompact",
        "WorktreeCreate", "WorktreeRemove",
        "InstructionsLoaded", "ConfigChange",
        "Elicitation", "ElicitationResult",
        "TeammateIdle", "TaskCompleted"
    };

    private static readonly HashSet<string> AsyncEvents = new()
    {
        "PostToolUse", "PostToolUseFailure",
        "SessionStart", "SessionEnd",
        "SubagentStart", "InstructionsLoaded",
        "PreCompact", "PostCompact",
        "WorktreeRemove", "ElicitationResult"
    };

    // --- Entry Point ---
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "install":
                        return Install();
                    case "uninstall":
                        return Uninstall();
                    case "status":
                        return Status();
                    case "help":
                    case "--help":
                    case "-h":
                        ShowHelp();
                        return 0;
                }
            }

            return await HandleHookEvent();
        }
        catch
        {
            // Exit 0 on any error so Claude Code falls back gracefully
            return 0;
        }
    }

    // --- Hook Event Relay ---
    private static async Task<int> HandleHookEvent()
    {
        // 1. Read all stdin
        var rawInput = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(rawInput))
            return 0;

        // 2. Parse JSON, extract hook_event_name — this is ALL we parse (generic relay)
        string eventName;
        try
        {
            using var doc = JsonDocument.Parse(rawInput);
            eventName = doc.RootElement.GetProperty("hook_event_name").GetString() ?? "";
        }
        catch
        {
            return 0;
        }

        if (string.IsNullOrEmpty(eventName))
            return 0;

        // 3. Build IPC envelope
        var envelope = new JsonObject
        {
            ["requestId"] = Guid.NewGuid().ToString(),
            ["eventName"] = eventName,
            ["payload"] = rawInput,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        var envelopeJson = envelope.ToJsonString();
        var envelopeBytes = Encoding.UTF8.GetBytes(envelopeJson);

        // 4. Connect to named pipe
        using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipeClient.ConnectAsync(ConnectionTimeoutMs);
        }
        catch (TimeoutException)
        {
            // ClaudeTracker not running — exit 0 silently
            return 0;
        }

        // 5. Send 4-byte length-prefixed UTF8 message
        var lengthPrefix = BitConverter.GetBytes(envelopeBytes.Length); // Little-endian
        await pipeClient.WriteAsync(lengthPrefix, 0, 4);
        await pipeClient.WriteAsync(envelopeBytes, 0, envelopeBytes.Length);
        await pipeClient.FlushAsync();

        // 6. Read 4-byte length-prefixed response (with ResponseTimeoutMs CTS)
        using var cts = new CancellationTokenSource(ResponseTimeoutMs);

        var responseLengthBytes = await ReadExactAsync(pipeClient, 4, cts.Token);
        if (responseLengthBytes == null)
            return 0;

        var responseLength = BitConverter.ToInt32(responseLengthBytes, 0);
        if (responseLength <= 0 || responseLength > 5 * 1024 * 1024) // 5 MB max
            return 0;

        var responseBytes = await ReadExactAsync(pipeClient, responseLength, cts.Token);
        if (responseBytes == null)
            return 0;

        var responseJson = Encoding.UTF8.GetString(responseBytes);

        // 7. Extract jsonOutput from response, write to stdout if non-null
        try
        {
            using var responseDoc = JsonDocument.Parse(responseJson);
            if (responseDoc.RootElement.TryGetProperty("jsonOutput", out var jsonOutput) &&
                jsonOutput.ValueKind != JsonValueKind.Null)
            {
                Console.Write(jsonOutput.GetRawText());
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return 0;
    }

    // --- Install ---
    private static int Install()
    {
        try
        {
            var rawPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(rawPath))
            {
                Console.Error.WriteLine("Error: Could not determine executable path.");
                return 1;
            }

            // Use forward slashes — Claude Code runs hooks via bash, backslashes get eaten
            var exePath = rawPath.Replace('\\', '/');

            var settingsPath = ClaudeSettingsPath;
            var settingsDir = Path.GetDirectoryName(settingsPath)!;
            Directory.CreateDirectory(settingsDir);

            // Read existing settings or create new
            JsonObject settings;
            if (File.Exists(settingsPath))
            {
                var existingJson = File.ReadAllText(settingsPath);
                settings = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
            }
            else
            {
                settings = new JsonObject();
            }

            // Ensure hooks object exists
            if (settings["hooks"] is not JsonObject hooksObj)
            {
                hooksObj = new JsonObject();
                settings["hooks"] = hooksObj;
            }

            // Register each event
            foreach (var eventName in AllEvents)
            {
                var hookConfig = new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = exePath
                };

                // Add async flag for async events
                if (AsyncEvents.Contains(eventName))
                {
                    hookConfig["async"] = true;
                }

                // Special config for SessionEnd
                if (eventName == "SessionEnd")
                {
                    hookConfig["timeout"] = 2;
                }

                // Build the hook entry
                var hookEntry = new JsonObject
                {
                    ["hooks"] = new JsonArray { hookConfig }
                };

                // Add matcher for SessionStart
                if (eventName == "SessionStart")
                {
                    hookEntry["matcher"] = "startup|resume";
                }

                // Wrap in array
                var hookArray = new JsonArray { hookEntry };

                hooksObj[eventName] = hookArray;
            }

            // Write settings with indentation
            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var outputJson = settings.ToJsonString(writeOptions);
            File.WriteAllText(settingsPath, outputJson);

            Console.WriteLine($"ClaudeTracker hooks installed successfully.");
            Console.WriteLine($"  Settings: {settingsPath}");
            Console.WriteLine($"  Bridge:   {exePath}");
            Console.WriteLine($"  Events:   {AllEvents.Length} registered");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error installing hooks: {ex.Message}");
            return 1;
        }
    }

    // --- Uninstall ---
    private static int Uninstall()
    {
        try
        {
            var settingsPath = ClaudeSettingsPath;
            if (!File.Exists(settingsPath))
            {
                Console.WriteLine("No Claude settings file found. Nothing to uninstall.");
                return 0;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonNode.Parse(json)?.AsObject();
            if (settings == null)
            {
                Console.WriteLine("Could not parse settings file.");
                return 1;
            }

            if (settings["hooks"] is not JsonObject hooksObj)
            {
                Console.WriteLine("No hooks found in settings. Nothing to uninstall.");
                return 0;
            }

            // Remove only entries whose JSON contains "ClaudeTracker.HookBridge"
            var keysToRemove = new List<string>();
            foreach (var kvp in hooksObj)
            {
                var hookJson = kvp.Value?.ToJsonString() ?? "";
                if (hookJson.Contains("ClaudeTracker.HookBridge", StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                hooksObj.Remove(key);
            }

            // If hooks object is empty, remove it entirely
            if (hooksObj.Count == 0)
            {
                settings.Remove("hooks");
            }

            // Write back
            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var outputJson = settings.ToJsonString(writeOptions);
            File.WriteAllText(settingsPath, outputJson);

            Console.WriteLine($"ClaudeTracker hooks uninstalled successfully. Removed {keysToRemove.Count} event(s).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error uninstalling hooks: {ex.Message}");
            return 1;
        }
    }

    // --- Status ---
    private static int Status()
    {
        try
        {
            // Check if hooks are installed
            var settingsPath = ClaudeSettingsPath;
            var installed = false;
            var installedCount = 0;

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                if (json.Contains("ClaudeTracker.HookBridge", StringComparison.OrdinalIgnoreCase))
                {
                    installed = true;
                    // Count registered events
                    try
                    {
                        var settings = JsonNode.Parse(json)?.AsObject();
                        if (settings?["hooks"] is JsonObject hooksObj)
                        {
                            foreach (var kvp in hooksObj)
                            {
                                var hookJson = kvp.Value?.ToJsonString() ?? "";
                                if (hookJson.Contains("ClaudeTracker.HookBridge", StringComparison.OrdinalIgnoreCase))
                                {
                                    installedCount++;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore parse errors for counting
                    }
                }
            }

            Console.WriteLine($"Hooks installed: {(installed ? $"Yes ({installedCount} events)" : "No")}");

            // Check if ClaudeTracker is running by trying to connect to the pipe
            var trackerRunning = false;
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                pipeClient.ConnectAsync(1000).Wait();
                trackerRunning = true;
            }
            catch
            {
                // Connection failed — not running
            }

            Console.WriteLine($"ClaudeTracker running: {(trackerRunning ? "Yes" : "No")}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error checking status: {ex.Message}");
            return 1;
        }
    }

    // --- Help ---
    private static void ShowHelp()
    {
        Console.WriteLine("ClaudeTracker.HookBridge — Named pipe relay for Claude Code hooks");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ClaudeTracker.HookBridge              Read hook event from stdin, relay to ClaudeTracker");
        Console.WriteLine("  ClaudeTracker.HookBridge install      Register hooks in ~/.claude/settings.json");
        Console.WriteLine("  ClaudeTracker.HookBridge uninstall    Remove ClaudeTracker hooks from settings");
        Console.WriteLine("  ClaudeTracker.HookBridge status       Check installation and connection status");
        Console.WriteLine("  ClaudeTracker.HookBridge help         Show this help message");
    }

    // --- Helpers ---
    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from <paramref name="stream"/>.
    /// Returns null if the stream ends before all bytes are read.
    /// </summary>
    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var bytesRead = await stream.ReadAsync(buffer, offset, count - offset, ct);
            if (bytesRead == 0)
                return null; // Stream ended prematurely

            offset += bytesRead;
        }

        return buffer;
    }
}
