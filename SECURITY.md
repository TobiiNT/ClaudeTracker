# Security Policy

## Supported Versions

Only the latest release is supported with security updates.

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |
| Older   | No        |

## Reporting a Vulnerability

**Do not open a public issue for security vulnerabilities.**

Instead, please use [GitHub's private security advisory](https://github.com/TobiiNT/ClaudeTracker/security/advisories/new) to report vulnerabilities.

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

### Response Timeline

- **Acknowledgment:** Within 48 hours
- **Assessment:** Within 1 week
- **Fix:** Depending on severity, typically within 2 weeks

## Security Considerations

### Credential Storage

- Session keys are stored in the app's settings file (`%APPDATA%\ClaudeTracker\settings.json`) with standard user-level file permissions
- CLI OAuth tokens are read from Windows Credential Manager (managed by Claude Code)
- No credentials are transmitted to any server other than the official Anthropic API endpoints

### Network Security

- All API requests use HTTPS
- No telemetry or analytics data is collected
- The app only communicates with `claude.ai`, `console.anthropic.com`, `api.anthropic.com`, and `github.com` (for update checks)

### Auto-Update

- Updates are distributed via GitHub Releases
- Velopack verifies package integrity before applying updates

## Best Practices for Users

- Keep your session keys private — do not share them
- Use Claude Code CLI sync when possible (more secure than manual session keys)
- Keep ClaudeTracker updated to the latest version
- Review app permissions if you're concerned about security
