# 🎬 Vidly

<!-- Build & Security -->
[![CI - Build & Test](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/ci.yml/badge.svg)](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/ci.yml)
[![CodeQL](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/codeql.yml/badge.svg)](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/codeql.yml)
[![Docker](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/docker.yml/badge.svg)](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/docker.yml)
[![Pages](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/pages.yml/badge.svg)](https://sauravbhattacharya001.github.io/Vidly/)

<!-- Package & License -->
[![NuGet](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/sauravbhattacharya001/Vidly/packages)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

<!-- Tech -->
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.5.2-purple.svg)](https://dotnet.microsoft.com/en-us/download/dotnet-framework)
[![ASP.NET MVC](https://img.shields.io/badge/ASP.NET%20MVC-5.2-green.svg)](https://www.asp.net/mvc)
[![Tests](https://img.shields.io/badge/tests-3690%2B%20passing-brightgreen.svg)](Vidly.Tests)
[![Controllers](https://img.shields.io/badge/controllers-99-informational.svg)](#-architecture)
[![Services](https://img.shields.io/badge/services-96-informational.svg)](#-architecture)

<!-- Release & Dependabot -->
[![Latest Release](https://img.shields.io/github/v/release/sauravbhattacharya001/Vidly?include_prereleases&sort=semver)](https://github.com/sauravbhattacharya001/Vidly/releases/latest)
[![Dependabot](https://img.shields.io/badge/Dependabot-enabled-brightgreen?logo=dependabot)](https://github.com/sauravbhattacharya001/Vidly/network/updates)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/sauravbhattacharya001/Vidly/pulls)

<!-- Repo -->
[![GitHub last commit](https://img.shields.io/github/last-commit/sauravbhattacharya001/Vidly)](https://github.com/sauravbhattacharya001/Vidly/commits/master)
[![GitHub repo size](https://img.shields.io/github/repo-size/sauravbhattacharya001/Vidly)](https://github.com/sauravbhattacharya001/Vidly)
[![GitHub issues](https://img.shields.io/github/issues/sauravbhattacharya001/Vidly)](https://github.com/sauravbhattacharya001/Vidly/issues)
[![GitHub stars](https://img.shields.io/github/stars/sauravbhattacharya001/Vidly?style=social)](https://github.com/sauravbhattacharya001/Vidly)
[![GitHub forks](https://img.shields.io/github/forks/sauravbhattacharya001/Vidly?style=social)](https://github.com/sauravbhattacharya001/Vidly/network/members)
[![GitHub contributors](https://img.shields.io/github/contributors/sauravbhattacharya001/Vidly)](https://github.com/sauravbhattacharya001/Vidly/graphs/contributors)

A full-featured video rental store web application built with **ASP.NET MVC 5**, featuring **99 controllers**, **96 services**, **88 domain models**, and **3,690+ unit tests** across **100 test files**. Demonstrates enterprise-scale MVC patterns including layered architecture, repository pattern, service layer, dependency injection, rate limiting, security headers, and comprehensive validation.

---

## 📋 Table of Contents

- [Features](#-features)
- [Quick Start](#-quick-start)
- [Architecture](#️-architecture)
- [Getting Started](#-getting-started)
- [API / Routes](#-api--routes)
- [Testing](#-testing)
- [Tech Stack](#️-tech-stack)
- [Security](#-security)
- [Docker](#-docker)
- [Packages](#-packages)
- [Documentation](#-documentation)
- [Contributing](#-contributing)
- [License](#-license)

## ✨ Features

### Core Business
- **Movie Catalog** - Full CRUD with search, filtering by genre/rating, and sorting
- **Rental Management** - Checkout, returns, extensions, late fees, and rental history
- **Customer Management** - Profiles, segmentation, merge, loyalty points, and insights
- **Reservation System** - Waitlists, availability tracking, and rental calendars

### Engagement
- **Recommendations** - Smart movie recommendations and similarity matching
- **Movie Clubs** - Club management with marathon planning and movie nights
- **Gamification** - Achievements, quizzes, trivia, bingo, and movie tournaments
- **Social** - Reviews, watchlists, playlists, staff picks, and movie quotes board

### Business Operations
- **Financial** - Gift cards, coupons, subscriptions, refunds, budgets, and revenue analytics
- **Staff** - Scheduling, performance tracking, and store management
- **Franchise** - Multi-store tracking and franchise management
- **Security** - Rate limiting, CSRF protection, security headers, parental controls
- **Customer Intelligence** - Churn prediction, lifetime value scoring, customer health, segmentation, taste DNA
- **Demand Planning** - Demand forecasting, shelf optimization, retirement planning, inventory auditing

### Infrastructure
- **Repository Pattern** - 56 in-memory repositories with interfaces for easy swapping
- **Service Layer** - 96 dedicated services with dependency injection
- **Thread Safety** - Lock-based synchronization for concurrent access
- **Bundling & Minification** - Optimized client-side assets via `BundleConfig`
- **Bootstrap UI** - Responsive interface using Bootstrap with the Lumen theme

## ⚡ Quick Start

```bash
git clone https://github.com/sauravbhattacharya001/Vidly.git
cd Vidly
nuget restore Vidly.sln

# Open in Visual Studio and press F5
# Or run with Docker:
docker build -t vidly . && docker run -d -p 8080:80 vidly
```

## 🏗️ Architecture

```
Vidly/
├── Controllers/          # 99 MVC controllers
│   ├── MoviesController  # Movie CRUD, browsing, filtering
│   ├── RentalsController # Checkout, returns, extensions
│   ├── CustomersController # Customer management
│   └── ...               # 96 more domain controllers
├── Models/               # 88 domain entities with data annotations
├── ViewModels/           # 60 composed view models for Razor views
├── Services/             # 96 business logic services
│   ├── RecommendationService
│   ├── LoyaltyPointsService
│   ├── ChurnPredictorService
│   └── ...
├── Repositories/         # 56 in-memory repositories (IRepository<T>)
├── Filters/              # Rate limiting, security headers
├── App_Start/            # Routing, bundling, filter config
├── Content/              # CSS (Bootstrap, Lumen theme)
├── Scripts/              # JavaScript (jQuery, Bootstrap)
├── Views/                # Razor views with shared layout
├── docs/                 # API docs deployed to GitHub Pages
└── Vidly.Tests/          # 100 test files, 3,690+ test methods
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for a deep dive into the request lifecycle, layer responsibilities, threading model, and extension points.

## 🚀 Getting Started

### Prerequisites

- **Visual Studio 2017+** (or Visual Studio Code with the C# extension)
- **.NET Framework 4.5.2+** runtime and targeting pack
- **NuGet** package manager (built into Visual Studio)

### Installation

1. **Clone the repository:**
   ```bash
   git clone https://github.com/sauravbhattacharya001/Vidly.git
   cd Vidly
   ```

2. **Open the solution** in Visual Studio:
   ```
   Vidly.sln
   ```

3. **Restore NuGet packages:**
   - Visual Studio: Build → Restore NuGet Packages
   - CLI: `nuget restore Vidly.sln`

4. **Run the application:**
   - Press **F5** in Visual Studio (launches with IIS Express)
   - Navigate to `http://localhost:51355/`

## 📖 API / Routes

### Core Movie Routes

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/` | Home page |
| `GET` | `/movies` | Movie listing with search, filter, and sort |
| `GET` | `/movies/details/{id}` | Full details for a single movie |
| `GET` | `/movies/random` | Random movie showcase |
| `GET/POST` | `/movies/create` | Create a new movie |
| `GET/POST` | `/movies/edit/{id}` | Edit an existing movie |
| `POST` | `/movies/delete/{id}` | Delete a movie (CSRF-protected) |
| `GET` | `/movies/released/{year}/{month}` | Filter by release date |

### Additional Domains

The application includes dedicated controllers and routes for: rentals, customers, reviews, recommendations, watchlists, playlists, subscriptions, gift cards, coupons, loyalty, referrals, reservations, staff scheduling, franchise management, tournaments, quizzes, trivia, and more.

### Query Parameters (Movie Index)

- **`query`** - Case-insensitive substring search on movie name
- **`genre`** - Filter by genre enum value (e.g., `Action`, `Comedy`)
- **`minRating`** - Minimum star rating filter
- **`sortBy`** - Sort column: `name`, `rating`, `releasedate`, `genre`, or `id`

## 🧪 Testing

The project includes a comprehensive test suite with **3,690+ unit tests** across **100 test files** covering models, services, controllers, and view models.

```bash
# Restore and run tests
dotnet restore Vidly.Tests/Vidly.Tests.csproj
dotnet test Vidly.Tests/Vidly.Tests.csproj --collect:"XPlat Code Coverage"
```

### Test Coverage Highlights

| Area | Test Files | Coverage |
|------|-----------|----------|
| **Services** | 60+ | Business logic, edge cases, error handling |
| **Controllers** | 15+ | HTTP behavior, routing, validation |
| **Models** | 5+ | Data annotations, defaults, boundaries |
| **Security** | 2+ | Export injection, security headers |

Coverage reports are generated in Cobertura format and uploaded as CI artifacts on every push.

## 🛠️ Tech Stack

| Component | Technology |
|-----------|-----------|
| **Framework** | ASP.NET MVC 5.2.3 |
| **Runtime** | .NET Framework 4.5.2 |
| **View Engine** | Razor |
| **CSS Framework** | Bootstrap 3 (Lumen theme) |
| **JavaScript** | jQuery 1.10.2 |
| **Testing** | MSTest + Coverlet (3,690+ tests) |
| **CI/CD** | GitHub Actions |
| **Container** | Docker (Windows/IIS) |
| **Security** | CodeQL, Dependabot |

## 🔒 Security

Vidly treats hostile clients as the default threat model. The defenses are layered:

| Concern | Defense | Where |
|---|---|---|
| **CSRF** | `[ValidateAntiForgeryToken]` on every state-changing `[HttpPost]` action | All controllers |
| **XSS** | Razor auto-encoding; `JavaScriptStringEncode` for JS interpolation; explicit `HtmlEncode` before any `Html.Raw` | Views |
| **CSV / formula injection** | RFC 4180 quoting + leading `'` on cells starting with `= + - @ \t \r` (CWE-1236) | [`Utilities/CsvFormatter.cs`](Vidly/Utilities/CsvFormatter.cs) |
| **CRLF injection in CSV** | Newlines stripped from header rows (CWE-93) | [`Utilities/CsvFormatter.cs`](Vidly/Utilities/CsvFormatter.cs) |
| **JSON-in-HTML breakout** | `StringEscapeHandling.EscapeHtml` for any JSON embedded in `<script>` blocks | [`Utilities/JsonForScript.cs`](Vidly/Utilities/JsonForScript.cs) |
| **Hidden-field tampering** | Encrypt + HMAC server-side state with `MachineKey.Protect` and a per-call-site purpose string (CWE-565 / CWE-345) | [`Utilities/SignedPayload.cs`](Vidly/Utilities/SignedPayload.cs) |
| **Brute force / scraping** | `[RateLimit]` attribute with per-IP sliding window (CWE-307) on exports, PIN entry, and other sensitive endpoints | [`Filters/RateLimitAttribute.cs`](Vidly/Filters/RateLimitAttribute.cs) |
| **PIN verification** | `CryptographicOperations.FixedTimeEquals` to defeat timing oracles | `Services/ParentalControlService.cs` |
| **Transport / headers** | HSTS, `X-Content-Type-Options`, `X-Frame-Options`, CSP, referrer policy | [`Filters/SecurityHeadersAttribute.cs`](Vidly/Filters/SecurityHeadersAttribute.cs) |
| **Randomness for tokens** | `RandomNumberGenerator` (CSPRNG) for gift card codes, referral codes, etc. | `Services/GiftCardService.cs`, `Services/ReferralService.cs` |

### Round-tripping server state safely

Several mini-games (EmojiStory, Negotiator, ...) keep session state on the
client between requests. **Never put raw JSON in a hidden field** — a player
will open DevTools and edit their score. Use [`SignedPayload`](Vidly/Utilities/SignedPayload.cs):

```csharp
using Vidly.Utilities;

// Outbound — sign before rendering into the hidden input.
viewModel.SessionJson = SignedPayload.Protect(
    JsonConvert.SerializeObject(session),
    "Vidly.EmojiStory.Session.v1");

// Inbound — verify HMAC, decrypt, then deserialize. Tampered
// payloads silently restart the session instead of crashing.
EmojiGameSession session;
if (!SignedPayload.TryUnprotect(sessionJson, "Vidly.EmojiStory.Session.v1", out var json))
    session = new EmojiGameSession();
else
    session = JsonConvert.DeserializeObject<EmojiGameSession>(json) ?? new EmojiGameSession();
```

The `purpose` string scopes tokens to their endpoint — a session blob minted
for EmojiStory cannot be replayed against another controller.

Report vulnerabilities privately per [SECURITY.md](SECURITY.md).

## 🐳 Docker

Run Vidly in a Windows container with IIS:

```powershell
# Pull from GitHub Container Registry
docker pull ghcr.io/sauravbhattacharya001/vidly:latest

# Or build locally
docker build -t vidly .
docker run -d -p 8080:80 --name vidly vidly
# Access at http://localhost:8080
```

> **Note:** Requires [Docker Desktop](https://www.docker.com/products/docker-desktop/) with **Windows containers** enabled. The image uses `mcr.microsoft.com/dotnet/framework/aspnet:4.8`.

## 📦 Packages

Vidly is available as a NuGet package on [GitHub Packages](https://github.com/sauravbhattacharya001/Vidly/packages):

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/sauravbhattacharya001/index.json" \
  --name github --username YOUR_USERNAME --password YOUR_PAT
dotnet add package Vidly --source github
```

## 📚 Documentation

- **[📖 Live Docs](https://sauravbhattacharya001.github.io/Vidly/)** - Interactive documentation via GitHub Pages
- **[🎭 Mood Engine](https://sauravbhattacharya001.github.io/Vidly/mood-engine.html)** - Autonomous mood inference from rental patterns with proactive recommendations
- **[Architecture Guide](ARCHITECTURE.md)** - Request lifecycle, layers, threading, extension points
- **[Repositories Reference](docs/REPOSITORIES.md)** - Complete repository layer API with 28 interfaces and implementations
- **[Services Reference](docs/SERVICES.md)** - All 96 service classes documented
- **[Controllers Reference](docs/CONTROLLERS.md)** - Controller routes and actions
- **[Models Reference](docs/MODELS.md)** - Domain entities and relationships
- **[Testing Guide](docs/TESTING.md)** - Test architecture, conventions, and coverage
- **[Security Policy](SECURITY.md)** - Security measures and vulnerability reporting
- **[Contributing Guide](CONTRIBUTING.md)** - How to contribute
- **[Troubleshooting](TROUBLESHOOTING.md)** - Build, test, and CI sharp edges (legacy MSBuild + SDK-style test project)

## 🤝 Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

1. **Fork** the repository
2. **Create** a feature branch: `git checkout -b feature/your-feature`
3. **Read** the [Architecture Guide](ARCHITECTURE.md)
4. **Write tests** for new functionality
5. **Submit** a Pull Request

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  Built with ❤️ using ASP.NET MVC 5
</p>
