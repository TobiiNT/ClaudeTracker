# Contributing to ClaudeTracker

Thanks for your interest in contributing! This guide will help you get started.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10 (build 19041) or later
- An IDE: Visual Studio 2022, VS Code, or JetBrains Rider

### Development Setup

```bash
git clone https://github.com/TobiiNT/ClaudeTracker.git
cd ClaudeTracker
dotnet restore
dotnet build
dotnet test
```

To run in development with simulated data (no real credentials needed):

```bash
dotnet run --project src/ClaudeTracker -- --mock
```

### Project Structure

```
src/ClaudeTracker/
  Models/          Data models (usage, profiles, settings)
  Services/        Business logic with interfaces in Services/Interfaces/
  ViewModels/      MVVM ViewModels (CommunityToolkit.Mvvm)
  Views/           WPF XAML windows and user controls
  TrayIcon/        System tray icon management and SkiaSharp rendering
  Utilities/       Constants, validators, formatters

tests/ClaudeTracker.Tests/   xUnit tests
```

## How to Contribute

### Reporting Bugs

Open a [bug report](https://github.com/TobiiNT/ClaudeTracker/issues/new?template=bug_report.yml) with:

- Steps to reproduce
- Expected vs actual behavior
- Your Windows version and app version

### Suggesting Features

Open a [feature request](https://github.com/TobiiNT/ClaudeTracker/issues/new?template=feature_request.yml) describing the problem you're solving and your proposed solution.

### Contributing Code

1. Fork the repository
2. Create a feature branch: `git checkout -b feat/your-feature`
3. Make your changes
4. Run tests: `dotnet test`
5. Commit with a descriptive message
6. Push and open a pull request

## Code Guidelines

### Architecture

- **MVVM** pattern with **Dependency Injection** (wired in `App.xaml.cs`)
- Services are registered as **Singleton**, settings ViewModels as **Transient**
- Always program to interfaces (`Services/Interfaces/`)
- Constants go in `Utilities/Constants.cs`

### Style

- Follow standard C# naming conventions (PascalCase for public members, _camelCase for private fields)
- Use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm
- Keep settings JSON keys in camelCase
- Add XML doc comments (`/// <summary>`) to public APIs

### Commit Messages

Use clear, descriptive messages:

```
Add multi-profile support for switching between accounts
Fix tray icon flickering on DPI change
Update MaterialDesign to 5.2.0
```

### Branch Naming

- `feat/` — New features
- `fix/` — Bug fixes
- `docs/` — Documentation
- `refactor/` — Code refactoring

## Pull Request Process

1. Ensure `dotnet build --configuration Release` passes with 0 warnings
2. Ensure `dotnet test` passes
3. Update documentation if your change affects user-facing behavior
4. Keep PRs focused — one feature or fix per PR
5. Fill in the PR description explaining what and why

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
