# Contributing to Vidly

Thanks for considering contributing to Vidly! This guide covers everything you need to set up the project, understand the codebase, write quality code, and submit changes.

## Table of Contents

- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Domain Areas](#domain-areas)
- [Architecture & Patterns](#architecture--patterns)
- [Coding Guidelines](#coding-guidelines)
- [Testing](#testing)
- [CI/CD Pipeline](#cicd-pipeline)
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
- [Docker](https://www.docker.com/) (optional — for containerized builds)
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

### Docker Build

```bash
docker build -t vidly .
```

## Project Structure

Vidly is a large ASP.NET MVC application with **110 controllers**, **111 services**, **95 model files**, and **56 repository classes** organized into clear layers:

```
Vidly/
├── Vidly/                           # Main ASP.NET MVC 5 application
│   ├── Controllers/    (110 files)  # MVC controllers — one per feature area
│   ├── Services/       (111 files)  # Business logic — service layer
│   ├── Models/          (95 files)  # Domain models and DTOs
│   ├── Repositories/    (56 files)  # Data access (interfaces + in-memory implementations)
│   ├── ViewModels/      (65 files)  # View-specific data containers
│   ├── Filters/          (2 files)  # Action filters (SecurityHeaders, RateLimit)
│   ├── Utilities/        (2 files)  # Shared helpers (JsonSerializer, SortHelper)
│   └── App_Start/                   # Startup configuration (routes, bundles, filters)
├── Vidly.Tests/        (111 files)  # MSTest unit tests
├── docs/                            # Documentation site (GitHub Pages)
├── .github/                         # CI/CD, issue templates, Copilot config
│   ├── workflows/                   # CI, CodeQL, Docker, NuGet, Pages, auto-assign, etc.
│   ├── ISSUE_TEMPLATE/              # Bug report, feature request, perf, refactoring, etc.
│   └── PULL_REQUEST_TEMPLATE.md     # PR template
├── ARCHITECTURE.md                  # Detailed architecture guide
├── SECURITY.md                      # Security policy
├── Dockerfile                       # Multi-stage Docker build
└── Vidly.sln                        # Solution file
```

## Domain Areas

The codebase is organized around these functional domains. When contributing, identify which area your change belongs to:

### Core Rental Operations
Controllers/services for the fundamental rental business: `Movies`, `Customers`, `Rentals`, `LateeFees`, `Refunds`, `Reservations`, `Inventory`, `PenaltyWaiver`, `RentalCalendar`, `RentalExtension`, `RentalReturn`, `RentalSwap`, `RentalReceipt`, `RentalInsurance`, `Damage`, `LostAndFound`, `TradeIn`

### Customer Intelligence
Customer-facing analytics and lifecycle: `CustomerHealth`, `CustomerInsights`, `CustomerLifetimeValue`, `CustomerMerge`, `ChurnPredictor`, `CohortSurvival`, `Segmentation`, `TasteDna`, `TasteEvolution`, `HabitCoach`, `WinBack`, `Connections`, `AffinityNetwork`, `CustomerWrapped`

### Revenue & Business Analytics
Financial and operational intelligence: `Dashboard`, `RevenueAlerts`, `RevenueWeather`, `Budget`, `DemandForecast`, `CatalogGap`, `CatalogVelocity`, `StorePulse`, `PricingEngine`, `Pricing`, `RevenueLeakage`, `Strategy`, `ShelfOptimizer`, `InventoryOptimizer`, `Procurement`

### Discovery & Recommendations
Content discovery and curation: `Recommendations`, `Search`, `Mood`, `SeasonalRecommender`, `Playlist`, `Watchlist`, `WatchParty`, `Compare`, `Decade`, `Series`, `Franchise`, `Soundtrack`, `Directors`, `GenreEcosystem`, `StaffPicks`, `MovieCuration`, `MovieInsights`, `MovieSimilarity`

### Engagement & Gamification
Interactive and fun features: `Quiz`, `Trivia`, `Bingo`, `Crossword`, `DrinkingGame`, `EmojiStory`, `MadLibs`, `Roulette`, `Showdown`, `Tournament`, `Challenges`, `Achievements`, `Awards`, `MovieClub`, `MovieNight`, `Marathon`, `Predictions`, `Alphabet`

### Operations & Staff
Store management and staff tools: `StaffSchedule`, `StaffPerformance`, `StoreInfo`, `Announcements`, `ScreeningRoom`, `Autopilot`, `Calendar`, `Export`, `Statement`, `MembershipCard`, `StoreEvents`

### Promotions & Loyalty
Marketing and retention: `Coupons`, `GiftCards`, `GiftRegistry`, `Promotions`, `Loyalty`, `Referrals`, `Subscription`, `Bundles`, `MovieRequests`, `Waitlist`, `SeasonalPromotion`

### Trust & Safety
Fraud prevention and content moderation: `FraudDetector`, `AnomalyWatchdog`, `FrictionDetector`, `ParentalControl`, `Dispute`, `Negotiator`, `Survey`, `Reviews`

### Infrastructure
Cross-cutting concerns: `Notification`, `Activity`, `Timeline`, `Journey`, `Trends`, `Collections`, `Tags`, `Availability`, `SecurityHeadersAttribute`, `RateLimitAttribute`, `IClock`

## Architecture & Patterns

### Layered Architecture

```
Controller → Service → Repository → Static In-Memory Store
```

- **Controllers** handle HTTP, validate input, call services, return views/JSON
- **Services** contain business logic and orchestration
- **Repositories** abstract data access behind interfaces (`IRepository<T>`, plus domain-specific interfaces)
- **Models** are plain C# classes — no ORM, no database annotations

### Key Conventions

- **Repository pattern everywhere.** All data access goes through `IRepository<T>` and domain-specific interfaces (e.g., `IMovieRepository`, `IRentalRepository`)
- **Thread-safe in-memory stores.** Repositories use `Dictionary<int, T>` with explicit `lock (_lock)` and defensive cloning
- **Constructor injection.** Controllers accept repository/service interfaces for testability
- **No external database.** Data is stored in-memory with static collections (designed for demo/learning)
- **One controller, one service.** Each feature area has its own controller backed by a dedicated service class

For more detail, see [ARCHITECTURE.md](ARCHITECTURE.md).

### Adding a New Feature Area

When adding a new feature (e.g., a "Popcorn" feature):

1. **Model:** Create `Models/PopcornModels.cs` with domain classes
2. **Repository interface:** Create `Repositories/IPopcornRepository.cs`
3. **Repository implementation:** Create `Repositories/InMemoryPopcornRepository.cs` (thread-safe with lock + defensive copies)
4. **Service:** Create `Services/PopcornService.cs` with business logic
5. **ViewModel:** Create `ViewModels/PopcornViewModel.cs`
6. **Controller:** Create `Controllers/PopcornController.cs` — inject service/repos via constructor
7. **Tests:** Create `Vidly.Tests/PopcornServiceTests.cs` (and controller tests if applicable)

Follow the naming and structure of existing features. The codebase is consistent — pick any recent feature as a template.

## Coding Guidelines

### Style

- Follow standard [C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful names — no abbreviations unless universally understood (`Id`, `URL`)
- XML doc comments on all public types and members
- `var` for local variables when the type is obvious from the right-hand side
- Braces on their own line (Allman style, consistent with existing code)

### Architecture Rules

- **Never bypass the repository layer.** Controllers and services must not manipulate static collections directly.
- **Always return defensive copies** from repositories to prevent callers from mutating internal state.
- **Lock discipline:** All reads and writes to shared static state must be inside `lock (_lock)`.
- **Null checks:** Validate parameters with `?? throw new ArgumentNullException(nameof(...))` in constructors and public methods.
- **No breaking changes** to core interfaces (`IRepository<T>`, `IMovieRepository`, `ICustomerRepository`, `IRentalRepository`) without discussion in an issue first.
- **Service isolation:** Services should depend on repository interfaces, not on other services directly (prefer controller-level orchestration for cross-domain operations).
- **Name file-local helper types defensively.** `Vidly.Tests` pulls every `.cs` file under `Vidly/` into a single assembly via glob `<Compile Include>` patterns (see `Vidly.Tests/Vidly.Tests.csproj`). That means any nested helper class or enum you declare next to a service or model lives in the same `Vidly.Services` / `Vidly.Models` namespace as every other helper in the test build. A second `GenreCount`, `TrendDirection`, `PlaybookAction`, or `RecommendationType` in a different file will compile fine inside the main MVC project but break the test build with `CS0101`. Prefix file-local helpers with the owning concept (e.g. `WatchlistGenreCount`, `HealthTrendDirection`, `ShelfRecommendationType`) instead of using bare, generic names. If a helper is genuinely shared, promote it to `Vidly/Models/` and reference it from both sides.

### Verifying Your Change Before Pushing

Always run the test-project build before pushing — it is the only build that exercises every `.cs` file under `Vidly/` on the .NET SDK and will catch namespace collisions, missing usings, and broken type references that the Visual-Studio-only main project build can hide:

```bash
dotnet build Vidly.Tests/Vidly.Tests.csproj -c Release
dotnet test  Vidly.Tests/Vidly.Tests.csproj -c Release --no-build
```

The main `Vidly.csproj` requires `Microsoft.WebApplication.targets`, which only ships with a full Visual Studio installation, so CI is currently the canonical builder for the web app itself. Don't rely on "it built in VS" alone.

### Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(loyalty): add tier upgrade notifications
fix(rentals): prevent duplicate rental for same movie
perf(fraud): use Dictionary for O(1) lookups in FraudDetectorService
refactor(pricing): extract late fee calculation to helper
test(churn): add regression tests for ChurnPredictorService
docs: update domain areas in CONTRIBUTING.md
```

Scope should match the domain area (e.g., `rentals`, `loyalty`, `fraud`, `inventory`).

## Testing

### Test Requirements

- **All new features must include tests.** No exceptions.
- **All bug fixes should include a regression test** that fails without the fix.
- Tests use [MSTest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest) (`[TestClass]`, `[TestMethod]`, `Assert.*`)
- Test files go in `Vidly.Tests/` following the convention `<ClassName>Tests.cs`
- Currently **111 test files** — maintain or improve coverage.

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

The in-memory repositories use **static** backing stores. Tests share state across test methods. Always **clean up** anything you add:

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

## CI/CD Pipeline

Vidly has a comprehensive CI/CD setup in `.github/workflows/`:

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.yml` | Push/PR to `master` | Build, test, coverage |
| `codeql.yml` | Push/PR/schedule | Security scanning (CodeQL) |
| `docker.yml` | Push to `master` | Docker image build & push |
| `nuget-publish.yml` | Release published | Publish NuGet package |
| `pages.yml` | Push to `master` | Deploy docs to GitHub Pages |
| `labeler.yml` | PR opened | Auto-label PRs by file path |
| `issue-labeler.yml` | Issue opened | Auto-label issues by content |
| `release-drafter.yml` | PR merged | Draft release notes |
| `stale.yml` | Schedule | Close stale issues/PRs |
| `welcome.yml` | First contribution | Welcome message |
| `pr-size.yml` | PR opened | Label PR size (XS–XXL) |
| `auto-assign.yml` | PR opened | Auto-assign reviewers |
| `triage.yml` | Issue opened | Triage new issues |

All CI checks must pass before merge. Run `dotnet test` locally to catch issues early.

## Submitting Changes

1. Ensure all tests pass locally
2. Keep commits focused — one logical change per commit
3. Push your branch and open a **Pull Request** against `master`
4. Fill in the [PR template](.github/PULL_REQUEST_TEMPLATE.md) with a clear description
5. Link any related issues (e.g., "Closes #12")

### PR Checklist

- [ ] Tests pass (`dotnet test`)
- [ ] New code has test coverage
- [ ] No unrelated changes mixed in
- [ ] Commit messages follow conventional commits (with scope)
- [ ] Documentation updated if behavior changed
- [ ] New feature follows the layered architecture (Controller → Service → Repository)

## Issue Guidelines

### Bug Reports

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.yml). Include:

- Steps to reproduce
- Expected vs actual behavior
- Which domain area is affected
- Environment details (.NET version, OS)

### Feature Requests

Use the [feature request template](.github/ISSUE_TEMPLATE/feature_request.yml). Explain:

- The problem you're trying to solve
- Which domain area it belongs to
- Your proposed solution
- Any alternatives considered

### Specialized Templates

We also have templates for:

- **Performance issues** — `performance_issue.yml`
- **Refactoring proposals** — `refactoring_proposal.yml`
- **Documentation issues** — `documentation_issue.yml`
- **API/Database issues** — `api_database_issue.yml`

## Code Review Process

- PRs require at least one approval before merging
- Reviewers check for:
  - Correctness and edge cases
  - Test coverage (new code must be tested)
  - Adherence to layered architecture
  - Thread safety in repository code
  - Style consistency with existing code
- Be responsive to feedback — we aim to merge quickly
- Squash-merge is preferred for clean history

## Questions?

Open a [discussion](https://github.com/sauravbhattacharya001/Vidly/issues) or reach out in an issue. We're happy to help!
