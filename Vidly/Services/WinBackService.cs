using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Customer Win-Back Engine — identifies lapsed customers,
    /// classifies lapse reasons, generates personalized re-engagement campaigns,
    /// and estimates recovery probability.
    /// </summary>
    public class WinBackService
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;

        public WinBackService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            IClock clock = null)
        {
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _clock = clock ?? new SystemClock();
        }

        // ── Main Analysis ────────────────────────────────────────────

        /// <summary>
        /// Analyze all customers and return a win-back summary with cases,
        /// breakdowns, and proactive insights.
        /// </summary>
        public WinBackSummary Analyze(int lapseDaysThreshold = 60, int topN = 30)
        {
            var now = _clock.Now;
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var allMovies = _movieRepo.GetAll();

            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(allRentals);
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            var cases = new List<WinBackCase>();

            foreach (var customer in customers)
            {
                List<Rental> rentals;
                if (!rentalsByCustomer.TryGetValue(customer.Id, out rentals) || rentals.Count == 0)
                    continue;

                var lastRental = rentals.Max(r => r.RentalDate);
                var daysSince = (int)(now - lastRental).TotalDays;

                if (daysSince < lapseDaysThreshold)
                    continue;

                var winBack = BuildCase(customer, rentals, movieLookup, now, daysSince, lastRental);
                cases.Add(winBack);
            }

            cases = cases.OrderByDescending(c => c.WinBackProbability).Take(topN).ToList();

            var summary = new WinBackSummary
            {
                AnalysisDate = now,
                TotalLapsedCustomers = cases.Count,
                HighProbabilityTargets = cases.Count(c => c.WinBackProbability >= 0.5),
                CampaignsGenerated = cases.Count,
                RecoveredCustomers = 0,
                Cases = cases
            };

            // Breakdowns
            foreach (var c in cases)
            {
                if (!summary.ReasonBreakdown.ContainsKey(c.LapseReason))
                    summary.ReasonBreakdown[c.LapseReason] = 0;
                summary.ReasonBreakdown[c.LapseReason]++;

                if (!summary.CampaignBreakdown.ContainsKey(c.RecommendedCampaign))
                    summary.CampaignBreakdown[c.RecommendedCampaign] = 0;
                summary.CampaignBreakdown[c.RecommendedCampaign]++;
            }

            // Estimated recoverable revenue
            foreach (var c in cases)
            {
                var avgMonthlySpend = c.TotalLifetimeRentals > 0
                    ? c.TotalLifetimeSpend / Math.Max(1, c.DaysSinceLastRental / 30m)
                    : 0m;
                summary.EstimatedRecoverableRevenue += avgMonthlySpend * 6m * (decimal)c.WinBackProbability;
            }

            summary.ProactiveInsights = GenerateInsights(cases, rentalsByCustomer, movieLookup);

            return summary;
        }

        /// <summary>Analyze a single customer for win-back potential.</summary>
        public WinBackCase AnalyzeCustomer(int customerId, int lapseDaysThreshold = 60)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var rentals = _rentalRepo.GetByCustomer(customerId);
            if (rentals == null || rentals.Count == 0)
                throw new ArgumentException($"Customer {customerId} has no rental history.");

            var movieLookup = _movieRepo.GetAll().ToDictionary(m => m.Id);
            var now = _clock.Now;
            var lastRental = rentals.Max(r => r.RentalDate);
            var daysSince = (int)(now - lastRental).TotalDays;

            return BuildCase(customer, rentals.ToList(), movieLookup, now, daysSince, lastRental);
        }

        /// <summary>Get high-probability win-back targets.</summary>
        public IReadOnlyList<WinBackCase> GetHighProbabilityTargets(
            int lapseDaysThreshold = 60, double minProbability = 0.5)
        {
            return Analyze(lapseDaysThreshold, topN: 100)
                .Cases
                .Where(c => c.WinBackProbability >= minProbability)
                .ToList();
        }

        // ── Private Helpers ──────────────────────────────────────────

        private WinBackCase BuildCase(
            Customer customer,
            List<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            DateTime now,
            int daysSince,
            DateTime lastRental)
        {
            var totalSpend = rentals.Sum(r => r.TotalCost);
            var totalLateFees = rentals.Sum(r => r.LateFee);
            var lateFeeRatio = totalSpend > 0 ? totalLateFees / totalSpend : 0m;

            var genreCounts = CustomerRentalAnalytics.ComputeGenreDistribution(rentals, movieLookup);

            var topGenre = genreCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key ?? "Unknown";
            var diversity = CustomerRentalAnalytics.ShannonEntropy(genreCounts);

            var reason = ClassifyLapseReason(rentals, lateFeeRatio, diversity);
            var campaign = MapCampaign(reason);
            var messages = GenerateCampaignMessages(reason, topGenre, customer.Name, rentals);
            var probability = CalculateWinBackProbability(daysSince, rentals.Count, totalSpend, customer.MembershipType, lateFeeRatio);
            var offer = GenerateOffer(reason, topGenre);

            return new WinBackCase
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                Email = customer.Email,
                MembershipType = customer.MembershipType,
                LapseReason = reason,
                RecommendedCampaign = campaign,
                Status = WinBackStatus.Campaigned,
                IdentifiedDate = now,
                LastRentalDate = lastRental,
                DaysSinceLastRental = daysSince,
                TotalLifetimeRentals = rentals.Count,
                TotalLifetimeSpend = totalSpend,
                LateFeeRatio = lateFeeRatio,
                TopGenre = topGenre,
                GenreDiversity = diversity,
                CampaignMessages = messages,
                WinBackProbability = probability,
                PersonalizedOffer = offer
            };
        }

        private LapseReason ClassifyLapseReason(List<Rental> rentals, decimal lateFeeRatio, double diversity)
        {
            // Check price shock: late fees > 30% of spend in last 5 rentals
            var recent = rentals.OrderByDescending(r => r.RentalDate).Take(5).ToList();
            var recentSpend = recent.Sum(r => r.TotalCost);
            var recentLateFees = recent.Sum(r => r.LateFee);
            if (recentSpend > 0 && recentLateFees / recentSpend > 0.3m)
                return LapseReason.PriceShock;

            // Check bad experience: > 40% of all rentals had late fees
            var lateCount = rentals.Count(r => r.LateFee > 0);
            if (rentals.Count > 0 && (double)lateCount / rentals.Count > 0.4)
                return LapseReason.BadExperience;

            // Check narrow taste: low genre diversity
            if (diversity < 0.3)
                return LapseReason.NarrowTaste;

            // Check seasonal: rentals cluster in specific months
            if (IsSeasonalRenter(rentals))
                return LapseReason.SeasonalDropoff;

            return LapseReason.Inactivity;
        }

        private bool IsSeasonalRenter(List<Rental> rentals)
        {
            if (rentals.Count < 4) return false;

            var monthlyCounts = new int[12];
            foreach (var r in rentals)
                monthlyCounts[r.RentalDate.Month - 1]++;

            var mean = monthlyCounts.Average();
            if (mean < 0.01) return false;

            var variance = monthlyCounts.Sum(c => (c - mean) * (c - mean)) / 12.0;
            var stddev = Math.Sqrt(variance);
            var cv = stddev / mean;

            return cv > 1.5;
        }

        private CampaignType MapCampaign(LapseReason reason)
        {
            switch (reason)
            {
                case LapseReason.PriceShock: return CampaignType.PriceIncentive;
                case LapseReason.NarrowTaste: return CampaignType.GenreExpansion;
                case LapseReason.BadExperience: return CampaignType.LoyaltyRecovery;
                case LapseReason.SeasonalDropoff: return CampaignType.SeasonalReminder;
                case LapseReason.Inactivity: return CampaignType.WelcomeBack;
                default: return CampaignType.PersonalPick;
            }
        }

        private List<string> GenerateCampaignMessages(LapseReason reason, string topGenre, string name, List<Rental> rentals)
        {
            var msgs = new List<string>();
            var firstName = name?.Split(' ')[0] ?? "there";

            switch (reason)
            {
                case LapseReason.PriceShock:
                    msgs.Add($"Hey {firstName}, we noticed some bumpy fees on your last visits. Here's a free rental on us — no strings attached.");
                    msgs.Add("We've also introduced flexible return windows for loyal members like you.");
                    break;
                case LapseReason.NarrowTaste:
                    var allGenres = Enum.GetNames(typeof(Genre));
                    var suggestions = allGenres.Where(g => g != topGenre).Take(3);
                    msgs.Add($"Love {topGenre}? You might also enjoy: {string.Join(", ", suggestions)}.");
                    msgs.Add($"We've added 20+ new titles outside your usual picks — come explore!");
                    break;
                case LapseReason.BadExperience:
                    msgs.Add($"{firstName}, we value your loyalty. We're sorry about past hassles — here's bonus loyalty points to make it right.");
                    msgs.Add("Our new flexible return policy means fewer surprises on your bill.");
                    break;
                case LapseReason.SeasonalDropoff:
                    var peakMonths = GetPeakMonths(rentals);
                    msgs.Add($"Your favorite rental season is coming up! Last year you loved renting in {peakMonths}.");
                    msgs.Add("Early-bird deals are live — reserve your picks now!");
                    break;
                default:
                    msgs.Add($"We miss you, {firstName}! It's been a while since your last visit.");
                    msgs.Add($"Check out our latest {topGenre} arrivals — picked just for you.");
                    break;
            }

            return msgs;
        }

        private string GetPeakMonths(List<Rental> rentals)
        {
            var monthlyCounts = rentals.GroupBy(r => r.RentalDate.Month)
                .OrderByDescending(g => g.Count())
                .Take(2)
                .Select(g => new DateTime(2000, g.Key, 1).ToString("MMMM"));
            return string.Join(" & ", monthlyCounts);
        }

        private double CalculateWinBackProbability(int daysSince, int totalRentals, decimal totalSpend, MembershipType tier, decimal lateFeeRatio)
        {
            // Recency: closer is better (decay over 365 days)
            var recencyScore = Math.Max(0, 1.0 - daysSince / 365.0);

            // Lifetime value: logarithmic scale
            var valueScore = Math.Min(1.0, Math.Log(1 + (double)totalSpend) / Math.Log(1 + 500));

            // Frequency: logarithmic
            var freqScore = Math.Min(1.0, Math.Log(1 + totalRentals) / Math.Log(1 + 50));

            // Tier bonus
            var tierScore = (double)(int)tier / 4.0;

            // Late fee penalty
            var latePenalty = Math.Min(1.0, (double)lateFeeRatio * 2);

            // Weighted combination
            var probability = (recencyScore * 0.30)
                            + (valueScore * 0.25)
                            + (freqScore * 0.20)
                            + (tierScore * 0.15)
                            - (latePenalty * 0.10);

            return Math.Max(0, Math.Min(1, probability));
        }

        private string GenerateOffer(LapseReason reason, string topGenre)
        {
            switch (reason)
            {
                case LapseReason.PriceShock:
                    return "1 free rental + waived late fees on your next 3 returns";
                case LapseReason.NarrowTaste:
                    return $"Rent any non-{topGenre} movie free — discover something new!";
                case LapseReason.BadExperience:
                    return "500 bonus loyalty points + free rental upgrade";
                case LapseReason.SeasonalDropoff:
                    return "20% off seasonal bundle — reserve up to 5 titles in advance";
                default:
                    return $"Free rental of any {topGenre} movie — we saved your favorites";
            }
        }

        // ShannonEntropy moved to CustomerRentalAnalytics.ShannonEntropy

        private List<string> GenerateInsights(
            List<WinBackCase> cases,
            Dictionary<int, List<Rental>> rentalsByCustomer,
            Dictionary<int, Movie> movieLookup)
        {
            var insights = new List<string>();
            if (cases.Count == 0) return insights;

            // Genre concentration among lapsed
            var genreGroups = cases.GroupBy(c => c.TopGenre).OrderByDescending(g => g.Count()).ToList();
            if (genreGroups.Count > 0)
            {
                var top = genreGroups[0];
                insights.Add($"{top.Count()} lapsed customers were heavy {top.Key} renters — consider expanding the {top.Key} catalog or adding new releases.");
            }

            // Price shock insight
            var priceShocked = cases.Count(c => c.LapseReason == LapseReason.PriceShock);
            if (priceShocked > 0)
            {
                var avgLateFee = cases.Where(c => c.LapseReason == LapseReason.PriceShock).Average(c => (double)c.LateFeeRatio);
                insights.Add($"{priceShocked} customers left due to price shock (avg late-fee ratio: {avgLateFee:P0}). Consider a grace-period policy or fee cap.");
            }

            // High-value lapsed
            var highValue = cases.Where(c => c.TotalLifetimeSpend > 100).ToList();
            if (highValue.Count > 0)
            {
                var totalLost = highValue.Sum(c => c.TotalLifetimeSpend);
                insights.Add($"{highValue.Count} high-value customers (>${100} lifetime) have lapsed — representing ${totalLost:N0} in historical revenue.");
            }

            // Membership tier skew
            var tierGroups = cases.GroupBy(c => c.MembershipType).OrderByDescending(g => g.Count()).ToList();
            if (tierGroups.Count > 0)
            {
                var topTier = tierGroups[0];
                insights.Add($"Most lapsed customers are {topTier.Key} members ({topTier.Count()} of {cases.Count}). Consider tier-specific retention programs.");
            }

            // Seasonal opportunity
            var seasonal = cases.Count(c => c.LapseReason == LapseReason.SeasonalDropoff);
            if (seasonal > 0)
            {
                insights.Add($"{seasonal} customers show seasonal rental patterns. Set up automated reminders before their peak months.");
            }

            return insights;
        }

        // BuildRentalLookup moved to CustomerRentalAnalytics.BuildRentalsByCustomer
    }
}
