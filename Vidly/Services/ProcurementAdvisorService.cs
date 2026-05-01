using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Catalog Procurement Advisor — autonomous movie acquisition recommendation engine.
    /// Analyzes rental demand patterns, identifies genre supply gaps, forecasts ROI for
    /// potential acquisitions, and produces budget allocation recommendations.
    /// </summary>
    public class ProcurementAdvisorService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;
        private readonly ProcurementConfig _config;

        public ProcurementAdvisorService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock,
            ProcurementConfig config = null)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _movieRepository = movieRepository;
            _rentalRepository = rentalRepository;
            _customerRepository = customerRepository;
            _clock = clock;
            _config = config ?? new ProcurementConfig();
        }

        /// <summary>
        /// Generate a full procurement advisory report.
        /// </summary>
        public ProcurementReport Analyze(decimal? budget = null, BudgetAllocationStrategy strategy = BudgetAllocationStrategy.Balanced)
        {
            var now = _clock.Now;
            var allMovies = _movieRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();
            var windowStart = now.AddDays(-_config.AnalysisWindowDays);

            var recentRentals = allRentals.Where(r => r.RentalDate >= windowStart).ToList();

            // 1. Build genre supply profiles
            var supplyProfiles = BuildSupplyProfiles(allMovies, recentRentals, allRentals, now);

            // 2. Detect demand signals
            var signals = DetectDemandSignals(supplyProfiles, recentRentals, allRentals, now);

            // 3. Generate acquisition candidates
            var candidates = GenerateCandidates(supplyProfiles, signals, now);

            // 4. Budget allocation
            var totalBudget = budget ?? candidates.Sum(c => c.EstimatedAcquisitionCost);
            var budgetPlan = AllocateBudget(candidates, supplyProfiles, totalBudget, strategy);

            // 5. Generate insights
            var insights = GenerateInsights(supplyProfiles, signals, candidates);

            // 6. Health score
            var healthScore = ComputeHealthScore(supplyProfiles, signals);

            var report = new ProcurementReport
            {
                GeneratedAt = now,
                HealthScore = healthScore,
                HealthVerdict = GetVerdict(healthScore),
                SupplyProfiles = supplyProfiles,
                TotalCatalogSize = allMovies.Count,
                UnderservedGenres = supplyProfiles.Count(p => p.IsUnderserved),
                Signals = signals,
                Candidates = candidates.Take(_config.MaxRecommendations).ToList(),
                TotalBudgetRecommended = totalBudget,
                BudgetPlan = budgetPlan,
                Strategy = strategy,
                Insights = insights,
                TotalProjectedRoi = candidates.Any() ? candidates.Average(c => c.ProjectedRoi) : 0,
                TotalTitlesToAcquire = candidates.Sum(c => c.RecommendedCopies),
                AveragePaybackDays = candidates.Any() ? (int)candidates.Average(c => c.PaybackDays) : 0
            };

            return report;
        }

        // ─── Supply Analysis ─────────────────────────────────────────────────

        private List<GenreSupplyProfile> BuildSupplyProfiles(
            IReadOnlyList<Movie> allMovies,
            List<Rental> recentRentals,
            IReadOnlyList<Rental> allRentals,
            DateTime now)
        {
            var profiles = new List<GenreSupplyProfile>();
            var genres = Enum.GetValues(typeof(Genre)).Cast<Genre>().ToList();
            var totalMovies = Math.Max(1, allMovies.Count);
            var totalRecentRentals = Math.Max(1, recentRentals.Count);

            // For growth rate, split window in half
            var halfWindow = _config.AnalysisWindowDays / 2;
            var midpoint = now.AddDays(-halfWindow);

            foreach (var genre in genres)
            {
                var genreMovies = allMovies.Where(m => m.Genre == genre).ToList();
                var genreRentals = recentRentals.Where(r =>
                {
                    var movie = allMovies.FirstOrDefault(m => m.Id == r.MovieId);
                    return movie != null && movie.Genre == genre;
                }).ToList();

                var titleCount = genreMovies.Count;
                var rentalCount = genreRentals.Count;
                var rentalsPerTitle = titleCount > 0 ? (double)rentalCount / titleCount : 0;

                // Growth rate: compare first half vs second half
                var firstHalf = genreRentals.Count(r => r.RentalDate < midpoint);
                var secondHalf = genreRentals.Count(r => r.RentalDate >= midpoint);
                var growthRate = firstHalf > 0 ? ((double)secondHalf - firstHalf) / firstHalf : (secondHalf > 0 ? 1.0 : 0);

                var shareOfCatalog = (double)titleCount / totalMovies;
                var shareOfDemand = (double)rentalCount / totalRecentRentals;
                var supplyDemandRatio = shareOfDemand > 0 ? shareOfCatalog / shareOfDemand : (shareOfCatalog > 0 ? 2.0 : 1.0);

                // Supply adequacy: 1.0 means well-served, <0.7 means underserved
                var adequacy = Math.Min(1.0, supplyDemandRatio / 1.0);

                profiles.Add(new GenreSupplyProfile
                {
                    Genre = genre,
                    TitleCount = titleCount,
                    RecentRentals = rentalCount,
                    RentalsPerTitle = Math.Round(rentalsPerTitle, 2),
                    SupplyAdequacy = Math.Round(adequacy, 3),
                    DemandGrowthRate = Math.Round(growthRate, 3),
                    ShareOfCatalog = Math.Round(shareOfCatalog, 3),
                    ShareOfDemand = Math.Round(shareOfDemand, 3),
                    SupplyDemandRatio = Math.Round(supplyDemandRatio, 3),
                    IsUnderserved = adequacy < _config.SupplyAdequacyTarget
                });
            }

            return profiles;
        }

        // ─── Demand Signal Detection ─────────────────────────────────────────

        private List<DemandSignal> DetectDemandSignals(
            List<GenreSupplyProfile> profiles,
            List<Rental> recentRentals,
            IReadOnlyList<Rental> allRentals,
            DateTime now)
        {
            var signals = new List<DemandSignal>();

            foreach (var profile in profiles)
            {
                // High velocity signal
                if (profile.RentalsPerTitle > 5)
                {
                    signals.Add(new DemandSignal
                    {
                        Type = DemandSignalType.HighVelocity,
                        Genre = profile.Genre,
                        Strength = Math.Min(1.0, profile.RentalsPerTitle / 10.0),
                        Description = $"{profile.Genre} titles averaging {profile.RentalsPerTitle:F1} rentals each — high demand pressure",
                        DetectedAt = now,
                        Evidence = new Dictionary<string, double>
                        {
                            { "rentals_per_title", profile.RentalsPerTitle },
                            { "total_rentals", profile.RecentRentals }
                        }
                    });
                }

                // Growing trend signal
                if (profile.DemandGrowthRate > _config.DemandGrowthThreshold)
                {
                    signals.Add(new DemandSignal
                    {
                        Type = DemandSignalType.GrowingTrend,
                        Genre = profile.Genre,
                        Strength = Math.Min(1.0, profile.DemandGrowthRate),
                        Description = $"{profile.Genre} demand growing {profile.DemandGrowthRate:P0} over analysis window",
                        DetectedAt = now,
                        Evidence = new Dictionary<string, double>
                        {
                            { "growth_rate", profile.DemandGrowthRate }
                        }
                    });
                }

                // Genre gap signal
                if (profile.IsUnderserved && profile.RecentRentals >= _config.MinRentalsForSignal)
                {
                    signals.Add(new DemandSignal
                    {
                        Type = DemandSignalType.GenreGap,
                        Genre = profile.Genre,
                        Strength = Math.Min(1.0, 1.0 - profile.SupplyAdequacy),
                        Description = $"{profile.Genre} supply adequacy at {profile.SupplyAdequacy:P0} — significant gap detected",
                        DetectedAt = now,
                        Evidence = new Dictionary<string, double>
                        {
                            { "supply_adequacy", profile.SupplyAdequacy },
                            { "demand_share", profile.ShareOfDemand },
                            { "catalog_share", profile.ShareOfCatalog }
                        }
                    });
                }

                // Underserved customer segments
                if (profile.ShareOfDemand > profile.ShareOfCatalog * 1.5 && profile.RecentRentals >= _config.MinRentalsForSignal)
                {
                    signals.Add(new DemandSignal
                    {
                        Type = DemandSignalType.UnderservedSegment,
                        Genre = profile.Genre,
                        Strength = Math.Min(1.0, (profile.ShareOfDemand / Math.Max(0.01, profile.ShareOfCatalog)) - 1.0),
                        Description = $"{profile.Genre} fans represent {profile.ShareOfDemand:P0} of demand but only {profile.ShareOfCatalog:P0} of catalog",
                        DetectedAt = now,
                        Evidence = new Dictionary<string, double>
                        {
                            { "demand_share", profile.ShareOfDemand },
                            { "catalog_share", profile.ShareOfCatalog }
                        }
                    });
                }
            }

            // Seasonal surge detection (compare current month's genre mix to overall)
            var currentMonth = now.Month;
            var lastMonthRentals = recentRentals.Where(r => r.RentalDate >= now.AddDays(-30)).ToList();
            if (lastMonthRentals.Count >= 5)
            {
                var allMovies = _movieRepository.GetAll();
                var monthGenreCounts = lastMonthRentals
                    .GroupBy(r => allMovies.FirstOrDefault(m => m.Id == r.MovieId)?.Genre)
                    .Where(g => g.Key.HasValue)
                    .ToDictionary(g => g.Key.Value, g => g.Count());

                var totalMonthRentals = Math.Max(1, lastMonthRentals.Count);
                foreach (var kvp in monthGenreCounts)
                {
                    var monthShare = (double)kvp.Value / totalMonthRentals;
                    var profile = profiles.FirstOrDefault(p => p.Genre == kvp.Key);
                    if (profile != null && monthShare > profile.ShareOfDemand * 1.5 && kvp.Value >= 3)
                    {
                        signals.Add(new DemandSignal
                        {
                            Type = DemandSignalType.SeasonalSurge,
                            Genre = kvp.Key,
                            Strength = Math.Min(1.0, monthShare / Math.Max(0.01, profile.ShareOfDemand) - 1.0),
                            Description = $"{kvp.Key} experiencing seasonal surge — {monthShare:P0} of recent activity vs {profile.ShareOfDemand:P0} baseline",
                            DetectedAt = now,
                            Evidence = new Dictionary<string, double>
                            {
                                { "month_share", monthShare },
                                { "baseline_share", profile.ShareOfDemand }
                            }
                        });
                    }
                }
            }

            return signals.OrderByDescending(s => s.Strength).ToList();
        }

        // ─── Candidate Generation ────────────────────────────────────────────

        private List<AcquisitionCandidate> GenerateCandidates(
            List<GenreSupplyProfile> profiles,
            List<DemandSignal> signals,
            DateTime now)
        {
            var candidates = new List<AcquisitionCandidate>();
            var genreSignals = signals.GroupBy(s => s.Genre).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var profile in profiles.Where(p => p.IsUnderserved || p.DemandGrowthRate > _config.DemandGrowthThreshold))
            {
                var genreSigs = genreSignals.ContainsKey(profile.Genre) ? genreSignals[profile.Genre] : new List<DemandSignal>();
                if (!genreSigs.Any() && !profile.IsUnderserved) continue;

                // Calculate recommended copies
                var copies = CalculateRecommendedCopies(profile);

                // ROI projection
                var acquisitionCost = copies * _config.AcquisitionCostPerTitle;
                var projectedMonthlyRentals = profile.RentalsPerTitle * copies * 1.2; // expect slight uplift from freshness
                var projectedMonthlyRevenue = (decimal)projectedMonthlyRentals * _config.DefaultDailyRate * 3; // avg 3 day rental
                var roi = acquisitionCost > 0 ? (projectedMonthlyRevenue * 3 - acquisitionCost) / acquisitionCost : 0;
                var paybackDays = projectedMonthlyRevenue > 0 ? (int)(acquisitionCost / projectedMonthlyRevenue * 30) : 365;

                // Urgency
                var maxSignalStrength = genreSigs.Any() ? genreSigs.Max(s => s.Strength) : 0;
                var urgency = DetermineUrgency(profile, maxSignalStrength);

                // Confidence
                var confidence = ComputeConfidence(profile, genreSigs);

                // Rationale
                var rationale = new List<string>();
                if (profile.IsUnderserved)
                    rationale.Add($"Genre is underserved (adequacy: {profile.SupplyAdequacy:P0})");
                if (profile.DemandGrowthRate > _config.DemandGrowthThreshold)
                    rationale.Add($"Demand growing at {profile.DemandGrowthRate:P0}");
                if (profile.RentalsPerTitle > 5)
                    rationale.Add($"High per-title velocity: {profile.RentalsPerTitle:F1} rentals/title");
                foreach (var sig in genreSigs.Where(s => s.Type == DemandSignalType.SeasonalSurge))
                    rationale.Add("Seasonal demand surge detected");

                candidates.Add(new AcquisitionCandidate
                {
                    Genre = profile.Genre,
                    RecommendedCopies = copies,
                    Urgency = urgency,
                    ConfidenceScore = Math.Round(confidence, 3),
                    EstimatedAcquisitionCost = acquisitionCost,
                    ProjectedMonthlyRevenue = Math.Round(projectedMonthlyRevenue, 2),
                    ProjectedRoi = Math.Round(roi, 3),
                    PaybackDays = paybackDays,
                    Rationale = rationale,
                    SupportingSignals = genreSigs
                });
            }

            return candidates.OrderByDescending(c => c.ProjectedRoi).ThenBy(c => c.Urgency).ToList();
        }

        private int CalculateRecommendedCopies(GenreSupplyProfile profile)
        {
            // Base: how many titles to close the gap
            var gapRatio = 1.0 - profile.SupplyAdequacy;
            var baseCopies = (int)Math.Ceiling(gapRatio * 5); // up to 5 for fully underserved

            // Boost for growth
            if (profile.DemandGrowthRate > 0.3) baseCopies += 2;
            else if (profile.DemandGrowthRate > _config.DemandGrowthThreshold) baseCopies += 1;

            // Boost for high velocity
            if (profile.RentalsPerTitle > 8) baseCopies += 2;
            else if (profile.RentalsPerTitle > 5) baseCopies += 1;

            return Math.Max(1, Math.Min(baseCopies, 10));
        }

        private ProcurementUrgency DetermineUrgency(GenreSupplyProfile profile, double maxSignalStrength)
        {
            var score = maxSignalStrength * 0.4 + (1.0 - profile.SupplyAdequacy) * 0.3 + Math.Min(1.0, profile.DemandGrowthRate) * 0.3;
            if (score > 0.8) return ProcurementUrgency.Critical;
            if (score > 0.6) return ProcurementUrgency.High;
            if (score > 0.4) return ProcurementUrgency.Medium;
            if (score > 0.2) return ProcurementUrgency.Low;
            return ProcurementUrgency.Monitor;
        }

        private double ComputeConfidence(GenreSupplyProfile profile, List<DemandSignal> signals)
        {
            var base_ = 0.5;
            // More signals = more confidence
            base_ += Math.Min(0.3, signals.Count * 0.1);
            // More data = more confidence
            if (profile.RecentRentals > 10) base_ += 0.1;
            if (profile.RecentRentals > 20) base_ += 0.1;
            return Math.Min(1.0, base_);
        }

        // ─── Budget Allocation ───────────────────────────────────────────────

        private List<BudgetAllocation> AllocateBudget(
            List<AcquisitionCandidate> candidates,
            List<GenreSupplyProfile> profiles,
            decimal totalBudget,
            BudgetAllocationStrategy strategy)
        {
            if (!candidates.Any() || totalBudget <= 0)
                return new List<BudgetAllocation>();

            var allocations = new List<BudgetAllocation>();

            switch (strategy)
            {
                case BudgetAllocationStrategy.RoiMaximized:
                    allocations = AllocateByRoi(candidates, totalBudget);
                    break;
                case BudgetAllocationStrategy.DiversityFocused:
                    allocations = AllocateByDiversity(candidates, profiles, totalBudget);
                    break;
                case BudgetAllocationStrategy.DemandDriven:
                    allocations = AllocateByDemand(candidates, profiles, totalBudget);
                    break;
                default: // Balanced
                    allocations = AllocateBalanced(candidates, profiles, totalBudget);
                    break;
            }

            return allocations;
        }

        private List<BudgetAllocation> AllocateByRoi(List<AcquisitionCandidate> candidates, decimal totalBudget)
        {
            var allocations = new List<BudgetAllocation>();
            var remaining = totalBudget;

            foreach (var c in candidates.OrderByDescending(x => x.ProjectedRoi))
            {
                if (remaining <= 0) break;
                var alloc = Math.Min(remaining, c.EstimatedAcquisitionCost);
                var titles = Math.Max(1, (int)(alloc / _config.AcquisitionCostPerTitle));
                allocations.Add(new BudgetAllocation
                {
                    Genre = c.Genre,
                    AllocatedBudget = alloc,
                    AllocationPercent = totalBudget > 0 ? (double)(alloc / totalBudget) * 100 : 0,
                    TitlesToAcquire = titles,
                    Justification = $"Highest projected ROI: {c.ProjectedRoi:P0}"
                });
                remaining -= alloc;
            }

            return allocations;
        }

        private List<BudgetAllocation> AllocateByDiversity(List<AcquisitionCandidate> candidates, List<GenreSupplyProfile> profiles, decimal totalBudget)
        {
            // Equal split across underserved genres
            var underserved = candidates.Where(c => profiles.Any(p => p.Genre == c.Genre && p.IsUnderserved)).ToList();
            if (!underserved.Any()) underserved = candidates;

            var perGenre = totalBudget / Math.Max(1, underserved.Count);
            return underserved.Select(c => new BudgetAllocation
            {
                Genre = c.Genre,
                AllocatedBudget = perGenre,
                AllocationPercent = underserved.Count > 0 ? 100.0 / underserved.Count : 0,
                TitlesToAcquire = Math.Max(1, (int)(perGenre / _config.AcquisitionCostPerTitle)),
                Justification = "Equal allocation for catalog diversity"
            }).ToList();
        }

        private List<BudgetAllocation> AllocateByDemand(List<AcquisitionCandidate> candidates, List<GenreSupplyProfile> profiles, decimal totalBudget)
        {
            var totalDemand = candidates.Sum(c =>
            {
                var p = profiles.FirstOrDefault(x => x.Genre == c.Genre);
                return p?.RecentRentals ?? 1;
            });

            return candidates.Select(c =>
            {
                var p = profiles.FirstOrDefault(x => x.Genre == c.Genre);
                var share = totalDemand > 0 ? (double)(p?.RecentRentals ?? 1) / totalDemand : 0;
                var alloc = totalBudget * (decimal)share;
                return new BudgetAllocation
                {
                    Genre = c.Genre,
                    AllocatedBudget = Math.Round(alloc, 2),
                    AllocationPercent = share * 100,
                    TitlesToAcquire = Math.Max(1, (int)(alloc / _config.AcquisitionCostPerTitle)),
                    Justification = $"Proportional to demand ({p?.RecentRentals ?? 0} recent rentals)"
                };
            }).ToList();
        }

        private List<BudgetAllocation> AllocateBalanced(List<AcquisitionCandidate> candidates, List<GenreSupplyProfile> profiles, decimal totalBudget)
        {
            // Weighted by: 40% ROI, 30% gap severity, 30% growth
            var scored = candidates.Select(c =>
            {
                var p = profiles.FirstOrDefault(x => x.Genre == c.Genre);
                var roiScore = Math.Min(1.0, (double)c.ProjectedRoi);
                var gapScore = p != null ? 1.0 - p.SupplyAdequacy : 0;
                var growthScore = p != null ? Math.Min(1.0, p.DemandGrowthRate) : 0;
                var composite = roiScore * 0.4 + gapScore * 0.3 + growthScore * 0.3;
                return new { Candidate = c, Score = composite };
            }).ToList();

            var totalScore = scored.Sum(s => s.Score);

            return scored.Select(s =>
            {
                var share = totalScore > 0 ? s.Score / totalScore : 0;
                var alloc = totalBudget * (decimal)share;
                return new BudgetAllocation
                {
                    Genre = s.Candidate.Genre,
                    AllocatedBudget = Math.Round(alloc, 2),
                    AllocationPercent = share * 100,
                    TitlesToAcquire = Math.Max(1, (int)(alloc / _config.AcquisitionCostPerTitle)),
                    Justification = $"Balanced score: {s.Score:F2} (ROI + gap + growth)"
                };
            }).ToList();
        }

        // ─── Insights ────────────────────────────────────────────────────────

        private List<ProcurementInsight> GenerateInsights(
            List<GenreSupplyProfile> profiles,
            List<DemandSignal> signals,
            List<AcquisitionCandidate> candidates)
        {
            var insights = new List<ProcurementInsight>();

            // Concentration risk
            var topGenre = profiles.OrderByDescending(p => p.ShareOfCatalog).FirstOrDefault();
            if (topGenre != null && topGenre.ShareOfCatalog > 0.35)
            {
                insights.Add(new ProcurementInsight
                {
                    Category = "Risk",
                    Title = "Catalog Concentration Risk",
                    Description = $"{topGenre.Genre} represents {topGenre.ShareOfCatalog:P0} of catalog — consider diversification",
                    Impact = topGenre.ShareOfCatalog
                });
            }

            // Quick wins
            var quickWins = candidates.Where(c => c.PaybackDays < 45 && c.ConfidenceScore > 0.7).ToList();
            if (quickWins.Any())
            {
                insights.Add(new ProcurementInsight
                {
                    Category = "Opportunity",
                    Title = "Quick-Win Acquisitions Available",
                    Description = $"{quickWins.Count} genre(s) show <45 day payback with high confidence: {string.Join(", ", quickWins.Select(q => q.Genre))}",
                    Impact = 0.8
                });
            }

            // Growth wave
            var growingGenres = profiles.Where(p => p.DemandGrowthRate > 0.3).ToList();
            if (growingGenres.Any())
            {
                insights.Add(new ProcurementInsight
                {
                    Category = "Trend",
                    Title = "Demand Growth Wave",
                    Description = $"{growingGenres.Count} genre(s) showing 30%+ growth: {string.Join(", ", growingGenres.Select(g => g.Genre))}",
                    Impact = growingGenres.Max(g => g.DemandGrowthRate)
                });
            }

            // Stagnant genres
            var stagnant = profiles.Where(p => p.RecentRentals == 0 && p.TitleCount > 0).ToList();
            if (stagnant.Any())
            {
                insights.Add(new ProcurementInsight
                {
                    Category = "Warning",
                    Title = "Zero-Demand Genres",
                    Description = $"{stagnant.Count} genre(s) have titles but zero recent rentals: {string.Join(", ", stagnant.Select(s => s.Genre))} — avoid acquisition",
                    Impact = 0.6
                });
            }

            // Supply-demand imbalance
            var imbalanced = profiles.Where(p => p.SupplyDemandRatio > 2.0 && p.TitleCount > 2).ToList();
            if (imbalanced.Any())
            {
                insights.Add(new ProcurementInsight
                {
                    Category = "Efficiency",
                    Title = "Oversupplied Genres",
                    Description = $"{imbalanced.Count} genre(s) are significantly oversupplied: {string.Join(", ", imbalanced.Select(i => i.Genre))} — reallocate budget elsewhere",
                    Impact = 0.5
                });
            }

            return insights.OrderByDescending(i => i.Impact).ToList();
        }

        // ─── Health Score ────────────────────────────────────────────────────

        private int ComputeHealthScore(List<GenreSupplyProfile> profiles, List<DemandSignal> signals)
        {
            var score = 100;

            // Penalize underserved genres
            var underservedCount = profiles.Count(p => p.IsUnderserved);
            score -= underservedCount * 8;

            // Penalize high-strength unmet signals
            var criticalSignals = signals.Count(s => s.Strength > 0.7);
            score -= criticalSignals * 5;

            // Penalize zero-demand genres with titles (waste)
            var wasted = profiles.Count(p => p.RecentRentals == 0 && p.TitleCount > 0);
            score -= wasted * 4;

            // Bonus for diversity
            var activeGenres = profiles.Count(p => p.RecentRentals > 0);
            if (activeGenres >= 8) score += 5;

            return Math.Max(0, Math.Min(100, score));
        }

        private string GetVerdict(int score)
        {
            if (score >= 85) return "Excellent — catalog well-aligned with demand";
            if (score >= 70) return "Good — minor gaps to address";
            if (score >= 55) return "Fair — several procurement opportunities";
            if (score >= 40) return "Needs Attention — significant supply-demand mismatches";
            return "Critical — major catalog gaps require immediate action";
        }
    }
}
