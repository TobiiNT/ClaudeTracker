# ADR 005: Three-Tier Authentication Fallback

**Status:** Accepted
**Date:** 2025-01-15

## Context

Users authenticate with Claude in different ways: some use the web UI (session cookies), some use the Claude CLI (OAuth tokens stored on disk), and some use the Anthropic Console API. We needed a flexible auth strategy that supports all these methods without requiring users to manually configure every credential.

## Decision

Implement a three-tier authentication fallback chain:

1. **Session Key** (manual): User pastes their `sk-ant-*` session cookie from claude.ai DevTools
2. **CLI OAuth** (automatic): Read OAuth tokens from Claude CLI's credential file (`~/.claude/.credentials.json`) or Windows Credential Manager
3. **Credentials File** (automatic): Fall back to stored CLI credentials JSON in the profile

The `ClaudeApiService` tries methods in order and uses the first one that succeeds. Each profile stores its own credential set independently.

## Consequences

- **Positive:** Most users with Claude CLI installed get zero-config setup
- **Positive:** Power users can manually configure session keys for fine-grained control
- **Positive:** Per-profile credentials allow monitoring multiple accounts
- **Negative:** Session keys expire and require periodic re-entry
- **Negative:** CLI credential format may change across Claude CLI versions, requiring maintenance
