# ADR 003: Percentage-Based Usage Tracking

**Status:** Accepted
**Date:** 2025-01-15

## Context

The Claude AI API does not expose absolute token counts for session or weekly limits. It returns usage as a percentage of an undisclosed limit that may vary by subscription tier and over time. We needed to decide whether to reverse-engineer token counts or work with percentages directly.

## Decision

Track and display usage primarily as percentages. Absolute token estimates are derived only where needed (burn rate calculation) using a configurable `EstimatedSessionTokenLimit` constant (currently 200,000).

- `ClaudeUsage.SessionPercentage` and `WeeklyPercentage` are the source of truth
- Burn rate converts percentage deltas to approximate tokens/min for intuitive display
- The estimated limit is a constant, not user-configurable, to avoid confusion

## Consequences

- **Positive:** Accurately reflects what the API provides; no guesswork about actual limits
- **Positive:** Works correctly even if Anthropic changes the underlying token limits
- **Negative:** Burn rate tokens/min is an approximation; users should not treat it as exact
- **Negative:** Cannot show "X tokens remaining" with precision
