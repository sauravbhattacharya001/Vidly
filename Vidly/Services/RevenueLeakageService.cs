using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Revenue Leakage Detector — scans 8 revenue streams to identify
    /// money the store is losing or leaving on the table. Produces a scored report
    /// with severity-classified leaks, dollar-impact estimates, and a prioritized
    /// remediation playbook.
    ///
    /// Detectors:
    ///   1. Uncollected Late Fees — overdue rentals accumulating unpaid fees
    ///   2. Expired Gift Cards — cards that expired with remaining balances
    ///   3. Underpriced Titles — high-demand movies priced below optimal rates
    ///   4. Idle Inventory — movies with zero rentals in a configurable window
    ///   5. Lapsed Subscribers — cancelled/expired subscriptions with recovery potential
    ///   6. Underutilized Subscriptions — paying subscribers using far below their allowance
    ///   7. Overdue Unreturned — long-overdue rentals likely to become write-offs
    ///   8. Dormant Customers — once-active customers who stopped renting
    /// </summary>
    public class RevenueLeakageService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IGiftCardRepository _giftCardRepository;
        private readonly ISubscriptionRepository _subscriptionRepository;
        private readonly IClock _clock;

        /// <summary>Days without a rental before a customer is considered dormant.</summary>
        public int DormantThresholdDays { get; set; }

        /// <summary>Days without any rental before inventory is considered idle.</summary>
        public int IdleInventoryDays { get; set; }

        /// <summary>Subscription utilization below this % triggers underutilization leak.</summary>
        public double UtilizationThreshold { get; set; }

        /// <summary>Days overdue before a rental is flagged as likely write-off.</summary>
        public int WriteOffThresholdDays { get; set; }

        /// <summary>Minimum rental count to qualify a movie for underpricing analysis.</summary>
        public int MinRentalsForPricingAnalysis { get; set; }

        public RevenueLeakageService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IGiftCardRepository giftCardRepository,
            ISubscriptionRepository subscriptionRepository,
            IClock clock)
        {
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (giftCardRepository == null) throw new ArgumentNullException("giftCardRepository");
            if (subscriptionRepository == null) throw new ArgumentNullException("subscriptionRepository");
            if (clock == null) throw new ArgumentNullException("clock");

            _rentalRepository = rentalRepository;
            _movieRepository = movieRepository;
            _customerRepository = customerRepository;
            _giftCardRepository = giftCardRepository;
            _subscriptionRepository = subscriptionRepository;
            _clock = clock;

            DormantThresholdDays = 90;
            IdleInventoryDays = 60;
            UtilizationThreshold = 0.25;
            WriteOffThresholdDays = 30;
            MinRentalsForPricingAnalysis = 3;
        }

        /// <summary>
        /// Run all 8 leak detectors and produce a full leakage report.
        /// </summary>
        public RevenueLeakageReport Analyze()
        {
            var now = _clock.Now;
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var allCustomers = _customerRepository.GetAll();
            var allGiftCards = _giftCardRepository.GetAll();
            var allSubscriptions = _subscriptionRepository.GetAll();

            var leaks = new List<RevenueLeak>();

            leaks.AddRange(DetectUncollectedLateFees(allRentals, now));
            leaks.AddRange(DetectExpiredGiftCards(allGiftCards, now));
            leaks.AddRange(DetectUnderpricedTitles(allRentals, allMovies, now));
            leaks.AddRange(DetectIdleInventory(allRentals, allMovies, now));
            leaks.AddRange(DetectLapsedSubscribers(allSubscriptions, allRentals, now));
            leaks.AddRange(DetectUnderutilizedSubscriptions(allSubscriptions, now));
            leaks.AddRange(DetectOverdueUnreturned(allRentals, now));
            leaks.AddRange(DetectDormantCustomers(allRentals, allCustomers, now));

            leaks.Sort((a, b) => b.EstimatedImpact.CompareTo(a.EstimatedImpact));

            var categoryBreakdown = new Dictionary<LeakCategory, decimal>();
            foreach (var leak in leaks)
            {
                if (categoryBreakdown.ContainsKey(leak.Category))
                    categoryBreakdown[leak.Category] += leak.EstimatedImpact;
                else
                    categoryBreakdown[leak.Category] = leak.EstimatedImpact;
            }

            decimal totalLeakage = leaks.Sum(l => l.EstimatedImpact);
            decimal recoverable = leaks
                .Where(l => l.Severity != LeakSeverity.Low)
                .Sum(l => l.EstimatedImpact * (decimal)l.Confidence);

            var playbook = BuildPlaybook(leaks, categoryBreakdown);

            int healthScore = CalculateHealthScore(totalLeakage, allRentals, now);

            string trend = DetectTrend(allRentals, allMovies, now);

            var report = new RevenueLeakageReport();
            report.GeneratedAt = now;
            report.HealthScore = healthScore;
            report.TotalLeakage = Math.Round(totalLeakage, 2);
            report.RecoverableRevenue = Math.Round(recoverable, 2);
            report.Leaks = leaks;
            report.Playbook = playbook;
            report.CategoryBreakdown = categoryBreakdown;
            report.Trend = trend;
            report.DetectorsRun = 8;

            return report;
        }

        /// <summary>
        /// Run analysis for a single leak category only.
        /// </summary>
        public List<RevenueLeak> AnalyzeCategory(LeakCategory category)
        {
            var now = _clock.Now;
            switch (category)
            {
                case LeakCategory.UncollectedLateFees:
                    return DetectUncollectedLateFees(_rentalRepository.GetAll(), now);
                case LeakCategory.ExpiredGiftCards:
                    return DetectExpiredGiftCards(_giftCardRepository.GetAll(), now);
                case LeakCategory.UnderpricedTitles:
                    return DetectUnderpricedTitles(_rentalRepository.GetAll(), _movieRepository.GetAll(), now);
                case LeakCategory.IdleInventory:
                    return DetectIdleInventory(_rentalRepository.GetAll(), _movieRepository.GetAll(), now);
                case LeakCategory.LapsedSubscribers:
                    return DetectLapsedSubscribers(_subscriptionRepository.GetAll(), _rentalRepository.GetAll(), now);
                case LeakCategory.UnderutilizedSubscriptions:
                    return DetectUnderutilizedSubscriptions(_subscriptionRepository.GetAll(), now);
                case LeakCategory.OverdueUnreturned:
                    return DetectOverdueUnreturned(_rentalRepository.GetAll(), now);
                case LeakCategory.DormantCustomers:
                    return DetectDormantCustomers(_rentalRepository.GetAll(), _customerRepository.GetAll(), now);
                default:
                    return new List<RevenueLeak>();
            }
        }

        // ── Detector 1: Uncollected Late Fees ──────────────────────────────

        private List<RevenueLeak> DetectUncollectedLateFees(IReadOnlyList<Rental> rentals, DateTime now)
        {
            var result = new List<RevenueLeak>();

            var overdue = rentals.Where(r => r.Status != RentalStatus.Returned && r.DueDate < now).ToList();
            if (overdue.Count == 0) return result;

            decimal totalUncollected = 0;
            var ids = new List<int>();
            foreach (var r in overdue)
            {
                int daysOver = (int)Math.Ceiling((now - r.DueDate).TotalDays);
                decimal fee = daysOver * r.DailyRate * 0.5m; // standard late fee formula
                totalUncollected += fee;
                ids.Add(r.Id);
            }

            var leak = new RevenueLeak();
            leak.Category = LeakCategory.UncollectedLateFees;
            leak.Severity = ClassifySeverity(totalUncollected);
            leak.Description = string.Format("{0} overdue rentals accumulating ${1:F2} in uncollected late fees", overdue.Count, totalUncollected);
            leak.EstimatedImpact = totalUncollected;
            leak.AffectedCount = overdue.Count;
            leak.Remediation = "Send overdue reminders and escalate rentals over 14 days to collections workflow";
            leak.Confidence = 0.9;
            leak.AffectedEntityIds = ids;
            result.Add(leak);

            return result;
        }

        // ── Detector 2: Expired Gift Cards ─────────────────────────────────

        private List<RevenueLeak> DetectExpiredGiftCards(IReadOnlyList<GiftCard> cards, DateTime now)
        {
            var result = new List<RevenueLeak>();

            var expired = cards.Where(c =>
                c.ExpirationDate.HasValue &&
                c.ExpirationDate.Value < now &&
                c.Balance > 0).ToList();

            if (expired.Count == 0) return result;

            decimal totalStranded = expired.Sum(c => c.Balance);
            var ids = expired.Select(c => c.Id).ToList();

            // Expired gift card balance is actually *recognized* revenue (breakage),
            // but represents lost customer goodwill and repeat business opportunity
            decimal lostOpportunity = totalStranded * 1.5m; // multiplier for lost future rentals

            var leak = new RevenueLeak();
            leak.Category = LeakCategory.ExpiredGiftCards;
            leak.Severity = ClassifySeverity(lostOpportunity);
            leak.Description = string.Format("{0} expired gift cards with ${1:F2} unused balance — estimated ${2:F2} in lost future business",
                expired.Count, totalStranded, lostOpportunity);
            leak.EstimatedImpact = lostOpportunity;
            leak.AffectedCount = expired.Count;
            leak.Remediation = "Offer grace-period reactivation to cardholders; convert to store credit to retain customers";
            leak.Confidence = 0.6;
            leak.AffectedEntityIds = ids;
            result.Add(leak);

            return result;
        }

        // ── Detector 3: Underpriced Titles ─────────────────────────────────

        private List<RevenueLeak> DetectUnderpricedTitles(IReadOnlyList<Rental> rentals, IReadOnlyList<Movie> movies, DateTime now)
        {
            var result = new List<RevenueLeak>();

            // Find movies with high demand but below-average daily rates
            var recentWindow = now.AddDays(-90);
            var recentRentals = rentals.Where(r => r.RentalDate >= recentWindow).ToList();
            if (recentRentals.Count == 0) return result;

            var rentalsByMovie = recentRentals.GroupBy(r => r.MovieId)
                .Where(g => g.Count() >= MinRentalsForPricingAnalysis)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (rentalsByMovie.Count == 0) return result;

            // Calculate average rate across all recent rentals
            decimal avgRate = recentRentals.Average(r => r.DailyRate);

            decimal totalUnderpriced = 0;
            var underpricedIds = new List<int>();

            foreach (var kvp in rentalsByMovie)
            {
                int movieId = kvp.Key;
                var movieRentals = kvp.Value;
                decimal movieAvgRate = movieRentals.Average(r => r.DailyRate);

                // High-demand movie priced 20%+ below average
                if (movieRentals.Count >= MinRentalsForPricingAnalysis * 2 && movieAvgRate < avgRate * 0.8m)
                {
                    decimal perRentalGap = avgRate - movieAvgRate;
                    decimal totalGap = perRentalGap * movieRentals.Count;
                    totalUnderpriced += totalGap;
                    underpricedIds.Add(movieId);
                }
            }

            if (totalUnderpriced > 0)
            {
                var leak = new RevenueLeak();
                leak.Category = LeakCategory.UnderpricedTitles;
                leak.Severity = ClassifySeverity(totalUnderpriced);
                leak.Description = string.Format("{0} high-demand titles priced below market — ${1:F2} potential uplift over 90 days",
                    underpricedIds.Count, totalUnderpriced);
                leak.EstimatedImpact = totalUnderpriced;
                leak.AffectedCount = underpricedIds.Count;
                leak.Remediation = "Review pricing for flagged titles; consider dynamic pricing or new-release premium tiers";
                leak.Confidence = 0.5;
                leak.AffectedEntityIds = underpricedIds;
                result.Add(leak);
            }

            return result;
        }

        // ── Detector 4: Idle Inventory ─────────────────────────────────────

        private List<RevenueLeak> DetectIdleInventory(IReadOnlyList<Rental> rentals, IReadOnlyList<Movie> movies, DateTime now)
        {
            var result = new List<RevenueLeak>();

            var cutoff = now.AddDays(-IdleInventoryDays);
            var recentlyRentedMovieIds = new HashSet<int>(
                rentals.Where(r => r.RentalDate >= cutoff).Select(r => r.MovieId));

            var idleMovies = movies.Where(m => !recentlyRentedMovieIds.Contains(m.Id)).ToList();
            if (idleMovies.Count == 0) return result;

            // Estimate opportunity cost: if each idle movie earned even 1 rental/month at avg rate
            decimal avgRate = rentals.Count > 0 ? rentals.Average(r => r.DailyRate) : 2.99m;
            decimal avgDays = 3; // average rental duration
            decimal perMovieOpportunity = avgRate * avgDays * ((decimal)IdleInventoryDays / 30m);
            decimal totalOpportunity = perMovieOpportunity * idleMovies.Count;

            var leak = new RevenueLeak();
            leak.Category = LeakCategory.IdleInventory;
            leak.Severity = ClassifySeverity(totalOpportunity);
            leak.Description = string.Format("{0} movies with zero rentals in {1} days — ${2:F2} estimated opportunity cost",
                idleMovies.Count, IdleInventoryDays, totalOpportunity);
            leak.EstimatedImpact = totalOpportunity;
            leak.AffectedCount = idleMovies.Count;
            leak.Remediation = "Promote idle titles with discounts or bundles; consider retirement for persistently idle inventory";
            leak.Confidence = 0.4;
            leak.AffectedEntityIds = idleMovies.Select(m => m.Id).ToList();
            result.Add(leak);

            return result;
        }

        // ── Detector 5: Lapsed Subscribers ─────────────────────────────────

        private List<RevenueLeak> DetectLapsedSubscribers(
            IReadOnlyList<CustomerSubscription> subscriptions,
            IReadOnlyList<Rental> rentals,
            DateTime now)
        {
            var result = new List<RevenueLeak>();

            var lapsed = subscriptions.Where(s =>
                s.Status == SubscriptionStatus.Cancelled || s.Status == SubscriptionStatus.Expired).ToList();

            if (lapsed.Count == 0) return result;

            // Calculate average monthly subscription revenue
            var activeSubs = subscriptions.Where(s => s.Status == SubscriptionStatus.Active).ToList();
            decimal avgMonthly = activeSubs.Count > 0
                ? activeSubs.Average(s => s.TotalBilled / Math.Max(1, (decimal)(now - s.StartDate).TotalDays / 30))
                : 9.99m;

            // Recoverable: lapsed within 90 days with rental history
            var recentlyLapsed = lapsed.Where(s =>
            {
                var endDate = s.CancelledDate ?? s.CurrentPeriodEnd;
                return (now - endDate).TotalDays <= 90;
            }).ToList();

            if (recentlyLapsed.Count == 0) return result;

            // Estimate 3 months of recovered revenue per win-back at 30% conversion
            decimal perSubRecovery = avgMonthly * 3;
            decimal totalRecovery = perSubRecovery * recentlyLapsed.Count * 0.3m;

            var leak = new RevenueLeak();
            leak.Category = LeakCategory.LapsedSubscribers;
            leak.Severity = ClassifySeverity(totalRecovery);
            leak.Description = string.Format("{0} recently lapsed subscribers — ${1:F2} recoverable at 30% win-back rate",
                recentlyLapsed.Count, totalRecovery);
            leak.EstimatedImpact = totalRecovery;
            leak.AffectedCount = recentlyLapsed.Count;
            leak.Remediation = "Launch win-back campaign: offer first month free or discounted plan upgrade";
            leak.Confidence = 0.35;
            leak.AffectedEntityIds = recentlyLapsed.Select(s => s.Id).ToList();
            result.Add(leak);

            return result;
        }

        // ── Detector 6: Underutilized Subscriptions ────────────────────────

        private List<RevenueLeak> DetectUnderutilizedSubscriptions(
            IReadOnlyList<CustomerSubscription> subscriptions, DateTime now)
        {
            var result = new List<RevenueLeak>();

            var planLimits = new Dictionary<SubscriptionPlanType, int>
            {
                { SubscriptionPlanType.Basic, 2 },
                { SubscriptionPlanType.Standard, 5 },
                { SubscriptionPlanType.Premium, 15 } // treat unlimited as 15 for utilization calc
            };

            var active = subscriptions.Where(s => s.Status == SubscriptionStatus.Active).ToList();
            var underutilized = new List<CustomerSubscription>();

            foreach (var sub in active)
            {
                int limit;
                if (!planLimits.TryGetValue(sub.PlanType, out limit)) continue;
                double utilization = (double)sub.RentalsUsedThisPeriod / limit;
                if (utilization < UtilizationThreshold)
                    underutilized.Add(sub);
            }

            if (underutilized.Count == 0) return result;

            // These customers are at churn risk — they're paying but not getting value
            // Impact = likely cancellation revenue loss
            var planPrices = new Dictionary<SubscriptionPlanType, decimal>
            {
                { SubscriptionPlanType.Basic, 4.99m },
                { SubscriptionPlanType.Standard, 9.99m },
                { SubscriptionPlanType.Premium, 14.99m }
            };

            decimal atRiskRevenue = 0;
            foreach (var sub in underutilized)
            {
                decimal price;
                if (planPrices.TryGetValue(sub.PlanType, out price))
                    atRiskRevenue += price * 6; // 6 months of potential churn
            }
            atRiskRevenue *= 0.5m; // 50% probability of churn

            var leak = new RevenueLeak();
            leak.Category = LeakCategory.UnderutilizedSubscriptions;
            leak.Severity = ClassifySeverity(atRiskRevenue);
            leak.Description = string.Format("{0} subscribers using less than {1}% of their plan — ${2:F2} at-risk revenue",
                underutilized.Count, (int)(UtilizationThreshold * 100), atRiskRevenue);
            leak.EstimatedImpact = atRiskRevenue;
            leak.AffectedCount = underutilized.Count;
            leak.Remediation = "Send personalized recommendations to boost engagement; offer plan downgrade to prevent full cancellation";
            leak.Confidence = 0.45;
            leak.AffectedEntityIds = underutilized.Select(s => s.Id).ToList();
            result.Add(leak);

            return result;
        }

        // ── Detector 7: Overdue Unreturned (Write-Off Risk) ────────────────

        private List<RevenueLeak> DetectOverdueUnreturned(IReadOnlyList<Rental> rentals, DateTime now)
        {
            var result = new List<RevenueLeak>();

            var longOverdue = rentals.Where(r =>
                r.Status != RentalStatus.Returned &&
                r.DueDate < now &&
                (now - r.DueDate).TotalDays >= WriteOffThresholdDays).ToList();

            if (longOverdue.Count == 0) return result;

            // These will likely never be returned — write-off the replacement cost
            decimal replacementCost = 0;
            foreach (var r in longOverdue)
            {
                // Estimate replacement cost at 20x daily rate (approximate retail price)
                replacementCost += r.DailyRate * 20;
            }

            var leak = new RevenueLeak();
            leak.Category = LeakCategory.OverdueUnreturned;
            leak.Severity = LeakSeverity.Critical;
            leak.Description = string.Format("{0} rentals overdue by {1}+ days — ${2:F2} in likely inventory write-offs",
                longOverdue.Count, WriteOffThresholdDays, replacementCost);
            leak.EstimatedImpact = replacementCost;
            leak.AffectedCount = longOverdue.Count;
            leak.Remediation = "Escalate to final notice; charge replacement fee to customer account; flag for collections";
            leak.Confidence = 0.8;
            leak.AffectedEntityIds = longOverdue.Select(r => r.Id).ToList();
            result.Add(leak);

            return result;
        }

        // ── Detector 8: Dormant Customers ──────────────────────────────────

        private List<RevenueLeak> DetectDormantCustomers(
            IReadOnlyList<Rental> rentals, IReadOnlyList<Customer> customers, DateTime now)
        {
            var result = new List<RevenueLeak>();

            // Find last rental date per customer
            var lastRentalByCustomer = new Dictionary<int, DateTime>();
            foreach (var r in rentals)
            {
                DateTime existing;
                if (!lastRentalByCustomer.TryGetValue(r.CustomerId, out existing) || r.RentalDate > existing)
                    lastRentalByCustomer[r.CustomerId] = r.RentalDate;
            }

            var dormantCustomers = new List<int>();
            foreach (var c in customers)
            {
                DateTime lastRental;
                if (lastRentalByCustomer.TryGetValue(c.Id, out lastRental))
                {
                    if ((now - lastRental).TotalDays >= DormantThresholdDays)
                        dormantCustomers.Add(c.Id);
                }
                else if (c.MemberSince.HasValue && (now - c.MemberSince.Value).TotalDays >= DormantThresholdDays)
                {
                    // Registered but never rented
                    dormantCustomers.Add(c.Id);
                }
            }

            if (dormantCustomers.Count == 0) return result;

            // Estimate lost revenue: average customer lifetime value per month * 3 months * reactivation rate
            decimal avgMonthlyPerCustomer = rentals.Count > 0 && customers.Count > 0
                ? rentals.Sum(r => r.DailyRate * 3) / customers.Count / 12
                : 5.0m;
            decimal totalLost = avgMonthlyPerCustomer * 3 * dormantCustomers.Count * 0.2m; // 20% reactivation rate

            var leak = new RevenueLeak();
            leak.Category = LeakCategory.DormantCustomers;
            leak.Severity = ClassifySeverity(totalLost);
            leak.Description = string.Format("{0} customers inactive for {1}+ days — ${2:F2} recoverable at 20% reactivation",
                dormantCustomers.Count, DormantThresholdDays, totalLost);
            leak.EstimatedImpact = totalLost;
            leak.AffectedCount = dormantCustomers.Count;
            leak.Remediation = "Send 'we miss you' campaign with personalized picks; offer welcome-back discount";
            leak.Confidence = 0.25;
            leak.AffectedEntityIds = dormantCustomers;
            result.Add(leak);

            return result;
        }

        // ── Playbook Builder ───────────────────────────────────────────────

        private List<RemediationAction> BuildPlaybook(
            List<RevenueLeak> leaks, Dictionary<LeakCategory, decimal> breakdown)
        {
            var actions = new List<RemediationAction>();
            int priority = 1;

            // Sort categories by impact for prioritization
            var sortedCategories = breakdown.OrderByDescending(kv => kv.Value).ToList();

            foreach (var kv in sortedCategories)
            {
                var categoryLeaks = leaks.Where(l => l.Category == kv.Key).ToList();
                if (categoryLeaks.Count == 0) continue;

                var topLeak = categoryLeaks.First();
                var action = new RemediationAction();
                action.Priority = priority++;
                action.PotentialRecovery = Math.Round(kv.Value * (decimal)topLeak.Confidence, 2);
                action.RelatedCategories = new List<LeakCategory> { kv.Key };

                switch (kv.Key)
                {
                    case LeakCategory.OverdueUnreturned:
                        action.Title = "Escalate Long-Overdue Rentals";
                        action.Description = "Send final notices for rentals overdue 30+ days. Charge replacement fees and refer to collections.";
                        action.Effort = "Low";
                        break;
                    case LeakCategory.UncollectedLateFees:
                        action.Title = "Automate Late Fee Collection";
                        action.Description = "Implement automated reminders at 1, 3, and 7 days overdue. Add auto-charge for known payment methods.";
                        action.Effort = "Medium";
                        break;
                    case LeakCategory.ExpiredGiftCards:
                        action.Title = "Launch Gift Card Grace Period";
                        action.Description = "Offer 30-day grace reactivation for recently expired cards. Convert remaining balances to store credit.";
                        action.Effort = "Low";
                        break;
                    case LeakCategory.UnderpricedTitles:
                        action.Title = "Implement Dynamic Pricing";
                        action.Description = "Adjust rates for high-demand titles. Consider time-based pricing tiers.";
                        action.Effort = "High";
                        break;
                    case LeakCategory.IdleInventory:
                        action.Title = "Run Idle Inventory Promotions";
                        action.Description = "Feature idle titles in themed collections. Offer 2-for-1 deals on slow movers.";
                        action.Effort = "Low";
                        break;
                    case LeakCategory.LapsedSubscribers:
                        action.Title = "Execute Win-Back Campaign";
                        action.Description = "Email lapsed subscribers with personalized offers. Offer first month free on re-subscription.";
                        action.Effort = "Medium";
                        break;
                    case LeakCategory.UnderutilizedSubscriptions:
                        action.Title = "Boost Subscriber Engagement";
                        action.Description = "Send personalized movie recommendations. Suggest plan downgrades to prevent full cancellation.";
                        action.Effort = "Medium";
                        break;
                    case LeakCategory.DormantCustomers:
                        action.Title = "Reactivation Outreach";
                        action.Description = "Launch 'we miss you' campaign with personalized picks based on rental history.";
                        action.Effort = "Medium";
                        break;
                    default:
                        action.Title = "Address " + kv.Key.ToString();
                        action.Description = topLeak.Remediation;
                        action.Effort = "Medium";
                        break;
                }

                actions.Add(action);
            }

            return actions;
        }

        // ── Health Score ───────────────────────────────────────────────────

        private int CalculateHealthScore(decimal totalLeakage, IReadOnlyList<Rental> rentals, DateTime now)
        {
            // Health = 100 - (leakage as % of recent revenue, capped at 100)
            var recent = rentals.Where(r => r.RentalDate >= now.AddDays(-90) && r.Status == RentalStatus.Returned).ToList();
            decimal recentRevenue = recent.Sum(r => r.TotalCost);

            if (recentRevenue <= 0) return totalLeakage > 0 ? 20 : 80;

            double leakagePct = (double)(totalLeakage / recentRevenue) * 100;

            // Score: 100 at 0% leakage, 0 at 100%+ leakage
            int score = (int)Math.Round(100 - leakagePct);
            return Math.Min(100, Math.Max(0, score));
        }

        // ── Trend Detection ────────────────────────────────────────────────

        private string DetectTrend(IReadOnlyList<Rental> rentals, IReadOnlyList<Movie> movies, DateTime now)
        {
            // Compare overdue counts: last 30 days vs previous 30 days
            var current30 = rentals.Where(r =>
                r.Status != RentalStatus.Returned && r.DueDate < now && r.DueDate >= now.AddDays(-30)).Count();
            var previous30 = rentals.Where(r =>
                r.Status != RentalStatus.Returned && r.DueDate < now.AddDays(-30) && r.DueDate >= now.AddDays(-60)).Count();

            if (previous30 == 0) return "stable";
            double change = (double)(current30 - previous30) / previous30;

            if (change < -0.15) return "improving";
            if (change > 0.15) return "worsening";
            return "stable";
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private LeakSeverity ClassifySeverity(decimal impact)
        {
            if (impact >= 500) return LeakSeverity.Critical;
            if (impact >= 200) return LeakSeverity.High;
            if (impact >= 50) return LeakSeverity.Medium;
            return LeakSeverity.Low;
        }
    }
}
