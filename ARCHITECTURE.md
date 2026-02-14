# Architecture Guide

This document describes Vidly's internal architecture in detail. It's intended for contributors who want to understand the codebase before making changes.

## Overview

Vidly follows the classic **ASP.NET MVC 5** architectural pattern with a layered design:

```
┌─────────────────────────────────────────────┐
│                   Browser                    │
└──────────────────┬──────────────────────────┘
                   │ HTTP Request
┌──────────────────▼──────────────────────────┐
│              RouteConfig                     │
│   Attribute routes → Convention routes       │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│              Controllers                     │
│   HomeController    MoviesController         │
│                     ↓ IMovieRepository       │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│              Repositories                    │
│   InMemoryMovieRepository                    │
│   (ConcurrentDictionary + lock)              │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│              Models / ViewModels              │
│   Movie  Customer  RandomMovieViewModel      │
└─────────────────────────────────────────────┘
```

## Request Lifecycle

1. **Routing:** `RouteConfig.RegisterRoutes()` registers both attribute-based routes (e.g., `[Route("movies/released/{year}/{month}")]`) and convention-based routes (`{controller}/{action}/{id}`). Attribute routes take precedence.

2. **Controller Selection:** ASP.NET's `DefaultControllerFactory` instantiates the matching controller. `MoviesController` uses a parameterless constructor that creates an `InMemoryMovieRepository` by default.

3. **Action Execution:** The controller action processes the request:
   - **GET actions** retrieve data from the repository and pass it to a Razor view
   - **POST actions** validate `ModelState`, perform mutations, and redirect (PRG pattern)

4. **View Rendering:** Razor views in `/Views/{Controller}/` receive strongly-typed models. The shared `_Layout.cshtml` provides the HTML shell with Bootstrap + Lumen theme.

## Layer Responsibilities

### Controllers (`/Controllers/`)

Controllers are **thin** — they orchestrate but don't contain business logic.

| Controller | Responsibility |
|-----------|---------------|
| `HomeController` | Static pages (Home, About, Contact) |
| `MoviesController` | Movie CRUD, filtering by release date, random movie |

**Key patterns in MoviesController:**
- **Constructor injection**: Accepts `IMovieRepository` for testability
- **Post/Redirect/Get (PRG)**: All POST actions redirect to `Index` on success
- **Anti-forgery**: `[ValidateAntiForgeryToken]` on all POST endpoints
- **Null guards**: Returns `HttpNotFound()` for missing resources

### Models (`/Models/`)

Models are plain C# classes with data annotation attributes for validation.

```csharp
public class Movie
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Movie name is required.")]
    [StringLength(255)]
    public string Name { get; set; }

    [DataType(DataType.Date)]
    public DateTime? ReleaseDate { get; set; }
}
```

**Design decisions:**
- `ReleaseDate` is nullable — movies can exist without a known release date
- `Id` is auto-assigned by the repository, not the caller
- Validation is declarative via attributes, checked by `ModelState.IsValid`

### Repositories (`/Repositories/`)

The repository layer abstracts data access behind interfaces:

```
IRepository<T>           — Generic CRUD (GetAll, GetById, Add, Update, Remove)
    └── IMovieRepository — Movie-specific (GetByReleaseDate, GetRandom)
            └── InMemoryMovieRepository — Thread-safe implementation
```

**Thread safety strategy:**

The `InMemoryMovieRepository` uses a **two-layer concurrency approach**:

1. **Static `List<Movie>`** — Shared across all controller instances within the app domain. Uses `lock(_lock)` for all read and write operations.

2. **Defensive copying** — The `Clone()` method creates copies of `Movie` objects before returning them. This prevents callers from accidentally mutating the repository's internal state.

```csharp
// Every public method locks and clones:
public Movie GetById(int id)
{
    lock (_lock)
    {
        var movie = _movies.SingleOrDefault(m => m.Id == id);
        return movie == null ? null : Clone(movie);
    }
}
```

**Why static storage?** This is a demo app without a database. Static fields survive across HTTP requests within the same IIS Express process, simulating persistence.

### ViewModels (`/ViewModels/`)

`RandomMovieViewModel` composes data from multiple models for the Random view:

```csharp
public class RandomMovieViewModel
{
    public Movie Movie { get; set; }
    public List<Customer> Customers { get; set; }
}
```

This keeps views strongly-typed without needing `ViewBag` for complex pages.

### Views (`/Views/`)

Razor views with the following layout:

| View | Template | Model |
|------|----------|-------|
| `Home/Index` | `_Layout.cshtml` | None |
| `Movies/Index` | `_Layout.cshtml` | `List<Movie>` |
| `Movies/Edit` | `_Layout.cshtml` | `Movie` (shared for create/edit) |
| `Movies/Random` | `_Layout.cshtml` | `RandomMovieViewModel` |
| `Movies/ByReleaseDate` | `_Layout.cshtml` | `List<Movie>` |

**Shared Edit view:** `Create()` action reuses the `Edit.cshtml` view by passing a new `Movie()`. The view handles both cases — if `Id == 0`, it's a create; otherwise, it's an edit.

## Routing

Two routing mechanisms coexist:

### Convention Routes (default)
```
{controller}/{action}/{id}
```
Handles: `/movies`, `/movies/create`, `/movies/edit/1`, `/home/about`

### Attribute Routes (explicit)
```csharp
[Route("movies/released/{year:range(1888,2100)}/{month:regex(\\d{2}):range(1,12)}")]
```
Handles: `/movies/released/2024/01`

**Why both?** Convention routes cover standard CRUD. The `ByReleaseDate` action has a non-standard URL structure with constraints that are cleaner to express as an attribute route.

## Configuration

### Bundling (`App_Start/BundleConfig.cs`)
CSS and JS files are bundled and minified for production. Key bundles:
- `~/bundles/jquery` — jQuery 1.10.2
- `~/bundles/bootstrap` — Bootstrap JS
- `~/Content/css` — Bootstrap CSS + Lumen theme

### Filters (`App_Start/FilterConfig.cs`)
Global `HandleErrorAttribute` for unhandled exception rendering.

### Application Insights (`ApplicationInsights.config`)
Telemetry collection for request tracking, dependency calls, and performance counters. The instrumentation key should be set per environment.

## Testing Architecture

Tests live in `Vidly.Tests/` as a separate project referencing the main `Vidly` project.

```
Vidly.Tests/
├── MovieModelTests.cs              — Model validation
├── CustomerModelTests.cs           — Model validation
├── ViewModelTests.cs               — ViewModel composition
├── MoviesControllerTests.cs        — Controller logic
└── InMemoryMovieRepositoryTests.cs — Repository operations
```

**Testing strategy:**
- **Models:** Validate data annotations using `Validator.TryValidateObject()` with `ValidationContext`
- **Controllers:** Create `MoviesController` with a fresh `InMemoryMovieRepository` per test. Assert on `ActionResult` types and model state.
- **Repository:** Direct unit tests of CRUD operations, thread safety, and edge cases.

All tests use MSTest (`[TestClass]`, `[TestMethod]`). Coverage is collected via Coverlet in Cobertura format.

## Extending Vidly

### Adding a New Entity

1. Create model in `/Models/` with validation attributes
2. Create `IEntityRepository` extending `IRepository<T>`
3. Implement `InMemoryEntityRepository`
4. Create controller with constructor injection
5. Add Razor views
6. Add tests for model, repository, and controller

### Replacing the Data Store

1. Implement `IMovieRepository` backed by EF/Dapper/etc.
2. Register in a DI container (e.g., Unity, Autofac, or Simple Injector)
3. Remove the parameterless constructor from `MoviesController`
4. Update `Global.asax.cs` to configure the DI container

### Adding Authentication

1. Install `Microsoft.AspNet.Identity.Owin`
2. Add `[Authorize]` to controllers/actions
3. Create `AccountController` with login/register
4. Add `_LoginPartial.cshtml` to the layout
