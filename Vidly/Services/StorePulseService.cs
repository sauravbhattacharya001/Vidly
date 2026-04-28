using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous store health monitoring — aggregates inventory, revenue,
    /// customer activity, and operations signals into a unified health score
    /// with anomaly detection and auto-generated action items.
    /// </summary>
    public class StorePulseService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        // Signal weights for overall score (must sum to 1.0)
        private const double WeightInventory = 0.15;
        private const double WeightRevenue = 0.20;
        private const double WeightOverdue = 0.20;
        private const double WeightCustomerActivity = 0.15;
        private const double WeightLateFeeBurden = 0.10;
        private const double WeightGenreConcentration = 0.10;
        private const double WeightReturnCompliance = 0.10;

        public StorePulseService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _movieRepository = movieRepository;
            _rentalRepository = rentalRepository;
            _customerRepository = customerRepository;
            _clock = clock;
        }

        /// <summary>
        /// Generate a full store pulse report with all signals, anomalies, and action items.
        /// </summary>
        public StorePulseReport GenerateReport()
        {
            var now = _clock.Now;
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var allCustomers = _customerRepository.GetAll();

            var signals = new List<PulseSignal>();

            signals.Add(CalculateInventoryUtilization(allRentals, allMovies, now));
            signals.Add(CalculateRevenueVelocity(allRentals, now));
            signals.Add(CalculateOverdueRate(allRentals, now));
            signals.Add(CalculateCustomerActivity(allRentals, allCustomers, now));
            signals.Add(CalculateLateFeeBurden(allRentals, now));
            signals.Add(CalculateGenreConcentration(allRentals, allMovies, now));
            signals.Add(CalculateReturnCompliance(allRentals, now));

            double weightedSum = 0;
            foreach (var s in signals)
            {
                double weight = GetWeight(s.Name);
                weightedSum += s.Score * weight;
            }
            int overallScore = Math.Min(100, Math.Max(0, (int)Math.Round(weightedSum)));

            var anomalies = DetectAnomalies(allRentals, allMovies, now);
            var actionItems = GenerateActionItems(signals, anomalies, allRentals, allMovies, allCustomers, now);
            var trend = CalculateTrend(allRentals, allMovies, allCustomers, now);

            var report = new StorePulseReport();
            report.GeneratedAt = now;
            report.OverallHealthScore = overallScore;
            report.HealthGrade = ScoreToGrade(overallScore);
            report.Signals = signals;
            report.Anomalies = anomalies;
            report.ActionItems = actionItems;
            report.Trend = trend;

            return report;
        }

        /// <summary>
        /// Lightweight health check returning just the overall score and grade.
        /// </summary>
        public StorePulseReport GetHealthCheck()
        {
            var report = GenerateReport();
            // Clear heavy data for lightweight response
            report.Anomalies = new List<PulseAnomaly>();
            report.ActionItems = new List<PulseActionItem>();
            return report;
        }

        /// <summary>
        /// Get a single signal by name.
        /// </summary>
        public PulseSignal GetSignal(string name)
        {
            var report = GenerateReport();
            foreach (var s in report.Signals)
            {
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        // ── Signal Calculators ───────────────────────────────────────

        private PulseSignal CalculateInventoryUtilization(
            IEnumerable<Rental> rentals, IEnumerable<Movie> movies, DateTime now)
        {
            var movieList = movies.ToList();
            int totalMovies = movieList.Count;
            int activeRentals = rentals.Count(
                delegate(Rental r) { return r.Status == RentalStatus.Active || r.Status == RentalStatus.Overdue; });

            double utilization = totalMovies > 0 ? (double)activeRentals / totalMovies : 0;
            int score;
            string status;

            if (utilization > 0.90)
            {
                score = 30;
                status = "critical";
            }
            else if (utilization > 0.75)
            {
                score = 60;
                status = "warning";
            }
            else if (utilization < 0.10)
            {
                score = 50;
                status = "warning";
            }
            else
            {
                score = 90;
                status = "healthy";
            }

            var signal = new PulseSignal();
            signal.Name = "Inventory Utilization";
            signal.Category = "inventory";
            signal.Score = score;
            signal.Status = status;
            signal.Description = string.Format("{0} of {1} titles currently rented ({2:P0} utilization)",
                activeRentals, totalMovies, utilization);
            signal.Metrics["activeRentals"] = activeRentals;
            signal.Metrics["totalMovies"] = totalMovies;
            signal.Metrics["utilizationRate"] = Math.Round(utilization * 100, 1);
            return signal;
        }

        private PulseSignal CalculateRevenueVelocity(IEnumerable<Rental> rentals, DateTime now)
        {
            var rentalList = rentals.ToList();
            var currentPeriodStart = now.AddDays(-30);
            var previousPeriodStart = now.AddDays(-60);

            decimal currentRevenue = 0;
            decimal previousRevenue = 0;
            foreach (var r in rentalList)
            {
                if (r.RentalDate >= currentPeriodStart && r.RentalDate <= now)
                    currentRevenue += r.TotalCost;
                else if (r.RentalDate >= previousPeriodStart && r.RentalDate < currentPeriodStart)
                    previousRevenue += r.TotalCost;
            }

            double changePercent = previousRevenue > 0
                ? (double)((currentRevenue - previousRevenue) / previousRevenue) * 100
                : 0;

            int score;
            string status;
            if (changePercent < -20)
            {
                score = 30;
                status = "critical";
            }
            else if (changePercent < -5)
            {
                score = 55;
                status = "warning";
            }
            else if (changePercent > 20)
            {
                score = 95;
                status = "healthy";
            }
            else
            {
                score = 80;
                status = "healthy";
            }

            var signal = new PulseSignal();
            signal.Name = "Revenue Velocity";
            signal.Category = "revenue";
            signal.Score = score;
            signal.Status = status;
            signal.Description = string.Format("30-day revenue: {0:C} ({1:+0.0;-0.0;0.0}% vs previous period)",
                currentRevenue, changePercent);
            signal.Metrics["currentRevenue"] = Math.Round(currentRevenue, 2);
            signal.Metrics["previousRevenue"] = Math.Round(previousRevenue, 2);
            signal.Metrics["changePercent"] = Math.Round(changePercent, 1);
            return signal;
        }

        private PulseSignal CalculateOverdueRate(IEnumerable<Rental> rentals, DateTime now)
        {
            int activeCount = 0;
            int overdueCount = 0;
            foreach (var r in rentals)
            {
                if (r.Status == RentalStatus.Active || r.Status == RentalStatus.Overdue)
                {
                    activeCount++;
                    if (r.DueDate < now)
                        overdueCount++;
                }
            }

            double overdueRate = activeCount > 0 ? (double)overdueCount / activeCount : 0;
            int score;
            string status;
            if (overdueRate > 0.25)
            {
                score = 20;
                status = "critical";
            }
            else if (overdueRate > 0.10)
            {
                score = 55;
                status = "warning";
            }
            else
            {
                score = 95;
                status = "healthy";
            }

            var signal = new PulseSignal();
            signal.Name = "Overdue Rate";
            signal.Category = "operations";
            signal.Score = score;
            signal.Status = status;
            signal.Description = string.Format("{0} of {1} active rentals overdue ({2:P0})",
                overdueCount, activeCount, overdueRate);
            signal.Metrics["overdueCount"] = overdueCount;
            signal.Metrics["activeCount"] = activeCount;
            signal.Metrics["overdueRate"] = Math.Round(overdueRate * 100, 1);
            return signal;
        }

        private PulseSignal CalculateCustomerActivity(
            IEnumerable<Rental> rentals, IEnumerable<Customer> customers, DateTime now)
        {
            var recentCutoff = now.AddDays(-30);
            var dormantCutoff = now.AddDays(-60);

            // Single-pass: track most recent rental date per customer and
            // whether they had any rental in the recent period. Replaces
            // the previous O(inactiveCustomers × totalRentals) nested loop
            // with O(totalRentals) using a per-customer latest-date map.
            var recentCustomerIds = new HashSet<int>();
            var latestRentalByCustomer = new Dictionary<int, DateTime>();
            foreach (var r in rentals)
            {
                if (r.RentalDate >= recentCutoff)
                    recentCustomerIds.Add(r.CustomerId);

                if (!latestRentalByCustomer.TryGetValue(r.CustomerId, out var prev)
                    || r.RentalDate > prev)
                {
                    latestRentalByCustomer[r.CustomerId] = r.RentalDate;
                }
            }

            int totalCustomers = customers.Count();
            int activeCustomers = recentCustomerIds.Count;
            int dormantCustomers = 0;
            foreach (var kv in latestRentalByCustomer)
            {
                if (!recentCustomerIds.Contains(kv.Key) && kv.Value < dormantCutoff)
                    dormantCustomers++;
            }

            double activeRate = totalCustomers > 0 ? (double)activeCustomers / totalCustomers : 0;
            int score;
            string status;
            if (activeRate < 0.20)
            {
                score = 30;
                status = "critical";
            }
            else if (activeRate < 0.40)
            {
                score = 55;
                status = "warning";
            }
            else
            {
                score = 85;
                status = "healthy";
            }

            var signal = new PulseSignal();
            signal.Name = "Customer Activity";
            signal.Category = "customers";
            signal.Score = score;
            signal.Status = status;
            signal.Description = string.Format("{0} active customers in last 30 days ({1:P0} of {2}), {3} dormant",
                activeCustomers, activeRate, totalCustomers, dormantCustomers);
            signal.Metrics["activeCustomers"] = activeCustomers;
            signal.Metrics["totalCustomers"] = totalCustomers;
            signal.Metrics["dormantCustomers"] = dormantCustomers;
            signal.Metrics["activeRate"] = Math.Round(activeRate * 100, 1);
            return signal;
        }

        private PulseSignal CalculateLateFeeBurden(IEnumerable<Rental> rentals, DateTime now)
        {
            var recentRentals = new List<Rental>();
            var cutoff = now.AddDays(-90);
            foreach (var r in rentals)
            {
                if (r.RentalDate >= cutoff)
                    recentRentals.Add(r);
            }

            decimal totalLateFees = 0;
            var customerLateFees = new Dictionary<int, decimal>();
            foreach (var r in recentRentals)
            {
                if (r.LateFee > 0)
                {
                    totalLateFees += r.LateFee;
                    if (!customerLateFees.ContainsKey(r.CustomerId))
                        customerLateFees[r.CustomerId] = 0;
                    customerLateFees[r.CustomerId] = customerLateFees[r.CustomerId] + r.LateFee;
                }
            }

            decimal avgLateFeePerCustomer = customerLateFees.Count > 0
                ? totalLateFees / customerLateFees.Count
                : 0;

            int score;
            string status;
            if (avgLateFeePerCustomer > 15)
            {
                score = 30;
                status = "critical";
            }
            else if (avgLateFeePerCustomer > 8)
            {
                score = 55;
                status = "warning";
            }
            else
            {
                score = 90;
                status = "healthy";
            }

            var signal = new PulseSignal();
            signal.Name = "Late Fee Burden";
            signal.Category = "customers";
            signal.Score = score;
            signal.Status = status;
            signal.Description = string.Format("Avg late fee per affected customer: {0:C} ({1} customers with late fees in 90 days)",
                avgLateFeePerCustomer, customerLateFees.Count);
            signal.Metrics["totalLateFees"] = Math.Round(totalLateFees, 2);
            signal.Metrics["affectedCustomers"] = customerLateFees.Count;
            signal.Metrics["avgPerCustomer"] = Math.Round(avgLateFeePerCustomer, 2);
            return signal;
        }

        private PulseSignal CalculateGenreConcentration(
            IEnumerable<Rental> rentals, IEnumerable<Movie> movies, DateTime now)
        {
            var movieLookup = new Dictionary<int, Movie>();
            foreach (var m in movies)
                movieLookup[m.Id] = m;

            var cutoff = now.AddDays(-30);
            var genreCounts = new Dictionary<string, int>();
            int totalRecentRentals = 0;

            foreach (var r in rentals)
            {
                if (r.RentalDate >= cutoff)
                {
                    totalRecentRentals++;
                    Movie movie;
                    if (movieLookup.TryGetValue(r.MovieId, out movie) && movie.Genre.HasValue)
                    {
                        string genre = movie.Genre.Value.ToString();
                        if (!genreCounts.ContainsKey(genre))
                            genreCounts[genre] = 0;
                        genreCounts[genre] = genreCounts[genre] + 1;
                    }
                }
            }

            string topGenre = "";
            int topCount = 0;
            foreach (var kv in genreCounts)
            {
                if (kv.Value > topCount)
                {
                    topCount = kv.Value;
                    topGenre = kv.Key;
                }
            }

            double concentration = totalRecentRentals > 0
                ? (double)topCount / totalRecentRentals
                : 0;

            int score;
            string status;
            if (concentration > 0.60)
            {
                score = 40;
                status = "warning";
            }
            else if (concentration > 0.45)
            {
                score = 65;
                status = "warning";
            }
            else
            {
                score = 90;
                status = "healthy";
            }

            var signal = new PulseSignal();
            signal.Name = "Genre Diversity";
            signal.Category = "inventory";
            signal.Score = score;
            signal.Status = status;
            signal.Description = string.Format("Top genre: {0} ({1:P0} of recent rentals). {2} genres represented.",
                topGenre.Length > 0 ? topGenre : "N/A", concentration, genreCounts.Count);
            signal.Metrics["topGenre"] = topGenre.Length > 0 ? topGenre : "N/A";
            signal.Metrics["concentration"] = Math.Round(concentration * 100, 1);
            signal.Metrics["genreCount"] = genreCounts.Count;
            return signal;
        }

        private PulseSignal CalculateReturnCompliance(IEnumerable<Rental> rentals, DateTime now)
        {
            var cutoff = now.AddDays(-90);
            int returnedCount = 0;
            int onTimeCount = 0;

            foreach (var r in rentals)
            {
                if (r.Status == RentalStatus.Returned && r.ReturnDate.HasValue && r.RentalDate >= cutoff)
                {
                    returnedCount++;
                    if (r.ReturnDate.Value <= r.DueDate)
                        onTimeCount++;
                }
            }

            double complianceRate = returnedCount > 0
                ? (double)onTimeCount / returnedCount
                : 1.0;

            int score;
            string status;
            if (complianceRate < 0.60)
            {
                score = 25;
                status = "critical";
            }
            else if (complianceRate < 0.80)
            {
                score = 55;
                status = "warning";
            }
            else
            {
                score = 90;
                status = "healthy";
            }

            var signal = new PulseSignal();
            signal.Name = "Return Compliance";
            signal.Category = "operations";
            signal.Score = score;
            signal.Status = status;
            signal.Description = string.Format("{0} of {1} returns on time ({2:P0} compliance rate)",
                onTimeCount, returnedCount, complianceRate);
            signal.Metrics["onTimeReturns"] = onTimeCount;
            signal.Metrics["totalReturns"] = returnedCount;
            signal.Metrics["complianceRate"] = Math.Round(complianceRate * 100, 1);
            return signal;
        }

        // ── Anomaly Detection ────────────────────────────────────────

        private List<PulseAnomaly> DetectAnomalies(
            IEnumerable<Rental> rentals, IEnumerable<Movie> movies, DateTime now)
        {
            var anomalies = new List<PulseAnomaly>();
            var rentalList = rentals.ToList();

            // Bucket rentals by week and detect volume anomalies
            var weeklyBuckets = new Dictionary<int, int>();
            foreach (var r in rentalList)
            {
                int weeksAgo = (int)Math.Floor((now - r.RentalDate).TotalDays / 7);
                if (weeksAgo >= 0 && weeksAgo < 52)
                {
                    if (!weeklyBuckets.ContainsKey(weeksAgo))
                        weeklyBuckets[weeksAgo] = 0;
                    weeklyBuckets[weeksAgo] = weeklyBuckets[weeksAgo] + 1;
                }
            }

            if (weeklyBuckets.Count >= 4)
            {
                // Calculate mean and std dev (excluding current week)
                double sum = 0;
                int count = 0;
                foreach (var kv in weeklyBuckets)
                {
                    if (kv.Key > 0)
                    {
                        sum += kv.Value;
                        count++;
                    }
                }

                if (count > 0)
                {
                    double mean = sum / count;
                    double varianceSum = 0;
                    foreach (var kv in weeklyBuckets)
                    {
                        if (kv.Key > 0)
                        {
                            double diff = kv.Value - mean;
                            varianceSum += diff * diff;
                        }
                    }
                    double stdDev = Math.Sqrt(varianceSum / count);

                    int currentWeekCount;
                    if (!weeklyBuckets.TryGetValue(0, out currentWeekCount))
                        currentWeekCount = 0;

                    if (stdDev > 0)
                    {
                        double zScore = (currentWeekCount - mean) / stdDev;
                        if (zScore > 2)
                        {
                            var a = new PulseAnomaly();
                            a.SignalName = "Revenue Velocity";
                            a.Type = "spike";
                            a.Severity = zScore > 3 ? "critical" : "warning";
                            a.Description = string.Format(
                                "This week's rental volume ({0}) is {1:F1} standard deviations above average ({2:F0})",
                                currentWeekCount, zScore, mean);
                            a.DeviationPercent = Math.Round((currentWeekCount - mean) / mean * 100, 1);
                            a.DetectedAt = now;
                            anomalies.Add(a);
                        }
                        else if (zScore < -2)
                        {
                            var a = new PulseAnomaly();
                            a.SignalName = "Revenue Velocity";
                            a.Type = "drop";
                            a.Severity = zScore < -3 ? "critical" : "warning";
                            a.Description = string.Format(
                                "This week's rental volume ({0}) is {1:F1} standard deviations below average ({2:F0})",
                                currentWeekCount, Math.Abs(zScore), mean);
                            a.DeviationPercent = Math.Round((mean - currentWeekCount) / mean * 100, 1);
                            a.DetectedAt = now;
                            anomalies.Add(a);
                        }
                    }
                }
            }

            // Check for overdue spike
            int totalActive = 0;
            int totalOverdue = 0;
            foreach (var r in rentalList)
            {
                if (r.Status == RentalStatus.Active || r.Status == RentalStatus.Overdue)
                {
                    totalActive++;
                    if (r.DueDate < now)
                        totalOverdue++;
                }
            }

            if (totalActive > 0)
            {
                double overdueRate = (double)totalOverdue / totalActive;
                if (overdueRate > 0.30)
                {
                    var a = new PulseAnomaly();
                    a.SignalName = "Overdue Rate";
                    a.Type = "threshold_breach";
                    a.Severity = "critical";
                    a.Description = string.Format(
                        "Overdue rate at {0:P0} — exceeds 30% critical threshold ({1} of {2} rentals)",
                        overdueRate, totalOverdue, totalActive);
                    a.DeviationPercent = Math.Round(overdueRate * 100, 1);
                    a.DetectedAt = now;
                    anomalies.Add(a);
                }
            }

            return anomalies;
        }

        // ── Action Item Generation ───────────────────────────────────

        private List<PulseActionItem> GenerateActionItems(
            List<PulseSignal> signals,
            List<PulseAnomaly> anomalies,
            IEnumerable<Rental> rentals,
            IEnumerable<Movie> movies,
            IEnumerable<Customer> customers,
            DateTime now)
        {
            var items = new List<PulseActionItem>();

            foreach (var signal in signals)
            {
                if (signal.Score < 40)
                {
                    if (signal.Name == "Inventory Utilization")
                    {
                        var item = new PulseActionItem();
                        item.Priority = 1;
                        item.Category = "inventory";
                        item.Title = "Critical: Rebalance inventory";
                        item.Description = string.Format(
                            "Utilization at {0}%. Consider purchasing additional copies of high-demand titles or running promotions on underperforming stock.",
                            signal.Metrics.ContainsKey("utilizationRate") ? signal.Metrics["utilizationRate"] : "N/A");
                        item.Impact = "high";
                        item.IsAutomatable = false;
                        items.Add(item);
                    }
                    else if (signal.Name == "Revenue Velocity")
                    {
                        var item = new PulseActionItem();
                        item.Priority = 1;
                        item.Category = "revenue";
                        item.Title = "Urgent: Revenue decline detected";
                        item.Description = "30-day revenue has dropped significantly. Consider launching targeted promotions, bundle deals, or seasonal campaigns.";
                        item.Impact = "high";
                        item.IsAutomatable = true;
                        items.Add(item);
                    }
                    else if (signal.Name == "Overdue Rate")
                    {
                        var item = new PulseActionItem();
                        item.Priority = 1;
                        item.Category = "operations";
                        item.Title = "Critical: High overdue rate";
                        item.Description = "More than 25% of active rentals are overdue. Send automated reminder notifications and consider adjusting due date policies.";
                        item.Impact = "high";
                        item.IsAutomatable = true;
                        items.Add(item);
                    }
                    else if (signal.Name == "Customer Activity")
                    {
                        var item = new PulseActionItem();
                        item.Priority = 2;
                        item.Category = "customers";
                        item.Title = "Launch win-back campaign";
                        item.Description = string.Format(
                            "{0} dormant customers identified. Send personalized re-engagement offers based on their genre preferences.",
                            signal.Metrics.ContainsKey("dormantCustomers") ? signal.Metrics["dormantCustomers"] : "0");
                        item.Impact = "high";
                        item.IsAutomatable = true;
                        items.Add(item);
                    }
                    else if (signal.Name == "Late Fee Burden")
                    {
                        var item = new PulseActionItem();
                        item.Priority = 2;
                        item.Category = "customers";
                        item.Title = "Review late fee policy";
                        item.Description = "Average late fees per customer are high, risking churn. Consider grace period extensions or fee caps for loyal members.";
                        item.Impact = "medium";
                        item.IsAutomatable = false;
                        items.Add(item);
                    }
                    else if (signal.Name == "Return Compliance")
                    {
                        var item = new PulseActionItem();
                        item.Priority = 1;
                        item.Category = "operations";
                        item.Title = "Improve return compliance";
                        item.Description = "On-time return rate is critically low. Implement automated due-date reminders and consider incentives for on-time returns.";
                        item.Impact = "high";
                        item.IsAutomatable = true;
                        items.Add(item);
                    }
                }
                else if (signal.Score < 60)
                {
                    if (signal.Name == "Genre Diversity")
                    {
                        string topGenre = signal.Metrics.ContainsKey("topGenre")
                            ? signal.Metrics["topGenre"].ToString()
                            : "Unknown";
                        var item = new PulseActionItem();
                        item.Priority = 3;
                        item.Category = "inventory";
                        item.Title = string.Format("Diversify beyond {0}", topGenre);
                        item.Description = string.Format(
                            "{0} dominates rentals. Promote underrepresented genres with staff picks and featured collections.",
                            topGenre);
                        item.Impact = "medium";
                        item.IsAutomatable = true;
                        items.Add(item);
                    }

                    if (signal.Name == "Customer Activity")
                    {
                        var item = new PulseActionItem();
                        item.Priority = 3;
                        item.Category = "customers";
                        item.Title = "Boost customer engagement";
                        item.Description = "Customer activity is below optimal. Consider loyalty program enhancements or movie night events.";
                        item.Impact = "medium";
                        item.IsAutomatable = false;
                        items.Add(item);
                    }
                }
            }

            // Sort by priority
            items.Sort(delegate(PulseActionItem a, PulseActionItem b)
            {
                return a.Priority.CompareTo(b.Priority);
            });

            return items;
        }

        // ── Trend Calculation ────────────────────────────────────────

        private PulseTrend CalculateTrend(
            IEnumerable<Rental> rentals, IEnumerable<Movie> movies,
            IEnumerable<Customer> customers, DateTime now)
        {
            // Compare current 30-day period vs previous 30-day period using simple metrics
            var rentalList = rentals.ToList();
            var currentStart = now.AddDays(-30);
            var previousStart = now.AddDays(-60);

            int currentRentals = 0;
            int previousRentals = 0;
            decimal currentRev = 0;
            decimal previousRev = 0;
            foreach (var r in rentalList)
            {
                if (r.RentalDate >= currentStart && r.RentalDate <= now)
                {
                    currentRentals++;
                    currentRev += r.TotalCost;
                }
                else if (r.RentalDate >= previousStart && r.RentalDate < currentStart)
                {
                    previousRentals++;
                    previousRev += r.TotalCost;
                }
            }

            int scoreChange;
            string direction;
            if (previousRentals == 0 && currentRentals > 0)
            {
                scoreChange = 10;
                direction = "improving";
            }
            else if (previousRentals == 0)
            {
                scoreChange = 0;
                direction = "stable";
            }
            else
            {
                double rentalChange = (double)(currentRentals - previousRentals) / previousRentals;
                double revenueChange = previousRev > 0
                    ? (double)(currentRev - previousRev) / (double)previousRev
                    : 0;
                double combined = (rentalChange + revenueChange) / 2;

                if (combined > 0.05)
                {
                    direction = "improving";
                    scoreChange = Math.Min(20, (int)(combined * 100));
                }
                else if (combined < -0.05)
                {
                    direction = "declining";
                    scoreChange = Math.Max(-20, (int)(combined * 100));
                }
                else
                {
                    direction = "stable";
                    scoreChange = 0;
                }
            }

            var trend = new PulseTrend();
            trend.Direction = direction;
            trend.ScoreChange = scoreChange;
            trend.Summary = string.Format("{0} rentals ({1:+0;-0;0} vs prev), {2:C} revenue ({3:+0;-0;0} vs prev)",
                currentRentals, currentRentals - previousRentals,
                currentRev, currentRev - previousRev);
            return trend;
        }

        // ── Helpers ──────────────────────────────────────────────────

        private double GetWeight(string signalName)
        {
            if (signalName == "Inventory Utilization") return WeightInventory;
            if (signalName == "Revenue Velocity") return WeightRevenue;
            if (signalName == "Overdue Rate") return WeightOverdue;
            if (signalName == "Customer Activity") return WeightCustomerActivity;
            if (signalName == "Late Fee Burden") return WeightLateFeeBurden;
            if (signalName == "Genre Diversity") return WeightGenreConcentration;
            if (signalName == "Return Compliance") return WeightReturnCompliance;
            return 0;
        }

        private static string ScoreToGrade(int score)
        {
            if (score >= 95) return "A+";
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }
    }
}
