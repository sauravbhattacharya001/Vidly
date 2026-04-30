using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Rental Friction Detector — identifies friction points in the
    /// customer rental journey and generates targeted recommendations to reduce
    /// abandonment, improve experience, and increase engagement.
    /// 
    /// Detects 8 friction categories: availability constraints, price shocks,
    /// overdue patterns, frequency gaps, genre lock, return delays, new customer
    /// drop-off, and high-cost abandonment.
    /// </summary>
    public class FrictionDetectorService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        // Severity thresholds
        private const double ThresholdLow = 25.0;
        private const double ThresholdMedium = 50.0;
        private const double ThresholdHigh = 75.0;

        // Detection parameters
        private const double PriceShockMultiplier = 1.3;
        private const int OverdueCountThreshold = 2;
        private const double FrequencyGapMultiplier = 2.0;
        private const double GenreLockPercent = 0.80;
        private const double ReturnDelayThresholdDays = 2.0;
        private const int NewCustomerMaxRentals = 2;
        private const int NewCustomerInactiveDays = 30;
        private const double HighCostMultiplier = 2.0;
        private const int HighCostInactiveDays = 21;

        public FrictionDetectorService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepository = rentalRepository;
            _movieRepository = movieRepository;
            _customerRepository = customerRepository;
            _clock = clock;
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Generate a full store-wide friction report.
        /// </summary>
        public FrictionReport GenerateReport()
        {
            var now = _clock.Now;
            var customers = _customerRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();

            var profiles = new List<CustomerFrictionProfile>();
            foreach (var c in customers)
            {
                var profile = BuildCustomerProfile(c, allRentals, allMovies, now);
                if (profile.FrictionPoints.Count > 0)
                {
                    profiles.Add(profile);
                }
            }

            var heatmap = BuildHeatmap(profiles);
            var trends = GetTrends(7, 8);
            var storeRecs = GenerateStoreRecommendations(heatmap);
            var insights = GenerateInsights(profiles, heatmap);

            var topProfiles = profiles
                .OrderByDescending(p => p.OverallFrictionScore)
                .Take(10)
                .ToList();

            double healthScore = 100.0 - heatmap.StoreWideFrictionIndex;
            if (healthScore < 0) healthScore = 0;

            return new FrictionReport
            {
                GeneratedAt = now,
                TotalCustomersAnalyzed = customers.Count,
                CustomersWithFriction = profiles.Count,
                StoreHealthScore = Math.Round(healthScore, 1),
                Heatmap = heatmap,
                TopFrictionCustomers = topProfiles,
                Trends = trends,
                StoreWideRecommendations = storeRecs,
                Insights = insights
            };
        }

        /// <summary>
        /// Analyze a single customer's friction profile.
        /// </summary>
        public CustomerFrictionProfile AnalyzeCustomer(int customerId)
        {
            var customer = _customerRepository.GetAll().FirstOrDefault(c => c.Id == customerId);
            if (customer == null)
            {
                return new CustomerFrictionProfile
                {
                    CustomerId = customerId,
                    CustomerName = "Unknown",
                    OverallFrictionScore = 0,
                    RiskLevel = "Low",
                    FrictionPoints = new List<FrictionPoint>(),
                    Recommendations = new List<FrictionRecommendation>(),
                    TotalRentals = 0,
                    DaysSinceLastRental = 0,
                    AvgDaysOverdue = 0
                };
            }

            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            return BuildCustomerProfile(customer, allRentals, allMovies, _clock.Now);
        }

        /// <summary>
        /// Get the friction heatmap showing category distribution.
        /// </summary>
        public FrictionHeatmap GetHeatmap()
        {
            var customers = _customerRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var now = _clock.Now;

            var profiles = new List<CustomerFrictionProfile>();
            foreach (var c in customers)
            {
                var profile = BuildCustomerProfile(c, allRentals, allMovies, now);
                if (profile.FrictionPoints.Count > 0)
                {
                    profiles.Add(profile);
                }
            }

            return BuildHeatmap(profiles);
        }

        /// <summary>
        /// Get friction trends over time periods.
        /// </summary>
        public List<FrictionTrend> GetTrends(int periodDays, int periods)
        {
            if (periodDays < 1) periodDays = 7;
            if (periods < 1) periods = 8;

            var now = _clock.Now;
            var allRentals = _rentalRepository.GetAll();
            var customers = _customerRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var trends = new List<FrictionTrend>();

            for (int i = periods - 1; i >= 0; i--)
            {
                var periodEnd = now.AddDays(-i * periodDays);
                var periodStart = periodEnd.AddDays(-periodDays);

                // Simulate friction at that point in time by filtering rentals
                var periodRentals = allRentals
                    .Where(r => r.RentalDate <= periodEnd)
                    .ToList();

                var categoryCounts = new Dictionary<FrictionCategory, int>();
                int affectedCount = 0;

                foreach (var c in customers)
                {
                    var custRentals = periodRentals.Where(r => r.CustomerId == c.Id).ToList();
                    if (custRentals.Count == 0) continue;

                    var points = DetectFrictionPoints(c, custRentals, allMovies, periodEnd);
                    if (points.Count > 0)
                    {
                        affectedCount++;
                        foreach (var p in points)
                        {
                            if (!categoryCounts.ContainsKey(p.Category))
                                categoryCounts[p.Category] = 0;
                            categoryCounts[p.Category]++;
                        }
                    }
                }

                var dominant = categoryCounts.Count > 0
                    ? categoryCounts.OrderByDescending(kv => kv.Value).First().Key
                    : FrictionCategory.Frequency;

                double frictionIndex = customers.Count > 0
                    ? Math.Round((double)affectedCount / customers.Count * 100.0, 1)
                    : 0;

                trends.Add(new FrictionTrend
                {
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    FrictionIndex = frictionIndex,
                    AffectedCustomers = affectedCount,
                    DominantCategory = dominant
                });
            }

            return trends;
        }

        // ── Private Helpers ──────────────────────────────────────────

        private CustomerFrictionProfile BuildCustomerProfile(
            Customer customer,
            IReadOnlyList<Rental> allRentals,
            IReadOnlyList<Movie> allMovies,
            DateTime now)
        {
            var custRentals = allRentals.Where(r => r.CustomerId == customer.Id).ToList();
            var frictionPoints = DetectFrictionPoints(customer, custRentals, allMovies, now);

            double overallScore = 0;
            if (frictionPoints.Count > 0)
            {
                overallScore = frictionPoints.Average(fp => fp.Score);
            }

            string riskLevel = ClassifyRisk(overallScore);

            int daysSinceLast = 0;
            if (custRentals.Count > 0)
            {
                var lastRental = custRentals.Max(r => r.RentalDate);
                daysSinceLast = (int)Math.Ceiling((now - lastRental).TotalDays);
            }

            double avgOverdue = 0;
            var returnedRentals = custRentals.Where(r => r.ReturnDate.HasValue).ToList();
            if (returnedRentals.Count > 0)
            {
                avgOverdue = returnedRentals.Average(r =>
                {
                    var daysLate = (r.ReturnDate.Value - r.DueDate).TotalDays;
                    return daysLate > 0 ? daysLate : 0;
                });
            }

            var recommendations = GenerateRecommendations(frictionPoints);

            return new CustomerFrictionProfile
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                OverallFrictionScore = Math.Round(overallScore, 1),
                RiskLevel = riskLevel,
                FrictionPoints = frictionPoints,
                Recommendations = recommendations,
                TotalRentals = custRentals.Count,
                DaysSinceLastRental = daysSinceLast,
                AvgDaysOverdue = Math.Round(avgOverdue, 1)
            };
        }

        private List<FrictionPoint> DetectFrictionPoints(
            Customer customer,
            List<Rental> custRentals,
            IReadOnlyList<Movie> allMovies,
            DateTime now)
        {
            var points = new List<FrictionPoint>();
            if (custRentals.Count == 0) return points;

            // 1. Availability friction: customer holds overdue popular movies
            DetectAvailabilityFriction(customer, custRentals, allMovies, now, points);

            // 2. Pricing friction: recent price shock
            DetectPricingFriction(customer, custRentals, now, points);

            // 3. Overdue pattern friction
            DetectOverdueFriction(customer, custRentals, now, points);

            // 4. Frequency gap friction
            DetectFrequencyFriction(customer, custRentals, now, points);

            // 5. Genre lock friction
            DetectGenreLockFriction(customer, custRentals, allMovies, now, points);

            // 6. Return delay friction
            DetectReturnDelayFriction(customer, custRentals, now, points);

            // 7. New customer drop-off
            DetectNewCustomerDrop(customer, custRentals, now, points);

            // 8. High cost abandonment
            DetectHighCostAbandonment(customer, custRentals, now, points);

            return points;
        }

        private void DetectAvailabilityFriction(
            Customer customer, List<Rental> custRentals,
            IReadOnlyList<Movie> allMovies, DateTime now, List<FrictionPoint> points)
        {
            var overdueRentals = custRentals
                .Where(r => r.Status != RentalStatus.Returned && r.DueDate < now)
                .ToList();

            if (overdueRentals.Count == 0) return;

            // Each overdue rental represents availability friction
            int overdueHeld = overdueRentals.Count;

            if (overdueHeld > 0)
            {
                double score = Math.Min(100.0, overdueHeld * 35.0);
                points.Add(new FrictionPoint
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Category = FrictionCategory.Availability,
                    Severity = ClassifySeverity(score),
                    Score = score,
                    Description = "Customer holding overdue titles, blocking availability for others",
                    Evidence = String.Format("{0} movie(s) held past due date", overdueHeld),
                    DetectedAt = now
                });
            }
        }

        private void DetectPricingFriction(
            Customer customer, List<Rental> custRentals, DateTime now, List<FrictionPoint> points)
        {
            if (custRentals.Count < 3) return;

            var sorted = custRentals.OrderBy(r => r.RentalDate).ToList();
            int midpoint = sorted.Count / 2;
            var older = sorted.Take(midpoint).ToList();
            var newer = sorted.Skip(midpoint).ToList();

            double oldAvgRate = older.Average(r => (double)r.DailyRate);
            double newAvgRate = newer.Average(r => (double)r.DailyRate);

            if (oldAvgRate > 0 && newAvgRate > oldAvgRate * PriceShockMultiplier)
            {
                double increase = (newAvgRate - oldAvgRate) / oldAvgRate * 100.0;
                double score = Math.Min(100.0, increase);
                points.Add(new FrictionPoint
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Category = FrictionCategory.Pricing,
                    Severity = ClassifySeverity(score),
                    Score = score,
                    Description = "Significant price increase in recent rentals vs. historical average",
                    Evidence = String.Format("Rate increased {0:F0}% (from ${1:F2} to ${2:F2} avg daily)",
                        increase, oldAvgRate, newAvgRate),
                    DetectedAt = now
                });
            }
        }

        private void DetectOverdueFriction(
            Customer customer, List<Rental> custRentals, DateTime now, List<FrictionPoint> points)
        {
            int overdueCount = custRentals.Count(r =>
                (r.Status == RentalStatus.Overdue) ||
                (r.ReturnDate.HasValue && r.ReturnDate.Value > r.DueDate));

            if (overdueCount > OverdueCountThreshold)
            {
                double ratio = (double)overdueCount / custRentals.Count;
                double score = Math.Min(100.0, ratio * 100.0 + overdueCount * 5.0);
                points.Add(new FrictionPoint
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Category = FrictionCategory.Overdue,
                    Severity = ClassifySeverity(score),
                    Score = score,
                    Description = "Repeated overdue returns indicate process or scheduling friction",
                    Evidence = String.Format("{0} of {1} rentals returned late ({2:F0}%)",
                        overdueCount, custRentals.Count, ratio * 100.0),
                    DetectedAt = now
                });
            }
        }

        private void DetectFrequencyFriction(
            Customer customer, List<Rental> custRentals, DateTime now, List<FrictionPoint> points)
        {
            if (custRentals.Count < 3) return;

            var dates = custRentals.OrderBy(r => r.RentalDate).Select(r => r.RentalDate).ToList();
            var gaps = new List<double>();
            for (int i = 1; i < dates.Count; i++)
            {
                gaps.Add((dates[i] - dates[i - 1]).TotalDays);
            }

            double avgGap = gaps.Average();
            double currentGap = (now - dates.Last()).TotalDays;

            if (avgGap > 0 && currentGap > avgGap * FrequencyGapMultiplier)
            {
                double ratio = currentGap / avgGap;
                double score = Math.Min(100.0, (ratio - 1.0) * 50.0);
                points.Add(new FrictionPoint
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Category = FrictionCategory.Frequency,
                    Severity = ClassifySeverity(score),
                    Score = score,
                    Description = "Rental frequency has dropped significantly below baseline",
                    Evidence = String.Format("Current gap: {0:F0} days vs avg gap: {1:F0} days ({2:F1}x longer)",
                        currentGap, avgGap, ratio),
                    DetectedAt = now
                });
            }
        }

        private void DetectGenreLockFriction(
            Customer customer, List<Rental> custRentals,
            IReadOnlyList<Movie> allMovies, DateTime now, List<FrictionPoint> points)
        {
            if (custRentals.Count < 5) return;

            var genreCounts = new Dictionary<Genre, int>();
            foreach (var r in custRentals)
            {
                var movie = allMovies.FirstOrDefault(m => m.Id == r.MovieId);
                if (movie != null && movie.Genre.HasValue)
                {
                    var genre = movie.Genre.Value;
                    if (!genreCounts.ContainsKey(genre))
                        genreCounts[genre] = 0;
                    genreCounts[genre]++;
                }
            }

            if (genreCounts.Count == 0) return;

            var topGenre = genreCounts.OrderByDescending(kv => kv.Value).First();
            double topPercent = (double)topGenre.Value / custRentals.Count;

            if (topPercent >= GenreLockPercent)
            {
                double score = Math.Min(100.0, (topPercent - 0.5) * 200.0);
                points.Add(new FrictionPoint
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Category = FrictionCategory.GenreLock,
                    Severity = ClassifySeverity(score),
                    Score = score,
                    Description = "Customer stuck in single genre - exploration paralysis",
                    Evidence = String.Format("{0:F0}% of rentals are {1} ({2}/{3})",
                        topPercent * 100.0, topGenre.Key, topGenre.Value, custRentals.Count),
                    DetectedAt = now
                });
            }
        }

        private void DetectReturnDelayFriction(
            Customer customer, List<Rental> custRentals, DateTime now, List<FrictionPoint> points)
        {
            var returnedRentals = custRentals.Where(r => r.ReturnDate.HasValue).ToList();
            if (returnedRentals.Count < 3) return;

            var delays = returnedRentals
                .Select(r => (r.ReturnDate.Value - r.DueDate).TotalDays)
                .Where(d => d > 0)
                .ToList();

            if (delays.Count == 0) return;

            double avgDelay = delays.Average();
            if (avgDelay > ReturnDelayThresholdDays)
            {
                double score = Math.Min(100.0, avgDelay * 15.0);
                points.Add(new FrictionPoint
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Category = FrictionCategory.ReturnDelay,
                    Severity = ClassifySeverity(score),
                    Score = score,
                    Description = "Consistently returning movies late — possible process or convenience issue",
                    Evidence = String.Format("Average {0:F1} days late across {1} late returns",
                        avgDelay, delays.Count),
                    DetectedAt = now
                });
            }
        }

        private void DetectNewCustomerDrop(
            Customer customer, List<Rental> custRentals, DateTime now, List<FrictionPoint> points)
        {
            if (custRentals.Count > NewCustomerMaxRentals) return;
            if (custRentals.Count == 0) return;

            var lastRentalDate = custRentals.Max(r => r.RentalDate);
            double daysSince = (now - lastRentalDate).TotalDays;

            if (daysSince > NewCustomerInactiveDays)
            {
                double score = Math.Min(100.0, (daysSince / NewCustomerInactiveDays) * 40.0);
                points.Add(new FrictionPoint
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Category = FrictionCategory.NewCustomerDrop,
                    Severity = ClassifySeverity(score),
                    Score = score,
                    Description = "New customer appears to have disengaged after initial rental(s)",
                    Evidence = String.Format("Only {0} rental(s), last activity {1:F0} days ago",
                        custRentals.Count, daysSince),
                    DetectedAt = now
                });
            }
        }

        private void DetectHighCostAbandonment(
            Customer customer, List<Rental> custRentals, DateTime now, List<FrictionPoint> points)
        {
            if (custRentals.Count < 3) return;

            var sorted = custRentals.OrderBy(r => r.RentalDate).ToList();
            var lastRental = sorted.Last();
            var previousRentals = sorted.Take(sorted.Count - 1).ToList();

            double avgCost = previousRentals.Average(r => (double)r.TotalCost);
            double lastCost = (double)lastRental.TotalCost;

            if (avgCost > 0 && lastCost > avgCost * HighCostMultiplier)
            {
                double daysSince = (now - lastRental.RentalDate).TotalDays;
                if (daysSince > HighCostInactiveDays)
                {
                    double costRatio = lastCost / avgCost;
                    double score = Math.Min(100.0, (costRatio - 1.0) * 30.0 + (daysSince / HighCostInactiveDays) * 20.0);
                    points.Add(new FrictionPoint
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        Category = FrictionCategory.HighCostAbandonment,
                        Severity = ClassifySeverity(score),
                        Score = score,
                        Description = "Customer stopped renting after an unusually expensive rental",
                        Evidence = String.Format("Last rental cost ${0:F2} ({1:F1}x avg of ${2:F2}), inactive {3:F0} days",
                            lastCost, costRatio, avgCost, daysSince),
                        DetectedAt = now
                    });
                }
            }
        }

        // ── Recommendations ──────────────────────────────────────────

        private List<FrictionRecommendation> GenerateRecommendations(List<FrictionPoint> points)
        {
            var recs = new List<FrictionRecommendation>();
            foreach (var p in points)
            {
                switch (p.Category)
                {
                    case FrictionCategory.Availability:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.Reminder,
                            Action = "Send friendly return reminder for overdue popular titles",
                            ExpectedImpact = 0.6,
                            Rationale = "Timely reminders recover 60% of overdue returns within 48 hours"
                        });
                        break;

                    case FrictionCategory.Pricing:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.Discount,
                            Action = "Offer loyalty discount on next rental to offset price shock",
                            ExpectedImpact = 0.5,
                            Rationale = "Discounts after price increases retain 50% of at-risk customers"
                        });
                        break;

                    case FrictionCategory.Overdue:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.ExtendedDueDate,
                            Action = "Offer extended rental periods based on return history",
                            ExpectedImpact = 0.45,
                            Rationale = "Longer due dates reduce overdue rates by 45% for habitual late returners"
                        });
                        break;

                    case FrictionCategory.Frequency:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.PersonalOutreach,
                            Action = "Send personalized re-engagement message with curated picks",
                            ExpectedImpact = 0.35,
                            Rationale = "Personalized outreach recovers 35% of disengaging customers"
                        });
                        break;

                    case FrictionCategory.GenreLock:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.GenreSuggestion,
                            Action = "Recommend adjacent genres based on taste profile",
                            ExpectedImpact = 0.4,
                            Rationale = "Genre diversification suggestions increase exploration by 40%"
                        });
                        break;

                    case FrictionCategory.ReturnDelay:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.GracePeriod,
                            Action = "Implement 1-day grace period with gentle nudge notification",
                            ExpectedImpact = 0.55,
                            Rationale = "Grace periods reduce repeat late returns by 55%"
                        });
                        break;

                    case FrictionCategory.NewCustomerDrop:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.BundleDeal,
                            Action = "Offer welcome bundle: 3 rentals at discounted rate",
                            ExpectedImpact = 0.5,
                            Rationale = "Welcome bundles convert 50% of one-time renters into regulars"
                        });
                        break;

                    case FrictionCategory.HighCostAbandonment:
                        recs.Add(new FrictionRecommendation
                        {
                            TargetFriction = p.Category,
                            Type = RecommendationType.LoyaltyReward,
                            Action = "Award loyalty points retroactively and notify about rewards balance",
                            ExpectedImpact = 0.4,
                            Rationale = "Retroactive rewards recover 40% of cost-shocked customers"
                        });
                        break;
                }
            }
            return recs;
        }

        private List<FrictionRecommendation> GenerateStoreRecommendations(FrictionHeatmap heatmap)
        {
            var recs = new List<FrictionRecommendation>();

            if (heatmap.CategoryCounts.Count == 0) return recs;

            // Top 3 categories get store-wide recommendations
            var topCategories = heatmap.CategoryCounts
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var cat in topCategories)
            {
                var tempPoints = new List<FrictionPoint>
                {
                    new FrictionPoint { Category = cat }
                };
                var catRecs = GenerateRecommendations(tempPoints);
                recs.AddRange(catRecs);
            }

            return recs;
        }

        // ── Heatmap & Insights ───────────────────────────────────────

        private FrictionHeatmap BuildHeatmap(List<CustomerFrictionProfile> profiles)
        {
            var categoryCounts = new Dictionary<FrictionCategory, int>();
            var categorySeveritySum = new Dictionary<FrictionCategory, double>();
            var categorySeverityCount = new Dictionary<FrictionCategory, int>();

            foreach (var profile in profiles)
            {
                foreach (var fp in profile.FrictionPoints)
                {
                    if (!categoryCounts.ContainsKey(fp.Category))
                    {
                        categoryCounts[fp.Category] = 0;
                        categorySeveritySum[fp.Category] = 0;
                        categorySeverityCount[fp.Category] = 0;
                    }
                    categoryCounts[fp.Category]++;
                    categorySeveritySum[fp.Category] += fp.Score;
                    categorySeverityCount[fp.Category]++;
                }
            }

            var avgSeverity = new Dictionary<FrictionCategory, double>();
            foreach (var kv in categorySeveritySum)
            {
                avgSeverity[kv.Key] = Math.Round(kv.Value / categorySeverityCount[kv.Key], 1);
            }

            var topSource = categoryCounts.Count > 0
                ? categoryCounts.OrderByDescending(kv => kv.Value).First().Key
                : FrictionCategory.Frequency;

            double storeIndex = profiles.Count > 0
                ? Math.Round(profiles.Average(p => p.OverallFrictionScore), 1)
                : 0;

            return new FrictionHeatmap
            {
                CategoryCounts = categoryCounts,
                CategoryAvgSeverity = avgSeverity,
                TopFrictionSource = topSource,
                StoreWideFrictionIndex = storeIndex
            };
        }

        private Dictionary<FrictionCategory, string> GenerateInsights(
            List<CustomerFrictionProfile> profiles, FrictionHeatmap heatmap)
        {
            var insights = new Dictionary<FrictionCategory, string>();

            foreach (var cat in heatmap.CategoryCounts.Keys)
            {
                int count = heatmap.CategoryCounts[cat];
                double avg = heatmap.CategoryAvgSeverity.ContainsKey(cat)
                    ? heatmap.CategoryAvgSeverity[cat] : 0;

                switch (cat)
                {
                    case FrictionCategory.Availability:
                        insights[cat] = String.Format(
                            "{0} customers holding overdue popular titles (avg severity {1:F0}). Consider automated return reminders.",
                            count, avg);
                        break;
                    case FrictionCategory.Pricing:
                        insights[cat] = String.Format(
                            "{0} customers experienced price shock (avg severity {1:F0}). Review pricing strategy for gradual adjustments.",
                            count, avg);
                        break;
                    case FrictionCategory.Overdue:
                        insights[cat] = String.Format(
                            "{0} customers have overdue patterns (avg severity {1:F0}). Extended due dates may reduce friction.",
                            count, avg);
                        break;
                    case FrictionCategory.Frequency:
                        insights[cat] = String.Format(
                            "{0} customers showing frequency decline (avg severity {1:F0}). Re-engagement campaigns recommended.",
                            count, avg);
                        break;
                    case FrictionCategory.GenreLock:
                        insights[cat] = String.Format(
                            "{0} customers locked in single genre (avg severity {1:F0}). Genre discovery features could help.",
                            count, avg);
                        break;
                    case FrictionCategory.ReturnDelay:
                        insights[cat] = String.Format(
                            "{0} customers consistently returning late (avg severity {1:F0}). Process improvements needed.",
                            count, avg);
                        break;
                    case FrictionCategory.NewCustomerDrop:
                        insights[cat] = String.Format(
                            "{0} new customers disengaged early (avg severity {1:F0}). Onboarding experience needs attention.",
                            count, avg);
                        break;
                    case FrictionCategory.HighCostAbandonment:
                        insights[cat] = String.Format(
                            "{0} customers stopped after expensive rentals (avg severity {1:F0}). Cost transparency could help.",
                            count, avg);
                        break;
                }
            }

            return insights;
        }

        // ── Classification Helpers ───────────────────────────────────

        private static FrictionSeverity ClassifySeverity(double score)
        {
            if (score >= ThresholdHigh) return FrictionSeverity.Critical;
            if (score >= ThresholdMedium) return FrictionSeverity.High;
            if (score >= ThresholdLow) return FrictionSeverity.Medium;
            return FrictionSeverity.Low;
        }

        private static string ClassifyRisk(double score)
        {
            if (score >= ThresholdHigh) return "Critical";
            if (score >= ThresholdMedium) return "High";
            if (score >= ThresholdLow) return "Medium";
            return "Low";
        }
    }
}
