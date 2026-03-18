# Vidly Services API Reference

Complete reference for all 44 service classes in `Vidly/Services/`.
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
  - [RentalInsuranceService](#rentalinsuranceservice)
  - [RentalPolicyConstants](#rentalpolicyconstants)
- [Customer Management](#customer-management)
  - [MembershipTierService](#membershiptierservice)
  - [LoyaltyPointsService](#loyaltypointsservice)
  - [CustomerSegmentationService](#customersegmentationservice)
  - [CustomerActivityService](#customeractivityservice)
  - [ChurnPredictorService](#churnpredictorservice)
  - [ParentalControlService](#parentalcontrolservice)
  - [AchievementService](#achievementservice)
  - [ReferralService](#referralservice)
  - [SubscriptionService](#subscriptionservice)
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
  - [MovieQuizService](#moviequizservice)
  - [MovieRequestService](#movierequestservice)
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
  - [LateReturnPredictorService](#latereturnpredictorservice)
  - [RentalSurveyService](#rentalsurveyservice)
  - [StaffSchedulingService](#staffschedulingservice)
  - [StoreEventService](#storeeventservice)

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

### RentalInsuranceService

_Optional rental insurance — covers late fees, damage charges, and lost-disc replacement across three tiers._

**Source:** `Vidly/Services/RentalInsuranceService.cs` (375 lines) · **Methods:** 15

| Method | Returns | Parameters |
|--------|---------|------------|
| `Purchase` | `InsurancePolicy` | `int rentalId, int customerId, InsuranceTier tier` |
| `CalculatePremium` | `decimal` | `decimal dailyRate, InsuranceTier tier` |
| `FileClaim` | `InsuranceClaim` | `int policyId, ClaimType claimType, decimal amount` |
| `DenyClaim` | `InsuranceClaim` | `int claimId, string reason` |
| `GetPolicy` | `InsurancePolicy` | `int policyId` |
| `GetPolicyForRental` | `InsurancePolicy` | `int rentalId` |
| `GetCustomerPolicies` | `List<InsurancePolicy>` | `int customerId` |
| `GetClaimsForPolicy` | `List<InsuranceClaim>` | `int policyId` |
| `GetCustomerClaims` | `List<InsuranceClaim>` | `int customerId` |
| `CancelPolicy` | `InsurancePolicy` | `int policyId` |
| `ExpirePolicy` | `InsurancePolicy` | `int policyId` |
| `GetAnalytics` | `InsuranceAnalytics` | `—` |
| `GetUptakeRate` | `decimal` | `—` |
| `IsFrequentClaimer` | `bool` | `int customerId` |
| `Reset` | `void` | `—` |

**Tiers:**

| Tier | Premium | Coverage Limit | Covered Claim Types |
|------|---------|----------------|---------------------|
| Basic | 15% of daily rate | $10.00 | Late fees |
| Standard | 30% of daily rate | $25.00 | Late fees, damage |
| Premium | 50% of daily rate | $50.00 | Late fees, damage, lost disc |

**Models:** `InsurancePolicy`, `InsuranceClaim`, `InsuranceTier` (enum),
`ClaimType` (enum: `LateFee`, `Damage`, `LostDisc`), `InsuranceAnalytics`.

### RentalPolicyConstants

_Single source of truth for rental policy constants shared across PricingService, RentalReturnService, and LoyaltyPointsService._

**Source:** `Vidly/Services/RentalPolicyConstants.cs` (87 lines) · **Type:** static class

| Constant/Property | Type | Description |
|-------------------|------|-------------|
| `LateFeePerDay` | `decimal` | Per-day late fee before tier discount ($1.50) |
| `MaxLateFeeCap` | `decimal` | Maximum late fee on any single rental ($25.00) |
| `OnTimeReturnBonus` | `int` | Bonus loyalty points for on-time/early return (25) |
| `TierPointsMultiplier` | `IReadOnlyDictionary<MembershipType, decimal>` | Loyalty points multiplier by membership tier |
| `TierLateDiscount` | `IReadOnlyDictionary<MembershipType, decimal>` | Late-fee discount percentage by membership tier |
| `TierGracePeriod` | `IReadOnlyDictionary<MembershipType, int>` | Grace period in days by membership tier |

> **Why this exists:** Previously these values were independently declared
> in each service with identical values, creating a divergence risk if one
> service updated a constant without the others.


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

### AchievementService

_Gamification system that awards badges and tracks milestones based on customer rental behavior. Badges are evaluated dynamically from rental history — no persistent badge state needed._

**Source:** `Vidly/Services/AchievementService.cs` (31 KB) · **Methods:** 3

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetProfile` | `AchievementProfile` | `int customerId` |
| `GetLeaderboard` | `List<AchievementLeaderboardEntry>` | `int top = 10` |
| `GetStats` | `AchievementStats` | `—` |

### ReferralService

_Manages the customer referral program: create referrals, convert them when referred friends sign up, track rewards, and provide analytics._

**Source:** `Vidly/Services/ReferralService.cs` (16 KB) · **Methods:** 10

| Method | Returns | Parameters |
|--------|---------|------------|
| `GenerateReferralCode` | `string` | `int customerId` |
| `CreateReferral` | `Referral` | `int referrerId, string referredName, string referredEmail` |
| `ConvertReferral` | `Referral` | `string referralCode, int newCustomerId` |
| `ExpireOldReferrals` | `int` | `—` |
| `GetReferralsByCustomer` | `IReadOnlyList<Referral>` | `int customerId` |
| `GetByCode` | `Referral` | `string code` |
| `GetCustomerSummary` | `ReferralSummary` | `int customerId` |
| `GetProgramStats` | `ReferralProgramStats` | `—` |
| `GetAll` | `IReadOnlyList<Referral>` | `ReferralStatus? status = null` |
| `IsValidEmailFormat` | `static bool` | `string email` |

### SubscriptionService

_Manages subscription rental plans: subscribe, cancel, pause/resume, upgrade/downgrade, billing, and usage tracking._

**Source:** `Vidly/Services/SubscriptionService.cs` (22 KB) · **Methods:** 12

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetPlan` | `static SubscriptionPlan` | `SubscriptionPlanType planType` |
| `GetAvailablePlans` | `static IReadOnlyList<SubscriptionPlan>` | `—` |
| `Subscribe` | `CustomerSubscription` | `int customerId, SubscriptionPlanType planType` |
| `Cancel` | `CustomerSubscription` | `int subscriptionId, string reason` |
| `Pause` | `CustomerSubscription` | `int subscriptionId` |
| `Resume` | `CustomerSubscription` | `int subscriptionId` |
| `ChangePlan` | `CustomerSubscription` | `int subscriptionId, SubscriptionPlanType newPlanType` |
| `RecordRental` | `int` | `int subscriptionId` |
| `ProcessRenewals` | `RenewalSummary` | `—` |
| `GetUsage` | `SubscriptionUsage` | `int subscriptionId` |
| `GetByCustomerId` | `CustomerSubscription` | `int customerId` |
| `GetRevenueBreakdown` | `SubscriptionRevenue` | `—` |


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

### MovieQuizService

_Movie trivia quiz engine that generates questions from the store's movie catalog. Supports difficulty levels, category filtering, daily challenges, streak tracking, loyalty point rewards, and leaderboards._

**Source:** `Vidly/Services/MovieQuizService.cs` (26 KB) · **Methods:** 11

| Method | Returns | Parameters |
|--------|---------|------------|
| `StartQuiz` | `QuizSession` | `int customerId, QuizDifficulty difficulty, QuizCategory category, int questio...` |
| `SubmitAnswer` | `QuizAnswer` | `int sessionId, int questionId, int selectedOptionIndex, double responseTimeSe...` |
| `CompleteQuiz` | `QuizSession` | `int sessionId` |
| `AbandonQuiz` | `void` | `int sessionId` |
| `GetSession` | `QuizSession` | `int sessionId` |
| `GetCustomerSessions` | `IReadOnlyList<QuizSession>` | `int customerId` |
| `GetDailyChallenge` | `DailyChallenge` | `DateTime? date = null` |
| `SubmitDailyAnswer` | `QuizAnswer` | `int customerId, int selectedOptionIndex, double responseTimeSeconds = 0` |
| `GetCustomerStats` | `QuizStats` | `int customerId` |
| `GetLeaderboard` | `List<LeaderboardEntry>` | `int top = 10` |
| `GetHint` | `string` | `int sessionId, int questionId` |

### MovieRequestService

_Manages customer movie requests — submission, voting, trending, fulfillment, and demand analytics._

**Source:** `Vidly/Services/MovieRequestService.cs` (12 KB) · **Methods:** 11

| Method | Returns | Parameters |
|--------|---------|------------|
| `SubmitRequest` | `MovieRequest` | `int customerId, string title, int? year = null, Genre? genre = null, string r...` |
| `Upvote` | `bool` | `int requestId, int customerId` |
| `RemoveVote` | `bool` | `int requestId, int customerId` |
| `MarkUnderReview` | `MovieRequest` | `int requestId, string staffNote = null` |
| `Fulfill` | `MovieRequest` | `int requestId, string staffNote = null` |
| `Decline` | `MovieRequest` | `int requestId, string staffNote` |
| `GetTrending` | `IReadOnlyList<TrendingRequest>` | `int count = 10` |
| `GetCustomerRequests` | `IReadOnlyList<MovieRequest>` | `int customerId` |
| `Search` | `IReadOnlyList<MovieRequest>` | `string query, MovieRequestStatus? status = null, Genre? genre = null` |
| `GetStats` | `MovieRequestStats` | `—` |
| `GetGenreBreakdown` | `IDictionary<string, int>` | `—` |


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

### LateReturnPredictorService

_Predicts which active rentals are at risk of being returned late. Analyzes customer history, rental timing patterns, and current rental state to produce a risk score (0–100) with actionable recommendations._

**Source:** `Vidly/Services/LateReturnPredictorService.cs` (10 KB) · **Methods:** 3

| Method | Returns | Parameters |
|--------|---------|------------|
| `PredictAll` | `List<LateReturnPrediction>` | `—` |
| `PredictForRental` | `LateReturnPrediction` | `int rentalId` |
| `GetSummary` | `PredictionSummary` | `—` |

### RentalSurveyService

_Manages post-rental satisfaction surveys: submission, validation, NPS calculation, reporting, trend analysis, and actionable insights._

**Source:** `Vidly/Services/RentalSurveyService.cs` (21 KB) · **Methods:** 10

| Method | Returns | Parameters |
|--------|---------|------------|
| `Submit` | `RentalSurvey` | `int customerId, int rentalId, int npsScore, int overallSatisfaction, ...` |
| `GetCustomerSurveys` | `List<RentalSurvey>` | `int customerId` |
| `CalculateNps` | `double?` | `List<RentalSurvey> surveys = null` |
| `GenerateReport` | `SurveyReport` | `DateTime? from = null, DateTime? to = null` |
| `GetAtRiskCustomers` | `List<AtRiskCustomer>` | `—` |
| `GetImprovementOpportunities` | `List<ImprovementOpportunity>` | `—` |
| `GenerateInvitations` | `List<SurveyInvitation>` | `—` |
| `GetPendingInvitations` | `List<SurveyInvitation>` | `—` |
| `GetAll` | `List<RentalSurvey>` | `—` |
| `GetById` | `RentalSurvey` | `int id` |

### StaffSchedulingService

_Manages staff scheduling: shift CRUD, availability tracking, conflict detection, coverage analysis, swap requests, and weekly hour enforcement._

**Source:** `Vidly/Services/StaffSchedulingService.cs` (19 KB) · **Methods:** 24

| Method | Returns | Parameters |
|--------|---------|------------|
| `ScheduleShift` | `Shift` | `int staffId, DateTime start, DateTime end, ShiftType type = ShiftType.Regula...` |
| `GetShift` | `Shift` | `int shiftId` |
| `GetShiftsForDate` | `IReadOnlyList<Shift>` | `DateTime date` |
| `GetShiftsForStaff` | `IReadOnlyList<Shift>` | `int staffId, DateTime? from = null, DateTime? to = null` |
| `CancelShift` | `bool` | `int shiftId` |
| `ConfirmShift` | `bool` | `int shiftId` |
| `GetConflictingShifts` | `IReadOnlyList<Shift>` | `int staffId, DateTime start, DateTime end` |
| `CheckRestViolation` | `string` | `int staffId, DateTime start, DateTime end` |
| `SetAvailability` | `StaffAvailability` | `int staffId, DayOfWeek day, TimeSpan from, TimeSpan to` |
| `GetAvailability` | `IReadOnlyList<StaffAvailability>` | `int staffId` |
| `GetAvailableStaffForDay` | `IReadOnlyList<StaffAvailability>` | `DayOfWeek day` |
| `IsStaffAvailable` | `bool` | `int staffId, DateTime start, DateTime end` |
| `GetCoverageReport` | `CoverageReport` | `DateTime date` |
| `GetWeeklyCoverage` | `IReadOnlyList<CoverageReport>` | `DateTime weekStart` |
| `GetUnderstaffedDays` | `IReadOnlyList<CoverageReport>` | `DateTime from, DateTime to` |
| `GetWeeklySummary` | `StaffWeeklySummary` | `int staffId, DateTime weekStart` |
| `GetAllWeeklySummaries` | `IReadOnlyList<StaffWeeklySummary>` | `DateTime weekStart` |
| `GetOvertimeRisk` | `IReadOnlyList<StaffWeeklySummary>` | `DateTime weekStart, double additionalHours = 0` |
| `RequestSwap` | `ShiftSwapRequest` | `int staffId, int shiftId, SwapRequestType type, int? targetStaffId = null` |
| `ApproveSwap` | `bool` | `int requestId, int? coveringStaffId = null` |
| `DenySwap` | `bool` | `int requestId` |
| `GetPendingSwapRequests` | `IReadOnlyList<ShiftSwapRequest>` | `—` |
| `GetSwapHistory` | `IReadOnlyList<ShiftSwapRequest>` | `int staffId` |
| `GetSchedulingFairness` | `double` | `DateTime weekStart` |

### StoreEventService

_Manages in-store events (screenings, trivia nights, release parties), including RSVPs, attendance tracking, capacity management, and event recommendations based on customer rental history._

**Source:** `Vidly/Services/StoreEventService.cs` (15 KB) · **Methods:** 15

| Method | Returns | Parameters |
|--------|---------|------------|
| `CreateEvent` | `StoreEvent` | `StoreEvent ev` |
| `GetEvents` | `IReadOnlyList<StoreEvent>` | `StoreEventStatus? status = null` |
| `GetById` | `StoreEvent` | `int eventId` |
| `GetUpcoming` | `IReadOnlyList<StoreEvent>` | `—` |
| `CancelEvent` | `void` | `int eventId` |
| `CompleteEvent` | `void` | `int eventId` |
| `Rsvp` | `EventRsvp` | `int eventId, int customerId, int guestCount = 1, string dietaryNotes = null` |
| `CancelRsvp` | `void` | `int eventId, int customerId` |
| `RecordAttendance` | `void` | `int eventId, int customerId` |
| `GetEventRsvps` | `IReadOnlyList<EventRsvp>` | `int eventId` |
| `GetCustomerEvents` | `IReadOnlyList<StoreEvent>` | `int customerId` |
| `GetRemainingCapacity` | `int` | `int eventId` |
| `GetRecommendations` | `IReadOnlyList<EventSuggestion>` | `int customerId, List<Rental> rentals, List<Movie> movies, int limit = 5` |
| `GetStats` | `EventStats` | `—` |
| `GetEventsInRange` | `IReadOnlyList<StoreEvent>` | `int days` |

---

## Inventory & Physical Media

### AvailabilityService

_Tracks real-time movie availability across the store's inventory, including copy counts, due dates, and calendar views for upcoming returns._

**Source:** `Vidly/Services/AvailabilityService.cs` (10 KB) · **Methods:** 6

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetAllAvailability` | `List<MovieAvailability>` | `—` |
| `GetMovieAvailability` | `MovieAvailability` | `int movieId` |
| `GetAvailabilityCalendar` | `List<CalendarDay>` | `int days = 14` |
| `GetSummary` | `AvailabilitySummary` | `—` |
| `GetNextAvailableDate` | `DateTime` | `int movieId` |
| `GetComingSoon` | `List<MovieAvailability>` | `int withinDays = 3` |

---

### CopyConditionService

_Manages physical condition tracking of movie copies through checkout/return inspections, damage rates, renter risk profiling, and replacement recommendations._

**Source:** `Vidly/Services/CopyConditionService.cs` (18 KB) · **Methods:** 13

| Method | Returns | Parameters |
|--------|---------|------------|
| `RecordCheckout` | `ConditionInspection` | `int movieId, int rentalId, ...` |
| `RecordReturn` | `ConditionInspection` | `int movieId, int rentalId, ...` |
| `RecordAudit` | `ConditionInspection` | `int movieId, ...` |
| `GetRentalDelta` | `RentalConditionDelta` | `int rentalId` |
| `GetDeteriorationHistory` | `IReadOnlyList<RentalConditionDelta>` | `int movieId` |
| `GetCopyStatus` | `CopyConditionStatus` | `int movieId` |
| `GetCopiesNeedingReplacement` | `IReadOnlyList<CopyConditionStatus>` | `—` |
| `GetRenterProfile` | `RenterRiskProfile` | `int customerId` |
| `GetHighRiskRenters` | `IReadOnlyList<RenterRiskProfile>` | `—` |
| `GenerateReport` | `ConditionReport` | `—` |
| `GetInspectionHistory` | `IReadOnlyList<ConditionInspection>` | `int movieId` |
| `GetRentalInspections` | `IReadOnlyList<ConditionInspection>` | `int rentalId` |
| `GetInspectionCount` | `int` | `—` |

---

### LostAndFoundService

_Full lost-and-found workflow: item registration, claim submissions with approval/rejection, matching by category/color, disposal/donation of unclaimed items, and reporting._

**Source:** `Vidly/Services/LostAndFoundService.cs` (15 KB) · **Methods:** 17

| Method | Returns | Parameters |
|--------|---------|------------|
| `RegisterItem` | `LostItem` | `LostItem item` |
| `GetById` | `LostItem` | `int id` |
| `ListItems` | `List<LostItem>` | `...` |
| `UpdateItem` | `LostItem` | `int id, Action<LostItem> modifier` |
| `SubmitClaim` | `LostItemClaim` | `int itemId, int customerId, string description` |
| `ApproveClaim` | `LostItemClaim` | `int claimId, string staffId` |
| `RejectClaim` | `LostItemClaim` | `int claimId, string reason` |
| `GetClaimsForItem` | `List<LostItemClaim>` | `int itemId` |
| `GetClaimsByCustomer` | `List<LostItemClaim>` | `int customerId` |
| `Search` | `List<LostItem>` | `string keyword` |
| `FindMatches` | `List<LostItem>` | `LostItemCategory category, string color = null, string keyword = null` |
| `GetOverdueForDisposal` | `List<LostItem>` | `DateTime? asOf = null` |
| `DisposeItem` | `LostItem` | `int id, string staffId` |
| `DonateItem` | `LostItem` | `int id, string staffId, string donationNotes = null` |
| `BatchDispose` | `List<LostItem>` | `string staffId, DateTime? asOf = null` |
| `GenerateReport` | `LostAndFoundReport` | `DateTime? asOf = null` |

---

## Rental Operations (Extended)

### RentalCalendarService

_Generates calendar views of rental activity — monthly breakdowns with checkout/due/return/overdue counts per day, plus upcoming event lists._

**Source:** `Vidly/Services/RentalCalendarService.cs` (9 KB) · **Methods:** 2

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetCalendarMonth` | `CalendarMonth` | `int year, int month, int? customerId = null` |
| `GetUpcomingEvents` | `List<CalendarEvent>` | `int days = 7, int? customerId = null` |

---

### RentalExtensionService

_Handles rental due-date extensions with eligibility checks, fee calculation, max-extension limits, and extension history tracking._

**Source:** `Vidly/Services/RentalExtensionService.cs` (18 KB) · **Methods:** 6

| Method | Returns | Parameters |
|--------|---------|------------|
| `RequestExtension` | `ExtensionResult` | `int rentalId, int additionalDays = 0` |
| `CheckEligibility` | `ExtensionEligibility` | `int rentalId` |
| `GetExtensionHistory` | `IReadOnlyList<ExtensionRecord>` | `int rentalId` |
| `GetCustomerExtensions` | `IReadOnlyList<ExtensionRecord>` | `int customerId` |
| `GetStats` | `ExtensionStats` | `—` |
| `GetExtensionSummary` | `string` | `int rentalId` |

**Constants:** `MaxExtensionsPerRental = 3`, `DefaultExtensionDays = 3`, `MaxExtensionDays = 7`, `ExtensionRateMultiplier = 0.75`

---

### RentalReceiptService

_Generates formatted receipts for rentals (individual and batch), with tax calculation, text/CSV export, and customer spending summaries._

**Source:** `Vidly/Services/RentalReceiptService.cs` (22 KB) · **Methods:** 6

| Method | Returns | Parameters |
|--------|---------|------------|
| `GenerateReceipt` | `Receipt` | `int rentalId, ReceiptOptions options = null` |
| `GenerateBatchReceipt` | `BatchReceipt` | `IEnumerable<int> rentalIds, ReceiptOptions options = null` |
| `FormatAsText` | `string` | `Receipt receipt` |
| `FormatAsCsv` | `string` | `Receipt receipt` |
| `FormatBatchAsText` | `string` | `BatchReceipt batch` |
| `GetSpendingSummary` | `SpendingSummary` | `int customerId, DateTime? from = null, DateTime? to = null` |

**Constants:** `TaxRate = 8.5%`, `ReceiptWidth = 48`

---

### RentalTrendService

_Analyzes rental patterns over time — day-of-week breakdowns, genre trends, monthly volume tracking, retention cohorts, and exportable text/JSON reports._

**Source:** `Vidly/Services/RentalTrendService.cs` (21 KB) · **Methods:** 7

| Method | Returns | Parameters |
|--------|---------|------------|
| `Analyze` | `RentalTrendReport` | `DateTime from, DateTime to` |
| `GetDayOfWeekBreakdown` | `List<DayOfWeekBreakdown>` | `DateTime from, DateTime to` |
| `GetGenreTrends` | `List<GenreTrend>` | `DateTime from, DateTime to` |
| `GetMonthlyVolumes` | `List<MonthlyVolume>` | `DateTime from, DateTime to` |
| `GetRetentionCohorts` | `List<RetentionCohort>` | `DateTime from, DateTime to` |
| `GenerateTextReport` | `string` | `DateTime from, DateTime to` |
| `ExportJson` | `string` | `DateTime from, DateTime to` |

**Constants:** `PeakThreshold = 1.5`, `QuietThreshold = 0.5`

---

## Content & Social

### AwardsService

_Manages movie awards ceremonies — yearly ceremony creation, category/nominee tracking, voting, and winner announcements._

**Source:** `Vidly/Services/AwardsService.cs` (22 KB) · **Methods:** 2+

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetAvailableYears` | `List<int>` | `—` |
| `GetCeremony` | `AwardsCeremony` | `int year` |

---

### MarathonPlannerService

_Plans movie marathon sessions — builds viewing schedules from genre/duration criteria with break planning and movie suggestions._

**Source:** `Vidly/Services/MarathonPlannerService.cs` (5 KB) · **Methods:** 2

| Method | Returns | Parameters |
|--------|---------|------------|
| `BuildPlan` | `MarathonPlan` | `MarathonRequest request` |
| `SuggestMovies` | `List<Movie>` | `int count, Genre? genreFilter = null` |

---

### MoodMatcherService

_Maps customer moods to movie recommendations using genre/mood profiles with configurable mood-to-genre mappings._

**Source:** `Vidly/Services/MoodMatcherService.cs` (8 KB) · **Methods:** 3

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetAllMoods` | `IReadOnlyList<MoodProfile>` | `—` |
| `GetMoodProfile` | `MoodProfile` | `Mood mood` |
| `GetRecommendations` | `MoodMatchResult` | `Mood mood, int maxResults = 10` |

---

### MovieClubService

_Full social club system — create/manage clubs, handle memberships with moderator roles, club watchlists, and democratic movie polls with voting._

**Source:** `Vidly/Services/MovieClubService.cs` (21 KB) · **Methods:** 18

| Method | Returns | Parameters |
|--------|---------|------------|
| `CreateClub` | `MovieClub` | `string name, string description, int founderId, ...` |
| `GetClub` | `MovieClub` | `int clubId` |
| `ListClubs` | `IReadOnlyList<MovieClub>` | `string genre = null` |
| `PauseClub` | `void` | `int clubId, int requesterId` |
| `ResumeClub` | `void` | `int clubId, int requesterId` |
| `DisbandClub` | `void` | `int clubId, int requesterId` |
| `JoinClub` | `ClubMembership` | `int clubId, int customerId` |
| `LeaveClub` | `void` | `int clubId, int customerId` |
| `PromoteToModerator` | `void` | `int clubId, int customerId, int requesterId` |
| `GetMembers` | `IReadOnlyList<ClubMembership>` | `int clubId` |
| `GetCustomerClubs` | `IReadOnlyList<MovieClub>` | `int customerId` |
| `AddToWatchlist` | `ClubWatchlistItem` | `int clubId, int movieId, int customerId, ...` |
| `MarkAsWatched` | `void` | `int clubId, int watchlistItemId, double? rating = null` |
| `GetWatchlist` | `IReadOnlyList<ClubWatchlistItem>` | `int clubId, bool includeWatched = false` |
| `CreatePoll` | `ClubPoll` | `int clubId, string title, List<(int, string)> options, ...` |
| `CastVote` | `void` | `int pollId, int optionId, int customerId` |
| `ClosePoll` | `ClubPollOption` | `int pollId, int requesterId` |
| `GetPolls` | `IReadOnlyList<ClubPoll>` | `int clubId, PollStatus? status = null` |

---

### MovieSeriesService

_Manages franchise/series grouping — series CRUD, movie ordering within series, per-customer watch progress tracking, and "next up" recommendations._

**Source:** `Vidly/Services/MovieSeriesService.cs` (12 KB) · **Methods:** 15

| Method | Returns | Parameters |
|--------|---------|------------|
| `CreateSeries` | `MovieSeries` | `string name, string description = null, Genre? genre = null, bool isOngoing = false` |
| `GetSeries` | `MovieSeries` | `int seriesId` |
| `ListSeries` | `List<MovieSeries>` | `Genre? genre = null` |
| `SearchSeries` | `List<MovieSeries>` | `string query` |
| `DeleteSeries` | `bool` | `int seriesId` |
| `AddMovie` | `SeriesEntry` | `int seriesId, int movieId, int orderIndex, string label = null` |
| `RemoveMovie` | `bool` | `int seriesId, int movieId` |
| `GetSeriesEntries` | `List<SeriesEntry>` | `int seriesId` |
| `ReorderEntry` | `bool` | `int entryId, int newOrderIndex` |
| `MarkWatched` | `SeriesProgress` | `int customerId, int seriesEntryId` |
| `UnmarkWatched` | `bool` | `int customerId, int seriesEntryId` |
| `GetProgress` | `SeriesProgressSummary` | `int customerId, int seriesId, List<Movie> movieLookup` |
| `GetAllProgress` | `List<SeriesProgressSummary>` | `int customerId, List<Movie> movieLookup` |
| `GetNextUpRecommendations` | `List<SeriesEntryDetail>` | `int customerId, List<Movie> movieLookup` |
| `GetSeriesForMovie` | `List<MovieSeries>` | `int movieId` |

---

### MovieTournamentService

_Bracket-style movie tournaments — create single-elimination brackets (4/8/16 movies), vote on matches, advance rounds, and track hall-of-fame results._

**Source:** `Vidly/Services/MovieTournamentService.cs` (17 KB) · **Methods:** 9

| Method | Returns | Parameters |
|--------|---------|------------|
| `CreateTournament` | `Tournament` | `...` |
| `Vote` | `TournamentMatch` | `int tournamentId, int matchId, int winnerMovieId, string reason = null` |
| `GetTournament` | `Tournament` | `int id` |
| `ListTournaments` | `IReadOnlyList<Tournament>` | `TournamentStatus? status = null` |
| `GetRoundMatches` | `IReadOnlyList<TournamentMatch>` | `int tournamentId, int round` |
| `GetPendingMatches` | `IReadOnlyList<TournamentMatch>` | `int tournamentId` |
| `GetHallOfFame` | `IReadOnlyList<TournamentResult>` | `—` |
| `GetMovieRecords` | `IReadOnlyList<MovieTournamentRecord>` | `—` |
| `CancelTournament` | `bool` | `int tournamentId` |

**Valid bracket sizes:** 4, 8, 16

---

## Staff & Store Operations (Extended)

### StaffPicksService

_Staff-curated movie recommendations — manage picks by staff member and theme, with filtering and featured pick support._

**Source:** `Vidly/Services/StaffPicksService.cs` (9 KB) · **Methods:** 8

| Method | Returns | Parameters |
|--------|---------|------------|
| `GetAllPicks` | `List<StaffPickViewModel>` | `—` |
| `GetPageViewModel` | `StaffPicksPageViewModel` | `string filterStaff = null, string filterTheme = null` |
| `GetPicksByStaff` | `List<StaffPickViewModel>` | `string staffName` |
| `GetPicksByTheme` | `List<StaffPickViewModel>` | `string theme` |
| `AddPick` | `StaffPick` | `int movieId, string staffName, string theme, string note, bool isFeatured = false` |
| `RemovePick` | `bool` | `int id` |
| `GetStaffNames` | `List<string>` | `—` |
| `GetThemes` | `List<string>` | `—` |

---

### StoreAnnouncementService

_In-store announcement system with scheduling, publishing, pinning, customer acknowledgment tracking, view analytics, and expiration management._

**Source:** `Vidly/Services/StoreAnnouncementService.cs` (17 KB) · **Methods:** 17

| Method | Returns | Parameters |
|--------|---------|------------|
| `Create` | `Announcement` | `Announcement a` |
| `Update` | `Announcement` | `int id, Action<Announcement> modifier` |
| `GetById` | `Announcement` | `int id` |
| `Publish` | `Announcement` | `int id` |
| `ActivateScheduled` | `List<Announcement>` | `—` |
| `ExpireStale` | `List<Announcement>` | `—` |
| `Archive` | `Announcement` | `int id` |
| `Pin` | `void` | `int announcementId, string staffId` |
| `Unpin` | `void` | `int announcementId` |
| `IsPinned` | `bool` | `int announcementId` |
| `Acknowledge` | `AnnouncementAcknowledgment` | `int announcementId, int customerId` |
| `HasAcknowledged` | `bool` | `int announcementId, int customerId` |
| `GetAcknowledgments` | `List<AnnouncementAcknowledgment>` | `int announcementId` |
| `GetPendingAcknowledgments` | `List<Announcement>` | `int customerId` |
| `RecordView` | `void` | `int announcementId` |
| `GetBoard` | `List<Announcement>` | `string customerTier = null, AnnouncementFilter filter = null` |
| `GetAll` | `List<Announcement>` | `AnnouncementStatus? status = null` |
| `GetAnalytics` | `AnnouncementAnalytics` | `—` |

---

**Total:** 57 services, 500+ public methods across 7 domains.

*Auto-generated from source. Last updated: 2026-03-18.*