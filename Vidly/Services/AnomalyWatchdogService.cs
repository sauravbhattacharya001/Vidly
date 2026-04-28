using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Rental Anomaly Watchdog — proactively detects suspicious and
    /// anomalous rental patterns across 6 detection engines: rental bursts,
    /// genre shifts, return pattern anomalies, inventory concentration,
    /// rate abuse, and identity risks. Maintains a risk-scored watchlist
    /// of flagged customers.
    /// </summary>
    public class AnomalyWatchdogService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IClock _clock;
        private readonly WatchdogConfig _config;

        private static readonly List<AnomalyAlert> _alerts = new List<AnomalyAlert>();
        private static readonly Dictionary<int, WatchlistEntry> _watchlist
            = new Dictionary<int, WatchlistEntry>();
        private static int _nextAlertId = 1;

        public AnomalyWatchdogService(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo,
            IClock clock,
            WatchdogConfig config = null)
        {
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? new WatchdogConfig();
        }

        /// <summary>
        /// Runs all 6 detection engines, builds alerts, updates watchlist,
        /// and returns a complete anomaly report.
        /// </summary>
        public AnomalyReport RunFullScan()
        {
            var alerts = new List<AnomalyAlert>();
            alerts.AddRange(DetectRentalBursts());
            alerts.AddRange(DetectGenreShifts());
            alerts.AddRange(DetectReturnAnomalies());
            alerts.AddRange(DetectInventoryConcentration());
            alerts.AddRange(DetectRateAbuse());
            alerts.AddRange(DetectIdentityRisks());

            // Store alerts
            foreach (var alert in alerts)
            {
                alert.Id = _nextAlertId++;
                _alerts.Add(alert);
            }

            // Update watchlist from alerts
            UpdateWatchlist(alerts);

            var report = new AnomalyReport
            {
                GeneratedAt = _clock.Now,
                Alerts = alerts,
                Watchlist = GetWatchlist(),
                ThreatLevel = ClassifyThreatLevel(alerts),
                TotalAlertsGenerated = alerts.Count,
                CriticalCount = alerts.Count(a => a.Severity == AnomalySeverity.Critical),
                HighCount = alerts.Count(a => a.Severity == AnomalySeverity.High),
                MediumCount = alerts.Count(a => a.Severity == AnomalySeverity.Medium),
                LowCount = alerts.Count(a => a.Severity == AnomalySeverity.Low)
            };

            report.Summary = BuildSummary(report);
            return report;
        }

        /// <summary>
        /// Detects customers with an unusually high number of rentals in 24 hours.
        /// </summary>
        public List<AnomalyAlert> DetectRentalBursts()
        {
            var alerts = new List<AnomalyAlert>();
            var now = _clock.Now;
            var cutoff = now.AddHours(-24);
            var allRentals = _rentalRepo.GetAll();
            var customers = _customerRepo.GetAll();

            var recentByCustomer = allRentals
                .Where(r => r.RentalDate >= cutoff)
                .GroupBy(r => r.CustomerId)
                .Where(g => g.Count() >= _config.RentalBurstThreshold);

            foreach (var group in recentByCustomer)
            {
                var count = group.Count();
                var customer = customers.FirstOrDefault(c => c.Id == group.Key);
                var ratio = (double)count / _config.RentalBurstThreshold;
                var score = Math.Min(100, ratio * 40);

                var alert = new AnomalyAlert
                {
                    Type = AnomalyType.RentalBurst,
                    Severity = ratio >= 3 ? AnomalySeverity.Critical
                             : ratio >= 2 ? AnomalySeverity.High
                             : AnomalySeverity.Medium,
                    CustomerId = group.Key,
                    CustomerName = customer?.Name ?? "Unknown",
                    Title = string.Format("Rental burst: {0} rentals in 24h", count),
                    Description = string.Format("{0} rented {1} movies in the last 24 hours (threshold: {2}).",
                        customer?.Name ?? "Customer #" + group.Key, count, _config.RentalBurstThreshold),
                    Score = score,
                    DetectedAt = now
                };

                alert.Evidence.AddRange(group.Select(r =>
                    string.Format("Rented \"{0}\" at {1:g}", r.MovieName ?? "Movie #" + r.MovieId, r.RentalDate)));
                alert.RecommendedActions.Add("Review account for potential abuse");
                alert.RecommendedActions.Add("Temporarily limit rental rate");
                if (ratio >= 2)
                    alert.RecommendedActions.Add("Flag for manual review before next rental");

                alerts.Add(alert);
            }

            return alerts;
        }

        /// <summary>
        /// Detects customers whose recent genre preferences have shifted
        /// dramatically from their historical patterns.
        /// </summary>
        public List<AnomalyAlert> DetectGenreShifts()
        {
            var alerts = new List<AnomalyAlert>();
            var now = _clock.Now;
            var windowStart = now.AddDays(-_config.GenreShiftWindow);
            var allRentals = _rentalRepo.GetAll();
            var allMovies = _movieRepo.GetAll();
            var customers = _customerRepo.GetAll();

            var movieGenres = allMovies
                .Where(m => m.Genre.HasValue)
                .ToDictionary(m => m.Id, m => m.Genre.Value);

            var rentalsByCustomer = allRentals
                .Where(r => movieGenres.ContainsKey(r.MovieId))
                .GroupBy(r => r.CustomerId);

            foreach (var group in rentalsByCustomer)
            {
                var allCustRentals = group.ToList();
                // Need enough history to compare
                var historicalRentals = allCustRentals.Where(r => r.RentalDate < windowStart).ToList();
                var recentRentals = allCustRentals.Where(r => r.RentalDate >= windowStart).ToList();

                if (historicalRentals.Count < 3 || recentRentals.Count < 2)
                    continue;

                var historicalDist = BuildGenreDistribution(historicalRentals, movieGenres);
                var recentDist = BuildGenreDistribution(recentRentals, movieGenres);

                var distance = ComputeDistributionDistance(historicalDist, recentDist);

                if (distance >= _config.GenreShiftThreshold)
                {
                    var customer = customers.FirstOrDefault(c => c.Id == group.Key);
                    var score = Math.Min(100, (distance / 1.0) * 60);

                    var alert = new AnomalyAlert
                    {
                        Type = AnomalyType.GenreShift,
                        Severity = distance >= 1.2 ? AnomalySeverity.High
                                 : distance >= 0.9 ? AnomalySeverity.Medium
                                 : AnomalySeverity.Low,
                        CustomerId = group.Key,
                        CustomerName = customer?.Name ?? "Unknown",
                        Title = string.Format("Genre shift detected (distance: {0:F2})", distance),
                        Description = string.Format("{0}'s recent genre preferences differ significantly from their history.",
                            customer?.Name ?? "Customer #" + group.Key),
                        Score = score,
                        DetectedAt = now
                    };

                    // Build evidence showing the shift
                    foreach (var genre in Enum.GetValues(typeof(Genre)).Cast<Genre>())
                    {
                        double histPct, recentPct;
                        historicalDist.TryGetValue(genre, out histPct);
                        recentDist.TryGetValue(genre, out recentPct);
                        if (Math.Abs(histPct - recentPct) > 0.15)
                        {
                            alert.Evidence.Add(string.Format("{0}: was {1:P0}, now {2:P0}",
                                genre, histPct, recentPct));
                        }
                    }

                    alert.RecommendedActions.Add("Verify account ownership (possible shared/compromised account)");
                    alert.RecommendedActions.Add("Monitor for further behavioral changes");
                    alerts.Add(alert);
                }
            }

            return alerts;
        }

        /// <summary>
        /// Detects customers with abnormal return patterns — high late return rate
        /// or suspiciously perfect on-time returns.
        /// </summary>
        public List<AnomalyAlert> DetectReturnAnomalies()
        {
            var alerts = new List<AnomalyAlert>();
            var now = _clock.Now;
            var allRentals = _rentalRepo.GetAll();
            var customers = _customerRepo.GetAll();

            var returnedByCustomer = allRentals
                .Where(r => r.Status == RentalStatus.Returned && r.ReturnDate.HasValue)
                .GroupBy(r => r.CustomerId);

            foreach (var group in returnedByCustomer)
            {
                var rentals = group.ToList();
                if (rentals.Count < 3) continue;

                var customer = customers.FirstOrDefault(c => c.Id == group.Key);

                // Late return rate check
                var lateCount = rentals.Count(r => r.ReturnDate.Value > r.DueDate);
                var lateRate = (double)lateCount / rentals.Count;

                if (lateRate >= _config.LateReturnRateThreshold)
                {
                    var score = Math.Min(100, lateRate * 80);
                    var alert = new AnomalyAlert
                    {
                        Type = AnomalyType.ReturnPatternAnomaly,
                        Severity = lateRate >= 0.8 ? AnomalySeverity.High
                                 : lateRate >= 0.6 ? AnomalySeverity.Medium
                                 : AnomalySeverity.Low,
                        CustomerId = group.Key,
                        CustomerName = customer?.Name ?? "Unknown",
                        Title = string.Format("High late return rate: {0:P0}", lateRate),
                        Description = string.Format("{0} returned {1} of {2} rentals late ({3:P0}).",
                            customer?.Name ?? "Customer #" + group.Key, lateCount, rentals.Count, lateRate),
                        Score = score,
                        DetectedAt = now
                    };
                    alert.Evidence.Add(string.Format("{0} late out of {1} total returns", lateCount, rentals.Count));
                    alert.RecommendedActions.Add("Send late return warning");
                    alert.RecommendedActions.Add("Consider deposit requirement");
                    alerts.Add(alert);
                }

                // Perfect timing check: always returning on exact due date
                var perfectCount = rentals.Count(r => r.ReturnDate.Value.Date == r.DueDate.Date);
                var perfectRate = (double)perfectCount / rentals.Count;

                if (perfectRate >= 0.9 && rentals.Count >= 5)
                {
                    var alert = new AnomalyAlert
                    {
                        Type = AnomalyType.ReturnPatternAnomaly,
                        Severity = AnomalySeverity.Low,
                        CustomerId = group.Key,
                        CustomerName = customer?.Name ?? "Unknown",
                        Title = string.Format("Suspiciously perfect timing: {0:P0} exact due-date returns", perfectRate),
                        Description = string.Format("{0} returns on the exact due date {1:P0} of the time — possible gaming.",
                            customer?.Name ?? "Customer #" + group.Key, perfectRate),
                        Score = 25,
                        DetectedAt = now
                    };
                    alert.Evidence.Add(string.Format("{0} of {1} returns on exact due date", perfectCount, rentals.Count));
                    alert.RecommendedActions.Add("Monitor for pattern continuation");
                    alerts.Add(alert);
                }
            }

            return alerts;
        }

        /// <summary>
        /// Detects when a single customer holds a disproportionate share
        /// of all active rentals.
        /// </summary>
        public List<AnomalyAlert> DetectInventoryConcentration()
        {
            var alerts = new List<AnomalyAlert>();
            var now = _clock.Now;
            var allRentals = _rentalRepo.GetAll();
            var customers = _customerRepo.GetAll();

            var activeRentals = allRentals.Where(r => r.Status != RentalStatus.Returned).ToList();
            if (activeRentals.Count == 0) return alerts;

            var byCustomer = activeRentals.GroupBy(r => r.CustomerId);

            foreach (var group in byCustomer)
            {
                var count = group.Count();
                var fraction = (double)count / activeRentals.Count;

                if (fraction >= _config.ConcentrationThreshold)
                {
                    var customer = customers.FirstOrDefault(c => c.Id == group.Key);
                    var score = Math.Min(100, (fraction / _config.ConcentrationThreshold) * 50);

                    var alert = new AnomalyAlert
                    {
                        Type = AnomalyType.InventoryConcentration,
                        Severity = fraction >= 0.6 ? AnomalySeverity.Critical
                                 : fraction >= 0.45 ? AnomalySeverity.High
                                 : AnomalySeverity.Medium,
                        CustomerId = group.Key,
                        CustomerName = customer?.Name ?? "Unknown",
                        Title = string.Format("Inventory concentration: {0:P0} of active rentals", fraction),
                        Description = string.Format("{0} holds {1} of {2} active rentals ({3:P0}).",
                            customer?.Name ?? "Customer #" + group.Key, count, activeRentals.Count, fraction),
                        Score = score,
                        DetectedAt = now
                    };
                    alert.Evidence.Add(string.Format("{0} active rentals out of {1} total", count, activeRentals.Count));
                    alert.RecommendedActions.Add("Enforce concurrent rental limits");
                    alert.RecommendedActions.Add("Contact customer to verify intent");
                    alerts.Add(alert);
                }
            }

            return alerts;
        }

        /// <summary>
        /// Detects customers who consistently rent the most expensive movies
        /// and return them exactly on time — potential "try before you buy" exploitation.
        /// </summary>
        public List<AnomalyAlert> DetectRateAbuse()
        {
            var alerts = new List<AnomalyAlert>();
            var now = _clock.Now;
            var allRentals = _rentalRepo.GetAll();
            var customers = _customerRepo.GetAll();

            var returnedRentals = allRentals
                .Where(r => r.Status == RentalStatus.Returned && r.ReturnDate.HasValue)
                .ToList();

            if (returnedRentals.Count == 0) return alerts;

            var storeAvgRate = returnedRentals.Average(r => (double)r.DailyRate);

            var byCustomer = returnedRentals.GroupBy(r => r.CustomerId);

            foreach (var group in byCustomer)
            {
                var rentals = group.ToList();
                if (rentals.Count < 3) continue;

                var avgRate = rentals.Average(r => (double)r.DailyRate);
                var onTimeCount = rentals.Count(r =>
                    r.ReturnDate.Value.Date <= r.DueDate.Date);
                var onTimeRate = (double)onTimeCount / rentals.Count;

                // Flag if customer's avg rate is 1.5x+ store average AND returns on time 90%+
                if (avgRate >= storeAvgRate * 1.5 && onTimeRate >= 0.9)
                {
                    var customer = customers.FirstOrDefault(c => c.Id == group.Key);
                    var rateRatio = avgRate / storeAvgRate;
                    var score = Math.Min(100, rateRatio * 30);

                    var alert = new AnomalyAlert
                    {
                        Type = AnomalyType.RateAbuse,
                        Severity = rateRatio >= 3.0 ? AnomalySeverity.High
                                 : rateRatio >= 2.0 ? AnomalySeverity.Medium
                                 : AnomalySeverity.Low,
                        CustomerId = group.Key,
                        CustomerName = customer?.Name ?? "Unknown",
                        Title = string.Format("Possible rate abuse: avg ${0:F2}/day vs store ${1:F2}/day",
                            avgRate, storeAvgRate),
                        Description = string.Format("{0} consistently rents premium movies (avg ${1:F2}/day, store avg ${2:F2}/day) with {3:P0} on-time returns.",
                            customer?.Name ?? "Customer #" + group.Key, avgRate, storeAvgRate, onTimeRate),
                        Score = score,
                        DetectedAt = now
                    };
                    alert.Evidence.Add(string.Format("Average daily rate: ${0:F2} ({1:F1}x store average)", avgRate, rateRatio));
                    alert.Evidence.Add(string.Format("On-time return rate: {0:P0}", onTimeRate));
                    alert.RecommendedActions.Add("Consider premium rental deposit");
                    alert.RecommendedActions.Add("Review for copy/piracy risk");
                    alerts.Add(alert);
                }
            }

            return alerts;
        }

        /// <summary>
        /// Flags customers with incomplete identity profiles (no email and no phone).
        /// </summary>
        public List<AnomalyAlert> DetectIdentityRisks()
        {
            var alerts = new List<AnomalyAlert>();
            var now = _clock.Now;
            var customers = _customerRepo.GetAll();

            foreach (var customer in customers)
            {
                var missingEmail = string.IsNullOrWhiteSpace(customer.Email);
                var missingPhone = string.IsNullOrWhiteSpace(customer.Phone);

                if (missingEmail && missingPhone)
                {
                    var alert = new AnomalyAlert
                    {
                        Type = AnomalyType.IdentityRisk,
                        Severity = AnomalySeverity.Medium,
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        Title = string.Format("Incomplete identity: {0}", customer.Name),
                        Description = string.Format("{0} has no email and no phone on file — potential fake account.",
                            customer.Name),
                        Score = 40,
                        DetectedAt = now
                    };
                    alert.Evidence.Add("Missing: email");
                    alert.Evidence.Add("Missing: phone");
                    alert.RecommendedActions.Add("Request identity verification");
                    alert.RecommendedActions.Add("Limit account privileges until verified");
                    alerts.Add(alert);
                }
            }

            return alerts;
        }

        /// <summary>Returns the current watchlist, ordered by risk score descending.</summary>
        public List<WatchlistEntry> GetWatchlist()
        {
            return _watchlist.Values
                .Where(w => w.Active)
                .OrderByDescending(w => w.RiskScore)
                .ToList();
        }

        /// <summary>Acknowledges an alert by ID.</summary>
        public void AcknowledgeAlert(int alertId)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert != null)
                alert.Acknowledged = true;
        }

        /// <summary>
        /// Classifies the overall threat level based on alert severity distribution.
        /// </summary>
        public string ClassifyThreatLevel(List<AnomalyAlert> alerts)
        {
            if (alerts == null || alerts.Count == 0)
                return "Green";

            if (alerts.Any(a => a.Severity == AnomalySeverity.Critical))
                return "Red";
            if (alerts.Any(a => a.Severity == AnomalySeverity.High))
                return "Orange";
            if (alerts.Count(a => a.Severity == AnomalySeverity.Medium) >= 1)
                return "Yellow";

            return "Green";
        }

        /// <summary>Clears all stored alerts and watchlist entries (for testing).</summary>
        public static void Reset()
        {
            _alerts.Clear();
            _watchlist.Clear();
            _nextAlertId = 1;
        }

        #region Private helpers

        private void UpdateWatchlist(List<AnomalyAlert> alerts)
        {
            var customerAlerts = alerts
                .Where(a => a.CustomerId.HasValue)
                .GroupBy(a => a.CustomerId.Value);

            foreach (var group in customerAlerts)
            {
                var customerId = group.Key;
                var alertCount = group.Count();
                var totalScore = Math.Min(100, group.Sum(a => a.Score));
                var topReason = group.OrderByDescending(a => a.Score).First();

                if (_watchlist.ContainsKey(customerId))
                {
                    var entry = _watchlist[customerId];
                    entry.AlertCount += alertCount;
                    entry.RiskScore = Math.Min(100, entry.RiskScore + totalScore);
                    entry.Active = true;
                }
                else if (_watchlist.Count < _config.MaxWatchlistSize)
                {
                    _watchlist[customerId] = new WatchlistEntry
                    {
                        CustomerId = customerId,
                        CustomerName = topReason.CustomerName,
                        Reason = topReason.Title,
                        AddedAt = _clock.Now,
                        AlertCount = alertCount,
                        RiskScore = totalScore,
                        Active = true
                    };
                }
            }
        }

        private Dictionary<Genre, double> BuildGenreDistribution(
            List<Rental> rentals, Dictionary<int, Genre> movieGenres)
        {
            var counts = new Dictionary<Genre, int>();
            var total = 0;

            foreach (var rental in rentals)
            {
                Genre genre;
                if (movieGenres.TryGetValue(rental.MovieId, out genre))
                {
                    if (!counts.ContainsKey(genre))
                        counts[genre] = 0;
                    counts[genre]++;
                    total++;
                }
            }

            var dist = new Dictionary<Genre, double>();
            if (total == 0) return dist;

            foreach (var kv in counts)
                dist[kv.Key] = (double)kv.Value / total;

            return dist;
        }

        private double ComputeDistributionDistance(
            Dictionary<Genre, double> a, Dictionary<Genre, double> b)
        {
            var allGenres = new HashSet<Genre>(a.Keys);
            foreach (var k in b.Keys) allGenres.Add(k);

            double sum = 0;
            foreach (var genre in allGenres)
            {
                double va = 0, vb = 0;
                a.TryGetValue(genre, out va);
                b.TryGetValue(genre, out vb);
                sum += Math.Abs(va - vb);
            }

            return sum;
        }

        private string BuildSummary(AnomalyReport report)
        {
            if (report.TotalAlertsGenerated == 0)
                return "All clear — no anomalies detected.";

            var parts = new List<string>();
            parts.Add(string.Format("{0} anomalies detected", report.TotalAlertsGenerated));
            if (report.CriticalCount > 0) parts.Add(string.Format("{0} critical", report.CriticalCount));
            if (report.HighCount > 0) parts.Add(string.Format("{0} high", report.HighCount));
            if (report.MediumCount > 0) parts.Add(string.Format("{0} medium", report.MediumCount));
            if (report.LowCount > 0) parts.Add(string.Format("{0} low", report.LowCount));
            if (report.Watchlist.Count > 0)
                parts.Add(string.Format("{0} customers on watchlist", report.Watchlist.Count));

            return string.Join(". ", parts) + ".";
        }

        #endregion
    }
}
