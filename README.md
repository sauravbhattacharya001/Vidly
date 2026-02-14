# 🎬 Vidly

[![CI — Build & Test](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/ci.yml/badge.svg)](https://github.com/sauravbhattacharya001/Vidly/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.5.2-purple.svg)](https://dotnet.microsoft.com/en-us/download/dotnet-framework)
[![ASP.NET MVC](https://img.shields.io/badge/ASP.NET%20MVC-5.2-green.svg)](https://www.asp.net/mvc)

A video rental store web application built with **ASP.NET MVC 5** that demonstrates core MVC patterns — routing, controllers, models, view models, and Razor views.

---

## ✨ Features

- **Movie Catalog** — Browse, create, edit, and delete movies with full CRUD support
- **Custom Routing** — Attribute-based routes for filtering movies by release date
- **View Models** — Composed data objects for rich, strongly-typed view rendering
- **Validation** — Data annotation-based model validation with user-friendly error messages
- **Thread-Safe Data Store** — Concurrent access handled via lock-based synchronization
- **Bundling & Minification** — Optimized client-side assets via `BundleConfig`
- **Bootstrap UI** — Clean, responsive interface using Bootstrap with the Lumen theme

## 🏗️ Architecture

```
Vidly/
├── Controllers/
│   ├── HomeController.cs           # Landing, About, Contact pages
│   └── MoviesController.cs         # Movie CRUD, browsing, filtering
├── Models/
│   ├── Customer.cs                 # Customer entity with validation
│   └── Movie.cs                    # Movie entity with validation
├── ViewModels/
│   └── RandomMovieViewModel.cs     # Composite view model
├── Views/
│   ├── Home/                       # Home, About, Contact views
│   ├── Movies/                     # Movie views (Random, Edit, etc.)
│   └── Shared/                     # Layout, navbar, error views
├── App_Start/
│   ├── BundleConfig.cs             # JS/CSS bundling configuration
│   ├── FilterConfig.cs             # Global action filters
│   └── RouteConfig.cs              # URL routing rules
├── Content/                        # CSS (Bootstrap, Lumen theme)
├── Scripts/                        # JavaScript (jQuery, Bootstrap)
├── Vidly.Tests/                    # Unit test project with coverage
└── Global.asax.cs                  # Application entry point
```

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

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/` | Home page |
| `GET` | `/movies` | Movie listing (supports `?pageIndex=N&sortBy=Name`) |
| `GET` | `/movies/random` | Random movie showcase with customers |
| `GET` | `/movies/create` | Create a new movie (form) |
| `POST` | `/movies/create` | Submit new movie |
| `GET` | `/movies/edit/{id}` | Edit an existing movie (form) |
| `POST` | `/movies/edit/{id}` | Submit movie edits |
| `POST` | `/movies/delete/{id}` | Delete a movie |
| `GET` | `/movies/released/{year}/{month}` | Filter movies by release year and month |

### URL Parameters

- **`pageIndex`** — Page number for pagination (default: 1)
- **`sortBy`** — Sort field: `Name` (default) or `Id`
- **`year`** — Release year filter (range: 1888–2100)
- **`month`** — Release month filter (range: 1–12, two digits)

## 🧪 Testing

The project includes a comprehensive test suite with **22 unit tests** covering models, view models, and controllers.

```bash
# Restore and run tests (requires .NET SDK)
dotnet restore Vidly.Tests/Vidly.Tests.csproj
dotnet test Vidly.Tests/Vidly.Tests.csproj --collect:"XPlat Code Coverage"
```

### Test Coverage

| Test Class | Tests | What's Covered |
|-----------|-------|----------------|
| `MovieModelTests` | 7 | Validation (Required, StringLength), defaults, boundary cases |
| `CustomerModelTests` | 5 | Validation (Required, StringLength), defaults, boundary cases |
| `ViewModelTests` | 3 | Default initialization, population |
| `MoviesControllerTests` | 7 | Index sorting, Random, Edit, Create, ByReleaseDate, 404 handling |

Coverage reports are generated in Cobertura format and uploaded as CI artifacts on every push.

## 🛠️ Tech Stack

| Component | Technology |
|-----------|-----------|
| **Framework** | ASP.NET MVC 5.2.3 |
| **Runtime** | .NET Framework 4.5.2 |
| **View Engine** | Razor |
| **CSS Framework** | Bootstrap 3 (Lumen theme) |
| **JavaScript** | jQuery 1.10.2 |
| **Telemetry** | Application Insights |
| **Testing** | MSTest + Coverlet |
| **CI/CD** | GitHub Actions |

## 🤝 Contributing

Contributions are welcome! Here's how:

1. **Fork** the repository
2. **Create** a feature branch: `git checkout -b feature/your-feature`
3. **Commit** your changes: `git commit -m "Add your feature"`
4. **Push** to the branch: `git push origin feature/your-feature`
5. **Open** a Pull Request

Please ensure your code follows the existing style and includes appropriate tests.

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  Built with ❤️ using ASP.NET MVC 5
</p>
