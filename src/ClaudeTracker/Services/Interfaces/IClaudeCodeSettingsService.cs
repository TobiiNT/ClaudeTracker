using ClaudeTracker.Models;

namespace ClaudeTracker.Services.Interfaces;

/// <summary>Reads and writes Claude Code's ~/.claude/settings.json permission rules.</summary>
public interface IClaudeCodeSettingsService
{
    /// <summary>Returns all permission rules from Claude Code's settings.</summary>
    List<PermissionRuleEntry> GetPermissions();

    /// <summary>Removes a permission rule by its key from Claude Code's settings.</summary>
    void RemovePermission(string key);
}
