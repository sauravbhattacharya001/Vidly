# API Routes Reference

Complete reference for all Vidly controller routes, organized by domain area.

> **Routing Convention:** Vidly uses ASP.NET MVC convention routing: `/{Controller}/{Action}/{id?}`.  
> All routes below follow `GET` unless marked otherwise. Form submissions use `POST`.

---

## Table of Contents

- [Core](#core)
  - [Home](#home)
  - [Dashboard](#dashboard)
  - [Search](#search)
  - [Notifications](#notifications)
- [Catalog](#catalog)
  - [Movies](#movies)
  - [Series](#series)
  - [Collections](#collections)
  - [Bundles](#bundles)
  - [Availability](#availability)
  - [Predictions](#predictions)
- [Customers](#customers)
  - [Customer Management](#customer-management)
  - [Customer Insights](#customer-insights)
  - [Achievements](#achievements)
  - [Referrals](#referrals)
- [Transactions](#transactions)
  - [Rentals](#rentals)
  - [Coupons](#coupons)
  - [Gift Cards](#gift-cards)
- [Social](#social)
  - [Reviews](#reviews)
  - [Watchlist](#watchlist)
  - [Recommendations](#recommendations)
  - [Compare](#compare)
  - [Quiz](#quiz)
  - [Movie Night](#movie-night)
  - [Marathon](#marathon)
- [Data](#data)
  - [Export](#export)
  - [Activity](#activity)

---

## Core

### Home

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/` or `/Home/Index` | Landing page |
| GET | `/Home/About` | About page |
| GET | `/Home/Contact` | Contact page |

### Dashboard

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Dashboard` | Admin dashboard with rental stats, revenue, popular movies |

### Search

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Search?query={q}` | Global search across movies, customers, and rentals |

### Notifications

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Notifications` | All system notifications |
| GET | `/Notifications/Customer/{id}` | Notifications for a specific customer |

---

## Catalog

### Movies

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Movies` | List all movies (paginated) |
| GET | `/Movies/Details/{id}` | Movie detail page |
| GET | `/Movies/Create` | New movie form |
| POST | `/Movies/Create` | Submit new movie |
| GET | `/Movies/Edit/{id}` | Edit movie form |
| POST | `/Movies/Edit/{id}` | Submit movie edit |
| POST | `/Movies/Delete/{id}` | Delete a movie |
| GET | `/Movies/Random` | Random movie suggestion |
| GET | `/Movies/ByReleaseDate` | Movies sorted by release date |

### Series

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Series` | List all movie series |
| GET | `/Series/Details/{id}` | Series detail page with movie list |
| GET | `/Series/Search?query={q}` | Search series |
| GET | `/Series/Create` | New series form |
| POST | `/Series/AddMovie` | Add a movie to a series |
| POST | `/Series/RemoveMovie` | Remove a movie from a series |
| POST | `/Series/MarkWatched` | Mark a movie as watched in series |
| GET | `/Series/Progress/{id}` | Watch progress for a series |
| GET | `/Series/NextUp/{id}` | Next unwatched movie in series |
| GET | `/Series/Stats/{id}` | Series statistics |
| POST | `/Series/Delete/{id}` | Delete a series |

### Collections

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Collections` | List all curated collections |
| GET | `/Collections/Details/{id}` | Collection detail page |
| GET | `/Collections/Create` | New collection form |
| POST | `/Collections/Create` | Submit new collection |
| GET | `/Collections/Edit/{id}` | Edit collection form |
| POST | `/Collections/Edit/{id}` | Submit collection edit |
| POST | `/Collections/AddMovie` | Add movie to collection |
| POST | `/Collections/RemoveMovie` | Remove movie from collection |
| POST | `/Collections/Delete/{id}` | Delete a collection |

### Bundles

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Bundles` | List all rental bundles |
| POST | `/Bundles/Create` | Create a new bundle |
| POST | `/Bundles/Toggle/{id}` | Activate/deactivate a bundle |
| POST | `/Bundles/Delete/{id}` | Delete a bundle |
| GET | `/Bundles/Calculator` | Bundle savings calculator form |
| POST | `/Bundles/Calculator` | Calculate bundle savings |

### Availability

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Availability` | Stock availability dashboard |
| GET | `/Availability/Movie/{id}` | Availability for specific movie |
| GET | `/Availability/ComingSoon` | Upcoming releases |

### Predictions

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Predictions` | AI-powered rental predictions |
| GET | `/Predictions/Details/{id}` | Prediction details for a movie |

---

## Customers

### Customer Management

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Customers` | List all customers |
| GET | `/Customers/Details/{id}` | Customer profile |
| GET | `/Customers/Create` | New customer form |
| POST | `/Customers/Create` | Submit new customer |
| GET | `/Customers/Edit/{id}` | Edit customer form |
| POST | `/Customers/Edit/{id}` | Submit customer edit |
| POST | `/Customers/Delete/{id}` | Delete a customer |

### Customer Insights

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/CustomerInsights?customerId={id}` | Analytics dashboard: loyalty score, spending, genre preferences, rental timeline |

### Achievements

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Achievements` | Achievements overview |
| GET | `/Achievements/Profile/{id}` | Customer achievement profile |
| GET | `/Achievements/Leaderboard` | Top customers by achievement points |
| GET | `/Achievements/Badge/{id}` | Badge details |

### Referrals

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Referrals` | Referral program dashboard |
| POST | `/Referrals/Create` | Create a referral |
| POST | `/Referrals/Convert/{id}` | Convert a referral (mark as used) |

---

## Transactions

### Rentals

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Rentals` | List all rentals |
| GET | `/Rentals/Details/{id}` | Rental detail page |
| GET | `/Rentals/Checkout` | Checkout form (select movie + customer) |
| POST | `/Rentals/Checkout` | Process rental checkout |
| POST | `/Rentals/Return/{id}` | Return a rental |
| POST | `/Rentals/Delete/{id}` | Delete a rental record |
| GET | `/Rentals/Receipt/{id}` | Printable rental receipt |
| GET | `/Rentals/Overdue` | List overdue rentals |

### Coupons

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Coupons` | List all coupons |
| GET | `/Coupons/Create` | New coupon form |
| POST | `/Coupons/Create` | Submit new coupon |
| GET | `/Coupons/Edit/{id}` | Edit coupon form |
| POST | `/Coupons/Edit/{id}` | Submit coupon edit |
| POST | `/Coupons/Toggle/{id}` | Activate/deactivate coupon |
| POST | `/Coupons/Delete/{id}` | Delete a coupon |

### Gift Cards

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/GiftCards` | List all gift cards |
| GET | `/GiftCards/Create` | New gift card form |
| POST | `/GiftCards/Create` | Submit new gift card |
| GET | `/GiftCards/Details/{id}` | Gift card details |
| GET | `/GiftCards/Balance` | Check balance form |
| POST | `/GiftCards/Balance` | Check gift card balance |
| POST | `/GiftCards/Toggle/{id}` | Activate/deactivate gift card |
| POST | `/GiftCards/TopUp` | Add funds to a gift card |

---

## Social

### Reviews

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Reviews` | All reviews |
| GET | `/Reviews/Movie/{id}` | Reviews for a specific movie |
| POST | `/Reviews/Create` | Submit a new review |
| POST | `/Reviews/Delete/{id}` | Delete a review |

### Watchlist

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Watchlist` | Current watchlist |
| GET | `/Watchlist/Add` | Add to watchlist form |
| POST | `/Watchlist/Add` | Add movie to watchlist |
| POST | `/Watchlist/Remove/{id}` | Remove from watchlist |
| POST | `/Watchlist/Clear` | Clear entire watchlist |
| POST | `/Watchlist/UpdatePriority` | Reorder watchlist items |

### Recommendations

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Recommendations` | Personalized movie recommendations |

### Compare

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Compare` | Movie comparison tool (select movies) |
| POST | `/Compare` | Compare selected movies side-by-side |

### Quiz

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Quiz` | Quiz landing page |
| GET | `/Quiz/Start?difficulty={d}&category={c}` | Start a new quiz |
| GET | `/Quiz/Play/{id}` | Play quiz (current question) |
| POST | `/Quiz/Answer` | Submit answer |
| GET | `/Quiz/Results/{id}` | Quiz results page |
| GET | `/Quiz/Leaderboard` | Quiz leaderboard |

### Movie Night

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/MovieNight` | Movie night planner |
| POST | `/MovieNight/Plan` | Generate movie night plan |

### Marathon

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Marathon` | Marathon planner |
| POST | `/Marathon/Plan` | Generate marathon schedule |
| POST | `/Marathon/Suggest` | Get marathon suggestions |

---

## Data

### Export

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Export` | Export options page |
| GET | `/Export/Movies` | Download movies as CSV |
| GET | `/Export/Customers` | Download customers as CSV |
| GET | `/Export/Rentals` | Download rentals as CSV |

### Activity

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Activity` | Recent system activity log |

---

## Error Handling

All controllers return standard HTTP status codes:

| Status | Meaning |
|--------|---------|
| 200 | Success — view rendered |
| 302 | Redirect — after successful POST (PRG pattern) |
| 404 | Resource not found (movie, customer, rental, etc.) |
| 500 | Server error |

Form validation errors are returned inline on the same view with `ModelState` error messages.

---

## Authentication

Vidly currently uses in-memory repositories without authentication. All routes are publicly accessible. For production deployment, add `[Authorize]` attributes to controllers and configure ASP.NET Identity.

---

*Generated from controller analysis. See [docs/CONTROLLERS.md](CONTROLLERS.md) for implementation details.*
