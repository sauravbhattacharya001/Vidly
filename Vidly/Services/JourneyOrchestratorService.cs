using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ── Enums ────────────────────────────────────────────────────────

    /// <summary>
    /// Customer lifecycle stage in the journey orchestrator.
    /// </summary>
    public enum JourneyStage
    {
        Newcomer = 1,
        Exploring = 2,
        Active = 3,
        Loyal = 4,
        Champion = 5,
        Cooling = 6,
        AtRisk = 7,
        Lapsed = 8
    }

    /// <summary>
    /// Engagement trend direction.
    /// </summary>
    public enum EngagementTrend
    {
        Rising,
        Stable,
        Declining
    }

    /// <summary>
    /// Type of intervention recommended for a customer.
    /// </summary>
    public enum InterventionType
    {
        WelcomeSeries,
        GenreDiscovery,
        LoyaltyMilestone,
        VipPreview,
        AmbassadorProgram,
        ReEngagementNudge,
        RetentionOffer,
        WinBackCampaign
    }

    // ── Result Models ────────────────────────────────────────────────

    /// <summary>
    /// Complete lifecycle profile for a single customer.
    /// </summary>
    public class JourneyProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public JourneyStage CurrentStage { get; set; }
        public JourneyStage? PreviousStage { get; set; }
        public int DaysInCurrentStage { get; set; }
        public double StageConfidence { get; set; }
        public double RentalVelocity { get; set; }
        public double GenreExplorationBreadth { get; set; }
        public EngagementTrend Trend { get; set; }
        public int TotalRentals { get; set; }
        public DateTime? LastRentalDate { get; set; }
        public DateTime? MemberSince { get; set; }
    }

    /// <summary>
    /// A single stage transition event.
    /// </summary>
    public class StageTransition
    {
        public JourneyStage FromStage { get; set; }
        public JourneyStage ToStage { get; set; }
        public DateTime TransitionDate { get; set; }
        public string Trigger { get; set; }
    }

    /// <summary>
    /// Full journey history for a customer.
    /// </summary>
    public class JourneyMap
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public JourneyProfile CurrentProfile { get; set; }
        public List<StageTransition> Transitions { get; set; } = new List<StageTransition>();
    }

    /// <summary>
    /// A recommended intervention for a customer.
    /// </summary>
    public class Intervention
    {
        public InterventionType Type { get; set; }
        public int Priority { get; set; }
        public string Message { get; set; }
        public List<string> SuggestedMovies { get; set; } = new List<string>();
        public int ExpiresInDays { get; set; }
        public bool AutoExecutable { get; set; }
    }

    /// <summary>
    /// A proactive alert about a customer journey event.
    /// </summary>
    public class JourneyAlert
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string AlertType { get; set; }
        public string Message { get; set; }
        public JourneyStage? FromStage { get; set; }
        public JourneyStage? ToStage { get; set; }
        public int Severity { get; set; }
    }

    /// <summary>
    /// Fleet-wide journey dashboard.
    /// </summary>
    public class JourneyDashboard
    {
        public Dictionary<JourneyStage, int> StageDistribution { get; set; }
            = new Dictionary<JourneyStage, int>();
        public List<TransitionCount> TransitionMatrix { get; set; }
            = new List<TransitionCount>();
        public List<Intervention> InterventionQueue { get; set; }
            = new List<Intervention>();
        public double HealthPercentage { get; set; }
        public double AverageVelocity { get; set; }
        public int TotalCustomers { get; set; }
        public List<JourneyAlert> RecentAlerts { get; set; }
            = new List<JourneyAlert>();
    }

    /// <summary>
    /// Counts transitions from one stage to another.
    /// </summary>
    public class TransitionCount
    {
        public JourneyStage From { get; set; }
        public JourneyStage To { get; set; }
        public int Count { get; set; }
    }

    // ── Service ──────────────────────────────────────────────────────

    /// <summary>
    /// Customer Journey Orchestrator — autonomous lifecycle stage tracking
    /// with personalized intervention plans. Classifies customers through
    /// 8 lifecycle stages, detects transitions, generates prioritized
    /// interventions, and surfaces proactive alerts.
    /// </summary>
    public class JourneyOrchestratorService
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;

        // Stage thresholds
        private const int NewcomerMaxDays = 30;
        private const int NewcomerMaxRentals = 3;
        private const int ExploringMaxRentals = 10;
        private const int ActiveMaxRentals = 25;
        private const int LoyalMaxRentals = 50;
        private const int ChampionMinRentals = 50;
        private const int ChampionPlatinumMinRentals = 25;
        private const int AtRiskMinDays = 30;
        private const int LapsedMinDays = 60;
        private const double CoolingVelocityDropRatio = 0.5;

        public JourneyOrchestratorService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            IClock clock = null)
        {
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
            _clock = clock ?? new SystemClock();
        }

        // ── Individual Analysis ──────────────────────────────────────

        /// <summary>
        /// Classify a customer into their current lifecycle stage.
        /// </summary>
        public JourneyProfile ClassifyCustomer(int customerId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var allRentals = _rentalRepo.GetAll();
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(allRentals);
            var rentals = rentalsByCustomer.ContainsKey(customerId)
                ? rentalsByCustomer[customerId]
                : new List<Rental>();

            return BuildProfile(customer, rentals);
        }

        /// <summary>
        /// Get the full journey map with reconstructed stage history.
        /// </summary>
        public JourneyMap GetFullJourney(int customerId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var allRentals = _rentalRepo.GetAll();
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(allRentals);
            var rentals = rentalsByCustomer.ContainsKey(customerId)
                ? rentalsByCustomer[customerId]
                : new List<Rental>();

            var profile = BuildProfile(customer, rentals);
            var transitions = ReconstructTransitions(customer, rentals);

            return new JourneyMap
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                CurrentProfile = profile,
                Transitions = transitions
            };
        }

        /// <summary>
        /// Get prioritized interventions for a customer based on their stage.
        /// </summary>
        public List<Intervention> GetInterventions(int customerId)
        {
            var profile = ClassifyCustomer(customerId);
            var allMovies = _movieRepo.GetAll();

            var allRentals = _rentalRepo.GetAll();
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(allRentals);
            var rentals = rentalsByCustomer.ContainsKey(customerId)
                ? rentalsByCustomer[customerId]
                : new List<Rental>();

            return GenerateInterventions(profile, rentals, allMovies);
        }

        // ── Fleet-Wide Analysis ──────────────────────────────────────

        /// <summary>
        /// Build a fleet-wide journey dashboard.
        /// </summary>
        public JourneyDashboard GetDashboard()
        {
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var allMovies = _movieRepo.GetAll();
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(allRentals);

            var profiles = new List<JourneyProfile>();
            foreach (var c in customers)
            {
                var rentals = rentalsByCustomer.ContainsKey(c.Id)
                    ? rentalsByCustomer[c.Id]
                    : new List<Rental>();
                profiles.Add(BuildProfile(c, rentals));
            }

            // Stage distribution
            var distribution = new Dictionary<JourneyStage, int>();
            foreach (JourneyStage stage in Enum.GetValues(typeof(JourneyStage)))
                distribution[stage] = 0;
            foreach (var p in profiles)
                distribution[p.CurrentStage]++;

            // Transition matrix from reconstructed journeys
            var transitionCounts = new Dictionary<string, TransitionCount>();
            foreach (var c in customers)
            {
                var rentals = rentalsByCustomer.ContainsKey(c.Id)
                    ? rentalsByCustomer[c.Id]
                    : new List<Rental>();
                var transitions = ReconstructTransitions(c, rentals);
                var cutoff = _clock.Now.AddDays(-30);
                foreach (var t in transitions.Where(tr => tr.TransitionDate >= cutoff))
                {
                    var key = $"{t.FromStage}->{t.ToStage}";
                    if (!transitionCounts.ContainsKey(key))
                        transitionCounts[key] = new TransitionCount
                        {
                            From = t.FromStage,
                            To = t.ToStage,
                            Count = 0
                        };
                    transitionCounts[key].Count++;
                }
            }

            // Intervention queue (top priority across all customers)
            var interventionQueue = new List<Intervention>();
            foreach (var p in profiles.Where(
                pr => pr.CurrentStage == JourneyStage.Cooling
                    || pr.CurrentStage == JourneyStage.AtRisk
                    || pr.CurrentStage == JourneyStage.Lapsed
                    || pr.CurrentStage == JourneyStage.Newcomer))
            {
                var rentals = rentalsByCustomer.ContainsKey(p.CustomerId)
                    ? rentalsByCustomer[p.CustomerId]
                    : new List<Rental>();
                var interventions = GenerateInterventions(p, rentals, allMovies);
                interventionQueue.AddRange(interventions);
            }
            interventionQueue = interventionQueue
                .OrderBy(i => i.Priority)
                .Take(20)
                .ToList();

            // Health: % in positive stages
            var positiveStages = new HashSet<JourneyStage>
            {
                JourneyStage.Newcomer, JourneyStage.Exploring,
                JourneyStage.Active, JourneyStage.Loyal, JourneyStage.Champion
            };
            var positiveCount = profiles.Count(p => positiveStages.Contains(p.CurrentStage));
            var healthPct = profiles.Count > 0
                ? Math.Round(100.0 * positiveCount / profiles.Count, 1)
                : 100.0;

            // Average velocity
            var avgVelocity = profiles.Count > 0
                ? Math.Round(profiles.Average(p => p.RentalVelocity), 2)
                : 0.0;

            // Recent alerts
            var alerts = GetAlerts();

            return new JourneyDashboard
            {
                StageDistribution = distribution,
                TransitionMatrix = transitionCounts.Values.ToList(),
                InterventionQueue = interventionQueue,
                HealthPercentage = healthPct,
                AverageVelocity = avgVelocity,
                TotalCustomers = customers.Count,
                RecentAlerts = alerts
            };
        }

        /// <summary>
        /// Get proactive alerts for customers with notable journey events.
        /// </summary>
        public List<JourneyAlert> GetAlerts()
        {
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(allRentals);
            var alerts = new List<JourneyAlert>();

            foreach (var c in customers)
            {
                var rentals = rentalsByCustomer.ContainsKey(c.Id)
                    ? rentalsByCustomer[c.Id]
                    : new List<Rental>();
                var profile = BuildProfile(c, rentals);
                var transitions = ReconstructTransitions(c, rentals);

                // Alert: negative transitions
                var recentNegative = transitions.Where(t =>
                    t.TransitionDate >= _clock.Now.AddDays(-30)
                    && (t.ToStage == JourneyStage.Cooling
                        || t.ToStage == JourneyStage.AtRisk
                        || t.ToStage == JourneyStage.Lapsed)).ToList();

                foreach (var t in recentNegative)
                {
                    var severity = t.ToStage == JourneyStage.Lapsed ? 1
                        : t.ToStage == JourneyStage.AtRisk ? 2 : 3;

                    // Champion dropping is extra severe
                    if (t.FromStage == JourneyStage.Champion
                        || t.FromStage == JourneyStage.Loyal)
                        severity = Math.Max(1, severity - 1);

                    alerts.Add(new JourneyAlert
                    {
                        CustomerId = c.Id,
                        CustomerName = c.Name,
                        AlertType = "NegativeTransition",
                        Message = $"{c.Name} moved from {t.FromStage} to {t.ToStage}",
                        FromStage = t.FromStage,
                        ToStage = t.ToStage,
                        Severity = severity
                    });
                }

                // Alert: approaching milestone
                if (profile.CurrentStage == JourneyStage.Active && profile.TotalRentals >= 22)
                {
                    alerts.Add(new JourneyAlert
                    {
                        CustomerId = c.Id,
                        CustomerName = c.Name,
                        AlertType = "MilestoneApproaching",
                        Message = $"{c.Name} is approaching Loyal status ({profile.TotalRentals}/25 rentals)",
                        FromStage = JourneyStage.Active,
                        ToStage = JourneyStage.Loyal,
                        Severity = 4
                    });
                }

                if (profile.CurrentStage == JourneyStage.Loyal && profile.TotalRentals >= 45)
                {
                    alerts.Add(new JourneyAlert
                    {
                        CustomerId = c.Id,
                        CustomerName = c.Name,
                        AlertType = "MilestoneApproaching",
                        Message = $"{c.Name} is approaching Champion status ({profile.TotalRentals}/50 rentals)",
                        FromStage = JourneyStage.Loyal,
                        ToStage = JourneyStage.Champion,
                        Severity = 4
                    });
                }
            }

            return alerts.OrderBy(a => a.Severity).ToList();
        }

        // ── Private Helpers ──────────────────────────────────────────

        private JourneyProfile BuildProfile(Customer customer, List<Rental> rentals)
        {
            var now = _clock.Now;
            var totalRentals = rentals.Count;
            var memberDays = customer.MemberSince.HasValue
                ? (int)(now - customer.MemberSince.Value).TotalDays
                : 0;

            var lastRental = rentals
                .OrderByDescending(r => r.RentalDate)
                .FirstOrDefault();
            var lastRentalDate = lastRental?.RentalDate;
            var daysSinceLastRental = lastRentalDate.HasValue
                ? (int)(now - lastRentalDate.Value).TotalDays
                : int.MaxValue;

            // Velocity: rentals per week in last 30 days
            var recentCutoff = now.AddDays(-30);
            var recentRentals = rentals.Where(r => r.RentalDate >= recentCutoff).ToList();
            var recentVelocity = recentRentals.Count / 4.286; // 30 days ≈ 4.286 weeks

            // Historical velocity: rentals per week in the 30 days before that
            var historicalCutoff = now.AddDays(-60);
            var historicalRentals = rentals
                .Where(r => r.RentalDate >= historicalCutoff && r.RentalDate < recentCutoff)
                .ToList();
            var historicalVelocity = historicalRentals.Count / 4.286;

            // Engagement trend
            EngagementTrend trend;
            if (historicalVelocity < 0.01 && recentVelocity < 0.01)
                trend = EngagementTrend.Stable;
            else if (historicalVelocity < 0.01)
                trend = EngagementTrend.Rising;
            else
            {
                var velocityRatio = recentVelocity / historicalVelocity;
                trend = velocityRatio > 1.2 ? EngagementTrend.Rising
                    : velocityRatio < 0.8 ? EngagementTrend.Declining
                    : EngagementTrend.Stable;
            }

            // Genre exploration breadth
            var allMovies = _movieRepo.GetAll();
            var totalGenres = Enum.GetValues(typeof(Genre)).Length;
            var rentedGenres = rentals
                .Select(r => allMovies.FirstOrDefault(m => m.Id == r.MovieId))
                .Where(m => m?.Genre != null)
                .Select(m => m.Genre.Value)
                .Distinct()
                .Count();
            var genreBreadth = totalGenres > 0
                ? Math.Round((double)rentedGenres / totalGenres, 2)
                : 0.0;

            // Classify stage
            var stage = ClassifyStage(
                customer, totalRentals, memberDays, daysSinceLastRental,
                recentVelocity, historicalVelocity);

            // Confidence score
            var confidence = CalculateConfidence(
                stage, totalRentals, memberDays, daysSinceLastRental,
                recentVelocity, historicalVelocity);

            // Estimate days in current stage
            var daysInStage = EstimateDaysInStage(
                stage, customer, rentals, now);

            // Detect previous stage
            var transitions = ReconstructTransitions(customer, rentals);
            var prevStage = transitions.Count > 0
                ? (JourneyStage?)transitions.Last().FromStage
                : null;

            return new JourneyProfile
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                CurrentStage = stage,
                PreviousStage = prevStage,
                DaysInCurrentStage = daysInStage,
                StageConfidence = confidence,
                RentalVelocity = Math.Round(recentVelocity, 2),
                GenreExplorationBreadth = genreBreadth,
                Trend = trend,
                TotalRentals = totalRentals,
                LastRentalDate = lastRentalDate,
                MemberSince = customer.MemberSince
            };
        }

        private JourneyStage ClassifyStage(
            Customer customer, int totalRentals, int memberDays,
            int daysSinceLastRental, double recentVelocity, double historicalVelocity)
        {
            // Lapsed: no rental in 60+ days
            if (daysSinceLastRental >= LapsedMinDays && totalRentals > 0)
                return JourneyStage.Lapsed;

            // AtRisk: no rental in 30-60 days
            if (daysSinceLastRental >= AtRiskMinDays && totalRentals > 0)
                return JourneyStage.AtRisk;

            // Cooling: velocity dropped >50% from historical
            if (totalRentals >= ExploringMaxRentals
                && historicalVelocity > 0.2
                && recentVelocity < historicalVelocity * CoolingVelocityDropRatio)
                return JourneyStage.Cooling;

            // Champion: 50+ rentals OR Platinum + 25+ rentals
            if (totalRentals >= ChampionMinRentals)
                return JourneyStage.Champion;
            if (customer.MembershipType == MembershipType.Platinum
                && totalRentals >= ChampionPlatinumMinRentals)
                return JourneyStage.Champion;

            // Loyal: 25-50 rentals
            if (totalRentals >= ActiveMaxRentals)
                return JourneyStage.Loyal;

            // Active: 10-25 rentals
            if (totalRentals >= ExploringMaxRentals)
                return JourneyStage.Active;

            // Newcomer: joined < 30 days ago and < 3 rentals
            if (memberDays <= NewcomerMaxDays && totalRentals < NewcomerMaxRentals)
                return JourneyStage.Newcomer;

            // Exploring: 3-10 rentals
            if (totalRentals >= NewcomerMaxRentals)
                return JourneyStage.Exploring;

            // Default: newcomer
            return JourneyStage.Newcomer;
        }

        private double CalculateConfidence(
            JourneyStage stage, int totalRentals, int memberDays,
            int daysSinceLastRental, double recentVelocity, double historicalVelocity)
        {
            double confidence = 70.0; // base

            switch (stage)
            {
                case JourneyStage.Champion:
                    // Higher confidence with more rentals
                    confidence = Math.Min(100, 80 + (totalRentals - 50) * 0.5);
                    break;
                case JourneyStage.Loyal:
                    confidence = Math.Min(95, 75 + (totalRentals - 25) * 0.8);
                    break;
                case JourneyStage.Active:
                    confidence = Math.Min(90, 70 + (totalRentals - 10) * 1.0);
                    break;
                case JourneyStage.Lapsed:
                    confidence = Math.Min(100, 70 + (daysSinceLastRental - 60) * 0.5);
                    break;
                case JourneyStage.AtRisk:
                    confidence = Math.Min(90, 60 + (daysSinceLastRental - 30) * 1.0);
                    break;
                case JourneyStage.Cooling:
                    var dropRatio = historicalVelocity > 0
                        ? 1.0 - (recentVelocity / historicalVelocity)
                        : 0.5;
                    confidence = Math.Min(95, 60 + dropRatio * 40);
                    break;
                case JourneyStage.Newcomer:
                    confidence = memberDays <= 7 ? 95 : 80;
                    break;
                case JourneyStage.Exploring:
                    confidence = Math.Min(90, 70 + totalRentals * 2);
                    break;
            }

            return Math.Round(Math.Max(50, confidence), 0);
        }

        private int EstimateDaysInStage(
            JourneyStage stage, Customer customer, List<Rental> rentals, DateTime now)
        {
            switch (stage)
            {
                case JourneyStage.Newcomer:
                    return customer.MemberSince.HasValue
                        ? (int)(now - customer.MemberSince.Value).TotalDays
                        : 0;

                case JourneyStage.Lapsed:
                case JourneyStage.AtRisk:
                    var lastRental = rentals
                        .OrderByDescending(r => r.RentalDate)
                        .FirstOrDefault();
                    if (lastRental == null) return 0;
                    var daysSince = (int)(now - lastRental.RentalDate).TotalDays;
                    return stage == JourneyStage.Lapsed
                        ? daysSince - LapsedMinDays
                        : daysSince - AtRiskMinDays;

                default:
                    // Estimate based on when they hit the rental count threshold
                    var sorted = rentals.OrderBy(r => r.RentalDate).ToList();
                    int threshold = 0;
                    switch (stage)
                    {
                        case JourneyStage.Exploring: threshold = NewcomerMaxRentals; break;
                        case JourneyStage.Active: threshold = ExploringMaxRentals; break;
                        case JourneyStage.Loyal: threshold = ActiveMaxRentals; break;
                        case JourneyStage.Champion: threshold = ChampionMinRentals; break;
                        default: threshold = 0; break;
                    }
                    if (threshold > 0 && sorted.Count >= threshold)
                    {
                        var entryDate = sorted[threshold - 1].RentalDate;
                        return Math.Max(0, (int)(now - entryDate).TotalDays);
                    }
                    return 0;
            }
        }

        private List<StageTransition> ReconstructTransitions(
            Customer customer, List<Rental> rentals)
        {
            var transitions = new List<StageTransition>();
            var sorted = rentals.OrderBy(r => r.RentalDate).ToList();
            if (sorted.Count == 0) return transitions;

            // Walk through rental history and detect stage changes
            var prevStage = JourneyStage.Newcomer;
            var checkpoints = new List<int> { NewcomerMaxRentals, ExploringMaxRentals,
                ActiveMaxRentals, ChampionMinRentals };

            foreach (var threshold in checkpoints)
            {
                if (sorted.Count >= threshold)
                {
                    JourneyStage newStage;
                    string trigger;
                    switch (threshold)
                    {
                        case 3:
                            newStage = JourneyStage.Exploring;
                            trigger = "Reached 3 rentals";
                            break;
                        case 10:
                            newStage = JourneyStage.Active;
                            trigger = "Reached 10 rentals";
                            break;
                        case 25:
                            newStage = JourneyStage.Loyal;
                            trigger = "Reached 25 rentals";
                            break;
                        default:
                            newStage = JourneyStage.Champion;
                            trigger = "Reached 50 rentals";
                            break;
                    }

                    if (newStage != prevStage)
                    {
                        transitions.Add(new StageTransition
                        {
                            FromStage = prevStage,
                            ToStage = newStage,
                            TransitionDate = sorted[threshold - 1].RentalDate,
                            Trigger = trigger
                        });
                        prevStage = newStage;
                    }
                }
            }

            // Check for Platinum Champion shortcut
            if (customer.MembershipType == MembershipType.Platinum
                && sorted.Count >= ChampionPlatinumMinRentals
                && sorted.Count < ChampionMinRentals
                && prevStage != JourneyStage.Champion)
            {
                transitions.Add(new StageTransition
                {
                    FromStage = prevStage,
                    ToStage = JourneyStage.Champion,
                    TransitionDate = sorted[ChampionPlatinumMinRentals - 1].RentalDate,
                    Trigger = "Platinum member with 25+ rentals"
                });
                prevStage = JourneyStage.Champion;
            }

            // Check for negative transitions based on current state
            var now = _clock.Now;
            var lastRentalDate = sorted.Last().RentalDate;
            var daysSinceLast = (int)(now - lastRentalDate).TotalDays;

            if (daysSinceLast >= LapsedMinDays && prevStage != JourneyStage.Lapsed)
            {
                // Went through AtRisk first
                transitions.Add(new StageTransition
                {
                    FromStage = prevStage,
                    ToStage = JourneyStage.AtRisk,
                    TransitionDate = lastRentalDate.AddDays(AtRiskMinDays),
                    Trigger = "No rental for 30 days"
                });
                transitions.Add(new StageTransition
                {
                    FromStage = JourneyStage.AtRisk,
                    ToStage = JourneyStage.Lapsed,
                    TransitionDate = lastRentalDate.AddDays(LapsedMinDays),
                    Trigger = "No rental for 60 days"
                });
            }
            else if (daysSinceLast >= AtRiskMinDays && prevStage != JourneyStage.AtRisk)
            {
                transitions.Add(new StageTransition
                {
                    FromStage = prevStage,
                    ToStage = JourneyStage.AtRisk,
                    TransitionDate = lastRentalDate.AddDays(AtRiskMinDays),
                    Trigger = "No rental for 30 days"
                });
            }

            return transitions;
        }

        private List<Intervention> GenerateInterventions(
            JourneyProfile profile, List<Rental> rentals, IReadOnlyList<Movie> allMovies)
        {
            var interventions = new List<Intervention>();
            var rentedMovieIds = new HashSet<int>(rentals.Select(r => r.MovieId));
            var unrentedMovies = allMovies.Where(m => !rentedMovieIds.Contains(m.Id)).ToList();

            // Get favorite genres
            var genreCounts = rentals
                .Select(r => allMovies.FirstOrDefault(m => m.Id == r.MovieId))
                .Where(m => m?.Genre != null)
                .GroupBy(m => m.Genre.Value)
                .OrderByDescending(g => g.Count())
                .ToList();
            var topGenre = genreCounts.FirstOrDefault()?.Key;
            var triedGenres = new HashSet<Genre>(genreCounts.Select(g => g.Key));
            var untriedGenres = Enum.GetValues(typeof(Genre)).Cast<Genre>()
                .Where(g => !triedGenres.Contains(g))
                .ToList();

            switch (profile.CurrentStage)
            {
                case JourneyStage.Newcomer:
                    var welcomeMovies = topGenre.HasValue
                        ? unrentedMovies
                            .Where(m => m.Genre == topGenre.Value && m.Rating >= 4)
                            .Take(3)
                            .Select(m => m.Name).ToList()
                        : unrentedMovies
                            .Where(m => m.Rating >= 4)
                            .Take(3)
                            .Select(m => m.Name).ToList();
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.WelcomeSeries,
                        Priority = 2,
                        Message = "Welcome to Vidly! Here are some top-rated picks to get you started.",
                        SuggestedMovies = welcomeMovies,
                        ExpiresInDays = 14,
                        AutoExecutable = true
                    });
                    break;

                case JourneyStage.Exploring:
                    var discoveryMovies = untriedGenres.Any()
                        ? unrentedMovies
                            .Where(m => m.Genre.HasValue && untriedGenres.Contains(m.Genre.Value)
                                && m.Rating >= 3)
                            .Take(3)
                            .Select(m => m.Name).ToList()
                        : new List<string>();
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.GenreDiscovery,
                        Priority = 3,
                        Message = $"You've explored {triedGenres.Count} genres so far — try something new!",
                        SuggestedMovies = discoveryMovies,
                        ExpiresInDays = 21,
                        AutoExecutable = true
                    });
                    break;

                case JourneyStage.Active:
                    var milestone = profile.TotalRentals >= 20 ? 25 : 15;
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.LoyaltyMilestone,
                        Priority = 4,
                        Message = $"You're at {profile.TotalRentals} rentals — just {milestone - profile.TotalRentals} more to hit {milestone}!",
                        SuggestedMovies = new List<string>(),
                        ExpiresInDays = 30,
                        AutoExecutable = false
                    });
                    break;

                case JourneyStage.Loyal:
                    var vipMovies = unrentedMovies
                        .Where(m => m.IsNewRelease)
                        .Take(3)
                        .Select(m => m.Name).ToList();
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.VipPreview,
                        Priority = 3,
                        Message = "As a loyal member, get first access to these new releases!",
                        SuggestedMovies = vipMovies,
                        ExpiresInDays = 7,
                        AutoExecutable = true
                    });
                    break;

                case JourneyStage.Champion:
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.AmbassadorProgram,
                        Priority = 4,
                        Message = "You're a Vidly Champion! Refer a friend and both get a free rental.",
                        SuggestedMovies = new List<string>(),
                        ExpiresInDays = 60,
                        AutoExecutable = false
                    });
                    break;

                case JourneyStage.Cooling:
                    var reEngageMovies = topGenre.HasValue
                        ? unrentedMovies
                            .Where(m => m.Genre == topGenre.Value)
                            .OrderByDescending(m => m.Rating ?? 0)
                            .Take(3)
                            .Select(m => m.Name).ToList()
                        : new List<string>();
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.ReEngagementNudge,
                        Priority = 2,
                        Message = "We've noticed you've been renting less — check out these picks we saved for you!",
                        SuggestedMovies = reEngageMovies,
                        ExpiresInDays = 14,
                        AutoExecutable = true
                    });
                    break;

                case JourneyStage.AtRisk:
                    var retentionMovies = topGenre.HasValue
                        ? unrentedMovies
                            .Where(m => m.Genre == topGenre.Value)
                            .OrderByDescending(m => m.Rating ?? 0)
                            .Take(3)
                            .Select(m => m.Name).ToList()
                        : unrentedMovies
                            .OrderByDescending(m => m.Rating ?? 0)
                            .Take(3)
                            .Select(m => m.Name).ToList();
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.RetentionOffer,
                        Priority = 1,
                        Message = "We miss you! Here's 20% off your next rental.",
                        SuggestedMovies = retentionMovies,
                        ExpiresInDays = 7,
                        AutoExecutable = true
                    });
                    break;

                case JourneyStage.Lapsed:
                    var winBackMovies = unrentedMovies
                        .OrderByDescending(m => m.Rating ?? 0)
                        .Take(5)
                        .Select(m => m.Name).ToList();
                    interventions.Add(new Intervention
                    {
                        Type = InterventionType.WinBackCampaign,
                        Priority = 1,
                        Message = $"It's been a while! Here's a free rental to welcome you back — plus {winBackMovies.Count} movies you missed.",
                        SuggestedMovies = winBackMovies,
                        ExpiresInDays = 14,
                        AutoExecutable = true
                    });
                    break;
            }

            return interventions;
        }
    }
}
