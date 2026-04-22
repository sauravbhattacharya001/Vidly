using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous fraud detection engine that analyzes rental patterns to flag
    /// suspicious activity. Uses 7 detection rules: velocity, late patterns,
    /// new account bursts, high-value targeting, concurrent overload, damage
    /// patterns, and weekend surges. Produces composite risk scores with
    /// tiered classification and evidence-backed fraud signals.
    /// </summary>
    public class FraudDetectorService
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;

        public FraudDetectorService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo)
        {
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
        }

        // ── Individual Analysis ─────────────────────────────────────

        /// <summary>
        /// Analyze fraud risk for a single customer.
        /// </summary>
        public FraudProfile Analyze(int customerId, DateTime asOfDate)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var allRentals = _rentalRepo.GetByCustomer(customerId)
                .OrderBy(r => r.RentalDate)
                .ToList();
            var activeRentals = _rentalRepo.GetActiveByCustomer(customerId);

            var signals = new List<FraudSignal>();

            // Rule 1: Velocity Check
            CheckVelocity(allRentals, asOfDate, signals);

            // Rule 2: Late Return Pattern
            CheckLatePattern(allRentals, signals);

            // Rule 3: New Account Burst
            CheckNewAccountBurst(customer, allRentals, asOfDate, signals);

            // Rule 4: High-Value Targeting
            CheckHighValueTargeting(allRentals, signals);

            // Rule 5: Concurrent Overload
            CheckConcurrentOverload(customer, activeRentals, signals);

            // Rule 6: Damage Pattern
            CheckDamagePattern(allRentals, signals);

            // Rule 7: Weekend Surge
            CheckWeekendSurge(allRentals, signals);

            double riskScore = Math.Min(100.0,
                signals.Sum(s => (int)s.Severity * s.Confidence * 15.0));

            string riskTier = riskScore < 20 ? "Clean"
                : riskScore < 50 ? "Watch"
                : riskScore < 80 ? "Suspect"
                : "Blocked";

            return new FraudProfile
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                MembershipType = customer.MembershipType.ToString(),
                RiskScore = Math.Round(riskScore, 1),
                RiskTier = riskTier,
                Signals = signals,
                TotalRentals = allRentals.Count,
                ActiveRentals = activeRentals.Count,
                LastRentalDate = allRentals.LastOrDefault()?.RentalDate,
                AnalyzedAt = asOfDate
            };
        }

        // ── Summary ─────────────────────────────────────────────────

        /// <summary>
        /// Analyze all customers and produce a fraud summary.
        /// </summary>
        public FraudSummary GetSummary(DateTime asOfDate, int topN = 10)
        {
            var customers = _customerRepo.GetAll();
            var profiles = new List<FraudProfile>();

            foreach (var c in customers)
            {
                profiles.Add(Analyze(c.Id, asOfDate));
            }

            var flagged = profiles.Where(p => p.Signals.Count > 0).ToList();
            var signalDist = new Dictionary<string, int>();
            foreach (var p in flagged)
            {
                foreach (var s in p.Signals)
                {
                    if (!signalDist.ContainsKey(s.RuleId))
                        signalDist[s.RuleId] = 0;
                    signalDist[s.RuleId]++;
                }
            }

            return new FraudSummary
            {
                TotalCustomers = profiles.Count,
                FlaggedCustomers = flagged.Count,
                CriticalAlerts = flagged.Count(p => p.Signals.Any(s => s.Severity == FraudSeverity.Critical)),
                HighAlerts = flagged.Count(p => p.Signals.Any(s => s.Severity == FraudSeverity.High)),
                MediumAlerts = flagged.Count(p => p.Signals.Any(s => s.Severity == FraudSeverity.Medium)),
                SignalDistribution = signalDist,
                TopRisks = profiles.OrderByDescending(p => p.RiskScore).Take(topN).ToList(),
                AllProfiles = profiles.OrderByDescending(p => p.RiskScore).ToList(),
                GeneratedAt = asOfDate
            };
        }

        // ── Detection Rules ─────────────────────────────────────────

        private void CheckVelocity(List<Rental> rentals, DateTime asOfDate, List<FraudSignal> signals)
        {
            var last24h = rentals.Count(r => (asOfDate - r.RentalDate).TotalHours <= 24);
            var last7d = rentals.Count(r => (asOfDate - r.RentalDate).TotalDays <= 7);

            if (last24h > 5)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "VELOCITY",
                    RuleName = "Velocity Check",
                    Description = "Abnormally high rental frequency detected",
                    Severity = FraudSeverity.Critical,
                    Confidence = Math.Min(1.0, last24h / 10.0),
                    Evidence = $"{last24h} rentals in last 24h (threshold: 5)"
                });
            }
            else if (last7d > 15)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "VELOCITY",
                    RuleName = "Velocity Check",
                    Description = "High rental frequency over past week",
                    Severity = FraudSeverity.High,
                    Confidence = Math.Min(1.0, last7d / 20.0),
                    Evidence = $"{last7d} rentals in last 7d (threshold: 15)"
                });
            }
        }

        private void CheckLatePattern(List<Rental> rentals, List<FraudSignal> signals)
        {
            var returned = rentals.Where(r => r.ReturnDate.HasValue).ToList();
            if (returned.Count < 5) return;

            int lateCount = returned.Count(r => r.ReturnDate > r.DueDate);
            double lateRate = (double)lateCount / returned.Count;

            if (lateRate > 0.6)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "LATE_PATTERN",
                    RuleName = "Late Return Pattern",
                    Description = "Chronic late returns indicate disregard for rental terms",
                    Severity = lateRate > 0.8 ? FraudSeverity.High : FraudSeverity.Medium,
                    Confidence = lateRate,
                    Evidence = $"{lateCount}/{returned.Count} returns late ({lateRate:P0})"
                });
            }
        }

        private void CheckNewAccountBurst(Customer customer, List<Rental> rentals,
            DateTime asOfDate, List<FraudSignal> signals)
        {
            if (!customer.MemberSince.HasValue) return;
            var accountAge = (asOfDate - customer.MemberSince.Value).TotalDays;
            if (accountAge > 7) return;

            if (rentals.Count > 3)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "NEW_BURST",
                    RuleName = "New Account Burst",
                    Description = "New account with suspiciously high rental activity",
                    Severity = FraudSeverity.High,
                    Confidence = Math.Min(1.0, rentals.Count / 6.0),
                    Evidence = $"{rentals.Count} rentals within {accountAge:F0} days of account creation"
                });
            }
        }

        private void CheckHighValueTargeting(List<Rental> rentals, List<FraudSignal> signals)
        {
            if (rentals.Count < 3) return;

            int highValue = rentals.Count(r => r.DailyRate > 4.00m);
            double hvRate = (double)highValue / rentals.Count;

            if (hvRate > 0.7)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "HIGH_VALUE",
                    RuleName = "High-Value Targeting",
                    Description = "Disproportionate focus on premium/new-release titles",
                    Severity = FraudSeverity.High,
                    Confidence = hvRate,
                    Evidence = $"{highValue}/{rentals.Count} are high-value titles ({hvRate:P0})"
                });
            }
        }

        private void CheckConcurrentOverload(Customer customer,
            IReadOnlyList<Rental> active, List<FraudSignal> signals)
        {
            int limit;
            switch (customer.MembershipType)
            {
                case Models.MembershipType.Silver: limit = 5; break;
                case Models.MembershipType.Gold: limit = 8; break;
                case Models.MembershipType.Platinum: limit = 12; break;
                default: limit = 3; break;
            }

            if (active.Count > limit)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "CONCURRENT",
                    RuleName = "Concurrent Overload",
                    Description = "Active rentals exceed membership tier limits",
                    Severity = FraudSeverity.Critical,
                    Confidence = Math.Min(1.0, (double)active.Count / (limit * 2)),
                    Evidence = $"{active.Count} active rentals (limit: {limit} for {customer.MembershipType})"
                });
            }
        }

        private void CheckDamagePattern(List<Rental> rentals, List<FraudSignal> signals)
        {
            var returned = rentals.Where(r => r.ReturnDate.HasValue).ToList();
            if (returned.Count < 3) return;

            int damaged = returned.Count(r => r.DamageCharge > 0);
            double dmgRate = (double)damaged / returned.Count;

            if (dmgRate > 0.4)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "DAMAGE",
                    RuleName = "Damage Pattern",
                    Description = "Abnormally high rate of damaged returns",
                    Severity = dmgRate > 0.6 ? FraudSeverity.High : FraudSeverity.Medium,
                    Confidence = dmgRate,
                    Evidence = $"{damaged}/{returned.Count} returns damaged ({dmgRate:P0})"
                });
            }
        }

        private void CheckWeekendSurge(List<Rental> rentals, List<FraudSignal> signals)
        {
            if (rentals.Count < 5) return;

            int weekend = rentals.Count(r =>
                r.RentalDate.DayOfWeek == DayOfWeek.Saturday ||
                r.RentalDate.DayOfWeek == DayOfWeek.Sunday);
            double weekendRate = (double)weekend / rentals.Count;

            if (weekendRate > 0.8)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "WEEKEND_SURGE",
                    RuleName = "Weekend Surge",
                    Description = "Almost all rentals concentrated on weekends",
                    Severity = FraudSeverity.Medium,
                    Confidence = weekendRate,
                    Evidence = $"{weekend}/{rentals.Count} rentals on weekends ({weekendRate:P0})"
                });
            }
        }
    }

    // ── Models ──────────────────────────────────────────────────────

    public enum FraudSeverity
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public class FraudSignal
    {
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string Description { get; set; }
        public FraudSeverity Severity { get; set; }
        public double Confidence { get; set; }
        public string Evidence { get; set; }
    }

    public class FraudProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string MembershipType { get; set; }
        public double RiskScore { get; set; }
        public string RiskTier { get; set; }
        public List<FraudSignal> Signals { get; set; }
        public int TotalRentals { get; set; }
        public int ActiveRentals { get; set; }
        public DateTime? LastRentalDate { get; set; }
        public DateTime AnalyzedAt { get; set; }
    }

    public class FraudSummary
    {
        public int TotalCustomers { get; set; }
        public int FlaggedCustomers { get; set; }
        public int CriticalAlerts { get; set; }
        public int HighAlerts { get; set; }
        public int MediumAlerts { get; set; }
        public Dictionary<string, int> SignalDistribution { get; set; }
        public List<FraudProfile> TopRisks { get; set; }
        public List<FraudProfile> AllProfiles { get; set; }
        public DateTime GeneratedAt { get; set; }
    }
}
