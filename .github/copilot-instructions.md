# Copilot Instructions — Vidly

## Project Overview

Vidly is an ASP.NET MVC 5 web application for managing a movie catalog. It uses the classic MVC pattern with an in-memory repository for data storage (no database).

## Architecture

```
Controllers/     → Thin controllers handling HTTP requests (HomeController, MoviesController)
Models/          → Plain C# classes with DataAnnotation validation (Movie, Customer)
Views/           → Razor (.cshtml) templates with Bootstrap + Lumen theme
ViewModels/      → Composite models for complex views (RandomMovieViewModel)
Repositories/    → Data access layer behind IRepository<T> / IMovieRepository interfaces
App_Start/       → Route config, bundles, filters
```

### Key Design Patterns

- **Repository pattern** with interface injection (`IMovieRepository`) for testability
- **Post/Redirect/Get (PRG)** on all form submissions
- **Anti-forgery tokens** (`[ValidateAntiForgeryToken]`) on all POST endpoints
- **Defensive cloning** in repository — returned objects are copies, not references
- **Thread-safe storage** — `InMemoryMovieRepository` uses `lock(_lock)` on a static `List<Movie>`

### Important: Two Project Styles

- **Main project** (`Vidly/Vidly.csproj`): Old-style .NET Framework 4.5.2 project (MSBuild XML, `packages.config`, NuGet packages in `packages/` folder). **Cannot be built directly with `dotnet build`**.
- **Test project** (`Vidly.Tests/Vidly.Tests.csproj`): SDK-style project targeting `net472`. Compiles source files from the main project directly via `<Compile Include="..">` links. This is the buildable entry point.

## How to Build

```powershell
# Only the test project is buildable via dotnet CLI
dotnet restore Vidly.Tests/Vidly.Tests.csproj
dotnet build Vidly.Tests/Vidly.Tests.csproj --configuration Release --no-restore
```

## How to Test

```powershell
dotnet test Vidly.Tests/Vidly.Tests.csproj --configuration Release --no-build
```

Tests use **MSTest** (`[TestClass]`, `[TestMethod]`). Coverage collected via Coverlet (Cobertura format).

### Test Files

| File | What it tests |
|------|---------------|
| `MovieModelTests.cs` | `Movie` model validation via `DataAnnotations` |
| `CustomerModelTests.cs` | `Customer` model validation |
| `ViewModelTests.cs` | `RandomMovieViewModel` composition |
| `MoviesControllerTests.cs` | Controller actions, routing, model state |
| `InMemoryMovieRepositoryTests.cs` | CRUD operations, thread safety, edge cases |

## Conventions

- **Controllers** should remain thin — no business logic, only orchestration
- All POST actions must have `[ValidateAntiForgeryToken]`
- Null checks: return `HttpNotFoundResult` for missing resources
- Model validation via `DataAnnotations` attributes, not manual checks
- Repository methods must be thread-safe (use `lock`)
- New entities should follow the existing pattern: Model → IRepository → Repository → Controller → Views → Tests

## Adding a New Feature

1. Add model in `Models/` with `[Required]`, `[StringLength]`, etc.
2. Create or extend repository interface in `Repositories/`
3. Implement the repository (thread-safe, with `Clone()`)
4. Add controller actions with constructor injection
5. Create Razor views
6. **Write tests** for model, repository, and controller

## CI/CD

- **CI:** `.github/workflows/ci.yml` — builds and tests on push/PR to master
- **Pages:** `.github/workflows/pages.yml` — deploys docs to GitHub Pages
- **Labeler:** `.github/workflows/labeler.yml` — auto-labels PRs by changed files
- **Stale:** `.github/workflows/stale.yml` — marks stale issues after 60 days

## Common Pitfalls

- Don't try to `dotnet build Vidly/Vidly.csproj` — it's old-style and won't work
- The test project compiles main source via linked files, so changes to `Vidly/` source are immediately reflected in tests
- `InMemoryMovieRepository` uses a **static** list — all tests share state unless you create a fresh instance
- Route attribute constraints use regex: `{month:regex(\\d{2}):range(1,12)}` — escape backslashes
