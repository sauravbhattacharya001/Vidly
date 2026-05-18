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
    /// <summary>
    /// Tunable thresholds for <see cref="FraudDetectorService"/>.
    /// All knobs are intentionally explicit so the service stays testable
    /// and the historically-magic numbers in the detection rules become
    /// discoverable in one place.
    /// </summary>
    public class FraudDetectorConfig
    {
        // Velocity ---------------------------------------------------
        /// <summary>Rental count in last 24h above which Critical velocity fires.</summary>
        public int Velocity24hCritical { get; set; } = 5;
        /// <summary>Divisor used to scale 24h velocity into a confidence value (0..1).</summary>
        public double Velocity24hConfidenceDivisor { get; set; } = 10.0;
        /// <summary>Rental count in last 7d above which High velocity fires.</summary>
        public int Velocity7dHigh { get; set; } = 15;
        /// <summary>Divisor used to scale 7d velocity into a confidence value (0..1).</summary>
        public double Velocity7dConfidenceDivisor { get; set; } = 20.0;

        // Late pattern -----------------------------------------------
        /// <summary>Minimum returned rentals required to evaluate late pattern.</summary>
        public int LatePatternMinReturned { get; set; } = 5;
        /// <summary>Late-return rate above which Medium late pattern fires.</summary>
        public double LatePatternMediumRate { get; set; } = 0.6;
        /// <summary>Late-return rate above which the signal escalates to High.</summary>
        public double LatePatternHighRate { get; set; } = 0.8;

        // New account burst ------------------------------------------
        /// <summary>Account age (days) below which new-burst rule is in scope.</summary>
        public int NewBurstAccountAgeDays { get; set; } = 7;
        /// <summary>Rental count above which a new account is flagged.</summary>
        public int NewBurstRentalCount { get; set; } = 3;
        /// <summary>Divisor used to scale new-burst rental count into confidence.</summary>
        public double NewBurstConfidenceDivisor { get; set; } = 6.0;

        // High-value targeting ---------------------------------------
        /// <summary>Minimum rentals required to evaluate high-value targeting.</summary>
        public int HighValueMinRentals { get; set; } = 3;
        /// <summary>Daily rate above which a rental counts as "high-value".</summary>
        public decimal HighValueDailyRate { get; set; } = 4.00m;
        /// <summary>Share of high-value rentals above which the signal fires.</summary>
        public double HighValueRate { get; set; } = 0.7;

        // Concurrent overload — per-tier active rental limits --------
        public int ConcurrentLimitDefault { get; set; } = 3;
        public int ConcurrentLimitSilver { get; set; } = 5;
        public int ConcurrentLimitGold { get; set; } = 8;
        public int ConcurrentLimitPlatinum { get; set; } = 12;

        // Damage pattern ---------------------------------------------
        /// <summary>Minimum returned rentals required to evaluate damage pattern.</summary>
        public int DamagePatternMinReturned { get; set; } = 3;
        /// <summary>Damage rate above which Medium damage pattern fires.</summary>
        public double DamagePatternMediumRate { get; set; } = 0.4;
        /// <summary>Damage rate above which the signal escalates to High.</summary>
        public double DamagePatternHighRate { get; set; } = 0.6;

        // Weekend surge ----------------------------------------------
        /// <summary>Minimum rentals required to evaluate weekend surge.</summary>
        public int WeekendSurgeMinRentals { get; set; } = 5;
        /// <summary>Share of weekend rentals above which the signal fires.</summary>
        public double WeekendSurgeRate { get; set; } = 0.8;

        // Composite scoring ------------------------------------------
        /// <summary>Weight multiplier applied per signal when summing the risk score.</summary>
        public double SignalWeight { get; set; } = 15.0;
        /// <summary>Risk-tier thresholds (exclusive upper bounds for the lower tiers).</summary>
        public double WatchTierMin { get; set; } = 20.0;
        public double SuspectTierMin { get; set; } = 50.0;
        public double BlockedTierMin { get; set; } = 80.0;
    }

    public class FraudDetectorService
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly FraudDetectorConfig _config;

        public FraudDetectorService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo)
            : this(customerRepo, rentalRepo, movieRepo, null)
        {
        }

        public FraudDetectorService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            FraudDetectorConfig config)
        {
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
            _config = config ?? new FraudDetectorConfig();
        }

        /// <summary>Active configuration (exposed for diagnostics and tests).</summary>
        public FraudDetectorConfig Config { get { return _config; } }

        // ── Individual Analysis ─────────────────────────────────────

        /// <summary>
        /// Analyze fraud risk for a single customer.
        /// </summary>
        public FraudProfile Analyze(int customerId, DateTime asOfDate)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var allRentals = _rentalRepo.GetByCustomer(customerId).ToList();
            return AnalyzeInternal(customer, allRentals, asOfDate);
        }

        // ── Summary ─────────────────────────────────────────────────

        /// <summary>
        /// Analyze all customers and produce a fraud summary.
        /// Batch-fetches all rentals once and groups by customer to avoid
        /// N+1 repository queries (previously 2 queries per customer).
        /// </summary>
        public FraudSummary GetSummary(DateTime asOfDate, int topN = 10)
        {
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(allRentals);

            var profiles = new List<FraudProfile>();

            foreach (var c in customers)
            {
                List<Rental> customerRentals;
                rentalsByCustomer.TryGetValue(c.Id, out customerRentals);
                profiles.Add(AnalyzeInternal(c, customerRentals, asOfDate));
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

        /// <summary>
        /// Internal analysis that accepts pre-fetched customer and rental data,
        /// avoiding per-customer repository queries when called from batch methods.
        /// </summary>
        private FraudProfile AnalyzeInternal(Customer customer, List<Rental> customerRentals, DateTime asOfDate)
        {
            var allRentals = customerRentals != null
                ? customerRentals.OrderBy(r => r.RentalDate).ToList()
                : new List<Rental>();
            var activeRentals = allRentals.Where(r => r.Status != RentalStatus.Returned).ToList();

            var signals = new List<FraudSignal>();

            CheckVelocity(allRentals, asOfDate, signals);
            CheckLatePattern(allRentals, signals);
            CheckNewAccountBurst(customer, allRentals, asOfDate, signals);
            CheckHighValueTargeting(allRentals, signals);
            CheckConcurrentOverload(customer, activeRentals, signals);
            CheckDamagePattern(allRentals, signals);
            CheckWeekendSurge(allRentals, signals);

            double riskScore = Math.Min(100.0,
                signals.Sum(s => (int)s.Severity * s.Confidence * _config.SignalWeight));

            string riskTier = riskScore < _config.WatchTierMin ? "Clean"
                : riskScore < _config.SuspectTierMin ? "Watch"
                : riskScore < _config.BlockedTierMin ? "Suspect"
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

        // ── Detection Rules ─────────────────────────────────────────

        private void CheckVelocity(List<Rental> rentals, DateTime asOfDate, List<FraudSignal> signals)
        {
            var last24h = rentals.Count(r => (asOfDate - r.RentalDate).TotalHours <= 24);
            var last7d = rentals.Count(r => (asOfDate - r.RentalDate).TotalDays <= 7);

            if (last24h > _config.Velocity24hCritical)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "VELOCITY",
                    RuleName = "Velocity Check",
                    Description = "Abnormally high rental frequency detected",
                    Severity = FraudSeverity.Critical,
                    Confidence = Math.Min(1.0, last24h / _config.Velocity24hConfidenceDivisor),
                    Evidence = $"{last24h} rentals in last 24h (threshold: {_config.Velocity24hCritical})"
                });
            }
            else if (last7d > _config.Velocity7dHigh)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "VELOCITY",
                    RuleName = "Velocity Check",
                    Description = "High rental frequency over past week",
                    Severity = FraudSeverity.High,
                    Confidence = Math.Min(1.0, last7d / _config.Velocity7dConfidenceDivisor),
                    Evidence = $"{last7d} rentals in last 7d (threshold: {_config.Velocity7dHigh})"
                });
            }
        }

        private void CheckLatePattern(List<Rental> rentals, List<FraudSignal> signals)
        {
            var returned = rentals.Where(r => r.ReturnDate.HasValue).ToList();
            if (returned.Count < _config.LatePatternMinReturned) return;

            int lateCount = returned.Count(r => r.ReturnDate > r.DueDate);
            double lateRate = (double)lateCount / returned.Count;

            if (lateRate > _config.LatePatternMediumRate)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "LATE_PATTERN",
                    RuleName = "Late Return Pattern",
                    Description = "Chronic late returns indicate disregard for rental terms",
                    Severity = lateRate > _config.LatePatternHighRate ? FraudSeverity.High : FraudSeverity.Medium,
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
            if (accountAge > _config.NewBurstAccountAgeDays) return;

            if (rentals.Count > _config.NewBurstRentalCount)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "NEW_BURST",
                    RuleName = "New Account Burst",
                    Description = "New account with suspiciously high rental activity",
                    Severity = FraudSeverity.High,
                    Confidence = Math.Min(1.0, rentals.Count / _config.NewBurstConfidenceDivisor),
                    Evidence = $"{rentals.Count} rentals within {accountAge:F0} days of account creation"
                });
            }
        }

        private void CheckHighValueTargeting(List<Rental> rentals, List<FraudSignal> signals)
        {
            if (rentals.Count < _config.HighValueMinRentals) return;

            int highValue = rentals.Count(r => r.DailyRate > _config.HighValueDailyRate);
            double hvRate = (double)highValue / rentals.Count;

            if (hvRate > _config.HighValueRate)
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
                case Models.MembershipType.Silver: limit = _config.ConcurrentLimitSilver; break;
                case Models.MembershipType.Gold: limit = _config.ConcurrentLimitGold; break;
                case Models.MembershipType.Platinum: limit = _config.ConcurrentLimitPlatinum; break;
                default: limit = _config.ConcurrentLimitDefault; break;
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
            if (returned.Count < _config.DamagePatternMinReturned) return;

            int damaged = returned.Count(r => r.DamageCharge > 0);
            double dmgRate = (double)damaged / returned.Count;

            if (dmgRate > _config.DamagePatternMediumRate)
            {
                signals.Add(new FraudSignal
                {
                    RuleId = "DAMAGE",
                    RuleName = "Damage Pattern",
                    Description = "Abnormally high rate of damaged returns",
                    Severity = dmgRate > _config.DamagePatternHighRate ? FraudSeverity.High : FraudSeverity.Medium,
                    Confidence = dmgRate,
                    Evidence = $"{damaged}/{returned.Count} returns damaged ({dmgRate:P0})"
                });
            }
        }

        private void CheckWeekendSurge(List<Rental> rentals, List<FraudSignal> signals)
        {
            if (rentals.Count < _config.WeekendSurgeMinRentals) return;

            int weekend = rentals.Count(r =>
                r.RentalDate.DayOfWeek == DayOfWeek.Saturday ||
                r.RentalDate.DayOfWeek == DayOfWeek.Sunday);
            double weekendRate = (double)weekend / rentals.Count;

            if (weekendRate > _config.WeekendSurgeRate)
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
