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
│              Controllers (15)                │
│   MoviesController    RentalsController      │
│   CustomersController WatchlistController    │
│   CollectionsController ReviewsController    │
│   DashboardController ActivityController     │
│   RecommendationsController HomeController   │
│   NotificationsController ExportController   │
│   BundlesController   CouponsController      │
│   GiftCardsController                        │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│              Services (32)                   │
│  ┌─ Core ─────────────────────────────────┐ │
│  │ DashboardService  RentalHistoryService │ │
│  │ MovieInsightsService  ReviewService    │ │
│  │ CollectionService  PricingService      │ │
│  │ InventoryService  LoyaltyPointsService │ │
│  │ NotificationService  WatchlistService  │ │
│  └────────────────────────────────────────┘ │
│  ┌─ Analytics/ML ─────────────────────────┐ │
│  │ MovieSimilarityService                 │ │
│  │ RecommendationService                  │ │
│  │ CustomerActivityService                │ │
│  │ CustomerSegmentationService            │ │
│  │ ChurnPredictorService                  │ │
│  │ RatingEngineService                    │ │
│  │ RevenueAnalyticsService                │ │
│  │ RentalForecastService                  │ │
│  └────────────────────────────────────────┘ │
│  ┌─ Operations ───────────────────────────┐ │
│  │ RentalReturnService  ReservationService│ │
│  │ StaffPerformanceService                │ │
│  │ DisputeResolutionService               │ │
│  │ FranchiseTrackerService                │ │
│  │ MovieLifecycleService                  │ │
│  │ MovieNightPlannerService               │ │
│  └────────────────────────────────────────┘ │
│  ┌─ Commerce ─────────────────────────────┐ │
│  │ BundleService   CouponService          │ │
│  │ GiftCardService  MembershipTierService │ │
│  │ SeasonalPromotionService               │ │
│  └────────────────────────────────────────┘ │
│  ┌─ Content ──────────────────────────────┐ │
│  │ TaggingService  ParentalControlService │ │
│  └────────────────────────────────────────┘ │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│              Repositories (12)               │
│   InMemoryMovieRepository                    │
│   InMemoryCustomerRepository                 │
│   InMemoryRentalRepository                   │
│   InMemoryWatchlistRepository                │
│   InMemoryCollectionRepository               │
│   InMemoryReviewRepository                   │
│   InMemoryCouponRepository                   │
│   InMemoryGiftCardRepository                 │
│   InMemoryDisputeRepository                  │
│   InMemoryReservationRepository              │
│   InMemoryTagRepository                      │
│   (Dictionary + lock, defensive cloning)     │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│         Models / ViewModels                  │
│   Movie  Customer  Rental  Review            │
│   WatchlistItem  MovieCollection  MovieTag   │
│   BundleDeal  Coupon  GiftCard  Reservation  │
│   DashboardModels  MovieInsightModels        │
│   RentalHistoryModels  ChurnModels           │
│   DisputeModels  FranchiseModels             │
│   StaffModels  RevenueModels                 │
│   MembershipTierModels  MovieNightModels     │
│   InventoryModels  11 ViewModels             │
└─────────────────────────────────────────────┘
```

## Request Lifecycle

1. **Routing:** `RouteConfig.RegisterRoutes()` registers both attribute-based routes (e.g., `[Route("movies/released/{year}/{month}")]`) and convention-based routes (`{controller}/{action}/{id}`). Attribute routes take precedence.

2. **Controller Selection:** ASP.NET's `DefaultControllerFactory` instantiates the matching controller. Controllers use a parameterless constructor that creates in-memory repositories by default.

3. **Action Execution:** The controller action processes the request:
   - **GET actions** retrieve data from repositories/services and pass it to a Razor view
   - **POST actions** validate `ModelState`, perform mutations, and redirect (PRG pattern)

4. **View Rendering:** Razor views in `/Views/{Controller}/` receive strongly-typed models. The shared `_Layout.cshtml` provides the HTML shell with Bootstrap + Lumen theme.

## Layer Responsibilities

### Controllers (`/Controllers/`)

Controllers are **thin** — they orchestrate but don't contain business logic.

| Controller | Responsibility |
|-----------|---------------|
| `HomeController` | Static pages (Home, About, Contact) |
| `MoviesController` | Movie CRUD, search/filter, sort, random movie |
| `CustomersController` | Customer CRUD, search by name/membership, sort |
| `RentalsController` | Rental checkout, return, search, overdue tracking |
| `WatchlistController` | Customer watchlists — add/remove/reorder/notes |
| `CollectionsController` | Movie collections — create/edit/add-remove items |
| `ReviewsController` | Movie reviews — create/edit/delete with star ratings |
| `DashboardController` | Revenue/analytics dashboard (read-only) |
| `ActivityController` | Customer activity history and rental analytics |
| `RecommendationsController` | Movie recommendations for customers |
| `NotificationsController` | Customer notification center — overdue alerts, watchlist availability, membership milestones |
| `ExportController` | Data export — CSV/JSON downloads for rentals, customers, movies |
| `BundlesController` | Bundle deal management — create/edit/delete movie bundles with discounts |
| `CouponsController` | Coupon code management — create/validate/apply promotional coupons |
| `GiftCardsController` | Gift card system — create/activate/redeem/check balance |

**Key patterns across all controllers:**
- **Constructor injection**: Accept repository/service interfaces for testability
- **Parameterless fallback**: Default constructor creates in-memory implementations
- **Post/Redirect/Get (PRG)**: All POST actions redirect on success
- **Anti-forgery**: `[ValidateAntiForgeryToken]` on all POST endpoints
- **Over-posting prevention**: `[Bind(Exclude = "Id")]` on Create actions
- **Null guards**: Returns `HttpNotFound()` for missing resources

### Services (`/Services/`)

Services contain business logic extracted from controllers. They are organized into five categories:

#### Core Services

| Service | Responsibility |
|---------|---------------|
| `DashboardService` | Top movies/customers, revenue by genre/membership, monthly trends |
| `RentalHistoryService` | Filtered history, timelines, popular times, retention, loyalty, seasonal trends, reports |
| `MovieInsightsService` | Per-movie analytics: rental summary, revenue, demographics, performance scoring, comparison |
| `ReviewService` | Review CRUD, rating aggregation, helpful-vote tracking |
| `CollectionService` | Movie collection management with privacy controls |
| `NotificationService` | Customer alerts — overdue rentals (urgent), due-soon (high), watchlist availability, new arrivals, membership milestones, upgrade suggestions |
| `PricingService` | Membership tier benefits, rental cost calculation with discounts, late fee computation, promotional pricing |
| `InventoryService` | Movie availability tracking, stock level management, demand forecasting |
| `LoyaltyPointsService` | Points accumulation from rentals, tier-based multipliers, rewards catalog, points history |
| `WatchlistService` | Watchlist management, cross-customer comparison, genre/preference insights |

#### Analytics & ML Services

| Service | Responsibility |
|---------|---------------|
| `MovieSimilarityService` | Cosine-similarity based movie recommendations using genre, rating, and rental patterns |
| `RecommendationService` | Customer-specific recommendations using watch history and similar-user preferences |
| `CustomerActivityService` | Customer engagement metrics, spending analytics, rental patterns |
| `CustomerSegmentationService` | RFM analysis (Recency/Frequency/Monetary), automated customer tier classification |
| `ChurnPredictorService` | Customer churn risk scoring with multi-factor analysis (recency, frequency, monetary, engagement) |
| `RatingEngineService` | Bayesian rating with configurable prior, trending scores (time-decay weighted), controversy detection |
| `RevenueAnalyticsService` | Period-based revenue reports, genre/membership breakdown, period-over-period comparison |
| `RentalForecastService` | Day-of-week demand distribution, inventory recommendations, forecast summary generation |

#### Operations Services

| Service | Responsibility |
|---------|---------------|
| `RentalReturnService` | Single and batch rental returns, late fee calculation with grace periods and damage assessment |
| `ReservationService` | Reservation queue management — place/cancel/fulfill, waitlist with auto-notify on availability |
| `StaffPerformanceService` | Staff member tracking, KPI computation (rentals processed, revenue, satisfaction), team analytics |
| `DisputeResolutionService` | Dispute lifecycle management — submit/review/approve/reject/escalate with status tracking |
| `FranchiseTrackerService` | Movie franchise management — group movies into franchises, completeness tracking, watch-order recommendations |
| `MovieLifecycleService` | Movie lifecycle phase detection (new/growing/peak/declining/catalog), pricing recommendations by phase |
| `MovieNightPlannerService` | Curated movie night plans — genre mixing, runtime budgeting, group compatibility, snack pairing suggestions |

#### Commerce Services

| Service | Responsibility |
|---------|---------------|
| `BundleService` | Bundle deal CRUD — multi-movie discount packages with validation and availability checking |
| `CouponService` | Coupon validation and application — percentage/fixed discounts, minimum spend, expiry, usage limits |
| `GiftCardService` | Gift card lifecycle — generation, activation, redemption, balance tracking, transaction history |
| `MembershipTierService` | Tier benefits comparison, customer tier evaluation with upgrade/downgrade recommendations |
| `SeasonalPromotionService` | Seasonal promotion management — create/schedule/activate/deactivate with date-range targeting |

#### Content Services

| Service | Responsibility |
|---------|---------------|
| `TaggingService` | Movie tagging system — create/update/deactivate tags, tag movies, find movies by tags |
| `ParentalControlService` | Content rating management — set/suggest age ratings, content warnings, genre-based rating heuristics |

**Service construction:**
All services accept repository interfaces via constructor injection and use `ArgumentNullException` guards. Services are stateless — they compute everything from repository data on each call.

### Models (`/Models/`)

Models are plain C# classes with data annotation attributes for validation.

**Core entities:**

| Model | Purpose |
|-------|---------|
| `Movie` | Movie with name, release date, genre (enum), rating |
| `Customer` | Customer with name, email, membership tier, member-since date |
| `Rental` | Active/returned/overdue rental with computed costs and late fees |
| `Review` | Movie review with star rating (1–5), text, helpful votes |
| `WatchlistItem` | Customer's watchlist entry with priority, notes, watched flag |
| `MovieCollection` | Named collection of movies with privacy setting |
| `BundleDeal` | Multi-movie discount package with pricing rules |
| `Coupon` | Promotional coupon with code, discount type, expiry, usage limits |
| `GiftCard` | Prepaid gift card with balance, activation status, transaction history |
| `Reservation` | Movie reservation queue entry with position and status |
| `MovieTag` | Categorization tag for movies with active/inactive state |

**Analytics models (separated from services):**

| File | Contents |
|------|----------|
| `DashboardModels.cs` | `DashboardData`, `MovieRankEntry`, `CustomerRankEntry`, `GenreRevenueEntry`, `MembershipRevenueEntry`, `MonthlyRevenueEntry` |
| `MovieInsightModels.cs` | `MovieInsight`, `RentalSummary`, `RevenueBreakdown`, `CustomerDemographicBreakdown`, `MonthlyRentalPoint`, `PerformanceScore`, `MovieInsightComparison` |
| `RentalHistoryModels.cs` | `RentalHistoryEntry`, `TimelineEvent`, `PopularTimesResult`, `RetentionMetrics`, `CustomerChurnRisk`, `InventoryForecast`, `LoyaltyResult`, `SeasonalTrend`, `RentalReport` |
| `ChurnModels.cs` | `ChurnProfile`, `ChurnSummary` — customer churn risk factor breakdown |
| `DisputeModels.cs` | `Dispute`, `DisputeResult` — dispute lifecycle state machine models |
| `FranchiseModels.cs` | `Franchise`, `FranchiseEntry` — movie franchise grouping and ordering |
| `StaffModels.cs` | `StaffMember`, `StaffPerformance`, `StaffRole` — staff tracking and KPIs |
| `RevenueModels.cs` | `RevenueReport`, `PeriodComparison` — financial reporting models |
| `MembershipTierModels.cs` | `TierBenefits`, `TierComparison`, `TierEvaluation` — membership tier analysis |
| `MovieNightModels.cs` | `MovieNightPlan`, `MovieNightRequest` — curated movie night planning |
| `InventoryModels.cs` | Inventory tracking and demand forecasting models |

**Design decisions:**
- `ReleaseDate` is nullable — movies can exist without a known release date
- `Id` is auto-assigned by the repository, not the caller
- Validation is declarative via attributes, checked by `ModelState.IsValid`
- Analytics models live in `/Models/` (not inline in service files) to follow single-responsibility and match existing conventions

### Repositories (`/Repositories/`)

The repository layer abstracts data access behind interfaces:

```
IRepository<T>                    — Generic CRUD (GetAll, GetById, Add, Update, Remove)
    ├── IMovieRepository          — Search, GetByReleaseDate, GetRandom
    ├── ICustomerRepository       — Search by name/membership, GetStats
    ├── IRentalRepository         — Checkout, Return, Search, GetOverdue, GetStats, IsMovieRentedOut
    ├── IWatchlistRepository      — GetByCustomer, priority tracking, stats, most-watchlisted
    ├── ICollectionRepository     — Named movie collections with privacy controls
    ├── IReviewRepository         — Movie reviews, rating aggregation, helpful votes
    ├── ICouponRepository         — Coupon storage, lookup by code, usage tracking
    ├── IGiftCardRepository       — Gift card storage, lookup by code, balance tracking
    ├── IDisputeRepository        — Dispute records, lookup by customer/rental/status
    ├── IReservationRepository    — Reservation queue, lookup by customer/movie, position tracking
    ├── ITagRepository            — Movie tag storage, lookup by name, tag-movie associations
    └── (no explicit interface)   — Some services manage their own in-memory state (e.g., StaffPerformanceService)
```

Each has an `InMemory*Repository` implementation using:
- **Static `Dictionary<int, T>`** shared across all controller instances
- **`lock` synchronization** for thread safety
- **Defensive `Clone()` methods** to prevent caller mutation of internal state
- **Atomic `_nextId++`** for auto-incrementing IDs

**Why static storage?** This is a demo app without a database. Static fields survive across HTTP requests within the same IIS Express process, simulating persistence.

### ViewModels (`/ViewModels/`)

ViewModels compose data for complex views:

| ViewModel | Used by | Purpose |
|-----------|---------|---------|
| `MovieSearchViewModel` | `Movies/Index` | Movies list + search/filter/sort state |
| `CustomerSearchViewModel` | `Customers/Index` | Customers list + search/filter state + stats |
| `RentalSearchViewModel` | `Rentals/Index` | Rentals list + search/filter/sort state + stats |
| `RentalCheckoutViewModel` | `Rentals/Checkout` | New rental + available movies/customers |
| `RandomMovieViewModel` | `Movies/Random` | Random movie + customer list |
| `DashboardViewModel` | `Dashboard/Index` | Full dashboard analytics data |
| `ReviewIndexViewModel` | `Reviews/Index` | Reviews list + aggregated ratings |
| `WatchlistViewModel` | `Watchlist/Index` | Customer's watchlist with status tracking |
| `RecommendationViewModel` | `Recommendations/Index` | Recommended movies for a customer |
| `CouponIndexViewModel` | `Coupons/Index` | Coupon list with search/filter/stats |
| `GiftCardViewModel` | `GiftCards/Index` | Gift card list with balance summaries |

### Utilities (`/Utilities/`)

Cross-cutting utilities shared across controllers and services.

| Utility | Purpose |
|---------|---------|
| `JsonSerializer` | Secure JSON serialization with depth limiting, circular reference detection, and HTML-safe encoding |
| `SortHelper<T>` | Declarative, dictionary-based sort utility — replaces duplicated switch-over-sort-key patterns in controllers. Each controller defines sort columns as a `Dictionary<string, SortColumn<T>>` with key selectors, direction, and optional tie-breakers |

### Views (`/Views/`)

Razor views with Bootstrap + Lumen theme. Each controller has its own view folder. The shared `_Layout.cshtml` provides the HTML shell.

**Shared Edit pattern:** `Create()` actions reuse `Edit.cshtml` by passing a new entity. The view checks `Id == 0` to distinguish create from edit.

## Routing

Two routing mechanisms coexist:

### Convention Routes (default)
```
{controller}/{action}/{id}
```
Handles standard CRUD: `/movies`, `/movies/create`, `/movies/edit/1`

### Attribute Routes (explicit)
```csharp
[Route("movies/released/{year:range(1888,2100)}/{month:regex(\\d{2}):range(1,12)}")]
```
Handles non-standard URL structures with inline constraints.

## Configuration

### Bundling (`App_Start/BundleConfig.cs`)
CSS and JS files are bundled and minified for production:
- `~/bundles/jquery` — jQuery 1.10.2
- `~/bundles/bootstrap` — Bootstrap JS
- `~/Content/css` — Bootstrap CSS + Lumen theme

### Filters (`App_Start/FilterConfig.cs`)
Global `HandleErrorAttribute` for unhandled exception rendering.

### Application Insights (`ApplicationInsights.config`)
Telemetry collection for request tracking, dependency calls, and performance counters.

## Testing Architecture

Tests live in `Vidly.Tests/` targeting `net472`, with the main project targeting `net452`.

```
Vidly.Tests/
├── Model tests
│   ├── MovieModelTests.cs              — Movie validation
│   ├── CustomerModelTests.cs           — Customer validation
│   ├── RentalModelTests.cs             — Rental validation + cost computation
│   └── ViewModelTests.cs              — All 11 ViewModel classes
├── Repository tests
│   ├── InMemoryMovieRepositoryTests.cs
│   ├── InMemoryCustomerRepositoryTests.cs
│   └── InMemoryRentalRepositoryTests.cs
├── Controller tests
│   ├── MoviesControllerTests.cs
│   ├── CustomersControllerTests.cs
│   ├── RentalsControllerTests.cs
│   ├── RecommendationsControllerTests.cs
│   └── ExportControllerTests.cs
├── Core service tests
│   ├── DashboardTests.cs
│   ├── MovieInsightsServiceTests.cs
│   ├── MovieSimilarityServiceTests.cs
│   ├── RecommendationServiceTests.cs
│   ├── RentalHistoryServiceTests.cs
│   ├── CustomerActivityServiceTests.cs
│   ├── InventoryServiceTests.cs
│   ├── LoyaltyPointsServiceTests.cs
│   ├── PricingServiceTests.cs
│   ├── NotificationServiceTests.cs
│   ├── ReviewServiceTests.cs / ReviewTests.cs
│   ├── CollectionTests.cs
│   └── WatchlistTests.cs / WatchlistServiceTests.cs
├── Analytics/ML service tests
│   ├── CustomerSegmentationServiceTests.cs
│   ├── ChurnPredictorServiceTests.cs
│   ├── RatingEngineServiceTests.cs
│   ├── RevenueAnalyticsServiceTests.cs
│   └── RentalForecastServiceTests.cs
├── Operations service tests
│   ├── RentalReturnServiceTests.cs
│   ├── ReservationServiceTests.cs
│   ├── StaffPerformanceServiceTests.cs
│   ├── DisputeResolutionServiceTests.cs
│   ├── FranchiseTrackerServiceTests.cs
│   ├── MovieLifecycleServiceTests.cs
│   └── MovieNightPlannerServiceTests.cs
├── Commerce service tests
│   ├── BundleTests.cs
│   ├── CouponTests.cs
│   ├── GiftCardTests.cs
│   ├── MembershipTierServiceTests.cs
│   ├── SeasonalPromotionServiceTests.cs
│   └── TaggingServiceTests.cs
├── Content service tests
│   └── ParentalControlServiceTests.cs
├── Security tests
│   ├── SecurityHeadersTests.cs
│   └── ExportSecurityTests.cs
├── Activity tests
│   └── ActivityControllerTests.cs
└── Utility tests
    └── JsonSerializerTests.cs
```

**Testing strategy:**
- **Models:** Validate data annotations using `Validator.TryValidateObject()` with `ValidationContext`
- **ViewModels:** Test composition, property initialization, and computed properties across all 9 ViewModel types
- **Repositories:** Direct unit tests of CRUD operations, thread safety, search, and edge cases
- **Controllers:** Create controllers with fresh repositories per test. Assert on `ActionResult` types and model state
- **Services:** Inject repository fakes/mocks, verify business logic outputs

All tests use MSTest (`[TestClass]`, `[TestMethod]`). Coverage is collected via Coverlet in Cobertura format.

**Known test issues:** 29 pre-existing failures due to date-dependent assertions and seed data assumptions (e.g., overdue date thresholds, average revenue calculations, month boundaries).

## Performance Patterns

Several optimization patterns are used consistently across the codebase. Understanding these helps maintain performance when adding features.

### Single-Pass Aggregation

Multiple statistics are computed in a single loop instead of chaining separate LINQ queries:

```csharp
// ✗ O(3N) — three passes over the collection
var active = rentals.Count(r => r.Status == RentalStatus.Active);
var revenue = rentals.Sum(r => r.TotalCost);
var overdue = rentals.Count(r => r.Status == RentalStatus.Overdue);

// ✓ O(N) — single pass with accumulators
foreach (var r in rentals)
{
    switch (r.Status) { ... }
    totalRevenue += r.TotalCost;
}
```

Used in: `InMemoryRentalRepository.GetStats()`, `CustomerActivityService.BuildSummary()`, `DashboardService.ComputeMonthlyRevenue()`

### Dictionary-Based Grouping

Grouping by key uses pre-built dictionaries for O(1) bin lookups instead of nested iteration:

```csharp
// ✗ O(K×N) — iterates all items for each bucket
for (int i = 0; i < 6; i++)
{
    var monthItems = items.Where(r => InMonth(r, i)).ToList();
}

// ✓ O(N) — single pass, O(1) per-item lookup
var lookup = new Dictionary<(int Year, int Month), Entry>();
foreach (var r in items)
{
    if (lookup.TryGetValue((r.Date.Year, r.Date.Month), out var entry))
        entry.Count++;
}
```

Used in: `CustomerActivityService.BuildMonthlyActivity()`, `DashboardService.ComputeMonthlyRevenue()`, `CustomerActivityService.BuildGenreBreakdown()`

### Pre-Built Indexes for Collaborative Filtering

`MovieSimilarityService` builds two indexes from a single O(R) pass over all rentals, then derives all co-rental scores and renter counts from them:

```
allRentals  ──O(R)──►  customerMovies: { customerId → Set<movieId> }
                              │
                         O(edges)
                              │
                              ▼
                        movieRenters: { movieId → Set<customerId> }
```

This replaces the old pattern where `FindSimilar()` and `Compare()` each made 2–4 independent O(R) scans of all rentals. The indexes are reused across operations:

- `FindSimilar()` — builds indexes once, derives scores for all candidate movies
- `Compare()` — builds indexes once, gets renters and co-rental data for both movies
- `GetSimilarityMatrix()` — builds indexes once, computes all pairwise scores

### HashSet for O(1) Membership Checks

`InMemoryRentalRepository` maintains a `_rentedMovieIds` HashSet alongside the main Dictionary, updated on every Add/Update/Remove/Return. This enables O(1) availability checks in `IsMovieRentedOut()` and `Checkout()` instead of scanning all rentals.

### Defensive Cloning

All repository `Get*()` methods return cloned objects to prevent callers from mutating internal state. The `Clone()` methods are lightweight (property copy, no deep graph traversal) since models are flat value types + strings.

## Extending Vidly

### Adding a New Entity

1. Create model in `/Models/` with validation attributes
2. Create `IEntityRepository` extending `IRepository<T>`
3. Implement `InMemoryEntityRepository` with static Dictionary + lock + Clone
4. Create service in `/Services/` if business logic is needed
5. Create controller with constructor injection + parameterless fallback
6. Add ViewModels for complex views
7. Add Razor views
8. Add tests for model, repository, controller, and service

### Adding a New Service

1. Define an interface in the service file (e.g., `IRentalHistoryService`)
2. Implement against repository interfaces
3. Keep services stateless — compute from repositories each call
4. Extract data models into `/Models/` (not inline in the service file)
5. Add comprehensive tests

### Replacing the Data Store

1. Implement `IMovieRepository` / `ICustomerRepository` / `IRentalRepository` backed by EF/Dapper/etc.
2. Register in a DI container (e.g., Unity, Autofac, or Simple Injector)
3. Remove parameterless constructors from controllers
4. Update `Global.asax.cs` to configure the DI container

### Adding Authentication

1. Install `Microsoft.AspNet.Identity.Owin`
2. Add `[Authorize]` to controllers/actions
3. Create `AccountController` with login/register
4. Add `_LoginPartial.cshtml` to the layout

---

## Further Reading

- [Models Reference](docs/MODELS.md) — complete data model documentation
- [Controllers Reference](docs/CONTROLLERS.md) — all controller routes and actions
- [Services Reference](docs/SERVICES.md) — all service classes and their APIs
