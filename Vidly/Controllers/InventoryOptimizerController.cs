using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;

namespace Vidly.Controllers
{
    public class InventoryOptimizerController : Controller
    {
        private static readonly Random _rng = new Random(42);

        private static readonly string[] MovieTitles = {
            "The Matrix", "Inception", "Pulp Fiction", "The Godfather", "Interstellar",
            "Fight Club", "Parasite", "The Dark Knight", "Forrest Gump", "Gladiator",
            "Titanic", "Avatar", "Jurassic Park", "The Shawshank Redemption", "Goodfellas",
            "Alien", "Blade Runner", "Mad Max: Fury Road", "The Lion King", "Toy Story"
        };

        private static readonly string[] Genres = {
            "Sci-Fi", "Sci-Fi", "Crime", "Crime", "Sci-Fi",
            "Drama", "Thriller", "Action", "Drama", "Action",
            "Romance", "Sci-Fi", "Adventure", "Drama", "Crime",
            "Horror", "Sci-Fi", "Action", "Animation", "Animation"
        };

        public ActionResult Index()
        {
            var result = GenerateAnalysis();
            return View(result);
        }

        private InventoryOptimizerResult GenerateAnalysis()
        {
            var rng = new Random(DateTime.Now.DayOfYear);
            var titles = new List<TitleAnalysis>();

            for (int i = 0; i < MovieTitles.Length; i++)
            {
                var copies = rng.Next(2, 12);
                var avgRentals = Math.Round(rng.NextDouble() * copies * 1.2, 1);
                var utilization = Math.Min(100, Math.Round((avgRentals / copies) * 100, 1));
                var demandScore = (int)Math.Min(100, utilization * 0.6 + rng.Next(0, 40));
                var revenuePerCopy = Math.Round((decimal)(avgRentals * (2.5 + rng.NextDouble() * 2)), 2);

                Recommendation rec;
                RecommendationReason reason;

                if (demandScore > 80 && utilization > 85)
                {
                    rec = Recommendation.AcquireMore;
                    reason = RecommendationReason.HighDemand;
                }
                else if (utilization < 25)
                {
                    rec = Recommendation.Retire;
                    reason = RecommendationReason.LowUtilization;
                }
                else if (utilization < 40 && revenuePerCopy < 4m)
                {
                    rec = Recommendation.MarkDown;
                    reason = RecommendationReason.RevenueDeclining;
                }
                else if (demandScore > 60 && utilization < 50)
                {
                    rec = Recommendation.Promote;
                    reason = RecommendationReason.GenreGap;
                }
                else
                {
                    rec = Recommendation.Hold;
                    reason = RecommendationReason.SeasonalTrend;
                }

                titles.Add(new TitleAnalysis
                {
                    MovieId = i + 1,
                    Title = MovieTitles[i],
                    Genre = Genres[i],
                    CopiesOwned = copies,
                    AvgRentalsPerWeek = avgRentals,
                    DemandScore = demandScore,
                    UtilizationRate = utilization,
                    RevenuePerCopy = revenuePerCopy,
                    Recommendation = rec,
                    Reason = reason
                });
            }

            var forecasts = new List<DemandForecast>();
            var baseRentals = titles.Sum(t => t.AvgRentalsPerWeek);
            for (int w = 1; w <= 4; w++)
            {
                var predicted = baseRentals * (1 + (rng.NextDouble() - 0.3) * 0.1);
                forecasts.Add(new DemandForecast
                {
                    WeekNumber = w,
                    WeekLabel = DateTime.Now.AddDays(w * 7).ToString("MMM dd"),
                    PredictedRentals = Math.Round(predicted, 1),
                    ConfidenceLow = Math.Round(predicted * 0.85, 1),
                    ConfidenceHigh = Math.Round(predicted * 1.15, 1)
                });
            }

            var actions = new List<OptimizationAction>();
            foreach (var t in titles.Where(t => t.Recommendation == Recommendation.AcquireMore))
            {
                actions.Add(new OptimizationAction
                {
                    Priority = ActionPriority.High,
                    Description = $"Acquire 2-3 more copies of \"{t.Title}\" - demand exceeds supply",
                    MovieTitle = t.Title,
                    EstimatedRevenueImpact = Math.Round(t.RevenuePerCopy * 2.5m, 2)
                });
            }
            foreach (var t in titles.Where(t => t.Recommendation == Recommendation.Retire))
            {
                actions.Add(new OptimizationAction
                {
                    Priority = ActionPriority.Medium,
                    Description = $"Retire {t.CopiesOwned - 1} copies of \"{t.Title}\" - very low utilization",
                    MovieTitle = t.Title,
                    EstimatedRevenueImpact = Math.Round(t.CopiesOwned * 1.5m, 2)
                });
            }
            foreach (var t in titles.Where(t => t.Recommendation == Recommendation.Promote))
            {
                actions.Add(new OptimizationAction
                {
                    Priority = ActionPriority.Low,
                    Description = $"Promote \"{t.Title}\" - hidden demand detected, needs visibility",
                    MovieTitle = t.Title,
                    EstimatedRevenueImpact = Math.Round(t.RevenuePerCopy * 0.5m, 2)
                });
            }

            actions = actions.OrderBy(a => a.Priority).ToList();

            var avgUtil = titles.Average(t => t.UtilizationRate);
            var healthScore = new InventoryHealthScore
            {
                OverallScore = (int)Math.Min(100, avgUtil * 0.4 + 40 + rng.Next(0, 15)),
                UtilizationEfficiency = Math.Round(avgUtil, 1),
                DemandCoverage = Math.Round(titles.Count(t => t.DemandScore > 50) / (double)titles.Count * 100, 1),
                RevenueOptimality = Math.Round(75 + rng.NextDouble() * 20, 1),
                StaleInventoryRatio = Math.Round(titles.Count(t => t.UtilizationRate < 30) / (double)titles.Count * 100, 1)
            };

            return new InventoryOptimizerResult
            {
                HealthScore = healthScore,
                TitleAnalyses = titles.OrderByDescending(t => t.DemandScore).ToList(),
                Forecasts = forecasts,
                Actions = actions,
                GeneratedAt = DateTime.Now,
                AutoOptimizeEnabled = false
            };
        }
    }
}
