# Vidly Controllers API Reference

Complete reference for all 20 controllers in `Vidly/Controllers/`.
Each section documents the controller's routes, dependencies, and actions.

> **Architecture note:** Controllers follow the ASP.NET MVC 5 pattern
> with constructor injection for testability. Most controllers have a
> parameterless constructor (for the default MVC factory) that wires
> up in-memory repositories and services.
> See [ARCHITECTURE.md](../ARCHITECTURE.md) for the full system design.

---

## Contents

- [Core](#core)
  - [HomeController](#homecontroller)
  - [DashboardController](#dashboardcontroller)
  - [ExportController](#exportcontroller)
  - [NotificationsController](#notificationscontroller)
- [Movies](#movies)
  - [MoviesController](#moviescontroller)
  - [CompareController](#comparecontroller)
  - [RecommendationsController](#recommendationscontroller)
  - [MovieNightController](#movienightcontroller)
  - [PredictionsController](#predictionscontroller)
  - [CollectionsController](#collectionscontroller)
  - [ReviewsController](#reviewscontroller)
  - [WatchlistController](#watchlistcontroller)
- [Customers](#customers)
  - [CustomersController](#customerscontroller)
  - [ActivityController](#activitycontroller)
  - [AchievementsController](#achievementscontroller)
  - [ReferralsController](#referralscontroller)
- [Rentals & Transactions](#rentals--transactions)
  - [RentalsController](#rentalscontroller)
  - [CouponsController](#couponscontroller)
  - [GiftCardsController](#giftcardscontroller)
  - [BundlesController](#bundlescontroller)

---

## Core

### HomeController

Public-facing landing pages.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index()` | `/` | GET | Home page |
| `About()` | `/Home/About` | GET | About page |
| `Contact()` | `/Home/Contact` | GET | Contact page |

**Dependencies:** None

---

### DashboardController

Admin revenue dashboard with KPIs, top movies/customers, genre breakdown,
membership analysis, and recent activity.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index()` | `/Dashboard` | GET | Full dashboard with all KPI panels |

**Dependencies:** `DashboardService`

**View model:** `DashboardViewModel` — aggregated stats from rentals,
movies, and customers.

---

### ExportController

Bulk data export in CSV and JSON formats.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index()` | `/Export` | GET | Export page with format options |
| `Movies(format)` | `/Export/Movies?format=csv\|json` | GET | Export all movies |
| `Customers(format)` | `/Export/Customers?format=csv\|json` | GET | Export all customers |
| `Rentals(format)` | `/Export/Rentals?format=csv\|json` | GET | Export all rentals |

**Dependencies:** `IMovieRepository`, `ICustomerRepository`, `IRentalRepository`

**Supported formats:** `csv`, `json`. Uses `JsonSerializer` utility for
JSON output and `StringBuilder` for CSV generation.

---

### NotificationsController

Customer notification management.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index()` | `/Notifications` | GET | List all notifications |
| `Customer(id)` | `/Notifications/Customer/{id}` | GET | Notifications for a specific customer |

**Dependencies:** `NotificationService`, `ICustomerRepository`

---

## Movies

### MoviesController

CRUD operations and browsing for the movie catalog.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(query, genre, minRating, sortBy)` | `/Movies` | GET | Browse/search movies with filters |
| `Details(id)` | `/Movies/Details/{id}` | GET | Single movie details |
| `Create()` | `/Movies/Create` | GET | New movie form |
| `Create(movie)` | `/Movies/Create` | POST | Save new movie |
| `Edit(id)` | `/Movies/Edit/{id}` | GET | Edit movie form |
| `Edit(id, movie)` | `/Movies/Edit/{id}` | POST | Update movie |
| `Delete(id)` | `/Movies/Delete/{id}` | POST | Delete movie |
| `Random()` | `/Movies/Random` | GET | Random movie suggestion |
| `ByReleaseDate(year, month)` | `/Movies/ByReleaseDate/{year}/{month}` | GET | Movies by release date |

**Dependencies:** `IMovieRepository`

**Query parameters:**
- `query` — search by movie name (case-insensitive contains)
- `genre` — filter by `Genre` enum value
- `minRating` — minimum rating filter (1–5)
- `sortBy` — sort field (`name`, `date`, `rating`)

---

### CompareController

Side-by-side movie comparison.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(ids)` | `/Compare?ids=1,2,3` | GET | Compare movies by comma-separated IDs |
| `Index(selectedIds)` | `/Compare` | POST | Compare movies from form selection |

**Dependencies:** `MovieComparisonService`

**View model:** `CompareViewModel` — comparison matrix with rating
differences, genre overlap, and release date spread.

---

### RecommendationsController

Personalized movie recommendations based on rental history and preferences.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(customerId)` | `/Recommendations?customerId=` | GET | Recommendations for a customer |

**Dependencies:** `ICustomerRepository`, `IMovieRepository`,
`IRentalRepository`, `ITagRepository`, `RecommendationService`

If `customerId` is null, shows a customer selection page.

---

### MovieNightController

Movie night planner — generates themed movie lineups.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index()` | `/MovieNight` | GET | Planner form |
| `Plan(request)` | `/MovieNight/Plan` | POST | Generate a movie night plan |

**Dependencies:** `ICustomerRepository`, `MovieNightPlannerService`

**Input model:** `MovieNightRequest` — attendee count, mood preference,
duration, genre constraints.

---

### PredictionsController

Late-return prediction dashboard.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(level)` | `/Predictions?level=` | GET | Predictions list, optionally filtered by risk level |
| `Details(id)` | `/Predictions/Details/{id}` | GET | Detailed prediction for a specific rental |

**Dependencies:** `LateReturnPredictorService`

**Filter values:** `low`, `medium`, `high` (risk levels).

---

### CollectionsController

Curated movie collections — create, edit, and manage themed lists.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(query)` | `/Collections?query=` | GET | Browse/search collections |
| `Details(id)` | `/Collections/Details/{id}` | GET | Collection details with movie list |
| `Create()` | `/Collections/Create` | GET | New collection form |
| `Create(collection)` | `/Collections/Create` | POST | Save new collection |
| `Edit(id)` | `/Collections/Edit/{id}` | GET | Edit collection form |
| `Edit(id, collection)` | `/Collections/Edit/{id}` | POST | Update collection |
| `AddMovie(id, movieId, note)` | `/Collections/AddMovie` | POST | Add movie to collection |
| `RemoveMovie(id, movieId)` | `/Collections/RemoveMovie` | POST | Remove movie from collection |
| `Delete(id)` | `/Collections/Delete/{id}` | POST | Delete collection |

**Dependencies:** `ICollectionRepository`, `IMovieRepository`,
`CollectionService`

---

### ReviewsController

Movie review management.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(search, minStars, message, error)` | `/Reviews` | GET | All reviews with search/filter |
| `Movie(id, message, error)` | `/Reviews/Movie/{id}` | GET | Reviews for a specific movie |
| `Create(customerId, movieId, stars, reviewText)` | `/Reviews/Create` | POST | Submit a review |
| `Delete(id, returnUrl)` | `/Reviews/Delete/{id}` | POST | Delete a review |

**Dependencies:** `IReviewRepository`, `ICustomerRepository`,
`IMovieRepository`, `ReviewService`

**Validation:** `ReviewService` enforces one review per customer per
movie and validates star rating (1–5).

---

### WatchlistController

Customer watchlists with priority levels.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(customerId, message, error)` | `/Watchlist?customerId=` | GET | View watchlist |
| `Add(customerId, movieId)` | `/Watchlist/Add` | GET | Add-to-watchlist form |
| `Add(item)` | `/Watchlist/Add` | POST | Add movie to watchlist |
| `Remove(id, customerId)` | `/Watchlist/Remove/{id}` | POST | Remove from watchlist |
| `Clear(customerId)` | `/Watchlist/Clear` | POST | Clear entire watchlist |
| `UpdatePriority(id, priority, customerId)` | `/Watchlist/UpdatePriority` | POST | Change item priority |

**Dependencies:** `ICustomerRepository`, `IMovieRepository`,
`IWatchlistRepository`

**Priority levels:** `WatchlistPriority` enum — `Low`, `Medium`, `High`,
`MustWatch`.

---

## Customers

### CustomersController

CRUD operations for customer management.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(query, membershipType, sortBy)` | `/Customers` | GET | Browse/search customers |
| `Details(id)` | `/Customers/Details/{id}` | GET | Customer details |
| `Create()` | `/Customers/Create` | GET | New customer form |
| `Create(customer)` | `/Customers/Create` | POST | Save new customer |
| `Edit(id)` | `/Customers/Edit/{id}` | GET | Edit customer form |
| `Edit(id, customer)` | `/Customers/Edit/{id}` | POST | Update customer |
| `Delete(id)` | `/Customers/Delete/{id}` | POST | Delete customer |

**Dependencies:** `ICustomerRepository`

**Query parameters:**
- `query` — search by name or email
- `membershipType` — filter by `MembershipType` enum
- `sortBy` — sort field (`name`, `date`, `membership`)

---

### ActivityController

Customer activity timeline.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(customerId)` | `/Activity?customerId=` | GET | Activity log for a customer |

**Dependencies:** `ICustomerRepository`, `CustomerActivityService`

Shows recent rentals, reviews, watchlist changes, and other events
for the given customer.

---

### AchievementsController

Gamification system — badges, profiles, and leaderboard.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index()` | `/Achievements` | GET | All available achievements |
| `Profile(id)` | `/Achievements/Profile/{id}` | GET | Customer achievement profile |
| `Leaderboard(top)` | `/Achievements/Leaderboard?top=20` | GET | Top achievers |
| `Badge(id)` | `/Achievements/Badge/{id}` | GET | Badge details |

**Dependencies:** `AchievementService`, `ICustomerRepository`

---

### ReferralsController

Customer referral program management.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(customerId)` | `/Referrals?customerId=` | GET | Referral dashboard |
| `Create(referrerId, referredName, referredEmail)` | `/Referrals/Create` | POST | Create a referral |
| `Convert(referralCode, newCustomerId)` | `/Referrals/Convert` | POST | Convert referral to active customer |

**Dependencies:** `ReferralService`, `ICustomerRepository`

---

## Rentals & Transactions

### RentalsController

Rental lifecycle — checkout, returns, receipts, overdue tracking.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(query, status, sortBy)` | `/Rentals` | GET | Browse/search rentals |
| `Details(id)` | `/Rentals/Details/{id}` | GET | Rental details |
| `Checkout()` | `/Rentals/Checkout` | GET | Checkout form |
| `Checkout(viewModel)` | `/Rentals/Checkout` | POST | Process rental checkout |
| `Return(id)` | `/Rentals/Return/{id}` | POST | Process return |
| `Delete(id)` | `/Rentals/Delete/{id}` | POST | Delete rental |
| `Receipt(id)` | `/Rentals/Receipt/{id}` | GET | View rental receipt |
| `Overdue()` | `/Rentals/Overdue` | GET | List all overdue rentals |

**Dependencies:** `IRentalRepository`, `IMovieRepository`,
`ICustomerRepository`, `CouponService`

**Query parameters:**
- `query` — search by customer name or movie title
- `status` — filter by `RentalStatus` enum (`Active`, `Returned`, `Overdue`)
- `sortBy` — sort field (`date`, `customer`, `movie`, `status`)

**View model:** `RentalCheckoutViewModel` — customer ID, movie ID, coupon
code, rental duration.

---

### CouponsController

Coupon CRUD and management.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(status)` | `/Coupons?status=` | GET | List coupons with optional status filter |
| `Create()` | `/Coupons/Create` | GET | New coupon form |
| `Create(coupon)` | `/Coupons/Create` | POST | Save new coupon |
| `Edit(id)` | `/Coupons/Edit/{id}` | GET | Edit coupon form |
| `Edit(coupon)` | `/Coupons/Edit` | POST | Update coupon |
| `Toggle(id)` | `/Coupons/Toggle/{id}` | POST | Toggle active/inactive |
| `Delete(id)` | `/Coupons/Delete/{id}` | POST | Delete coupon |

**Dependencies:** `ICouponRepository`

**Discount types:** `Percentage`, `FixedAmount`. Supports minimum order
amount, max discount cap, usage limits, and date validity.

---

### GiftCardsController

Gift card issuance, balance checks, and top-ups.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index(status)` | `/GiftCards?status=` | GET | List all gift cards |
| `Create()` | `/GiftCards/Create` | GET | New gift card form |
| `Create(model)` | `/GiftCards/Create` | POST | Issue gift card |
| `Details(id)` | `/GiftCards/Details/{id}` | GET | Gift card details and history |
| `Balance()` | `/GiftCards/Balance` | GET | Balance check form |
| `Balance(model)` | `/GiftCards/Balance` | POST | Check balance by code |
| `Toggle(id)` | `/GiftCards/Toggle/{id}` | POST | Activate/deactivate card |
| `TopUp(code, amount)` | `/GiftCards/TopUp` | POST | Add funds to gift card |

**Dependencies:** `IGiftCardRepository`, `GiftCardService`

---

### BundlesController

Bundle deal management — multi-movie discount packages.

| Action | Route | Method | Description |
|--------|-------|--------|-------------|
| `Index()` | `/Bundles` | GET | List all bundle deals |
| `Create()` | `/Bundles/Create` | GET | New bundle form |
| `Create(bundle)` | `/Bundles/Create` | POST | Save new bundle |
| `Toggle(id)` | `/Bundles/Toggle/{id}` | POST | Toggle active/inactive |
| `Delete(id)` | `/Bundles/Delete/{id}` | POST | Delete bundle |
| `Calculator()` | `/Bundles/Calculator` | GET | Bundle savings calculator |
| `Calculator(viewModel)` | `/Bundles/Calculator` | POST | Calculate savings |

**Dependencies:** `BundleService`, `IMovieRepository`

**Bundle properties:** Name, description, min/max movies, discount type
(percentage/fixed), required genre, active dates.

---

## Cross-Cutting Concerns

### Action Filters

| Filter | File | Description |
|--------|------|-------------|
| `RateLimitAttribute` | `Filters/RateLimitAttribute.cs` | Per-action rate limiting |
| `SecurityHeadersAttribute` | `Filters/SecurityHeadersAttribute.cs` | Security response headers (CSP, X-Frame-Options, etc.) |

### Utilities

| Utility | File | Description |
|---------|------|-------------|
| `JsonSerializer` | `Utilities/JsonSerializer.cs` | JSON serialization for export actions |
| `SortHelper` | `Utilities/SortHelper.cs` | Reusable sorting logic for query parameters |

### Repositories

All controllers use the Repository pattern (`IRepository<T>` base) with
in-memory implementations. See [ARCHITECTURE.md](../ARCHITECTURE.md) for
the full repository listing and interface contracts.
