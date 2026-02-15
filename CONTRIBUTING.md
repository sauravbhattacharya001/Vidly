# Contributing to Vidly

Thanks for considering contributing to Vidly! This guide explains how to set up the project, write quality code, and submit changes.

## Table of Contents

- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Coding Guidelines](#coding-guidelines)
- [Testing](#testing)
- [Submitting Changes](#submitting-changes)
- [Issue Guidelines](#issue-guidelines)
- [Code Review Process](#code-review-process)

## Getting Started

1. **Fork** the repository on GitHub
2. **Clone** your fork locally:
   ```bash
   git clone https://github.com/<your-username>/Vidly.git
   cd Vidly
   ```
3. Create a **feature branch** from `master`:
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Development Setup

### Prerequisites

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0) (for building and running tests)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/) with the C# extension
- Git

### Building

The main project (`Vidly/`) targets .NET Framework 4.7.2 (ASP.NET MVC 5). The test project (`Vidly.Tests/`) is an SDK-style project that compiles source files from the main project directly:

```bash
# Restore and build the test project (includes all source)
dotnet restore Vidly.Tests/Vidly.Tests.csproj
dotnet build Vidly.Tests/Vidly.Tests.csproj --configuration Release
```

### Running Tests

```bash
dotnet test Vidly.Tests/Vidly.Tests.csproj --configuration Release
```

With coverage:

```bash
dotnet test Vidly.Tests/Vidly.Tests.csproj \
  --configuration Release \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults
```

## Project Structure

```
Vidly/
├── Vidly/                          # Main ASP.NET MVC application
│   ├── Controllers/                # MVC controllers (Home, Movies, Customers, Rentals)
│   ├── Models/                     # Domain models (Movie, Customer, Rental + enums)
│   ├── ViewModels/                 # View-specific data containers
│   ├── Repositories/               # Data access layer (interfaces + in-memory implementations)
│   ├── Filters/                    # Action filters (SecurityHeadersAttribute)
│   └── App_Start/                  # Startup configuration (routes, bundles, filters)
├── Vidly.Tests/                    # MSTest unit tests
├── docs/                           # Documentation site
├── .github/                        # CI/CD workflows, issue templates, Copilot config
├── ARCHITECTURE.md                 # Detailed architecture guide
└── SECURITY.md                     # Security policy
```

### Key Architectural Decisions

- **Repository pattern**: All data access goes through `IRepository<T>` and domain-specific interfaces (`IMovieRepository`, `ICustomerRepository`, `IRentalRepository`)
- **Thread-safe in-memory stores**: Repositories use `Dictionary<int, T>` with explicit locking and defensive cloning
- **Constructor injection**: Controllers accept repository interfaces for testability
- **No external database**: Data is stored in-memory with static collections (designed for demo/learning purposes)

For a deeper dive, see [ARCHITECTURE.md](ARCHITECTURE.md).

## Coding Guidelines

### Style

- Follow standard [C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful names — no abbreviations unless universally understood (`Id`, `URL`)
- XML doc comments on all public types and members
- `var` for local variables when the type is obvious from the right-hand side
- Braces on their own line (Allman style, consistent with existing code)

### Architecture Rules

- **Never bypass the repository layer.** Controllers should not manipulate static collections directly.
- **Always return defensive copies** from repositories to prevent callers from mutating internal state.
- **Lock discipline:** All reads and writes to shared static state must be inside `lock (_lock)`.
- **Null checks:** Validate parameters with `?? throw new ArgumentNullException(nameof(...))` in constructors and public methods.
- **No breaking changes** to `IRepository<T>`, `IMovieRepository`, `ICustomerRepository`, or `IRentalRepository` without discussion in an issue first.

### Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add customer search by phone number
fix: prevent duplicate rental for same movie
perf: use Dictionary for O(1) lookups
refactor: extract late fee calculation to helper
test: add concurrency tests for Checkout
docs: update API examples in README
```

## Testing

### Test Requirements

- **All new features must include tests.** No exceptions.
- **All bug fixes should include a regression test** that fails without the fix.
- Tests use [MSTest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest) (`[TestClass]`, `[TestMethod]`, `Assert.*`)
- Test files go in `Vidly.Tests/` and follow the naming convention `<ClassName>Tests.cs`

### Test Patterns

```csharp
[TestMethod]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    var repo = new InMemoryMovieRepository();

    // Act
    var result = repo.GetById(1);

    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(1, result.Id);
}
```

### Important: Shared Static State

The in-memory repositories use **static** backing stores. Tests share this state across test methods. Always **clean up** anything you add:

```csharp
repo.Add(entity);
var id = entity.Id;
try
{
    // ... your assertions ...
}
finally
{
    repo.Remove(id);
}
```

### Running CI Locally

The same checks that run in GitHub Actions CI:

```bash
dotnet restore Vidly.Tests/Vidly.Tests.csproj
dotnet build Vidly.Tests/Vidly.Tests.csproj --configuration Release --no-restore
dotnet test Vidly.Tests/Vidly.Tests.csproj --configuration Release --no-build
```

## Submitting Changes

1. Ensure all tests pass locally
2. Keep commits focused — one logical change per commit
3. Push your branch and open a **Pull Request** against `master`
4. Fill in the PR template with a clear description of what and why
5. Link any related issues (e.g., "Closes #12")

### PR Checklist

- [ ] Tests pass (`dotnet test`)
- [ ] New code has test coverage
- [ ] No unrelated changes mixed in
- [ ] Commit messages follow conventional commits
- [ ] Documentation updated if behavior changed

## Issue Guidelines

### Bug Reports

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.yml). Include:

- Steps to reproduce
- Expected vs actual behavior
- Environment details (.NET version, OS)

### Feature Requests

Use the [feature request template](.github/ISSUE_TEMPLATE/feature_request.yml). Explain:

- The problem you're trying to solve
- Your proposed solution
- Any alternatives you've considered

## Code Review Process

- PRs require at least one approval before merging
- Reviewers check for correctness, test coverage, style consistency, and architecture alignment
- Be responsive to feedback — we aim to merge quickly
- Squash-merge is preferred for clean history

## Questions?

Open a [discussion](https://github.com/sauravbhattacharya001/Vidly/issues) or reach out in an issue. We're happy to help!
