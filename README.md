# Vidly

A video rental store web application built with **ASP.NET MVC 5**. This project demonstrates core MVC patterns including routing, controllers, models, view models, and Razor views.

## Features

- **Movie Catalog** — Browse movies with pagination and sorting
- **Custom Routing** — Attribute-based routes for filtering by release date
- **View Models** — Composed data objects for rich view rendering
- **Bundling & Minification** — Optimized client-side assets via `BundleConfig`

## Project Structure

```
Vidly/
├── Controllers/
│   ├── HomeController.cs       # Landing page
│   └── MoviesController.cs     # Movie CRUD & browsing
├── Models/
│   ├── Customer.cs             # Customer entity
│   └── Movie.cs                # Movie entity
├── ViewModels/
│   └── RandomMovieViewModel.cs # Composite view model
├── Views/                      # Razor view templates
├── App_Start/
│   ├── BundleConfig.cs         # JS/CSS bundling
│   ├── FilterConfig.cs         # Global action filters
│   └── RouteConfig.cs          # URL routing rules
└── Global.asax.cs              # Application entry point
```

## Getting Started

### Prerequisites

- Visual Studio 2017+ (or VS Code with C# extension)
- .NET Framework 4.5+

### Running

1. Clone the repository:
   ```
   git clone https://github.com/sauravbhattacharya001/Vidly.git
   ```
2. Open `Vidly.sln` in Visual Studio
3. Restore NuGet packages (Build → Restore NuGet Packages)
4. Press **F5** to run

## Routes

| Route | Description |
|-------|-------------|
| `/` | Home page |
| `/movies` | Movie listing (supports `?pageIndex=N&sortBy=Name`) |
| `/movies/random` | Random movie showcase |
| `/movies/edit/{id}` | Edit a movie by ID |
| `/movies/released/{year}/{month}` | Filter by release date |

## License

This project is provided as-is for educational purposes.
