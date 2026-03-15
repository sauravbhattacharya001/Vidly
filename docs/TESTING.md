# Testing Guide

Comprehensive guide to Vidly's test suite — architecture, conventions, and how to run everything.

## Quick Start

```bash
# Run all tests
dotnet test Vidly.Tests/Vidly.Tests.csproj

# Run with coverage
dotnet test Vidly.Tests/Vidly.Tests.csproj \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults

# Run a specific test class
dotnet test --filter "FullyQualifiedName~CustomersControllerTests"

# Run a specific test method
dotnet test --filter "FullyQualifiedName~CustomersControllerTests.Index_ReturnsViewWithCustomerSearchViewModel"
```

## Test Architecture

### Framework & Tools

| Component | Tool | Version |
|-----------|------|---------|
| Test framework | MSTest | 3.1.1 |
| Test runner | Microsoft.NET.Test.Sdk | 17.8.0 |
| Coverage | coverlet.collector + coverlet.msbuild | 6.0.0 |
| Target framework | .NET Framework 4.7.2 | — |
| CI runner | Windows (required for .NET Framework) | — |

### Project Structure

The test project compiles source files directly from the main project (no project reference), which enables accurate coverage metrics without cross-referencing old-style `.csproj` from SDK-style projects.

```
Vidly.Tests/
├── Vidly.Tests.csproj          # SDK-style project, compiles main sources via wildcards
├── TestClock.cs                # Shared test infrastructure
├── *ControllerTests.cs         # Controller tests (9 files)
├── *ServiceTests.cs            # Service tests (48 files)
├── *ModelTests.cs              # Model/domain tests
├── InMemory*RepositoryTests.cs # Repository tests (3 files)
├── *Tests.cs                   # Other (ViewModels, security, serialization, etc.)
└── Services/                   # Subdirectory for additional service tests
```

### Numbers at a Glance

- **78 test files**
- **3,254 test methods** (`[TestMethod]`)
- **9 controller test files** covering all major controllers
- **48 service test files** covering all 55 services
- **3 in-memory repository test files**

## Conventions

### Naming

Tests follow the `MethodName_Scenario_ExpectedBehavior` pattern:

```csharp
[TestMethod]
public void Index_DefaultSort_ByName() { ... }

[TestMethod]
public void Create_ValidGiftCard_GeneratesCode() { ... }

[TestMethod]
public void Redeem_InsufficientBalance_ThrowsException() { ... }
```

### Test Setup

Most test classes use `[TestInitialize]` to reset in-memory repositories:

```csharp
[TestClass]
public class CustomersControllerTests
{
    [TestInitialize]
    public void Setup()
    {
        InMemoryCustomerRepository.Reset();
    }
    // ...
}
```

### TestClock

Time-dependent tests use `TestClock` (implements `IClock`) instead of `DateTime.Now`:

```csharp
var clock = new TestClock(new DateTime(2025, 1, 1, 12, 0, 0));

// Advance time by 30 days
clock.Advance(TimeSpan.FromDays(30));

// Set to an exact time
clock.SetTime(new DateTime(2025, 6, 15));
```

This ensures deterministic, reproducible tests regardless of when they run.

### Stub Repositories

Service tests use inline stub repositories (private inner classes) rather than mocking frameworks:

```csharp
[TestClass]
public class GiftCardServiceTests
{
    private class StubGiftCardRepository : IGiftCardRepository
    {
        private readonly Dictionary<int, GiftCard> _cards = new();
        // ... minimal in-memory implementation
    }
    // ...
}
```

This keeps tests self-contained with no external mocking dependencies.

### In-Memory Repositories

Controller tests use the shared `InMemoryCustomerRepository`, `InMemoryMovieRepository`, and `InMemoryRentalRepository` classes from the main project. These come pre-seeded with test data and are reset between tests via `[TestInitialize]`.

## Test Categories

### Controller Tests

Test the MVC action methods — verify correct `ViewResult` types, view model shapes, sorting, filtering, pagination, and error handling.

| Test File | Controller | Key Scenarios |
|-----------|-----------|---------------|
| `CustomersControllerTests` | Customers | CRUD, search, sorting, filtering, pagination |
| `MoviesControllerTests` | Movies | Listing, details, genre filtering |
| `RentalsControllerTests` | Rentals | Create, return, validation |
| `GiftCardsControllerTests` | GiftCards | Create, redeem, balance |
| `ExportControllerTests` | Export | CSV export, column ordering |
| `CompareControllerTests` | Compare | Movie comparison logic |
| `ActivityControllerTests` | Activity | Customer activity feeds |
| `RecommendationsControllerTests` | Recommendations | Suggestion algorithms |
| `MovieNightControllerTests` | MovieNight | Planning and scheduling |

### Service Tests

Test business logic in isolation. Services are constructed directly with stub/in-memory dependencies.

Major service test areas:

- **Rental lifecycle**: `RentalReturnServiceTests`, `RentalExtensionServiceTests`, `RentalInsuranceServiceTests`, `RentalReceiptServiceTests`, `RentalForecastServiceTests`
- **Customer engagement**: `LoyaltyPointsServiceTests`, `MembershipTierServiceTests`, `ReferralServiceTests`, `CustomerActivityServiceTests`, `CustomerSegmentationServiceTests`
- **Content & discovery**: `RecommendationServiceTests`, `MovieSimilarityServiceTests`, `MovieInsightsServiceTests`, `TaggingServiceTests`, `MovieSeriesServiceTests`
- **Commerce**: `CouponServiceTests`, `GiftCardServiceTests`, `PricingServiceTests`, `SeasonalPromotionServiceTests`, `BundleTests`
- **Operations**: `InventoryServiceTests`, `StaffSchedulingServiceTests`, `StaffPerformanceServiceTests`, `RevenueAnalyticsServiceTests`
- **Predictions**: `ChurnPredictorServiceTests`, `LateReturnPredictorServiceTests`, `RentalTrendServiceTests`

### Model Tests

Validate domain model behavior — property defaults, validation attributes, computed properties:

- `CustomerModelTests`, `MovieModelTests`, `RentalModelTests`
- `GiftCardTests`, `CouponTests`, `ReviewTests`
- `WatchlistTests`, `SubscriptionTests`, `CollectionTests`

### Infrastructure Tests

- `InMemoryCustomerRepositoryTests` — CRUD operations, search, filtering
- `InMemoryMovieRepositoryTests` — Genre filtering, availability
- `InMemoryRentalRepositoryTests` — Rental history, active rentals
- `SecurityHeadersTests` — HTTP security header validation
- `JsonSerializerTests` — Custom serialization
- `SortHelperTests` — Column sorting utilities
- `ExportSecurityTests` — Export authorization

## CI Integration

Tests run automatically on every push and PR via `.github/workflows/ci.yml`:

1. **Restore** → `dotnet restore`
2. **Build** → `dotnet build --no-restore`
3. **Test + Coverage** → `dotnet test` with Coverlet collecting `cobertura` format
4. **Threshold enforcement** → PowerShell script parses `coverage.cobertura.xml` and fails the build if line or branch coverage drops below minimum
5. **Coverage badge** → Generated as a Shields.io JSON endpoint

The CI runs on `windows-latest` since the project targets .NET Framework 4.7.2.

## Writing New Tests

### Adding a Service Test

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MyNewServiceTests
    {
        private MyNewService _service;
        private TestClock _clock;

        [TestInitialize]
        public void Setup()
        {
            _clock = new TestClock();
            _service = new MyNewService(_clock);
        }

        [TestMethod]
        public void DoThing_ValidInput_ReturnsExpected()
        {
            var result = _service.DoThing("input");
            Assert.AreEqual("expected", result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void DoThing_NullInput_Throws()
        {
            _service.DoThing(null);
        }
    }
}
```

### Adding a Controller Test

```csharp
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class MyControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryCustomerRepository.Reset();
            InMemoryMovieRepository.Reset();
        }

        [TestMethod]
        public void Index_ReturnsViewResult()
        {
            var controller = new MyController();
            var result = controller.Index() as ViewResult;
            Assert.IsNotNull(result);
        }
    }
}
```

### Guidelines

- **No mocking frameworks** — use stub repositories or in-memory implementations
- **No external dependencies** — tests must run offline without databases
- **Reset state** — always reset repositories in `[TestInitialize]`
- **Use `TestClock`** — never use `DateTime.Now` in tests
- **Test edge cases** — null inputs, empty collections, boundary values
- **One assertion concept per test** — multiple `Assert` calls are fine if they test the same logical concept
