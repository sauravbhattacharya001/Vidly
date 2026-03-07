# Vidly Services API Reference

Complete reference for all 33 service classes in `Vidly/Services/`.
Each service is instantiated with in-memory data stores and provides
domain-specific business logic for the Vidly rental platform.

> **Architecture note:** Services follow a stateful, in-memory pattern
> with list-based storage. See [ARCHITECTURE.md](../ARCHITECTURE.md)
> for the full system design.

---

## Contents

- [Core Rental Operations](#core-rental-operations)
  - [PricingService](#pricingservice)
  - [RentalReturnService](#rentalreturnservice)
  - [RentalHistoryService](#rentalhistoryservice)
  - [InventoryService](#inventoryservice)
  - [ReservationService](#reservationservice)
- [Customer Management](#customer-management)
  - [MembershipTierService](#membershiptierservice)
  - [LoyaltyPointsService](#loyaltypointsservice)
  - [CustomerSegmentationService](#customersegmentationservice)
  - [CustomerActivityService](#customeractivityservice)
  - [ChurnPredictorService](#churnpredictorservice)
  - [ParentalControlService](#parentalcontrolservice)
- [Discovery & Content](#discovery--content)
  - [RecommendationService](#recommendationservice)
  - [MovieSimilarityService](#moviesimilarityservice)
  - [MovieInsightsService](#movieinsightsservice)
  - [MovieLifecycleService](#movielifecycleservice)
  - [CollectionService](#collectionservice)
  - [TaggingService](#taggingservice)
  - [ReviewService](#reviewservice)
  - [WatchlistService](#watchlistservice)
  - [MovieNightPlannerService](#movienightplannerservice)
  - [FranchiseTrackerService](#franchisetrackerservice)
  - [MovieComparisonService](#moviecomparisonservice)
- [Commerce & Promotions](#commerce--promotions)
  - [CouponService](#couponservice)
  - [GiftCardService](#giftcardservice)
  - [BundleService](#bundleservice)
  - [SeasonalPromotionService](#seasonalpromotionservice)
  - [DisputeResolutionService](#disputeresolutionservice)
- [Analytics & Operations](#analytics--operations)
  - [RentalForecastService](#rentalforecastservice)
  - [RevenueAnalyticsService](#revenueanalyticsservice)
  - [RatingEngineService](#ratingengineservice)
  - [DashboardService](#dashboardservice)
  - [StaffPerformanceService](#staffperformanceservice)
  - [NotificationService](#notificationservice)

---

## Core Rental Operations

Pricing, returns, history, inventory, and reservations — the transactional heart of Vidly.

### PricingService

_Calculates rental pricing with membership discounts, late fee policies_

**Source:** `Vidly/Services/PricingService.cs` (21 KB) · **Methods:** 5

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetBenefits` | `static MembershipBenefits` | `MembershipType tier` |
| `CalculateRentalPrice` | `RentalPriceQuote` | `int movieId, int customerId, int? rentalDays = null` |
| `CalculateLateFee` | `LateFeeResult` | `Rental rental` |
| `CompareTiers` | `List<PricingTierComparison>` | `int movieId, int? rentalDays = null` |
| `GetBillingSummary` | `CustomerBillingSummary` | `int customerId` |

### RentalReturnService

_Orchestrates the movie return process — the service-layer counterpart_

**Source:** `Vidly/Services/RentalReturnService.cs` (28 KB) · **Methods:** 12

| Method | Returns | Parameters |
|--------|---------|------------|
| `ProcessReturn` | `ReturnReceipt` | `int rentalId, ReturnCondition condition = ReturnCondition.Good, DateTime? ret...` |
| `ProcessBatchReturn` | `BatchReturnResult` | `IEnumerable<int> rentalIds, ReturnCondition condition = ReturnCondition.Good,...` |
| `CalculateLateFee` | `ReturnLateFeeBreakdown` | `DateTime dueDate, DateTime returnDate, decimal dailyRate, MembershipType tier` |
| `GetGracePeriod` | `static int` | `MembershipType tier` |
| `GetTierLateDiscount` | `static decimal` | `MembershipType tier` |
| `GetDamageCharge` | `static decimal` | `ReturnCondition condition` |
| `CalculateLoyaltyPoints` | `static int` | `ReturnLateFeeBreakdown lateFee, ReturnCondition condition, MembershipType tier` |
| `GetTierPointMultiplier` | `static decimal` | `MembershipType tier` |
| `GetOverdueRentals` | `List<OverdueRentalInfo>` | `—` |
| `GetOverdueSummary` | `OverdueSummary` | `—` |
| `GetCustomerReturnProfile` | `CustomerReturnProfile` | `int customerId` |
| `EstimateCurrentLateFee` | `LateFeeEstimate` | `int rentalId` |

### RentalHistoryService

_Interface for rental history querying and analytics._

**Source:** `Vidly/Services/RentalHistoryService.cs` (27 KB) · **Methods:** 8

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetRentalHistory` | `IReadOnlyList<RentalHistoryEntry>` | `int? customerId, int? movieId, DateTime? from, DateTime? to, RentalStatus? st...` |
| `GetCustomerTimeline` | `IReadOnlyList<TimelineEvent>` | `int customerId` |
| `GetPopularTimes` | `PopularTimesResult` | `—` |
| `GetRetentionMetrics` | `RetentionMetrics` | `—` |
| `GetInventoryForecast` | `InventoryForecast` | `int daysAhead` |
| `GetLoyaltyScore` | `LoyaltyResult` | `int customerId` |
| `GetSeasonalTrends` | `IReadOnlyList<SeasonalTrend>` | `—` |
| `GenerateReport` | `RentalReport` | `ReportType type` |

### InventoryService

_Manages movie inventory — stock levels, availability checks,_

**Source:** `Vidly/Services/InventoryService.cs` (12 KB) · **Methods:** 9

| Method | Returns | Parameters |
|--------|---------|------------|
| `SetStock` | `void` | `int movieId, int copies` |
| `GetStockCount` | `int` | `int movieId` |
| `GetMovieStock` | `MovieStock` | `int movieId` |
| `GetAllStock` | `List<MovieStock>` | `—` |
| `GetByStockLevel` | `List<MovieStock>` | `StockLevel level` |
| `IsAvailable` | `bool` | `int movieId` |
| `GetSummary` | `InventorySummary` | `—` |
| `ForecastAvailability` | `List<AvailabilityForecast>` | `int movieId, int days = 7` |
| `GetRestockingNeeds` | `List<MovieStock>` | `int limit = 10` |

### ReservationService

_Manages movie reservations (holds). Customers can reserve movies_

**Source:** `Vidly/Services/ReservationService.cs` (19 KB) · **Methods:** 13

| Method | Returns | Parameters |
|--------|---------|------------|
| `PlaceReservation` | `Reservation` | `int customerId, int movieId` |
| `CancelReservation` | `Reservation` | `int reservationId` |
| `NotifyNextInQueue` | `Reservation` | `int movieId` |
| `FulfillReservation` | `Reservation` | `int reservationId` |
| `GetCustomerReservations` | `IReadOnlyList<Reservation>` | `int customerId` |
| `GetMovieQueue` | `IReadOnlyList<Reservation>` | `int movieId` |
| `GetQueuePosition` | `int` | `int customerId, int movieId` |
| `EstimateWaitDays` | `int` | `int customerId, int movieId` |
| `HasReservations` | `bool` | `int movieId` |
| `Search` | `IReadOnlyList<Reservation>` | `string query, ReservationStatus? status = null` |
| `GetStats` | `ReservationStats` | `—` |
| `GetQueueSummary` | `string` | `int movieId` |
| `ProcessExpiredReservations` | `int` | `—` |


## Customer Management

Membership tiers, loyalty, segmentation, churn prediction, and content controls.

### MembershipTierService

_Manages customer membership tiers based on rental activity._

**Source:** `Vidly/Services/MembershipTierService.cs` (20 KB) · **Methods:** 18

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetBenefits` | `TierBenefits` | `MembershipType tier` |
| `CompareTiers` | `TierComparison` | `—` |
| `EvaluateCustomer` | `TierEvaluation` | `int customerId` |
| `EvaluateCustomer` | `TierEvaluation` | `int customerId, DateTime referenceDate` |
| `EvaluateAllCustomers` | `List<TierEvaluation>` | `—` |
| `EvaluateAllCustomers` | `List<TierEvaluation>` | `DateTime referenceDate` |
| `ApplyTierChanges` | `List<TierChangeRecord>` | `—` |
| `ApplyTierChanges` | `List<TierChangeRecord>` | `DateTime referenceDate` |
| `GetChangeHistory` | `List<TierChangeRecord>` | `—` |
| `GetCustomerHistory` | `List<TierChangeRecord>` | `int customerId` |
| `GetTierDistribution` | `TierDistribution` | `—` |
| `GetNearUpgradeCustomers` | `List<TierEvaluation>` | `double threshold = 0.75` |
| `GetAtRiskCustomers` | `List<TierEvaluation>` | `—` |
| `GetMembershipReport` | `MembershipReport` | `int customerId` |
| `GetDiscountedRate` | `decimal` | `int customerId, decimal baseRate` |
| `CanRentMore` | `bool` | `int customerId` |
| `GetRemainingSlots` | `int` | `int customerId` |
| `GenerateSummaryReport` | `string` | `—` |

### LoyaltyPointsService

_Manages a loyalty points program where customers earn points for rentals_

**Source:** `Vidly/Services/LoyaltyPointsService.cs` (14 KB) · **Methods:** 9

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetTierMultiplier` | `static decimal` | `MembershipType tier` |
| `EarnPointsForRental` | `PointsTransaction` | `int rentalId` |
| `RedeemPoints` | `PointsTransaction` | `int customerId, RewardType reward` |
| `GetBalance` | `int` | `int customerId` |
| `GetHistory` | `List<PointsTransaction>` | `int customerId` |
| `GetSummary` | `LoyaltySummary` | `int customerId` |
| `GetLeaderboard` | `List<LeaderboardEntry>` | `int top = 10` |
| `GetRewardCost` | `static int` | `RewardType reward` |
| `GetRewardDescription` | `static string` | `RewardType reward` |

### CustomerSegmentationService

_Customer segmentation using RFM (Recency, Frequency, Monetary) analysis._

**Source:** `Vidly/Services/CustomerSegmentationService.cs` (15 KB) · **Methods:** 7

| Method | Returns | Parameters |
|--------|---------|------------|
| `AnalyzeAll` | `IReadOnlyList<RfmProfile>` | `DateTime asOfDate` |
| `AnalyzeCustomer` | `RfmProfile` | `int customerId, DateTime asOfDate` |
| `GetBySegment` | `IReadOnlyList<RfmProfile>` | `CustomerSegment segment, DateTime asOfDate` |
| `GetSummary` | `SegmentSummary` | `DateTime asOfDate` |
| `GetAtRiskCustomers` | `IReadOnlyList<RfmProfile>` | `DateTime asOfDate` |
| `GetMarketingRecommendations` | `IReadOnlyDictionary<CustomerSegment, string>` | `—` |
| `CompareSegments` | `IReadOnlyList<SegmentMigration>` | `DateTime periodA, DateTime periodB` |

### CustomerActivityService

_Generates comprehensive rental history and activity reports for customers._

**Source:** `Vidly/Services/CustomerActivityService.cs` (18 KB) · **Methods:** 1

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetActivityReport` | `CustomerActivityReport` | `int customerId` |

### ChurnPredictorService

_Predicts customer churn risk using multi-factor analysis of rental behavior._

**Source:** `Vidly/Services/ChurnPredictorService.cs` (18 KB) · **Methods:** 6

| Method | Returns | Parameters |
|--------|---------|------------|
| `Analyze` | `ChurnProfile` | `int customerId, DateTime asOfDate` |
| `AnalyzeAll` | `IReadOnlyList<ChurnProfile>` | `DateTime asOfDate` |
| `GetSummary` | `ChurnSummary` | `DateTime asOfDate, int topN = 10` |
| `GetByRiskLevel` | `IReadOnlyList<ChurnProfile>` | `ChurnRisk level, DateTime asOfDate` |
| `GetAboveThreshold` | `IReadOnlyList<ChurnProfile>` | `double threshold, DateTime asOfDate` |
| `GetWinnableCustomers` | `IReadOnlyList<ChurnProfile>` | `DateTime asOfDate, int minLifetimeRentals = 5` |

### ParentalControlService

_MPAA-style content rating for movies._

**Source:** `Vidly/Services/ParentalControlService.cs` (22 KB) · **Methods:** 21

| Method | Returns | Parameters |
|--------|---------|------------|
| `Allowed` | `static ContentAccessResult` | `—` |
| `AllowedWithWarnings` | `static ContentAccessResult` | `List<string> warnings` |
| `Blocked` | `static ContentAccessResult` | `string reason, bool canOverride = true` |
| `SetMovieRating` | `MovieContentProfile` | `int movieId, ContentRating rating, ContentAdvisory advisories = ContentAdviso...` |
| `GetMovieProfile` | `MovieContentProfile` | `int movieId` |
| `SuggestRating` | `ContentRating` | `Genre genre` |
| `SuggestAdvisories` | `ContentAdvisory` | `Genre genre` |
| `AutoRateUnratedMovies` | `int` | `—` |
| `EnableControls` | `ParentalControlProfile` | `int customerId, ContentRating maxRating, string pin = null, ContentAdvisory w...` |
| `DisableControls` | `bool` | `int customerId, string pin = null` |
| `GetControlProfile` | `ParentalControlProfile` | `int customerId` |
| `UpdatePin` | `bool` | `int customerId, string oldPin, string newPin` |
| `CheckAccess` | `ContentAccessResult` | `int customerId, int movieId` |
| `TryOverrideWithPin` | `bool` | `int customerId, string pin` |
| `GetAllowedMovies` | `IReadOnlyList<Movie>` | `int customerId` |
| `GetFamilyFriendlyMovies` | `IReadOnlyList<Movie>` | `—` |
| `GetMoviesByRating` | `IReadOnlyList<Movie>` | `ContentRating rating` |
| `GetMoviesByAdvisory` | `IReadOnlyList<Movie>` | `ContentAdvisory advisory` |
| `GetRatingDistribution` | `Dictionary<ContentRating, int>` | `—` |
| `GetAdvisoryDistribution` | `Dictionary<ContentAdvisory, int>` | `—` |
| `GetFamilyFriendlyPercent` | `double` | `—` |


## Discovery & Content

Recommendations, search, reviews, collections, and content organization.

### RecommendationService

_Generates personalized movie recommendations for customers based on_

**Source:** `Vidly/Services/RecommendationService.cs` (15 KB) · **Methods:** 1

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetRecommendations` | `RecommendationResult` | `int customerId, int maxRecommendations = 10` |

### MovieSimilarityService

_Finds similar movies using a multi-signal similarity scoring approach:_

**Source:** `Vidly/Services/MovieSimilarityService.cs` (19 KB) · **Methods:** 3

| Method | Returns | Parameters |
|--------|---------|------------|
| `FindSimilar` | `SimilarityResult` | `int movieId, int maxResults = 10` |
| `Compare` | `MovieComparison` | `int movieId1, int movieId2` |
| `GetSimilarityMatrix` | `SimilarityMatrix` | `—` |

### MovieInsightsService

_Per-movie deep analytics: rental trends, customer demographics,_

**Source:** `Vidly/Services/MovieInsightsService.cs` (18 KB) · **Methods:** 3

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetInsight` | `MovieInsight` | `int movieId` |
| `GetAllInsights` | `IReadOnlyList<MovieInsight>` | `—` |
| `Compare` | `MovieInsightComparison` | `int movieIdA, int movieIdB` |

### MovieLifecycleService

_Tracks movie lifecycle stages (NewRelease → Trending → Catalog → Archive)_

**Source:** `Vidly/Services/MovieLifecycleService.cs` (25 KB) · **Methods:** 8

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetProfile` | `MovieLifecycleProfile` | `int movieId, DateTime asOfDate` |
| `GetAllProfiles` | `IReadOnlyList<MovieLifecycleProfile>` | `DateTime asOfDate` |
| `GetPricingRecommendation` | `PricingRecommendation` | `int movieId, DateTime asOfDate` |
| `GetPricingByStage` | `IReadOnlyList<PricingRecommendation>` | `LifecycleStage stage, DateTime asOfDate` |
| `GetRetirementCandidates` | `IReadOnlyList<RetirementCandidate>` | `DateTime asOfDate` |
| `GetRestockSuggestions` | `IReadOnlyList<RestockSuggestion>` | `DateTime asOfDate` |
| `GetReport` | `LifecycleReport` | `DateTime asOfDate` |
| `GetTransitionAlerts` | `IReadOnlyList<TransitionAlert>` | `DateTime asOfDate` |

### CollectionService

_Business logic for movie collections — summaries, popularity, stats, and suggestions._

**Source:** `Vidly/Services/CollectionService.cs` (7 KB) · **Methods:** 4

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetCollectionSummary` | `CollectionSummary` | `int collectionId` |
| `GetPopularCollections` | `IReadOnlyList<MovieCollection>` | `int count` |
| `GetCollectionStats` | `CollectionStats` | `—` |
| `SuggestMovies` | `IReadOnlyList<Movie>` | `int collectionId` |

### TaggingService

_Movie Tagging Service — flexible, user-defined tags for movies_

**Source:** `Vidly/Services/TaggingService.cs` (25 KB) · **Methods:** 26

| Method | Returns | Parameters |
|--------|---------|------------|
| `CreateTag` | `MovieTag` | `string name, string description = null, string color = null, bool isStaffPick...` |
| `UpdateTag` | `MovieTag` | `int tagId, string name = null, string description = null, string color = null...` |
| `DeactivateTag` | `void` | `int tagId` |
| `ReactivateTag` | `void` | `int tagId` |
| `DeleteTag` | `int` | `int tagId` |
| `GetTag` | `MovieTag` | `int tagId` |
| `GetAllTags` | `IReadOnlyList<MovieTag>` | `bool includeInactive = false` |
| `SearchTags` | `IReadOnlyList<MovieTag>` | `string query` |
| `TagMovie` | `MovieTagAssignment` | `int tagId, int movieId, string appliedBy = null` |
| `UntagMovie` | `bool` | `int tagId, int movieId` |
| `GetMovieTags` | `IReadOnlyList<MovieTagAssignment>` | `int movieId` |
| `GetMoviesByTag` | `IReadOnlyList<Movie>` | `int tagId` |
| `GetMoviesByAllTags` | `IReadOnlyList<Movie>` | `IEnumerable<int> tagIds` |
| `GetMoviesByAnyTag` | `IReadOnlyList<Movie>` | `IEnumerable<int> tagIds` |
| `BulkTagMovies` | `int` | `int tagId, IEnumerable<int> movieIds, string appliedBy = null` |
| `BulkUntagMovies` | `int` | `int tagId, IEnumerable<int> movieIds` |
| `BulkTagSingleMovie` | `int` | `int movieId, IEnumerable<int> tagIds, string appliedBy = null` |
| `GetStaffPicks` | `IReadOnlyList<Movie>` | `—` |
| `PromoteToStaffPick` | `void` | `int tagId` |
| `DemoteFromStaffPick` | `void` | `int tagId` |
| `GetTagCloud` | `IReadOnlyList<TagUsageStats>` | `—` |
| `GetPopularTags` | `IReadOnlyList<TagUsageStats>` | `int limit = 10` |
| `GetSummary` | `TaggingSummary` | `—` |
| `SuggestTagsForMovie` | `IReadOnlyList<string>` | `int movieId` |
| `FindRelatedMovies` | `IReadOnlyList<Movie>` | `int movieId, int limit = 10` |
| `MergeTags` | `int` | `int sourceTagId, int targetTagId` |

### ReviewService

_Business logic for movie reviews — enrichment, stats, and moderation._

**Source:** `Vidly/Services/ReviewService.cs` (10 KB) · **Methods:** 8

| Method | Returns | Parameters |
|--------|---------|------------|
| `SubmitReview` | `Review` | `int customerId, int movieId, int stars, string reviewText` |
| `GetMovieReviews` | `IReadOnlyList<Review>` | `int movieId` |
| `GetCustomerReviews` | `IReadOnlyList<Review>` | `int customerId` |
| `GetMovieStats` | `ReviewStats` | `int movieId` |
| `GetTopRated` | `IReadOnlyList<MovieRating>` | `int count = 10, int minReviews = 1` |
| `GetSummary` | `ReviewSummary` | `—` |
| `DeleteReview` | `bool` | `int reviewId` |
| `Enrich` | `IReadOnlyList<Review>` | `IReadOnlyList<Review> reviews` |

### WatchlistService

_Business logic for customer watchlists — smart prioritization,_

**Source:** `Vidly/Services/WatchlistService.cs` (18 KB) · **Methods:** 7

| Method | Returns | Parameters |
|--------|---------|------------|
| `AddToWatchlist` | `WatchlistItem` | `int customerId, int movieId, string note = null, WatchlistPriority? priority ...` |
| `GetSmartWatchlist` | `IReadOnlyList<WatchlistRecommendation>` | `int customerId` |
| `GetTrendingMovies` | `IReadOnlyList<TrendingWatchlistMovie>` | `int limit = 10` |
| `CompareWatchlists` | `WatchlistComparison` | `int customerIdA, int customerIdB` |
| `GetInsights` | `WatchlistInsights` | `int customerId` |
| `SetPriority` | `void` | `int watchlistItemId, WatchlistPriority newPriority` |
| `BulkAdd` | `BulkAddResult` | `int customerId, IEnumerable<int> movieIds, WatchlistPriority priority = Watch...` |

### MovieNightPlannerService

_Generates themed movie night plans from the catalog. Selects and orders_

**Source:** `Vidly/Services/MovieNightPlannerService.cs` (19 KB) · **Methods:** 3

| Method | Returns | Parameters |
|--------|---------|------------|
| `GeneratePlan` | `MovieNightPlan` | `MovieNightRequest request` |
| `GenerateAlternatives` | `List<MovieNightPlan>` | `MovieNightRequest baseRequest, int count = 3` |
| `GetAvailableThemes` | `List<ThemeOption>` | `—` |

### FranchiseTrackerService

_Manages movie franchises/series — creation, customer progress tracking,_

**Source:** `Vidly/Services/FranchiseTrackerService.cs` (17 KB) · **Methods:** 16

| Method | Returns | Parameters |
|--------|---------|------------|
| `Create` | `Franchise` | `string name, List<int> movieIds, string description = null, int? startYear = ...` |
| `GetById` | `Franchise` | `int id` |
| `GetAll` | `List<Franchise>` | `—` |
| `Search` | `List<Franchise>` | `string query` |
| `AddMovie` | `Franchise` | `int franchiseId, int movieId, int? position = null` |
| `RemoveMovie` | `Franchise` | `int franchiseId, int movieId` |
| `ReorderMovies` | `Franchise` | `int franchiseId, List<int> newOrder` |
| `Delete` | `bool` | `int franchiseId` |
| `GetProgress` | `FranchiseProgress` | `int customerId, Franchise franchise, List<Rental> rentals` |
| `GetAllProgress` | `List<FranchiseProgress>` | `int customerId, List<Rental> rentals` |
| `GetReport` | `FranchiseReport` | `Franchise franchise, List<Rental> rentals, List<Movie> movies` |
| `GetRecommendations` | `List<FranchiseRecommendation>` | `int customerId, List<Rental> rentals, List<Movie> movies, int maxResults = 5` |
| `FindByMovie` | `List<Franchise>` | `int movieId` |
| `FindByTag` | `List<Franchise>` | `string tag` |
| `GetPopularFranchises` | `List<Franchise>` | `List<Rental> rentals, int top = 10` |
| `GenerateSummary` | `string` | `FranchiseReport report` |

### MovieComparisonService

_Compares movies side-by-side across multiple dimensions:_

**Source:** `Vidly/Services/MovieComparisonService.cs` (7 KB) · **Methods:** 2

| Method | Returns | Parameters |
|--------|---------|------------|
| `Compare` | `MovieComparisonResult` | `IEnumerable<int> movieIds` |
| `GetAvailableMovies` | `IReadOnlyList<Movie>` | `—` |


## Commerce & Promotions

Coupons, gift cards, bundles, seasonal promotions, and dispute resolution.

### CouponService

_Validates and applies promotional coupons to rental checkouts._

**Source:** `Vidly/Services/CouponService.cs` (5 KB) · **Methods:** 4

| Method | Returns | Parameters |
|--------|---------|------------|
| `Validate` | `CouponValidationResult` | `string code, decimal rentalSubtotal` |
| `Apply` | `decimal` | `string code, decimal rentalSubtotal` |
| `Fail` | `static CouponValidationResult` | `string message` |
| `Success` | `static CouponValidationResult` | `Coupon coupon, decimal discountAmount` |

### GiftCardService

_Service for gift card operations: creation, balance checks, redemption, and top-ups._

**Source:** `Vidly/Services/GiftCardService.cs` (9 KB) · **Methods:** 7

| Method | Returns | Parameters |
|--------|---------|------------|
| `GenerateCode` | `string` | `—` |
| `Create` | `GiftCard` | `decimal value, string purchaserName, string recipientName = null, string mess...` |
| `CheckBalance` | `GiftCardBalanceResult` | `string code` |
| `Redeem` | `GiftCardRedemptionResult` | `string code, decimal amount, string description = null` |
| `TopUp` | `GiftCardRedemptionResult` | `string code, decimal amount` |
| `Fail` | `static GiftCardBalanceResult` | `string message` |
| `Fail` | `static GiftCardRedemptionResult` | `string message` |

### BundleService

_Manages bundle deals and calculates discounts for multi-movie rentals._

**Source:** `Vidly/Services/BundleService.cs` (11 KB) · **Methods:** 9

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetAll` | `IReadOnlyList<BundleDeal>` | `—` |
| `GetActive` | `IReadOnlyList<BundleDeal>` | `—` |
| `GetById` | `BundleDeal` | `int id` |
| `Add` | `BundleDeal` | `BundleDeal bundle` |
| `Update` | `BundleDeal` | `BundleDeal bundle` |
| `Remove` | `void` | `int id` |
| `FindBestBundle` | `BundleApplyResult` | `IReadOnlyList<Movie> movies, IDictionary<int, decimal> dailyRates, int rental...` |
| `RecordUsage` | `void` | `int bundleId` |
| `GetStats` | `BundleStats` | `—` |

### SeasonalPromotionService

_Manages calendar-driven seasonal promotions that automatically apply_

**Source:** `Vidly/Services/SeasonalPromotionService.cs` (25 KB) · **Methods:** 16

| Method | Returns | Parameters |
|--------|---------|------------|
| `CreatePromotion` | `SeasonalPromotion` | `string name, DateTime startDate, DateTime endDate, PromotionDiscountType disc...` |
| `UpdatePromotion` | `SeasonalPromotion` | `int promotionId, string name = null, DateTime? startDate = null, DateTime? en...` |
| `DeletePromotion` | `bool` | `int promotionId` |
| `GetPromotionById` | `SeasonalPromotion` | `int promotionId` |
| `GetAllPromotions` | `IReadOnlyList<SeasonalPromotion>` | `—` |
| `GetActivePromotions` | `List<SeasonalPromotion>` | `DateTime? asOf = null` |
| `GetPromotionsForMovie` | `List<SeasonalPromotion>` | `int movieId, DateTime? asOf = null` |
| `CalculateBestDiscount` | `PromotionDiscount` | `int movieId, decimal basePrice, DateTime? asOf = null` |
| `RecordRedemption` | `bool` | `int promotionId` |
| `CreateSummerBlockbuster` | `SeasonalPromotion` | `int year` |
| `CreateHolidaySpecial` | `SeasonalPromotion` | `int year` |
| `CreateSpookySeason` | `SeasonalPromotion` | `int year` |
| `CreateOscarSeason` | `SeasonalPromotion` | `int year` |
| `CreateValentinesSpecial` | `SeasonalPromotion` | `int year` |
| `GetPromotionAnalytics` | `PromotionAnalytics` | `int promotionId` |
| `GetSummary` | `PromotionSummary` | `—` |

### DisputeResolutionService

_Manages the full lifecycle of customer disputes against rental charges_

**Source:** `Vidly/Services/DisputeResolutionService.cs` (21 KB) · **Methods:** 10

| Method | Returns | Parameters |
|--------|---------|------------|
| `SubmitDispute` | `DisputeResult` | `int customerId, int rentalId, DisputeType type, string reason, decimal disput...` |
| `StartReview` | `DisputeResult` | `int disputeId, string reviewerName` |
| `Approve` | `DisputeResult` | `int disputeId, string resolvedBy, string notes = null` |
| `PartiallyApprove` | `DisputeResult` | `int disputeId, decimal refundAmount, string resolvedBy, string notes = null` |
| `Deny` | `DisputeResult` | `int disputeId, string resolvedBy, string notes` |
| `ExpireStaleDisputes` | `int` | `—` |
| `GetSummary` | `DisputeSummary` | `—` |
| `GetCustomerHistory` | `CustomerDisputeHistory` | `int customerId` |
| `Success` | `static DisputeResult` | `Dispute dispute, string message` |
| `Fail` | `static DisputeResult` | `string message` |


## Analytics & Operations

Forecasting, revenue analysis, ratings, dashboards, and staff performance.

### RentalForecastService

_Forecasts rental demand using historical patterns. Analyzes day-of-week_

**Source:** `Vidly/Services/RentalForecastService.cs` (23 KB) · **Methods:** 8

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetDayOfWeekDistribution` | `DayOfWeekDistribution` | `—` |
| `GetMonthlyTrends` | `IReadOnlyList<MonthlyTrend>` | `—` |
| `GetGenrePopularity` | `IReadOnlyList<GenrePopularity>` | `int recentDays = 30` |
| `GetMovieVelocity` | `IReadOnlyList<MovieVelocity>` | `int topN = 10` |
| `ForecastDemand` | `IReadOnlyList<DailyForecast>` | `int days = 7, DateTime? startDate = null` |
| `GetInventoryRecommendations` | `InventoryRecommendation` | `—` |
| `GetForecastSummary` | `string` | `int forecastDays = 7` |
| `Empty` | `static DayOfWeekDistribution` | `—` |

### RevenueAnalyticsService

_Revenue analytics service providing financial reporting, trend analysis,_

**Source:** `Vidly/Services/RevenueAnalyticsService.cs` (20 KB) · **Methods:** 7

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetReport` | `RevenueReport` | `DateTime periodStart, DateTime periodEnd, int topCount = 5` |
| `GetRecentReport` | `RevenueReport` | `int days, DateTime? asOf = null, int topCount = 5` |
| `ComparePeriods` | `PeriodComparison` | `DateTime currentStart, DateTime currentEnd, DateTime previousStart, DateTime ...` |
| `CompareMonthOverMonth` | `PeriodComparison` | `int year, int month, int topCount = 5` |
| `ForecastRevenue` | `RevenueForecast` | `int forecastDays, DateTime? asOf = null` |
| `GetPeakRevenueDay` | `KeyValuePair<DateTime, decimal>` | `DateTime periodStart, DateTime periodEnd` |
| `GetRevenueByDayOfWeek` | `Dictionary<DayOfWeek, decimal>` | `DateTime periodStart, DateTime periodEnd` |

### RatingEngineService

_Advanced movie rating engine with Bayesian weighted ratings,_

**Source:** `Vidly/Services/RatingEngineService.cs` (15 KB) · **Methods:** 9

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetBayesianRating` | `BayesianRating` | `int movieId, int minVotes = DefaultMinVotes` |
| `GetRankedMovies` | `IReadOnlyList<BayesianRating>` | `int minVotes = DefaultMinVotes, int? limit = null` |
| `GetGenreRankings` | `IReadOnlyList<BayesianRating>` | `Genre genre, int minVotes = DefaultMinVotes, int? limit = null` |
| `GetAllGenreRankings` | `IDictionary<Genre, IReadOnlyList<BayesianRating>>` | `int minVotes = DefaultMinVotes, int topPerGenre = 10` |
| `GetTrendingScore` | `TrendingScore` | `int movieId, DateTime? asOf = null` |
| `GetTrendingMovies` | `IReadOnlyList<TrendingScore>` | `int count = 10, DateTime? asOf = null` |
| `GetControversyScore` | `ControversyScore` | `int movieId` |
| `GetMostControversial` | `IReadOnlyList<ControversyScore>` | `int count = 10, int minReviews = 5` |
| `GetRatingReport` | `MovieRatingReport` | `int movieId, int minVotes = DefaultMinVotes` |

### DashboardService

_Computes dashboard analytics from rental, movie, and customer data._

**Source:** `Vidly/Services/DashboardService.cs` (20 KB) · **Methods:** 1

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetDashboard` | `DashboardData` | `—` |

### StaffPerformanceService

_Tracks and analyzes staff member performance across rental transactions._

**Source:** `Vidly/Services/StaffPerformanceService.cs` (30 KB) · **Methods:** 17

| Method | Returns | Parameters |
|--------|---------|------------|
| `AddStaff` | `StaffMember` | `string name, StaffRole role, DateTime? hireDate = null, string email = null` |
| `GetStaff` | `StaffMember` | `int staffId` |
| `ListStaff` | `IReadOnlyList<StaffMember>` | `bool activeOnly = true` |
| `DeactivateStaff` | `bool` | `int staffId` |
| `ReactivateStaff` | `bool` | `int staffId` |
| `RecordTransaction` | `StaffTransaction` | `int staffId, int customerId, StaffTransactionType type, decimal revenue = 0m,...` |
| `GetTransactions` | `IReadOnlyList<StaffTransaction>` | `int staffId, DateTime? from = null, DateTime? to = null` |
| `GenerateReport` | `StaffPerformanceReport` | `int staffId, DateTime periodStart, DateTime periodEnd` |
| `GetLeaderboard` | `List<StaffRankingEntry>` | `DateTime periodStart, DateTime periodEnd` |
| `GetTeamSummary` | `TeamPerformanceSummary` | `DateTime periodStart, DateTime periodEnd` |
| `ComparePerformance` | `Dictionary<string, object>` | `int staffId, DateTime period1Start, DateTime period1End, DateTime period2Star...` |
| `GetHourlyActivity` | `Dictionary<int, int>` | `int staffId, DateTime date` |
| `GetPeakHours` | `List<KeyValuePair<int, int>>` | `int staffId, DateTime from, DateTime to, int topN = 3` |
| `GetFeedback` | `IReadOnlyList<StaffTransaction>` | `int staffId, DateTime? from = null, DateTime? to = null, int? minRating = nul...` |
| `GetRatingDistribution` | `Dictionary<int, int>` | `int staffId, DateTime? from = null, DateTime? to = null` |
| `GetFiveStarStreak` | `int` | `int staffId` |
| `GetDailyTrend` | `List<KeyValuePair<DateTime, int>>` | `int staffId, DateTime from, DateTime to` |

### NotificationService

_Generates in-app notifications for customers: overdue rentals,_

**Source:** `Vidly/Services/NotificationService.cs` (15 KB) · **Methods:** 2

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetNotifications` | `NotificationResult` | `int customerId` |
| `GetSummary` | `NotificationSummary` | `—` |

---

**Total:** 33 services, 280 public methods across 5 domains.

*Auto-generated from source. Last updated: 2026-03-06.*