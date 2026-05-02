using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Competitive Intelligence Engine — analyzes pricing and catalog
    /// positioning against simulated market benchmarks, detects opportunities and
    /// threats, and recommends strategic pricing moves.
    /// </summary>
    public class CompetitiveIntelService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IClock _clock;

        private static readonly string[] CompetitorNames =
            { "StreamFlix", "MovieVault", "CineRent", "QuickFlicks" };

        // Stable multipliers per competitor for deterministic benchmark generation.
        private static readonly decimal[] PriceMultipliers = { 0.92m, 1.08m, 0.98m, 1.15m };
        private static readonly double[] SatisfactionBases = { 4.1, 3.6, 3.9, 3.3 };
        private static readonly double[] CatalogMultipliers = { 1.2, 0.8, 1.05, 0.65 };

        public CompetitiveIntelService(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo,
            IClock clock)
        {
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // -------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------

        public CompetitiveIntelDashboard GetDashboard()
        {
            var benchmarks = GenerateBenchmarks();
            var positions = AnalyzePositionInternal(benchmarks);
            var opportunities = ScanOpportunitiesInternal(benchmarks, positions);
            var threats = DetectThreatsInternal(benchmarks, positions);
            var recommendations = GetRecommendationsInternal(positions, opportunities, threats);
            var health = ComputeHealth(positions, opportunities, threats);
            var insights = GenerateInsights(positions, opportunities, threats, health);

            return new CompetitiveIntelDashboard
            {
                PositionMap = positions,
                Opportunities = opportunities,
                Threats = threats,
                Recommendations = recommendations,
                Benchmarks = benchmarks,
                HealthScore = health,
                AutonomousInsights = insights
            };
        }

        public List<MarketPositionAssessment> AnalyzePosition()
        {
            return AnalyzePositionInternal(GenerateBenchmarks());
        }

        public List<MarketOpportunity> ScanOpportunities()
        {
            var bm = GenerateBenchmarks();
            return ScanOpportunitiesInternal(bm, AnalyzePositionInternal(bm));
        }

        public List<CompetitiveThreat> DetectThreats()
        {
            var bm = GenerateBenchmarks();
            return DetectThreatsInternal(bm, AnalyzePositionInternal(bm));
        }

        public List<StrategicRecommendation> GetRecommendations()
        {
            var bm = GenerateBenchmarks();
            var pos = AnalyzePositionInternal(bm);
            var opp = ScanOpportunitiesInternal(bm, pos);
            var thr = DetectThreatsInternal(bm, pos);
            return GetRecommendationsInternal(pos, opp, thr);
        }

        public CompetitiveHealthScore GetHealthScore()
        {
            var bm = GenerateBenchmarks();
            var pos = AnalyzePositionInternal(bm);
            var opp = ScanOpportunitiesInternal(bm, pos);
            var thr = DetectThreatsInternal(bm, pos);
            return ComputeHealth(pos, opp, thr);
        }

        // -------------------------------------------------------------------
        //  1. Benchmark Generator
        // -------------------------------------------------------------------

        internal List<CompetitorBenchmark> GenerateBenchmarks()
        {
            var movies = _movieRepo.GetAll();
            var rentals = _rentalRepo.GetAll();
            var now = _clock.Now;
            var result = new List<CompetitorBenchmark>();

            var genres = movies
                .Where(m => m.Genre.HasValue)
                .Select(m => m.Genre.Value)
                .Distinct()
                .ToList();

            if (genres.Count == 0)
                return result;

            // Our average rate from rentals
            decimal ourAvgRate = rentals.Count > 0
                ? rentals.Average(r => r.DailyRate)
                : 3.99m;

            foreach (var genre in genres)
            {
                int ourCount = movies.Count(m => m.Genre == genre);

                for (int c = 0; c < CompetitorNames.Length; c++)
                {
                    int catalogSize = Math.Max(1, (int)(ourCount * CatalogMultipliers[c]));
                    decimal avgRate = Math.Round(ourAvgRate * PriceMultipliers[c], 2);

                    result.Add(new CompetitorBenchmark
                    {
                        CompetitorName = CompetitorNames[c],
                        Genre = genre,
                        AvgDailyRate = avgRate,
                        CatalogSize = catalogSize,
                        CustomerSatisfaction = SatisfactionBases[c],
                        LastUpdated = now
                    });
                }
            }

            return result;
        }

        // -------------------------------------------------------------------
        //  2. Market Position Analyzer
        // -------------------------------------------------------------------

        internal List<MarketPositionAssessment> AnalyzePositionInternal(
            List<CompetitorBenchmark> benchmarks)
        {
            var movies = _movieRepo.GetAll();
            var rentals = _rentalRepo.GetAll();
            var result = new List<MarketPositionAssessment>();

            var genres = movies
                .Where(m => m.Genre.HasValue)
                .Select(m => m.Genre.Value)
                .Distinct();

            foreach (var genre in genres)
            {
                var ourMovies = movies.Where(m => m.Genre == genre).ToList();
                var genreRentals = rentals.Where(r =>
                    ourMovies.Any(m => m.Id == r.MovieId)).ToList();

                decimal ourAvg = genreRentals.Count > 0
                    ? genreRentals.Average(r => r.DailyRate)
                    : 3.99m;

                var competitorEntries = benchmarks.Where(b => b.Genre == genre).ToList();
                if (competitorEntries.Count == 0)
                    continue;

                decimal marketAvg = competitorEntries.Average(b => b.AvgDailyRate);
                int avgCompCatalog = (int)competitorEntries.Average(b => b.CatalogSize);
                decimal gapPct = marketAvg != 0
                    ? Math.Round((ourAvg - marketAvg) / marketAvg * 100, 1)
                    : 0m;

                var position = ClassifyPosition(gapPct, ourMovies.Count, avgCompCatalog);

                result.Add(new MarketPositionAssessment
                {
                    Genre = genre,
                    Position = position,
                    OurAvgPrice = Math.Round(ourAvg, 2),
                    MarketAvgPrice = Math.Round(marketAvg, 2),
                    PriceGapPercent = gapPct,
                    OurCatalogCount = ourMovies.Count,
                    AvgCompetitorCatalogCount = avgCompCatalog,
                    Assessment = DescribePosition(position, genre, gapPct)
                });
            }

            return result.OrderBy(a => a.Position).ToList();
        }

        private static MarketPosition ClassifyPosition(decimal gapPct, int ourCount, int compCount)
        {
            // Combine price advantage and catalog size advantage
            decimal catalogRatio = compCount > 0 ? (decimal)ourCount / compCount : 1m;
            decimal score = -gapPct * 0.6m + (catalogRatio - 1m) * 100m * 0.4m;

            if (score > 15) return MarketPosition.Leader;
            if (score > 5) return MarketPosition.Competitive;
            if (score > -5) return MarketPosition.AtParity;
            if (score > -15) return MarketPosition.Trailing;
            return MarketPosition.Vulnerable;
        }

        private static string DescribePosition(MarketPosition pos, Genre genre, decimal gapPct)
        {
            string dir = gapPct < 0 ? "below" : "above";
            string pct = Math.Abs(gapPct).ToString("F1");
            switch (pos)
            {
                case MarketPosition.Leader:
                    return $"Dominating {genre} — priced {pct}% {dir} market with strong catalog.";
                case MarketPosition.Competitive:
                    return $"Strong in {genre} — {pct}% {dir} market average, healthy position.";
                case MarketPosition.AtParity:
                    return $"{genre} is at market parity — {pct}% {dir} average, stable but undifferentiated.";
                case MarketPosition.Trailing:
                    return $"Losing ground in {genre} — {pct}% {dir} market, action needed.";
                default:
                    return $"Vulnerable in {genre} — {pct}% {dir} market, urgent intervention required.";
            }
        }

        // -------------------------------------------------------------------
        //  3. Opportunity Scanner
        // -------------------------------------------------------------------

        internal List<MarketOpportunity> ScanOpportunitiesInternal(
            List<CompetitorBenchmark> benchmarks,
            List<MarketPositionAssessment> positions)
        {
            var now = _clock.Now;
            var movies = _movieRepo.GetAll();
            var rentals = _rentalRepo.GetAll();
            var opportunities = new List<MarketOpportunity>();

            // --- PriceGap: genres where we're significantly cheaper ---
            foreach (var pos in positions.Where(p => p.PriceGapPercent < -10))
            {
                opportunities.Add(new MarketOpportunity
                {
                    Type = OpportunityType.PriceGap,
                    Title = $"Price advantage in {pos.Genre}",
                    Description = $"Our {pos.Genre} pricing is {Math.Abs(pos.PriceGapPercent):F1}% below market — room to raise prices without losing competitiveness.",
                    Genre = pos.Genre,
                    EstimatedRevenueImpact = Math.Round(Math.Abs(pos.PriceGapPercent) * 5m, 2),
                    ConfidencePercent = 75,
                    DetectedAt = now,
                    ExpiresAt = now.AddDays(30),
                    RecommendedMove = StrategicMove.PremiumPositioning
                });
            }

            // --- DemandSurge: genres with high recent rental velocity ---
            var last30 = rentals.Where(r => (now - r.RentalDate).TotalDays <= 30).ToList();
            var prev30 = rentals.Where(r =>
            {
                double days = (now - r.RentalDate).TotalDays;
                return days > 30 && days <= 60;
            }).ToList();

            var genreVelocity = movies
                .Where(m => m.Genre.HasValue)
                .Select(m => m.Genre.Value)
                .Distinct()
                .Select(g =>
                {
                    var gMovies = movies.Where(m => m.Genre == g).Select(m => m.Id).ToHashSet();
                    int recent = last30.Count(r => gMovies.Contains(r.MovieId));
                    int previous = prev30.Count(r => gMovies.Contains(r.MovieId));
                    return new { Genre = g, Recent = recent, Previous = previous };
                });

            foreach (var gv in genreVelocity.Where(v => v.Recent > v.Previous * 1.5 && v.Recent >= 2))
            {
                opportunities.Add(new MarketOpportunity
                {
                    Type = OpportunityType.DemandSurge,
                    Title = $"Demand surge in {gv.Genre}",
                    Description = $"{gv.Genre} rentals jumped from {gv.Previous} to {gv.Recent} in the last 30 days — capitalize with premium pricing.",
                    Genre = gv.Genre,
                    EstimatedRevenueImpact = Math.Round(gv.Recent * 2.5m, 2),
                    ConfidencePercent = 65,
                    DetectedAt = now,
                    ExpiresAt = now.AddDays(14),
                    RecommendedMove = StrategicMove.PremiumPositioning
                });
            }

            // --- CompetitorWeakness: competitors with low satisfaction ---
            var weakCompetitors = benchmarks
                .GroupBy(b => b.CompetitorName)
                .Where(g => g.Average(b => b.CustomerSatisfaction) < 3.5)
                .Select(g => g.Key)
                .ToList();

            foreach (var comp in weakCompetitors)
            {
                var compGenres = benchmarks
                    .Where(b => b.CompetitorName == comp)
                    .Select(b => b.Genre)
                    .ToList();

                foreach (var genre in compGenres.Take(2))
                {
                    opportunities.Add(new MarketOpportunity
                    {
                        Type = OpportunityType.CompetitorWeakness,
                        Title = $"Exploit {comp} weakness in {genre}",
                        Description = $"{comp} has low customer satisfaction — target their {genre} customers with competitive offers.",
                        Genre = genre,
                        EstimatedRevenueImpact = 15m,
                        ConfidencePercent = 55,
                        DetectedAt = now,
                        ExpiresAt = now.AddDays(60),
                        RecommendedMove = StrategicMove.AggressiveDiscount
                    });
                }
            }

            // --- SeasonalWindow: month-based detection ---
            int month = now.Month;
            if (month == 6 || month == 7 || month == 8) // summer
            {
                opportunities.Add(new MarketOpportunity
                {
                    Type = OpportunityType.SeasonalWindow,
                    Title = "Summer blockbuster season",
                    Description = "Peak rental season — increase Action and SciFi prices while demand is high.",
                    Genre = Genre.Action,
                    EstimatedRevenueImpact = 25m,
                    ConfidencePercent = 80,
                    DetectedAt = now,
                    ExpiresAt = new DateTime(now.Year, 9, 1),
                    RecommendedMove = StrategicMove.PremiumPositioning
                });
            }
            else if (month == 10 || month == 11) // fall horror + holiday
            {
                opportunities.Add(new MarketOpportunity
                {
                    Type = OpportunityType.SeasonalWindow,
                    Title = "Halloween and holiday rental surge",
                    Description = "Horror and family films spike — adjust pricing for seasonal demand.",
                    Genre = Genre.Horror,
                    EstimatedRevenueImpact = 18m,
                    ConfidencePercent = 75,
                    DetectedAt = now,
                    ExpiresAt = new DateTime(now.Year, 12, 1),
                    RecommendedMove = StrategicMove.FlashSale
                });
            }
            else if (month == 12 || month == 1 || month == 2) // winter
            {
                opportunities.Add(new MarketOpportunity
                {
                    Type = OpportunityType.SeasonalWindow,
                    Title = "Winter cozy rental season",
                    Description = "Cold weather drives indoor entertainment — bundle deals on Drama and Comedy.",
                    Genre = Genre.Drama,
                    EstimatedRevenueImpact = 20m,
                    ConfidencePercent = 70,
                    DetectedAt = now,
                    ExpiresAt = new DateTime(now.Year + (month == 12 ? 1 : 0), 3, 1),
                    RecommendedMove = StrategicMove.BundleDefense
                });
            }

            // --- NicheMonopoly: genres where we have titles but competitors have few ---
            foreach (var pos in positions.Where(p => p.OurCatalogCount > p.AvgCompetitorCatalogCount * 1.5))
            {
                opportunities.Add(new MarketOpportunity
                {
                    Type = OpportunityType.NicheMonopoly,
                    Title = $"Catalog dominance in {pos.Genre}",
                    Description = $"We have {pos.OurCatalogCount} titles vs competitor avg of {pos.AvgCompetitorCatalogCount} — premium pricing justified.",
                    Genre = pos.Genre,
                    EstimatedRevenueImpact = Math.Round(pos.OurCatalogCount * 3m, 2),
                    ConfidencePercent = 70,
                    DetectedAt = now,
                    ExpiresAt = null,
                    RecommendedMove = StrategicMove.PremiumPositioning
                });
            }

            // --- GenreGap: genres competitors cover but we don't ---
            var ourGenres = movies.Where(m => m.Genre.HasValue).Select(m => m.Genre.Value).Distinct().ToHashSet();
            var allGenres = Enum.GetValues(typeof(Genre)).Cast<Genre>();
            foreach (var g in allGenres.Where(g => !ourGenres.Contains(g)))
            {
                var compCoverage = benchmarks.Where(b => b.Genre == g).ToList();
                if (compCoverage.Count > 0)
                {
                    opportunities.Add(new MarketOpportunity
                    {
                        Type = OpportunityType.GenreGap,
                        Title = $"Missing genre: {g}",
                        Description = $"Competitors offer {g} titles but we have none — expansion opportunity.",
                        Genre = g,
                        EstimatedRevenueImpact = 10m,
                        ConfidencePercent = 50,
                        DetectedAt = now,
                        ExpiresAt = null,
                        RecommendedMove = StrategicMove.NicheCapture
                    });
                }
            }

            return opportunities
                .OrderByDescending(o => o.EstimatedRevenueImpact)
                .ToList();
        }

        // -------------------------------------------------------------------
        //  4. Threat Detector
        // -------------------------------------------------------------------

        internal List<CompetitiveThreat> DetectThreatsInternal(
            List<CompetitorBenchmark> benchmarks,
            List<MarketPositionAssessment> positions)
        {
            var threats = new List<CompetitiveThreat>();

            // --- Price undercutting threats ---
            foreach (var pos in positions.Where(p => p.PriceGapPercent > 15))
            {
                threats.Add(new CompetitiveThreat
                {
                    Level = pos.PriceGapPercent > 25 ? ThreatLevel.Critical : ThreatLevel.High,
                    Source = "Market pricing",
                    Description = $"We are {pos.PriceGapPercent:F1}% above market in {pos.Genre} — customers may defect.",
                    AffectedGenre = pos.Genre,
                    PotentialRevenueLoss = Math.Round(pos.PriceGapPercent * 3m, 2),
                    CounterMoves = new List<StrategicMove> { StrategicMove.PriceMatch, StrategicMove.AggressiveDiscount },
                    Urgency = pos.PriceGapPercent > 25 ? "Immediate" : "This week"
                });
            }

            // --- Catalog expansion threats: competitors with bigger catalogs ---
            foreach (var pos in positions.Where(p => p.AvgCompetitorCatalogCount > p.OurCatalogCount * 1.3))
            {
                threats.Add(new CompetitiveThreat
                {
                    Level = ThreatLevel.Moderate,
                    Source = "Competitor catalog",
                    Description = $"Competitors average {pos.AvgCompetitorCatalogCount} titles in {pos.Genre} vs our {pos.OurCatalogCount}.",
                    AffectedGenre = pos.Genre,
                    PotentialRevenueLoss = 8m,
                    CounterMoves = new List<StrategicMove> { StrategicMove.NicheCapture, StrategicMove.BundleDefense },
                    Urgency = "This month"
                });
            }

            // --- High-satisfaction competitor threats ---
            var topCompetitors = benchmarks
                .GroupBy(b => b.CompetitorName)
                .Where(g => g.Average(b => b.CustomerSatisfaction) > 4.0)
                .Select(g => g.Key)
                .ToList();

            foreach (var comp in topCompetitors)
            {
                var strongGenres = benchmarks
                    .Where(b => b.CompetitorName == comp && b.CustomerSatisfaction > 4.0)
                    .Select(b => b.Genre)
                    .Distinct()
                    .ToList();

                foreach (var genre in strongGenres.Take(2))
                {
                    threats.Add(new CompetitiveThreat
                    {
                        Level = ThreatLevel.Moderate,
                        Source = comp,
                        Description = $"{comp} has high satisfaction ({benchmarks.First(b => b.CompetitorName == comp && b.Genre == genre).CustomerSatisfaction:F1}/5) in {genre} — quality threat.",
                        AffectedGenre = genre,
                        PotentialRevenueLoss = 12m,
                        CounterMoves = new List<StrategicMove> { StrategicMove.PremiumPositioning, StrategicMove.BundleDefense },
                        Urgency = "Ongoing"
                    });
                }
            }

            // --- Vulnerable positions are inherent threats ---
            foreach (var pos in positions.Where(p => p.Position == MarketPosition.Vulnerable))
            {
                threats.Add(new CompetitiveThreat
                {
                    Level = ThreatLevel.Critical,
                    Source = "Market position",
                    Description = $"Vulnerable position in {pos.Genre} — at risk of losing this segment entirely.",
                    AffectedGenre = pos.Genre,
                    PotentialRevenueLoss = 20m,
                    CounterMoves = new List<StrategicMove>
                    {
                        StrategicMove.AggressiveDiscount,
                        StrategicMove.LossLeader,
                        StrategicMove.FlashSale
                    },
                    Urgency = "Immediate"
                });
            }

            return threats.OrderByDescending(t => t.Level).ToList();
        }

        // -------------------------------------------------------------------
        //  5. Strategy Recommender
        // -------------------------------------------------------------------

        internal List<StrategicRecommendation> GetRecommendationsInternal(
            List<MarketPositionAssessment> positions,
            List<MarketOpportunity> opportunities,
            List<CompetitiveThreat> threats)
        {
            var recs = new List<StrategicRecommendation>();

            // From opportunities
            foreach (var opp in opportunities.Take(5))
            {
                recs.Add(new StrategicRecommendation
                {
                    Move = opp.RecommendedMove,
                    Title = $"Capitalize: {opp.Title}",
                    Rationale = opp.Description,
                    TargetGenre = opp.Genre,
                    ExpectedRevenueChange = opp.EstimatedRevenueImpact,
                    ConfidencePercent = opp.ConfidencePercent,
                    Implementation = DescribeMoveImplementation(opp.RecommendedMove, opp.Genre),
                    RiskLevel = ThreatLevel.Low
                });
            }

            // From threats
            foreach (var threat in threats.Where(t => t.Level >= ThreatLevel.High).Take(3))
            {
                var move = threat.CounterMoves.FirstOrDefault();
                recs.Add(new StrategicRecommendation
                {
                    Move = move,
                    Title = $"Defend: {threat.AffectedGenre} against {threat.Source}",
                    Rationale = threat.Description,
                    TargetGenre = threat.AffectedGenre,
                    ExpectedRevenueChange = -threat.PotentialRevenueLoss * 0.5m,
                    ConfidencePercent = 60,
                    Implementation = DescribeMoveImplementation(move, threat.AffectedGenre),
                    RiskLevel = threat.Level
                });
            }

            // Trailing positions
            foreach (var pos in positions.Where(p => p.Position == MarketPosition.Trailing).Take(2))
            {
                recs.Add(new StrategicRecommendation
                {
                    Move = StrategicMove.PriceMatch,
                    Title = $"Recover: match market pricing in {pos.Genre}",
                    Rationale = $"Currently {Math.Abs(pos.PriceGapPercent):F1}% off market — aligning prices could recapture share.",
                    TargetGenre = pos.Genre,
                    ExpectedRevenueChange = Math.Round(Math.Abs(pos.PriceGapPercent) * 2m, 2),
                    ConfidencePercent = 55,
                    Implementation = $"Adjust {pos.Genre} daily rates to ~${pos.MarketAvgPrice:F2} per day.",
                    RiskLevel = ThreatLevel.Moderate
                });
            }

            return recs
                .OrderByDescending(r => r.ExpectedRevenueChange)
                .ToList();
        }

        private static string DescribeMoveImplementation(StrategicMove move, Genre? genre)
        {
            string genreName = genre?.ToString() ?? "all genres";
            switch (move)
            {
                case StrategicMove.AggressiveDiscount:
                    return $"Apply 20-30% discount on {genreName} for 2 weeks to capture competitor customers.";
                case StrategicMove.PremiumPositioning:
                    return $"Raise {genreName} rates 10-15% and emphasize catalog quality and exclusivity.";
                case StrategicMove.BundleDefense:
                    return $"Create multi-title bundle deals for {genreName} to increase per-transaction value.";
                case StrategicMove.NicheCapture:
                    return $"Expand {genreName} catalog and launch targeted marketing to underserved segment.";
                case StrategicMove.PriceMatch:
                    return $"Align {genreName} pricing with market average to eliminate price as a defection reason.";
                case StrategicMove.FlashSale:
                    return $"Run a 48-hour flash sale on {genreName} to drive volume and engagement.";
                case StrategicMove.LossLeader:
                    return $"Price select {genreName} titles below cost to drive store traffic and cross-sell.";
                default:
                    return $"Implement strategic adjustment for {genreName}.";
            }
        }

        // -------------------------------------------------------------------
        //  6. Health Scorer
        // -------------------------------------------------------------------

        internal CompetitiveHealthScore ComputeHealth(
            List<MarketPositionAssessment> positions,
            List<MarketOpportunity> opportunities,
            List<CompetitiveThreat> threats)
        {
            // Pricing Strength: higher when we're at or below market
            int pricingStrength = 50;
            if (positions.Count > 0)
            {
                decimal avgGap = positions.Average(p => p.PriceGapPercent);
                // Negative gap (cheaper) is good; positive gap (expensive) is bad
                pricingStrength = Clamp((int)(65 - avgGap * 2), 0, 100);
            }

            // Catalog Coverage: based on how our catalog compares
            int catalogCoverage = 50;
            if (positions.Count > 0)
            {
                double avgRatio = positions.Average(p =>
                    p.AvgCompetitorCatalogCount > 0
                        ? (double)p.OurCatalogCount / p.AvgCompetitorCatalogCount
                        : 1.0);
                catalogCoverage = Clamp((int)(avgRatio * 60), 0, 100);
            }

            // Opportunity Capture: more high-confidence opportunities = better
            int oppCapture = opportunities.Count == 0
                ? 30
                : Clamp(30 + opportunities.Count(o => o.ConfidencePercent >= 60) * 10, 0, 100);

            // Threat Resilience: fewer/lower threats = better
            int threatResilience = 80;
            int critCount = threats.Count(t => t.Level == ThreatLevel.Critical);
            int highCount = threats.Count(t => t.Level == ThreatLevel.High);
            threatResilience = Clamp(80 - critCount * 20 - highCount * 10, 0, 100);

            int overall = (pricingStrength * 30 + catalogCoverage * 25 +
                          oppCapture * 20 + threatResilience * 25) / 100;
            overall = Clamp(overall, 0, 100);

            return new CompetitiveHealthScore
            {
                Overall = overall,
                PricingStrength = pricingStrength,
                CatalogCoverage = catalogCoverage,
                OpportunityCapture = oppCapture,
                ThreatResilience = threatResilience,
                Grade = ScoreToGrade(overall)
            };
        }

        private static string ScoreToGrade(int score)
        {
            if (score >= 90) return "A+";
            if (score >= 85) return "A";
            if (score >= 80) return "A-";
            if (score >= 75) return "B+";
            if (score >= 70) return "B";
            if (score >= 65) return "B-";
            if (score >= 60) return "C+";
            if (score >= 55) return "C";
            if (score >= 50) return "C-";
            if (score >= 45) return "D+";
            if (score >= 40) return "D";
            if (score >= 35) return "D-";
            return "F";
        }

        // -------------------------------------------------------------------
        //  7. Insight Generator
        // -------------------------------------------------------------------

        internal List<string> GenerateInsights(
            List<MarketPositionAssessment> positions,
            List<MarketOpportunity> opportunities,
            List<CompetitiveThreat> threats,
            CompetitiveHealthScore health)
        {
            var insights = new List<string>();

            // Overall health insight
            insights.Add($"Competitive health score is {health.Overall}/100 ({health.Grade}) — " +
                (health.Overall >= 70
                    ? "strong market position overall."
                    : health.Overall >= 50
                        ? "adequate position but room for improvement."
                        : "below average — strategic intervention needed."));

            // Position distribution
            int leaderCount = positions.Count(p => p.Position == MarketPosition.Leader || p.Position == MarketPosition.Competitive);
            int trailingCount = positions.Count(p => p.Position == MarketPosition.Trailing || p.Position == MarketPosition.Vulnerable);
            if (positions.Count > 0)
            {
                insights.Add($"Market position: leading/competitive in {leaderCount} genre(s), trailing/vulnerable in {trailingCount}.");
            }

            // Top opportunity
            var topOpp = opportunities.FirstOrDefault();
            if (topOpp != null)
            {
                insights.Add($"Biggest opportunity: {topOpp.Title} — est. ${topOpp.EstimatedRevenueImpact:F2} revenue impact at {topOpp.ConfidencePercent}% confidence.");
            }

            // Threat summary
            int critThreats = threats.Count(t => t.Level >= ThreatLevel.High);
            if (critThreats > 0)
            {
                insights.Add($"⚠ {critThreats} high/critical threat(s) detected requiring attention.");
            }
            else
            {
                insights.Add("No critical threats detected — competitive landscape is stable.");
            }

            // Pricing insight
            if (positions.Count > 0)
            {
                var cheapest = positions.OrderBy(p => p.PriceGapPercent).FirstOrDefault();
                var priciest = positions.OrderByDescending(p => p.PriceGapPercent).FirstOrDefault();
                if (cheapest != null && priciest != null && cheapest.Genre != priciest.Genre)
                {
                    insights.Add($"Pricing range: {cheapest.Genre} is {Math.Abs(cheapest.PriceGapPercent):F1}% {(cheapest.PriceGapPercent < 0 ? "below" : "above")} market; {priciest.Genre} is {Math.Abs(priciest.PriceGapPercent):F1}% {(priciest.PriceGapPercent < 0 ? "below" : "above")} market.");
                }
            }

            return insights;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
