using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Customer Lifetime Trajectory Engine — predicts each customer's
    /// future behavior trajectory using 7 analytical engines.
    ///
    /// 1. Rental Velocity Forecaster — when will they rent next?
    /// 2. Genre Evolution Predictor — which genres will they gravitate toward?
    /// 3. Spending Trajectory Analyzer — how will their spending change?
    /// 4. Lifecycle Phase Classifier — where are they in the customer lifecycle?
    /// 5. Churn Risk Estimator — how likely are they to leave?
    /// 6. Lifetime Value Projector — what's their projected remaining value?
    /// 7. Insight Generator — natural-language trajectory insights
    /// </summary>
    public class TrajectoryEngineService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IClock _clock;

        // EMA smoothing factor for interval calculations
        private const double EmaSmoothingFactor = 0.3;

        public TrajectoryEngineService(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo,
            IClock clock)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepo = rentalRepo;
            _movieRepo = movieRepo;
            _customerRepo = customerRepo;
            _clock = clock;
        }

        // ================================================================
        //  Public API
        // ================================================================

        /// <summary>Generate a full fleet trajectory report.</summary>
        public TrajectoryReport GenerateReport()
        {
            var now = _clock.Now;
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);

            var trajectories = new List<CustomerTrajectory>();
            foreach (var customer in customers)
            {
                var rentals = allRentals.Where(r => r.CustomerId == customer.Id)
                    .OrderBy(r => r.RentalDate).ToList();
                trajectories.Add(BuildTrajectory(customer, rentals, movieLookup, now));
            }

            var fleetHealth = ComputeFleetHealth(trajectories, now);
            var insights = GenerateFleetInsights(trajectories, fleetHealth, now);
            var score = fleetHealth.HealthScore;

            return new TrajectoryReport
            {
                GeneratedAt = now,
                Trajectories = trajectories,
                FleetHealth = fleetHealth,
                Insights = insights,
                TrajectoryScore = score
            };
        }

        /// <summary>Generate trajectory for a single customer.</summary>
        public CustomerTrajectory GetCustomerTrajectory(int customerId)
        {
            var now = _clock.Now;
            var customer = _customerRepo.GetAll().FirstOrDefault(c => c.Id == customerId);
            if (customer == null)
                throw new KeyNotFoundException("Customer not found: " + customerId);

            var rentals = _rentalRepo.GetAll()
                .Where(r => r.CustomerId == customerId)
                .OrderBy(r => r.RentalDate).ToList();
            var movies = _movieRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);

            return BuildTrajectory(customer, rentals, movieLookup, now);
        }

        // ================================================================
        //  Engine 1: Rental Velocity Forecaster
        // ================================================================

        private RentalVelocityForecast ForecastVelocity(List<Rental> rentals, DateTime now)
        {
            var forecast = new RentalVelocityForecast();
            if (rentals.Count < 2)
            {
                forecast.Trend = "Stalled";
                forecast.ConfidencePercent = 0;
                return forecast;
            }

            // Compute intervals
            var intervals = new List<double>();
            for (int i = 1; i < rentals.Count; i++)
            {
                var gap = (rentals[i].RentalDate - rentals[i - 1].RentalDate).TotalDays;
                if (gap > 0) intervals.Add(gap);
            }

            if (intervals.Count == 0)
            {
                forecast.Trend = "Stalled";
                forecast.ConfidencePercent = 0;
                return forecast;
            }

            // EMA of intervals
            double ema = intervals[0];
            for (int i = 1; i < intervals.Count; i++)
            {
                ema = EmaSmoothingFactor * intervals[i] + (1 - EmaSmoothingFactor) * ema;
            }
            forecast.AvgIntervalDays = Math.Round(ema, 1);

            // Predict next rental
            var lastRental = rentals.Last().RentalDate;
            forecast.PredictedNextRental = lastRental.AddDays(ema);

            // Confidence: based on consistency of intervals (CV)
            var mean = intervals.Average();
            var variance = intervals.Sum(x => (x - mean) * (x - mean)) / intervals.Count;
            var stdDev = Math.Sqrt(variance);
            var cv = mean > 0 ? stdDev / mean : 1.0;
            forecast.ConfidencePercent = Math.Round(Math.Max(0, Math.Min(100, (1 - cv) * 100)), 1);

            // Trend: compare first-half avg vs second-half avg intervals
            if (intervals.Count >= 4)
            {
                int half = intervals.Count / 2;
                var firstHalf = intervals.Take(half).Average();
                var secondHalf = intervals.Skip(half).Average();
                double ratio = secondHalf / firstHalf;
                if (ratio < 0.8)
                    forecast.Trend = "Accelerating";
                else if (ratio > 1.2)
                    forecast.Trend = "Decelerating";
                else
                    forecast.Trend = "Steady";
            }
            else
            {
                forecast.Trend = "Steady";
            }

            // Override to Stalled if predicted next rental is way overdue
            var daysSinceLast = (now - lastRental).TotalDays;
            if (daysSinceLast > ema * 3 && daysSinceLast > 60)
            {
                forecast.Trend = "Stalled";
                forecast.ConfidencePercent = Math.Max(0, forecast.ConfidencePercent - 40);
            }

            return forecast;
        }

        // ================================================================
        //  Engine 2: Genre Evolution Predictor
        // ================================================================

        private GenreEvolution PredictGenreEvolution(
            List<Rental> rentals, Dictionary<int, Movie> movieLookup)
        {
            var result = new GenreEvolution();
            if (rentals.Count == 0) return result;

            // Current preferences (all rentals, weighted toward recent)
            var genreCounts = new Dictionary<string, double>();
            for (int i = 0; i < rentals.Count; i++)
            {
                Movie movie;
                if (!movieLookup.TryGetValue(rentals[i].MovieId, out movie) || movie.Genre == null)
                    continue;
                var g = movie.Genre.Value.ToString();
                double weight = 1.0 + (double)i / rentals.Count; // more recent = higher weight
                if (!genreCounts.ContainsKey(g)) genreCounts[g] = 0;
                genreCounts[g] += weight;
            }

            var total = genreCounts.Values.Sum();
            if (total > 0)
            {
                foreach (var kv in genreCounts)
                    result.CurrentPreferences[kv.Key] = Math.Round(kv.Value / total, 3);
            }

            // Split into first-half and second-half for evolution
            int mid = rentals.Count / 2;
            var earlyGenres = ComputeGenreWeights(rentals.Take(Math.Max(mid, 1)).ToList(), movieLookup);
            var lateGenres = ComputeGenreWeights(rentals.Skip(Math.Max(mid, 1)).ToList(), movieLookup);

            // Predicted = extrapolate the trend
            var allGenres = earlyGenres.Keys.Union(lateGenres.Keys).ToList();
            foreach (var g in allGenres)
            {
                double early = earlyGenres.ContainsKey(g) ? earlyGenres[g] : 0;
                double late = lateGenres.ContainsKey(g) ? lateGenres[g] : 0;
                double delta = late - early;
                double predicted = Math.Max(0, late + delta * 0.5);
                result.PredictedPreferences[g] = Math.Round(predicted, 3);

                if (delta > 0.1)
                    result.EmergingGenres.Add(g);
                else if (delta < -0.1)
                    result.FadingGenres.Add(g);
            }

            // Normalize predicted
            var predTotal = result.PredictedPreferences.Values.Sum();
            if (predTotal > 0)
            {
                foreach (var key in result.PredictedPreferences.Keys.ToList())
                    result.PredictedPreferences[key] = Math.Round(
                        result.PredictedPreferences[key] / predTotal, 3);
            }

            // Pattern detection
            int distinctGenres = genreCounts.Keys.Count;
            double maxShare = result.CurrentPreferences.Values.Any()
                ? result.CurrentPreferences.Values.Max() : 0;
            bool shifting = result.EmergingGenres.Count > 0 || result.FadingGenres.Count > 0;

            if (distinctGenres >= 5 && maxShare < 0.4)
                result.Pattern = "Explorer";
            else if (distinctGenres <= 2 || maxShare > 0.6)
                result.Pattern = shifting ? "Narrowing" : "Loyal";
            else if (shifting)
                result.Pattern = "Shifting";
            else
                result.Pattern = "Loyal";

            return result;
        }

        private Dictionary<string, double> ComputeGenreWeights(
            List<Rental> rentals, Dictionary<int, Movie> movieLookup)
        {
            var counts = new Dictionary<string, int>();
            foreach (var r in rentals)
            {
                Movie movie;
                if (!movieLookup.TryGetValue(r.MovieId, out movie) || movie.Genre == null)
                    continue;
                var g = movie.Genre.Value.ToString();
                if (!counts.ContainsKey(g)) counts[g] = 0;
                counts[g]++;
            }
            int total = counts.Values.Sum();
            return total > 0
                ? counts.ToDictionary(kv => kv.Key, kv => (double)kv.Value / total)
                : new Dictionary<string, double>();
        }

        // ================================================================
        //  Engine 3: Spending Trajectory Analyzer
        // ================================================================

        private SpendingTrajectory AnalyzeSpending(List<Rental> rentals, DateTime now)
        {
            var result = new SpendingTrajectory();
            if (rentals.Count == 0) return result;

            // Group by month
            var monthlySpend = rentals
                .GroupBy(r => new { r.RentalDate.Year, r.RentalDate.Month })
                .Select(g => new
                {
                    Month = new DateTime(g.Key.Year, g.Key.Month, 1),
                    Spend = g.Sum(r => r.TotalCost)
                })
                .OrderBy(x => x.Month)
                .ToList();

            if (monthlySpend.Count == 0) return result;

            result.AvgMonthlySpend = Math.Round(monthlySpend.Average(m => m.Spend), 2);

            if (monthlySpend.Count >= 2)
            {
                // Linear regression on monthly spend
                var xs = Enumerable.Range(0, monthlySpend.Count)
                    .Select(i => (double)i).ToArray();
                var ys = monthlySpend.Select(m => (double)m.Spend).ToArray();
                double slope = LinearRegressionSlope(xs, ys);

                result.SpendVelocity = Math.Round((decimal)slope, 2);
                var lastSpend = monthlySpend.Last().Spend;
                result.ForecastedNextMonthSpend = Math.Max(0,
                    Math.Round(lastSpend + (decimal)slope, 2));

                if (slope > 1.0)
                    result.Trend = "Rising";
                else if (slope < -1.0)
                    result.Trend = "Declining";
                else
                    result.Trend = "Stable";
            }
            else
            {
                result.ForecastedNextMonthSpend = result.AvgMonthlySpend;
                result.Trend = "Stable";
            }

            return result;
        }

        private static double LinearRegressionSlope(double[] xs, double[] ys)
        {
            int n = xs.Length;
            if (n < 2) return 0;
            double sumX = xs.Sum();
            double sumY = ys.Sum();
            double sumXY = xs.Zip(ys, (x, y) => x * y).Sum();
            double sumX2 = xs.Sum(x => x * x);
            double denom = n * sumX2 - sumX * sumX;
            return denom == 0 ? 0 : (n * sumXY - sumX * sumY) / denom;
        }

        // ================================================================
        //  Engine 4: Lifecycle Phase Classifier
        // ================================================================

        private LifecyclePhaseResult ClassifyLifecycle(
            List<Rental> rentals, DateTime now)
        {
            var result = new LifecyclePhaseResult();

            if (rentals.Count == 0)
            {
                result.Phase = LifecyclePhase.Discovery;
                result.Confidence = 50;
                result.DaysInPhase = 0;
                result.PredictedNextPhase = LifecyclePhase.Growing;
                return result;
            }

            var daysSinceLast = (now - rentals.Last().RentalDate).TotalDays;
            int totalRentals = rentals.Count;

            // Churned
            if (daysSinceLast > 120)
            {
                result.Phase = LifecyclePhase.Churned;
                result.Confidence = Math.Min(95, 60 + daysSinceLast / 10);
                result.DaysInPhase = (int)(daysSinceLast - 120);
                result.PredictedNextPhase = null; // terminal
                return result;
            }

            // Dormant
            if (daysSinceLast > 60)
            {
                result.Phase = LifecyclePhase.Dormant;
                result.Confidence = Math.Min(90, 50 + daysSinceLast / 5);
                result.DaysInPhase = (int)(daysSinceLast - 60);
                result.PredictedNextPhase = LifecyclePhase.Churned;
                return result;
            }

            // Discovery
            if (totalRentals < 3)
            {
                result.Phase = LifecyclePhase.Discovery;
                result.Confidence = 80;
                result.DaysInPhase = rentals.Count > 0
                    ? (int)(now - rentals.First().RentalDate).TotalDays : 0;
                result.PredictedNextPhase = LifecyclePhase.Growing;
                return result;
            }

            // Frequency analysis: compare recent vs earlier
            int half = totalRentals / 2;
            var earlyRentals = rentals.Take(half).ToList();
            var lateRentals = rentals.Skip(half).ToList();

            double earlyFreq = earlyRentals.Count > 1
                ? earlyRentals.Count / Math.Max(1, (earlyRentals.Last().RentalDate - earlyRentals.First().RentalDate).TotalDays / 30.0)
                : 0;
            double lateFreq = lateRentals.Count > 1
                ? lateRentals.Count / Math.Max(1, (lateRentals.Last().RentalDate - lateRentals.First().RentalDate).TotalDays / 30.0)
                : 0;

            double freqRatio = earlyFreq > 0 ? lateFreq / earlyFreq : 1;

            if (freqRatio > 1.2)
            {
                result.Phase = LifecyclePhase.Growing;
                result.Confidence = Math.Min(90, 60 + (freqRatio - 1) * 50);
                result.PredictedNextPhase = LifecyclePhase.Loyal;
            }
            else if (freqRatio > 0.8)
            {
                // Stable — loyal or plateaued
                if (lateFreq >= 2) // 2+ per month
                {
                    result.Phase = LifecyclePhase.Loyal;
                    result.Confidence = Math.Min(90, 60 + lateFreq * 5);
                    result.PredictedNextPhase = LifecyclePhase.Plateaued;
                }
                else
                {
                    result.Phase = LifecyclePhase.Plateaued;
                    result.Confidence = 65;
                    result.PredictedNextPhase = LifecyclePhase.Declining;
                }
            }
            else
            {
                result.Phase = LifecyclePhase.Declining;
                result.Confidence = Math.Min(90, 60 + (1 - freqRatio) * 50);
                result.PredictedNextPhase = LifecyclePhase.Dormant;
            }

            result.DaysInPhase = (int)(now - rentals[half].RentalDate).TotalDays;
            return result;
        }

        // ================================================================
        //  Engine 5: Churn Risk Estimator
        // ================================================================

        private ChurnRiskResult EstimateChurnRisk(
            List<Rental> rentals, RentalVelocityForecast velocity, DateTime now)
        {
            var result = new ChurnRiskResult();
            if (rentals.Count == 0)
            {
                result.RiskScore = 50;
                result.Tier = ChurnRiskTier.Warning;
                result.RiskFactors.Add("No rental history");
                return result;
            }

            double score = 0;
            var daysSinceLast = (now - rentals.Last().RentalDate).TotalDays;

            // Factor 1: Days since last rental (0-30 points)
            if (daysSinceLast > 120) { score += 30; result.RiskFactors.Add("No rental in " + (int)daysSinceLast + " days"); }
            else if (daysSinceLast > 60) { score += 20; result.RiskFactors.Add("Dormant for " + (int)daysSinceLast + " days"); }
            else if (daysSinceLast > 30) { score += 10; result.RiskFactors.Add("Inactive for " + (int)daysSinceLast + " days"); }

            // Factor 2: Velocity trend (0-25 points)
            if (velocity.Trend == "Stalled") { score += 25; result.RiskFactors.Add("Rental velocity stalled"); }
            else if (velocity.Trend == "Decelerating") { score += 15; result.RiskFactors.Add("Rental frequency declining"); }

            // Factor 3: Late fee history (0-20 points)
            var lateRentals = rentals.Count(r => r.LateFee > 0);
            double lateRate = (double)lateRentals / rentals.Count;
            if (lateRate > 0.5) { score += 20; result.RiskFactors.Add("High late fee rate (" + Math.Round(lateRate * 100) + "%)"); }
            else if (lateRate > 0.25) { score += 10; result.RiskFactors.Add("Moderate late fee rate"); }

            // Factor 4: Genre variety declining (0-15 points)
            if (rentals.Count >= 6)
            {
                int half = rentals.Count / 2;
                var earlyGenres = rentals.Take(half).Select(r => r.MovieId).Distinct().Count();
                var lateGenres = rentals.Skip(half).Select(r => r.MovieId).Distinct().Count();
                if (lateGenres < earlyGenres * 0.7)
                {
                    score += 15;
                    result.RiskFactors.Add("Engagement variety dropping");
                }
            }

            // Factor 5: Overdue gap vs expected interval (0-10 points)
            if (velocity.AvgIntervalDays > 0 && daysSinceLast > velocity.AvgIntervalDays * 2)
            {
                score += 10;
                result.RiskFactors.Add("Overdue by " + Math.Round(daysSinceLast - velocity.AvgIntervalDays) + " days vs expected interval");
            }

            result.RiskScore = (int)Math.Min(100, Math.Max(0, score));

            if (result.RiskScore >= 80) result.Tier = ChurnRiskTier.Lost;
            else if (result.RiskScore >= 60) result.Tier = ChurnRiskTier.Critical;
            else if (result.RiskScore >= 40) result.Tier = ChurnRiskTier.Warning;
            else if (result.RiskScore >= 20) result.Tier = ChurnRiskTier.Watch;
            else result.Tier = ChurnRiskTier.Safe;

            return result;
        }

        // ================================================================
        //  Engine 6: Lifetime Value Projector
        // ================================================================

        private LifetimeValueProjection ProjectLifetimeValue(
            List<Rental> rentals, SpendingTrajectory spending, ChurnRiskResult churn)
        {
            var result = new LifetimeValueProjection();
            if (rentals.Count == 0) return result;

            result.HistoricalLTV = rentals.Sum(r => r.TotalCost);

            // Survival probability based on churn risk
            double survivalFactor = 1.0 - (churn.RiskScore / 100.0);
            decimal baseMonthly = spending.ForecastedNextMonthSpend > 0
                ? spending.ForecastedNextMonthSpend
                : spending.AvgMonthlySpend;

            result.ProjectedRevenue30Days = Math.Round(baseMonthly * (decimal)survivalFactor, 2);
            result.ProjectedRevenue60Days = Math.Round(
                baseMonthly * 2 * (decimal)Math.Pow(survivalFactor, 2), 2);
            result.ProjectedRevenue90Days = Math.Round(
                baseMonthly * 3 * (decimal)Math.Pow(survivalFactor, 3), 2);
            result.ProjectedRevenue180Days = Math.Round(
                baseMonthly * 6 * (decimal)Math.Pow(survivalFactor, 6), 2);

            return result;
        }

        // ================================================================
        //  Engine 7: Insight Generator
        // ================================================================

        private List<string> GenerateCustomerInsights(
            CustomerTrajectory t, List<Rental> rentals, DateTime now)
        {
            var insights = new List<string>();
            if (rentals.Count == 0)
            {
                insights.Add("New customer with no rental history yet.");
                return insights;
            }

            // Velocity insights
            if (t.Velocity.Trend == "Accelerating")
                insights.Add(t.CustomerName + " is accelerating — renting more frequently than before.");
            else if (t.Velocity.Trend == "Stalled")
            {
                var daysSince = (now - rentals.Last().RentalDate).TotalDays;
                insights.Add(t.CustomerName + " has stalled — " + (int)daysSince +
                    " days since last rental (avg interval: " + t.Velocity.AvgIntervalDays + " days).");
            }

            // Genre insights
            if (t.GenreEvolution.EmergingGenres.Count > 0)
                insights.Add("Genre shift detected: growing interest in " +
                    string.Join(", ", t.GenreEvolution.EmergingGenres) + ".");
            if (t.GenreEvolution.FadingGenres.Count > 0)
                insights.Add("Fading interest in " +
                    string.Join(", ", t.GenreEvolution.FadingGenres) + ".");

            // Spending insights
            if (t.Spending.Trend == "Rising")
                insights.Add("Spending is rising — avg $" + t.Spending.AvgMonthlySpend +
                    "/month, forecasted $" + t.Spending.ForecastedNextMonthSpend + " next month.");
            else if (t.Spending.Trend == "Declining")
                insights.Add("Spending declining — forecasted $" +
                    t.Spending.ForecastedNextMonthSpend + " next month (down from $" +
                    t.Spending.AvgMonthlySpend + " avg).");

            // Churn insights
            if (t.ChurnRisk.Tier == ChurnRiskTier.Critical || t.ChurnRisk.Tier == ChurnRiskTier.Lost)
                insights.Add("⚠ High churn risk (score: " + t.ChurnRisk.RiskScore +
                    ") — " + string.Join("; ", t.ChurnRisk.RiskFactors) + ".");

            // LTV insights
            if (t.LTV.ProjectedRevenue90Days > 50)
                insights.Add("High-value customer — projected $" +
                    t.LTV.ProjectedRevenue90Days + " over next 90 days.");
            else if (t.LTV.HistoricalLTV > 100 && t.LTV.ProjectedRevenue90Days < 10)
                insights.Add("Previously high-value ($" + t.LTV.HistoricalLTV +
                    " historical) but projected value dropping to $" +
                    t.LTV.ProjectedRevenue90Days + " next 90 days.");

            // Lifecycle insights
            if (t.Lifecycle.PredictedNextPhase.HasValue)
                insights.Add("Lifecycle: " + t.Lifecycle.Phase + " → likely " +
                    t.Lifecycle.PredictedNextPhase.Value + " next.");

            return insights;
        }

        // ================================================================
        //  Fleet Health
        // ================================================================

        private FleetTrajectoryHealth ComputeFleetHealth(
            List<CustomerTrajectory> trajectories, DateTime now)
        {
            var health = new FleetTrajectoryHealth();
            health.TotalCustomers = trajectories.Count;

            foreach (var t in trajectories)
            {
                var phase = t.Lifecycle.Phase.ToString();
                if (!health.PhaseDistribution.ContainsKey(phase))
                    health.PhaseDistribution[phase] = 0;
                health.PhaseDistribution[phase]++;

                var tier = t.ChurnRisk.Tier.ToString();
                if (!health.ChurnRiskDistribution.ContainsKey(tier))
                    health.ChurnRiskDistribution[tier] = 0;
                health.ChurnRiskDistribution[tier]++;

                health.TotalProjectedRevenue90Days += t.LTV.ProjectedRevenue90Days;
            }

            // Health score: weighted by phase distribution
            if (trajectories.Count > 0)
            {
                int growingOrLoyal = trajectories.Count(t =>
                    t.Lifecycle.Phase == LifecyclePhase.Growing ||
                    t.Lifecycle.Phase == LifecyclePhase.Loyal);
                int declining = trajectories.Count(t =>
                    t.Lifecycle.Phase == LifecyclePhase.Declining ||
                    t.Lifecycle.Phase == LifecyclePhase.Dormant ||
                    t.Lifecycle.Phase == LifecyclePhase.Churned);
                int lowChurn = trajectories.Count(t =>
                    t.ChurnRisk.Tier == ChurnRiskTier.Safe ||
                    t.ChurnRisk.Tier == ChurnRiskTier.Watch);

                double phaseScore = (double)growingOrLoyal / trajectories.Count * 60;
                double churnScore = (double)lowChurn / trajectories.Count * 40;
                health.HealthScore = (int)Math.Round(Math.Min(100, phaseScore + churnScore));
            }

            return health;
        }

        private List<string> GenerateFleetInsights(
            List<CustomerTrajectory> trajectories, FleetTrajectoryHealth health, DateTime now)
        {
            var insights = new List<string>();
            if (trajectories.Count == 0)
            {
                insights.Add("No customer data available for trajectory analysis.");
                return insights;
            }

            insights.Add("Fleet of " + health.TotalCustomers + " customers analyzed.");

            // Phase summary
            int churned = trajectories.Count(t => t.Lifecycle.Phase == LifecyclePhase.Churned);
            int growing = trajectories.Count(t => t.Lifecycle.Phase == LifecyclePhase.Growing);
            int loyal = trajectories.Count(t => t.Lifecycle.Phase == LifecyclePhase.Loyal);

            if (churned > trajectories.Count * 0.3)
                insights.Add("⚠ " + churned + " customers (" +
                    Math.Round((double)churned / trajectories.Count * 100) +
                    "%) have churned — consider win-back campaigns.");

            if (growing > 0)
                insights.Add(growing + " customer(s) in growth phase — nurture with personalized offers.");

            if (loyal > 0)
                insights.Add(loyal + " loyal customer(s) — reward to maintain retention.");

            // Revenue projection
            if (health.TotalProjectedRevenue90Days > 0)
                insights.Add("Projected fleet revenue over 90 days: $" +
                    Math.Round(health.TotalProjectedRevenue90Days, 2) + ".");

            // High risk alerts
            var criticalCustomers = trajectories
                .Where(t => t.ChurnRisk.Tier == ChurnRiskTier.Critical)
                .ToList();
            if (criticalCustomers.Count > 0)
                insights.Add("🚨 " + criticalCustomers.Count +
                    " customer(s) at critical churn risk: " +
                    string.Join(", ", criticalCustomers.Select(c => c.CustomerName)) + ".");

            return insights;
        }

        // ================================================================
        //  Internal: Build full trajectory for one customer
        // ================================================================

        private CustomerTrajectory BuildTrajectory(
            Customer customer, List<Rental> rentals,
            Dictionary<int, Movie> movieLookup, DateTime now)
        {
            var trajectory = new CustomerTrajectory
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name
            };

            trajectory.Velocity = ForecastVelocity(rentals, now);
            trajectory.GenreEvolution = PredictGenreEvolution(rentals, movieLookup);
            trajectory.Spending = AnalyzeSpending(rentals, now);
            trajectory.Lifecycle = ClassifyLifecycle(rentals, now);
            trajectory.ChurnRisk = EstimateChurnRisk(rentals, trajectory.Velocity, now);
            trajectory.LTV = ProjectLifetimeValue(rentals, trajectory.Spending, trajectory.ChurnRisk);
            trajectory.Insights = GenerateCustomerInsights(trajectory, rentals, now);

            return trajectory;
        }
    }
}
