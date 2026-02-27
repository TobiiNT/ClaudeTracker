# ADR 001: MVVM with Dependency Injection

**Status:** Accepted
**Date:** 2025-01-15

## Context

ClaudeTracker is a WPF desktop application that needs a clear separation between UI, business logic, and data access. We needed a pattern that supports testability and maintainability as features grow.

## Decision

Use the MVVM (Model-View-ViewModel) pattern with `CommunityToolkit.Mvvm` for source-generated properties/commands, and `Microsoft.Extensions.DependencyInjection` for service resolution.

- All services are registered as Singletons in `App.xaml.cs`
- ViewModels use `[ObservableProperty]` and `[RelayCommand]` attributes
- Services are accessed through interfaces (e.g., `IClaudeApiService`, `IProfileService`)
- Settings-related ViewModels are registered as Transient (fresh state each time Settings opens)

## Consequences

- **Positive:** Clean testability via interface mocking, consistent property change notifications, reduced boilerplate
- **Positive:** Standard .NET DI container familiar to most C# developers
- **Negative:** CommunityToolkit source generators add build complexity; partial class pattern can confuse newcomers
- **Negative:** Singleton services require thread-safety awareness when accessed from timer callbacks
