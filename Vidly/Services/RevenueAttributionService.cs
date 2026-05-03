using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Revenue Attribution Engine — multi-touch revenue attribution
    /// that traces revenue back to its driving factors and generates actionable insights.
    ///
    /// 7 engines:
    /// 1. Channel Attribution — revenue by logical channel (new release vs catalog, genre)
    /// 2. Temporal Attribution — revenue by time period (month, day-of-week, season)
    /// 3. Customer Tier Attribution — revenue by membership tier with concentration index
    /// 4. Genre Revenue Engine — per-genre revenue, growth trends, market share
    /// 5. Pricing Rule Attribution — estimated impact of pricing rule types
    /// 6. Retention Attribution — new vs returning customer revenue split
    /// 7. Insight Generator — autonomous natural-language insights
    /// </summary>
    public class RevenueAttributionService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IClock _clock;

        public RevenueAttributionService(
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

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        /// <summary>Generate a full revenue attribution report.</summary>
        public RevenueAttributionReport GenerateReport()
        {
            var now = _clock.Now;
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll();
            var customers = _customerRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);
            var customerLookup = customers.ToDictionary(c => c.Id, c => c);

            var totalRevenue = ComputeTotalRevenue(rentals);
            var channels = BuildChannelAttribution(rentals, movieLookup, totalRevenue);
            var temporal = BuildTemporalAttribution(rentals, totalRevenue, "month");
            var tiers = BuildTierAttribution(rentals, customerLookup, totalRevenue);
            var genres = BuildGenreBreakdown(rentals, movieLookup, totalRevenue);
            var pricing = EstimatePricingImpacts(rentals, movieLookup);
            var retention = BuildRetentionAttribution(rentals, customerLookup, totalRevenue);
            var insights = GenerateInsights(totalRevenue, channels, temporal, tiers, genres, pricing, retention);
            var health = ComputeHealthScore(rentals, channels, genres, tiers);

            return new RevenueAttributionReport
            {
                GeneratedAt = now,
                TotalRevenue = totalRevenue,
                ChannelBreakdown = channels,
                TemporalBreakdown = temporal,
                TierBreakdown = tiers,
                GenreBreakdown = genres,
                PricingImpacts = pricing,
                RetentionBreakdown = retention,
                Insights = insights,
                AttributionHealthScore = health
            };
        }

        /// <summary>Get channel-level revenue breakdown.</summary>
        public List<ChannelAttribution> GetChannelBreakdown()
        {
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll().ToDictionary(m => m.Id, m => m);
            var total = ComputeTotalRevenue(rentals);
            return BuildChannelAttribution(rentals, movies, total);
        }

        /// <summary>Get temporal revenue breakdown.</summary>
        public List<TemporalAttribution> GetTemporalBreakdown(string granularity = "month")
        {
            var rentals = _rentalRepo.GetAll();
            var total = ComputeTotalRevenue(rentals);
            return BuildTemporalAttribution(rentals, total, granularity);
        }

        /// <summary>Get tier-level revenue attribution.</summary>
        public TierAttribution GetTierAttribution()
        {
            var rentals = _rentalRepo.GetAll();
            var customers = _customerRepo.GetAll().ToDictionary(c => c.Id, c => c);
            var total = ComputeTotalRevenue(rentals);
            return BuildTierAttribution(rentals, customers, total);
        }

        // ----------------------------------------------------------------
        //  Engine 1: Channel Attribution
        // ----------------------------------------------------------------

        private List<ChannelAttribution> BuildChannelAttribution(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movies,
            decimal totalRevenue)
        {
            var results = new List<ChannelAttribution>();
            if (rentals.Count == 0) return results;

            // Split by new release vs catalog
            var newReleaseRentals = new List<Rental>();
            var catalogRentals = new List<Rental>();

            foreach (var r in rentals)
            {
                Movie m;
                if (movies.TryGetValue(r.MovieId, out m) && m.ReleaseDate.HasValue &&
                    (r.RentalDate - m.ReleaseDate.Value).TotalDays <= 90)
                    newReleaseRentals.Add(r);
                else
                    catalogRentals.Add(r);
            }

            if (newReleaseRentals.Count > 0)
            {
                var rev = SumRevenue(newReleaseRentals);
                results.Add(new ChannelAttribution
                {
                    Channel = "New Release",
                    Revenue = rev,
                    SharePercent = totalRevenue > 0 ? (double)(rev / totalRevenue * 100) : 0,
                    RentalCount = newReleaseRentals.Count,
                    RevenuePerRental = rev / newReleaseRentals.Count
                });
            }

            if (catalogRentals.Count > 0)
            {
                var rev = SumRevenue(catalogRentals);
                results.Add(new ChannelAttribution
                {
                    Channel = "Catalog",
                    Revenue = rev,
                    SharePercent = totalRevenue > 0 ? (double)(rev / totalRevenue * 100) : 0,
                    RentalCount = catalogRentals.Count,
                    RevenuePerRental = rev / catalogRentals.Count
                });
            }

            return results.OrderByDescending(c => c.Revenue).ToList();
        }

        // ----------------------------------------------------------------
        //  Engine 2: Temporal Attribution
        // ----------------------------------------------------------------

        private List<TemporalAttribution> BuildTemporalAttribution(
            IReadOnlyList<Rental> rentals,
            decimal totalRevenue,
            string granularity)
        {
            if (rentals.Count == 0) return new List<TemporalAttribution>();

            IEnumerable<IGrouping<string, Rental>> groups;

            if (granularity == "dow")
            {
                groups = rentals.GroupBy(r => r.RentalDate.DayOfWeek.ToString());
            }
            else if (granularity == "season")
            {
                groups = rentals.GroupBy(r => GetSeason(r.RentalDate));
            }
            else
            {
                // month
                groups = rentals.GroupBy(r => r.RentalDate.ToString("yyyy-MM"));
            }

            var ordered = groups.OrderBy(g => g.Key).ToList();
            var results = new List<TemporalAttribution>();

            decimal prevRevenue = 0;
            bool hasPrev = false;

            foreach (var g in ordered)
            {
                var rev = SumRevenue(g.ToList());
                var growth = 0.0;
                if (hasPrev && prevRevenue > 0)
                    growth = (double)((rev - prevRevenue) / prevRevenue * 100);

                results.Add(new TemporalAttribution
                {
                    Period = g.Key,
                    Revenue = rev,
                    SharePercent = totalRevenue > 0 ? (double)(rev / totalRevenue * 100) : 0,
                    RentalCount = g.Count(),
                    GrowthPercent = Math.Round(growth, 1)
                });

                prevRevenue = rev;
                hasPrev = true;
            }

            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 3: Customer Tier Attribution
        // ----------------------------------------------------------------

        private TierAttribution BuildTierAttribution(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Customer> customers,
            decimal totalRevenue)
        {
            var tierNames = new[] { "Basic", "Silver", "Gold", "Platinum" };
            var tierEnums = new[] { MembershipType.Basic, MembershipType.Silver, MembershipType.Gold, MembershipType.Platinum };

            var tierRevenues = new List<TierRevenue>();

            for (int i = 0; i < tierNames.Length; i++)
            {
                var tier = tierEnums[i];
                var tierCustomerIds = new HashSet<int>(
                    customers.Values.Where(c => c.MembershipType == tier).Select(c => c.Id));
                var tierRentals = rentals.Where(r => tierCustomerIds.Contains(r.CustomerId)).ToList();
                var rev = SumRevenue(tierRentals);

                tierRevenues.Add(new TierRevenue
                {
                    Tier = tierNames[i],
                    CustomerCount = tierCustomerIds.Count,
                    TotalRevenue = rev,
                    RevenuePerCapita = tierCustomerIds.Count > 0 ? rev / tierCustomerIds.Count : 0,
                    SharePercent = totalRevenue > 0 ? (double)(rev / totalRevenue * 100) : 0
                });
            }

            // Gini-like concentration index
            var shares = tierRevenues.Where(t => t.CustomerCount > 0)
                .Select(t => (double)t.RevenuePerCapita).ToList();
            var concentration = ComputeGini(shares);

            return new TierAttribution
            {
                Tiers = tierRevenues,
                ConcentrationIndex = Math.Round(concentration, 3)
            };
        }

        // ----------------------------------------------------------------
        //  Engine 4: Genre Revenue
        // ----------------------------------------------------------------

        private List<GenreRevenue> BuildGenreBreakdown(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movies,
            decimal totalRevenue)
        {
            if (rentals.Count == 0) return new List<GenreRevenue>();

            var genreGroups = rentals
                .GroupBy(r =>
                {
                    Movie m;
                    return movies.TryGetValue(r.MovieId, out m) && m.Genre.HasValue
                        ? m.Genre.Value.ToString()
                        : "Unknown";
                })
                .OrderByDescending(g => g.Sum(r => RentalRevenue(r)));

            var results = new List<GenreRevenue>();

            foreach (var g in genreGroups)
            {
                var rev = SumRevenue(g.ToList());
                var rentalList = g.OrderBy(r => r.RentalDate).ToList();

                // Simple growth: compare first half vs second half
                double growth = 0;
                if (rentalList.Count >= 4)
                {
                    int mid = rentalList.Count / 2;
                    var firstHalf = SumRevenue(rentalList.Take(mid).ToList());
                    var secondHalf = SumRevenue(rentalList.Skip(mid).ToList());
                    if (firstHalf > 0)
                        growth = (double)((secondHalf - firstHalf) / firstHalf * 100);
                }

                string trend = growth > 10 ? "Rising" : growth < -10 ? "Declining" : "Stable";

                results.Add(new GenreRevenue
                {
                    Genre = g.Key,
                    Revenue = rev,
                    SharePercent = totalRevenue > 0 ? (double)(rev / totalRevenue * 100) : 0,
                    RentalCount = g.Count(),
                    RevenuePerRental = g.Count() > 0 ? rev / g.Count() : 0,
                    GrowthPercent = Math.Round(growth, 1),
                    Trend = trend
                });
            }

            return results;
        }

        // ----------------------------------------------------------------
        //  Engine 5: Pricing Rule Attribution
        // ----------------------------------------------------------------

        private List<PricingRuleImpact> EstimatePricingImpacts(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movies)
        {
            if (rentals.Count == 0) return new List<PricingRuleImpact>();

            var impacts = new List<PricingRuleImpact>();

            // Weekend surge estimate (Fri-Sun rentals at premium)
            var weekendRentals = rentals.Where(r =>
                r.RentalDate.DayOfWeek == DayOfWeek.Friday ||
                r.RentalDate.DayOfWeek == DayOfWeek.Saturday ||
                r.RentalDate.DayOfWeek == DayOfWeek.Sunday).ToList();

            if (weekendRentals.Count > 0)
            {
                var baseRevenue = SumRevenue(weekendRentals);
                var surgeEffect = baseRevenue * 0.25m / 1.25m; // reverse the 25% premium
                impacts.Add(new PricingRuleImpact
                {
                    RuleName = "Weekend Surge",
                    RuleType = "DemandSurge",
                    EstimatedImpact = Math.Round(surgeEffect, 2),
                    AffectedRentals = weekendRentals.Count,
                    AverageEffect = weekendRentals.Count > 0 ? Math.Round(surgeEffect / weekendRentals.Count, 2) : 0
                });
            }

            // Midweek discount estimate (Tue-Wed)
            var midweekRentals = rentals.Where(r =>
                r.RentalDate.DayOfWeek == DayOfWeek.Tuesday ||
                r.RentalDate.DayOfWeek == DayOfWeek.Wednesday).ToList();

            if (midweekRentals.Count > 0)
            {
                var baseRevenue = SumRevenue(midweekRentals);
                var discountEffect = baseRevenue * 0.15m / 0.85m; // reverse the 15% discount
                impacts.Add(new PricingRuleImpact
                {
                    RuleName = "Midweek Discount",
                    RuleType = "OffPeakDiscount",
                    EstimatedImpact = Math.Round(-discountEffect, 2),
                    AffectedRentals = midweekRentals.Count,
                    AverageEffect = midweekRentals.Count > 0 ? Math.Round(-discountEffect / midweekRentals.Count, 2) : 0
                });
            }

            // New release premium
            var newReleaseRentals = rentals.Where(r =>
            {
                Movie m;
                return movies.TryGetValue(r.MovieId, out m) && m.ReleaseDate.HasValue &&
                       (r.RentalDate - m.ReleaseDate.Value).TotalDays <= 30;
            }).ToList();

            if (newReleaseRentals.Count > 0)
            {
                var baseRevenue = SumRevenue(newReleaseRentals);
                var premiumEffect = baseRevenue * 0.35m / 1.35m;
                impacts.Add(new PricingRuleImpact
                {
                    RuleName = "New Release Premium",
                    RuleType = "NewReleasePremium",
                    EstimatedImpact = Math.Round(premiumEffect, 2),
                    AffectedRentals = newReleaseRentals.Count,
                    AverageEffect = newReleaseRentals.Count > 0 ? Math.Round(premiumEffect / newReleaseRentals.Count, 2) : 0
                });
            }

            return impacts.OrderByDescending(i => Math.Abs(i.EstimatedImpact)).ToList();
        }

        // ----------------------------------------------------------------
        //  Engine 6: Retention Attribution
        // ----------------------------------------------------------------

        private RetentionAttribution BuildRetentionAttribution(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Customer> customers,
            decimal totalRevenue)
        {
            if (rentals.Count == 0)
            {
                return new RetentionAttribution();
            }

            // A customer is "returning" if they have more than 1 rental
            var rentalsByCustomer = rentals.GroupBy(r => r.CustomerId).ToList();
            var newCustomerIds = new HashSet<int>(rentalsByCustomer.Where(g => g.Count() == 1).Select(g => g.Key));
            var returningCustomerIds = new HashSet<int>(rentalsByCustomer.Where(g => g.Count() > 1).Select(g => g.Key));

            var newRev = SumRevenue(rentals.Where(r => newCustomerIds.Contains(r.CustomerId)).ToList());
            var retRev = SumRevenue(rentals.Where(r => returningCustomerIds.Contains(r.CustomerId)).ToList());

            // Top 10% revenue concentration
            var customerRevenues = rentalsByCustomer
                .Select(g => new { CustomerId = g.Key, Revenue = SumRevenue(g.ToList()) })
                .OrderByDescending(x => x.Revenue)
                .ToList();

            int top10Count = Math.Max(1, customerRevenues.Count / 10);
            var top10Revenue = customerRevenues.Take(top10Count).Sum(x => x.Revenue);
            var top10Share = totalRevenue > 0 ? (double)(top10Revenue / totalRevenue * 100) : 0;

            return new RetentionAttribution
            {
                NewCustomerRevenue = newRev,
                NewCustomerShare = totalRevenue > 0 ? (double)(newRev / totalRevenue * 100) : 0,
                ReturningCustomerRevenue = retRev,
                ReturningCustomerShare = totalRevenue > 0 ? (double)(retRev / totalRevenue * 100) : 0,
                NewCustomerCount = newCustomerIds.Count,
                ReturningCustomerCount = returningCustomerIds.Count,
                RepeatRevenuePerCapita = returningCustomerIds.Count > 0 ? retRev / returningCustomerIds.Count : 0,
                Top10PercentRevenueShare = Math.Round(top10Share, 1)
            };
        }

        // ----------------------------------------------------------------
        //  Engine 7: Insight Generator
        // ----------------------------------------------------------------

        private List<string> GenerateInsights(
            decimal totalRevenue,
            List<ChannelAttribution> channels,
            List<TemporalAttribution> temporal,
            TierAttribution tiers,
            List<GenreRevenue> genres,
            List<PricingRuleImpact> pricing,
            RetentionAttribution retention)
        {
            var insights = new List<string>();

            if (totalRevenue == 0)
            {
                insights.Add("No revenue data available for attribution analysis.");
                return insights;
            }

            // Channel insights
            var topChannel = channels.OrderByDescending(c => c.Revenue).FirstOrDefault();
            if (topChannel != null)
                insights.Add(string.Format("{0} is the top revenue channel at {1:F1}% share (${2:N2} total).",
                    topChannel.Channel, topChannel.SharePercent, topChannel.Revenue));

            // Genre insights
            var topGenre = genres.OrderByDescending(g => g.Revenue).FirstOrDefault();
            if (topGenre != null)
                insights.Add(string.Format("{0} leads genre revenue with ${1:N2} ({2:F1}% share, {3}).",
                    topGenre.Genre, topGenre.Revenue, topGenre.SharePercent, topGenre.Trend));

            var risingGenres = genres.Where(g => g.Trend == "Rising").ToList();
            if (risingGenres.Count > 0)
                insights.Add(string.Format("Rising genres: {0} — consider increasing inventory.",
                    string.Join(", ", risingGenres.Select(g => g.Genre))));

            var decliningGenres = genres.Where(g => g.Trend == "Declining").ToList();
            if (decliningGenres.Count > 0)
                insights.Add(string.Format("Declining genres: {0} — review pricing or reduce stock.",
                    string.Join(", ", decliningGenres.Select(g => g.Genre))));

            // Most efficient genre (highest revenue per rental)
            var efficientGenre = genres.OrderByDescending(g => g.RevenuePerRental).FirstOrDefault();
            if (efficientGenre != null && genres.Count > 1)
                insights.Add(string.Format("{0} has the highest revenue per rental at ${1:N2}.",
                    efficientGenre.Genre, efficientGenre.RevenuePerRental));

            // Tier insights
            var topTier = tiers.Tiers.OrderByDescending(t => t.TotalRevenue).FirstOrDefault();
            if (topTier != null)
                insights.Add(string.Format("{0} tier contributes {1:F1}% of revenue (${2:N2} per capita).",
                    topTier.Tier, topTier.SharePercent, topTier.RevenuePerCapita));

            if (tiers.ConcentrationIndex > 0.5)
                insights.Add(string.Format("High tier concentration (Gini: {0:F3}) — revenue depends heavily on premium tiers.",
                    tiers.ConcentrationIndex));

            // Retention insights
            if (retention.ReturningCustomerShare > 60)
                insights.Add(string.Format("Returning customers drive {0:F1}% of revenue — strong retention.",
                    retention.ReturningCustomerShare));
            else if (retention.NewCustomerShare > 60)
                insights.Add(string.Format("New customers drive {0:F1}% of revenue — focus on converting them to repeat renters.",
                    retention.NewCustomerShare));

            if (retention.Top10PercentRevenueShare > 50)
                insights.Add(string.Format("Top 10% of customers generate {0:F1}% of revenue — high concentration risk.",
                    retention.Top10PercentRevenueShare));

            // Pricing insights
            var biggestPricing = pricing.OrderByDescending(p => Math.Abs(p.EstimatedImpact)).FirstOrDefault();
            if (biggestPricing != null)
                insights.Add(string.Format("{0} pricing rule has the largest estimated impact: ${1:N2} across {2} rentals.",
                    biggestPricing.RuleName, biggestPricing.EstimatedImpact, biggestPricing.AffectedRentals));

            // Temporal insights
            var peakMonth = temporal.OrderByDescending(t => t.Revenue).FirstOrDefault();
            if (peakMonth != null && temporal.Count > 1)
                insights.Add(string.Format("Peak revenue period: {0} with ${1:N2} ({2:F1}% share).",
                    peakMonth.Period, peakMonth.Revenue, peakMonth.SharePercent));

            return insights;
        }

        // ----------------------------------------------------------------
        //  Health Score
        // ----------------------------------------------------------------

        private double ComputeHealthScore(
            IReadOnlyList<Rental> rentals,
            List<ChannelAttribution> channels,
            List<GenreRevenue> genres,
            TierAttribution tiers)
        {
            if (rentals.Count == 0) return 0;

            double score = 50; // base

            // Data volume bonus (up to +15)
            score += Math.Min(15, rentals.Count * 0.5);

            // Channel diversity (up to +10)
            if (channels.Count >= 2) score += 10;

            // Genre diversity (up to +15) — Shannon entropy normalized
            if (genres.Count > 0)
            {
                var shares = genres.Select(g => g.SharePercent / 100.0).Where(s => s > 0).ToList();
                double entropy = -shares.Sum(s => s * Math.Log(s));
                double maxEntropy = Math.Log(Math.Max(1, shares.Count));
                double normalized = maxEntropy > 0 ? entropy / maxEntropy : 0;
                score += normalized * 15;
            }

            // Tier balance (up to +10) — lower concentration is better
            var tierConcentration = tiers.ConcentrationIndex;
            score += (1 - tierConcentration) * 10;

            return Math.Round(Math.Min(100, Math.Max(0, score)), 1);
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        private static decimal ComputeTotalRevenue(IReadOnlyList<Rental> rentals)
        {
            return SumRevenue(rentals);
        }

        private static decimal SumRevenue(IReadOnlyList<Rental> rentals)
        {
            decimal sum = 0;
            foreach (var r in rentals)
                sum += RentalRevenue(r);
            return sum;
        }

        private static decimal SumRevenue(IList<Rental> rentals)
        {
            decimal sum = 0;
            foreach (var r in rentals)
                sum += RentalRevenue(r);
            return sum;
        }

        private static decimal RentalRevenue(Rental r)
        {
            // Use DailyRate * days + LateFee + DamageCharge (same as TotalCost logic)
            var endDate = r.ReturnDate ?? DateTime.Today;
            var days = Math.Max(1, (int)Math.Ceiling((endDate - r.RentalDate).TotalDays));
            return (days * r.DailyRate) + r.LateFee + r.DamageCharge;
        }

        private static string GetSeason(DateTime date)
        {
            int month = date.Month;
            if (month >= 3 && month <= 5) return "Spring";
            if (month >= 6 && month <= 8) return "Summer";
            if (month >= 9 && month <= 11) return "Fall";
            return "Winter";
        }

        private static double ComputeGini(List<double> values)
        {
            if (values.Count <= 1) return 0;
            values.Sort();
            int n = values.Count;
            double sum = values.Sum();
            if (sum <= 0) return 0;

            double giniNumerator = 0;
            for (int i = 0; i < n; i++)
                giniNumerator += (2 * (i + 1) - n - 1) * values[i];

            return giniNumerator / (n * sum);
        }
    }
}
