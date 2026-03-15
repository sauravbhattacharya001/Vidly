# Vidly Data Models Reference

Complete reference for the domain model in `Vidly/Models/`.
Organized by business domain — each section documents the entities,
enums, and relationships that drive the feature.

> **Architecture note:** All models use in-memory list-based storage
> (no database). Services create and manage instances at startup.
> See [ARCHITECTURE.md](../ARCHITECTURE.md) for the full system design.

---

## Contents

- [Core Entities](#core-entities)
- [Rentals & Returns](#rentals--returns)
- [Customer Management](#customer-management)
- [Reviews & Ratings](#reviews--ratings)
- [Watchlists & Collections](#watchlists--collections)
- [Inventory & Availability](#inventory--availability)
- [Pricing & Promotions](#pricing--promotions)
- [Reservations](#reservations)
- [Analytics & Insights](#analytics--insights)
- [Social Features](#social-features)
- [Staff & Operations](#staff--operations)
- [Gamification](#gamification)

---

## Core Entities

### Movie

The central entity. Represents a film available for rental.

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `int` | Auto-incremented identifier |
| `Name` | `string` | Required, max 255 chars |
| `ReleaseDate` | `DateTime?` | Optional release date |
| `Genre` | `Genre?` | Enum (see below) |
| `Rating` | `int?` | 1–5 star rating |
| `DailyRate` | `decimal?` | Per-movie rate override ($0.01–$99.99) |
| `IsNewRelease` | `bool` | Computed: within 90 days of release |

### Genre (enum)

```
Action=1, Comedy=2, Drama=3, Horror=4, SciFi=5,
Animation=6, Thriller=7, Romance=8, Documentary=9, Adventure=10
```

### Customer

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `int` | Auto-incremented identifier |
| `Name` | `string` | Required, max 200 chars |
| `Email` | `string` | Optional contact email |
| `Phone` | `string` | Optional phone number |
| `MembershipType` | `MembershipType` | Tier level |

### MembershipType (enum)

```
Basic=1, Silver=2, Gold=3, Platinum=4
```

Higher tiers unlock discounts, priority reservations, and loyalty multipliers.

---

## Rentals & Returns

### Rental

The core transaction entity — a customer renting a movie.

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `int` | Auto-incremented |
| `CustomerId` | `int` | FK → Customer |
| `CustomerName` | `string` | Denormalized for display |
| `MovieId` | `int` | FK → Movie |
| `MovieName` | `string` | Denormalized for display |
| `RentalDate` | `DateTime` | When rented |
| `DueDate` | `DateTime` | When due back |
| `DailyRate` | `decimal` | Rate at time of rental |
| `LateFee` | `decimal` | Accumulated late fees |
| `Status` | `RentalStatus` | Current state |

### RentalStatus (enum)

```
Active=1    — Currently rented out
Overdue=2   — Past due date, accumulating fees
Returned=3  — Returned to inventory
```

### RentalHistoryEntry

Extended history record with timeline events, used by `RentalHistoryService`.

### TimelineEvent / TimelineEventType

Tracks lifecycle events: rented, returned, overdue notice, fee charged, etc.

### RentalTrendReport

Aggregated analytics: `DayOfWeekBreakdown`, `GenreTrend`, `MonthlyVolume`,
`RetentionCohort`, `PeakPeriod`.

---

## Customer Management

### MembershipTier Models

`MembershipTierModels.cs` defines the tier system:

| Model | Purpose |
|-------|---------|
| `TierConfig` | Thresholds and rules for each tier |
| `TierBenefits` | Perks unlocked at each tier |
| `TierEvaluation` | Result of evaluating a customer's tier eligibility |
| `TierChangeRecord` | Audit trail for tier promotions/demotions |
| `TierComparison` | Side-by-side comparison of two tiers |
| `TierDistribution` | Aggregate: how many customers per tier |
| `MembershipReport` | Full membership analytics report |

### Churn Models

Predictive analytics for customer retention:

| Model | Purpose |
|-------|---------|
| `ChurnRisk` | Risk level enum |
| `ChurnProfile` | Per-customer churn assessment |
| `ChurnFactors` | Contributing factors (rental frequency, recency, etc.) |
| `ChurnSummary` | Aggregate churn statistics |
| `TierChurnStats` | Churn breakdown by membership tier |
| `ChurnConfig` | Configurable thresholds for churn detection |

### Referral Models

| Model | Purpose |
|-------|---------|
| `Referral` | Individual referral record |
| `ReferralStatus` | Pending → Completed → Rewarded |
| `ReferralSummary` | Per-customer referral stats |
| `ReferralLeaderboardEntry` | Top referrers ranking |
| `ReferralProgramStats` | Program-wide metrics |
| `MonthlyReferralTrend` | Referral volume over time |

### Subscription Models

| Model | Purpose |
|-------|---------|
| `SubscriptionPlan` | Plan definition (type, price, limits) |
| `CustomerSubscription` | Active subscription instance |
| `SubscriptionBillingEvent` | Payment/renewal history |
| `SubscriptionPlanType` | Monthly, Quarterly, Annual |
| `SubscriptionStatus` | Active, Paused, Cancelled, Expired |

---

## Reviews & Ratings

### Review

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `int` | Auto-incremented |
| `CustomerId` | `int` | FK → Customer |
| `MovieId` | `int` | FK → Movie |
| `Stars` | `int` | 1–5 rating |
| `ReviewText` | `string` | Written review |
| `CreatedDate` | `DateTime` | When posted |
| `CustomerName` | `string` | Denormalized |
| `MovieName` | `string` | Denormalized |

### Survey Models

Post-rental feedback: `RentalSurvey`, `SurveyCategory`, `RentAgainResponse`,
`NpsCategory`, `SurveyReport`, `SurveyTrend`, `SurveyInvitation`.

---

## Watchlists & Collections

### WatchlistItem

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `int` | Auto-incremented |
| `CustomerId` | `int` | FK → Customer |
| `MovieId` | `int` | FK → Movie |
| `AddedDate` | `DateTime` | When added |
| `Note` | `string` | Personal note |
| `Priority` | `WatchlistPriority` | Urgency level |

### MovieCollection / CollectionItem

Curated movie lists: `Id`, `Name`, `Description`, `CreatedAt`, `IsPublished`.
Each `CollectionItem` links a movie to a collection with `SortOrder` and `Note`.

### MovieSeries Models

Franchise tracking: `MovieSeries`, `SeriesEntry`, `SeriesProgress`,
`SeriesProgressSummary`, `SeriesEntryDetail`.

---

## Inventory & Availability

### MovieStock / InventorySummary

| Model | Purpose |
|-------|---------|
| `MovieStock` | Per-movie copy count and availability |
| `StockLevel` | Enum: InStock, LowStock, OutOfStock |
| `InventorySummary` | Aggregate inventory health metrics |
| `GenreStock` | Stock levels broken down by genre |
| `AvailabilityForecast` | Predicted availability based on return dates |

### Availability Models

`AvailabilityModels.cs` provides calendar-based availability:

- `MovieAvailability` — current availability status per movie
- `CalendarDay` / `CalendarEvent` — day-by-day availability calendar
- `AvailabilitySummary` — aggregate availability metrics

### Condition Models

Physical media tracking:

| Model | Purpose |
|-------|---------|
| `ConditionGrade` | Mint, Good, Fair, Poor, Damaged |
| `ConditionInspection` | Check-in/check-out inspection record |
| `RentalConditionDelta` | Condition change during a rental |
| `CopyConditionStatus` | Current state of a specific copy |
| `RenterRiskProfile` | Customer's track record with media care |
| `ConditionReport` | Aggregate condition analytics |

### Lost and Found Models

`LostItem`, `LostItemClaim`, `LostAndFoundReport` — tracking items left behind.

---

## Pricing & Promotions

### BundleDeal

Multi-movie rental discounts:

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `int` | Auto-incremented |
| `Name` | `string` | Deal name |
| `MinMovies` / `MaxMovies` | `int` | Required movie count range |
| `DiscountType` | `BundleDiscountType` | Percentage or FixedAmount |
| `DiscountValue` | `decimal` | Discount amount |
| `IsActive` | `bool` | Whether deal is currently available |
| `TimesUsed` | `int` | Usage counter |

`BundleApplyResult` shows the breakdown after applying a deal.

### Coupon

| Property | Type | Notes |
|----------|------|-------|
| `Code` | `string` | Unique redemption code |
| `DiscountType` | `DiscountType` | Percentage or FixedAmount |
| `DiscountValue` | `decimal` | Discount amount |
| `MinimumOrderAmount` | `decimal` | Minimum spend to qualify |
| `ValidFrom` / `ValidUntil` | `DateTime` | Validity window |
| `IsActive` | `bool` | Active flag |

### GiftCard / GiftCardTransaction

Stored-value cards: `Code`, `OriginalValue`, `Balance`, `PurchaserName`,
`RecipientName`, `Message`. Transactions track purchases, redemptions,
and refunds with running balance.

### Insurance Models

Rental insurance policies and claims: `InsurancePolicy`, `InsuranceClaim`,
`InsuranceTier` (Basic, Standard, Premium), `InsuranceAnalytics`.

---

## Reservations

### Reservation

Queue system for out-of-stock movies:

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `int` | Auto-incremented |
| `CustomerId` | `int` | FK → Customer |
| `MovieId` | `int` | FK → Movie |
| `ReservedDate` | `DateTime` | When reserved |
| `QueuePosition` | `int` | Position in line |
| `Status` | `ReservationStatus` | Current state |

### ReservationStatus (enum)

```
Waiting=1      — In queue, movie still rented
Ready=2        — Movie returned, pickup window open
Fulfilled=3    — Customer picked up
Cancelled=4    — Customer cancelled
Expired=5      — Pickup window expired
```

---

## Analytics & Insights

### Dashboard Models

`DashboardData` aggregates: `MovieRankEntry`, `CustomerRankEntry`,
`GenreRevenueEntry`, `MembershipRevenueEntry`, `MonthlyRevenueEntry`.

### Movie Insight Models

Deep analytics per movie: `MovieInsight`, `RentalSummary`, `RevenueBreakdown`,
`CustomerDemographicBreakdown`, `MonthlyRentalPoint`, `PerformanceScore`,
`MovieInsightComparison`.

### Revenue Models

Financial reporting: `RevenueReport`, `GenreRevenue`, `MembershipRevenue`,
`MonthlyRevenue`, `TopCustomerRevenue`, `TopMovieRevenue`,
`PeriodComparison`, `RevenueForecast`.

### Late Return Prediction

ML-style risk assessment: `LateReturnPrediction`, `RiskFactor`, `RiskLevel`,
`PredictionSummary`.

### Rental Trend Models

Time-series analytics: `DayOfWeekBreakdown`, `GenreTrend`, `MonthlyVolume`,
`RetentionCohort`, `PeakPeriod`, `RentalTrendReport`.

---

## Social Features

### Movie Club Models

Community watch groups:

| Model | Purpose |
|-------|---------|
| `MovieClub` | Club entity with name, description, status |
| `ClubMembership` | Member ↔ club link with role |
| `ClubWatchlistItem` | Shared club watchlist |
| `ClubPoll` / `ClubPollOption` | Voting on next movie |
| `ClubMeeting` | Scheduled watch sessions |
| `ClubStats` | Club activity metrics |
| `ClubStatus` | Active, Archived |
| `ClubRole` | Member, Moderator, Owner |

### Movie Night Models

Themed movie night planning: `MovieNightTheme`, `MovieNightRequest`,
`MovieNightPlan`, `MovieNightSlot`.

### Marathon Models

Binge-watch planning: `MarathonRequest`, `MarathonPlan`, `MarathonEntry`,
`MarathonOrder`.

### Movie Request Models

Customer-driven acquisition requests: `MovieRequest`, `MovieRequestVote`,
`MovieRequestStatus`, `TrendingRequest`, `MovieRequestStats`.

---

## Staff & Operations

### Staff Models

Employee management: `StaffMember`, `StaffRole`, `StaffTransaction`,
`StaffTransactionType`, `StaffPerformanceReport`, `StaffRankingEntry`,
`TeamPerformanceSummary`.

### Scheduling Models

Shift management: `Shift`, `ShiftType`, `StaffAvailability`,
`ShiftSwapRequest`, `SwapRequestType`, `SwapRequestStatus`,
`CoverageReport`, `StaffWeeklySummary`.

### Store Event Models

In-store events: `StoreEvent`, `EventRsvp`, `EventSuggestion`, `EventStats`,
`EventTypeBreakdown`, `StoreEventType`, `StoreEventStatus`.

### Announcement Models

Staff/customer communications: `Announcement`, `AnnouncementCategory`,
`AnnouncementPriority`, `AnnouncementStatus`, `AnnouncementAcknowledgment`,
`AnnouncementPin`, `AnnouncementAnalytics`, `AnnouncementTrend`,
`AnnouncementFilter`.

### Dispute Models

Billing dispute handling: `Dispute`, `DisputeType`, `DisputeStatus`,
`DisputePriority`.

### Franchise Models

Multi-movie franchise tracking: `Franchise`, `FranchiseProgress`,
`FranchiseReport`, `FranchiseDropoff`, `FranchiseRecommendation`,
`RecommendationType`.

---

## Gamification

### Movie Tags

Content tagging system: `MovieTag`, `MovieTagAssignment`, `TagUsageStats`,
`TaggingSummary`. Tags have colors and staff-pick flags.

### Quiz Models

Movie trivia system: `QuizQuestion`, `QuizAnswer`, `QuizSession`,
`LeaderboardEntry`, `QuizStats`, `DailyChallenge`, `QuizDifficulty`
(Easy, Medium, Hard), `QuizCategory`, `QuizStatus`.

### Search Models

`GlobalSearchResults` — unified search across movies, customers, and rentals.

---

## Entity Relationships

```
Customer ──1:N──► Rental ◄──N:1── Movie
    │                                │
    ├──1:N──► Review ◄──N:1──────────┤
    ├──1:N──► WatchlistItem ◄──N:1───┤
    ├──1:N──► Reservation ◄──N:1─────┘
    ├──1:N──► CustomerSubscription
    ├──1:N──► Referral
    ├──1:N──► ClubMembership ──N:1──► MovieClub
    │
Movie ──1:N──► CollectionItem ──N:1──► MovieCollection
    ├──1:N──► MovieTagAssignment ──N:1──► MovieTag
    ├──1:N──► SeriesEntry ──N:1──► MovieSeries
    ├──1:N──► MovieStock
    │
Rental ──1:1──► RentalHistoryEntry
    ├──1:N──► TimelineEvent
    ├──1:1──► ConditionInspection
    ├──0:1──► InsurancePolicy ──1:N──► InsuranceClaim
    ├──0:1──► RentalSurvey
    ├──0:1──► LateReturnPrediction
```

---

*See also: [CONTROLLERS.md](CONTROLLERS.md) · [SERVICES.md](SERVICES.md) · [ARCHITECTURE.md](../ARCHITECTURE.md)*
