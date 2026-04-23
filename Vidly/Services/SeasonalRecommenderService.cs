using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    public class SeasonalProfile
    {
        public string CurrentSeason { get; set; }
        public int CurrentMonth { get; set; }
        public List<GenreSeasonality> GenreTrends { get; set; }
        public List<MovieRecommendation> Recommendations { get; set; }
        public List<SeasonalInsight> Insights { get; set; }
        public Dictionary<string, double> SeasonalActivity { get; set; }
    }

    public class GenreSeasonality
    {
        public string Genre { get; set; }
        public Dictionary<string, double> SeasonScores { get; set; }
        public string PeakSeason { get; set; }
        public double Seasonality { get; set; }
    }

    public class MovieRecommendation
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public int? Rating { get; set; }
        public double SeasonalScore { get; set; }
        public string Reason { get; set; }
    }

    public class SeasonalInsight
    {
        public string Icon { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; }
    }

    /// <summary>
    /// Smart Seasonal Recommender — analyzes rental patterns by season and
    /// recommends movies that perform best during the current time of year.
    /// </summary>
    public class SeasonalRecommenderService
    {
        private static readonly string[] Seasons = { "Winter", "Spring", "Summer", "Fall" };
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;

        public SeasonalRecommenderService(IMovieRepository movieRepo, IRentalRepository rentalRepo)
        {
            _movieRepo = movieRepo;
            _rentalRepo = rentalRepo;
        }

        public SeasonalProfile BuildProfile(string overrideSeason = null)
        {
            var now = DateTime.Today;
            var currentSeason = overrideSeason ?? GetSeason(now.Month);
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll().ToDictionary(m => m.Id);

            // Bucket rentals by season
            var rentalsBySeason = new Dictionary<string, List<Rental>>();
            foreach (var s in Seasons) rentalsBySeason[s] = new List<Rental>();
            foreach (var r in rentals)
                rentalsBySeason[GetSeason(r.RentalDate.Month)].Add(r);

            // Seasonal activity counts
            var seasonalActivity = Seasons.ToDictionary(s => s, s => (double)rentalsBySeason[s].Count);

            // Genre × season matrix
            var genreSeasonCounts = new Dictionary<string, Dictionary<string, int>>();
            foreach (var r in rentals)
            {
                Movie m;
                if (!movies.TryGetValue(r.MovieId, out m) || !m.Genre.HasValue) continue;
                var g = m.Genre.Value.ToString();
                if (!genreSeasonCounts.ContainsKey(g))
                    genreSeasonCounts[g] = Seasons.ToDictionary(s => s, s => 0);
                genreSeasonCounts[g][GetSeason(r.RentalDate.Month)]++;
            }

            // Build genre trends
            var genreTrends = new List<GenreSeasonality>();
            foreach (var kv in genreSeasonCounts)
            {
                var total = kv.Value.Values.Sum();
                if (total == 0) continue;
                var scores = kv.Value.ToDictionary(x => x.Key, x => Math.Round((double)x.Value / total, 3));
                var peak = scores.OrderByDescending(x => x.Value).First().Key;
                var vals = scores.Values.ToList();
                var mean = vals.Average();
                var std = mean > 0 ? Math.Sqrt(vals.Sum(v => (v - mean) * (v - mean)) / vals.Count) / mean : 0;
                genreTrends.Add(new GenreSeasonality
                {
                    Genre = kv.Key,
                    SeasonScores = scores,
                    PeakSeason = peak,
                    Seasonality = Math.Round(std, 3)
                });
            }
            genreTrends = genreTrends.OrderByDescending(g => g.Seasonality).ToList();

            // Genres that peak in current season
            var peakGenres = new HashSet<string>(
                genreTrends.Where(g => g.PeakSeason == currentSeason).Select(g => g.Genre));

            // Movie rental counts in current season
            var movieRentalCount = new Dictionary<int, int>();
            foreach (var r in rentalsBySeason[currentSeason])
            {
                if (!movieRentalCount.ContainsKey(r.MovieId))
                    movieRentalCount[r.MovieId] = 0;
                movieRentalCount[r.MovieId]++;
            }

            // Build recommendations
            var recs = new List<MovieRecommendation>();
            foreach (var m in movies.Values)
            {
                if (!m.Genre.HasValue) continue;
                var g = m.Genre.Value.ToString();
                int rentalCount;
                movieRentalCount.TryGetValue(m.Id, out rentalCount);

                double score = 0;
                if (peakGenres.Contains(g)) score += 50;
                score += (m.Rating ?? 0) * 8;
                score += rentalCount * 5;

                GenreSeasonality gt;
                gt = genreTrends.FirstOrDefault(x => x.Genre == g);
                if (gt != null)
                {
                    double ss;
                    if (gt.SeasonScores.TryGetValue(currentSeason, out ss))
                        score += ss * 30;
                }

                recs.Add(new MovieRecommendation
                {
                    MovieId = m.Id,
                    MovieName = m.Name,
                    Genre = g,
                    Rating = m.Rating,
                    SeasonalScore = Math.Round(score, 1),
                    Reason = peakGenres.Contains(g)
                        ? g + " peaks in " + currentSeason
                        : rentalCount > 0
                            ? "Rented " + rentalCount + "× this " + currentSeason
                            : "Highly rated " + g + " title"
                });
            }
            recs = recs.OrderByDescending(r => r.SeasonalScore).Take(10).ToList();

            // Build insights
            var insights = new List<SeasonalInsight>();
            var busiestSeason = seasonalActivity.OrderByDescending(x => x.Value).First();
            var quietestSeason = seasonalActivity.OrderBy(x => x.Value).First();
            insights.Add(new SeasonalInsight
            {
                Icon = "📈",
                Title = "Busiest Season",
                Description = busiestSeason.Key + " leads with " + (int)busiestSeason.Value + " rentals.",
                Severity = "info"
            });
            insights.Add(new SeasonalInsight
            {
                Icon = "📉",
                Title = "Quietest Season",
                Description = quietestSeason.Key + " has only " + (int)quietestSeason.Value + " rentals — consider promotions.",
                Severity = "warning"
            });

            var mostSeasonal = genreTrends.FirstOrDefault();
            if (mostSeasonal != null)
                insights.Add(new SeasonalInsight
                {
                    Icon = "🎯",
                    Title = "Most Seasonal Genre",
                    Description = mostSeasonal.Genre + " is highly seasonal (peaks in " + mostSeasonal.PeakSeason + ").",
                    Severity = "success"
                });

            if (peakGenres.Any())
                insights.Add(new SeasonalInsight
                {
                    Icon = SeasonIcon(currentSeason),
                    Title = currentSeason + " Favorites",
                    Description = "Top genres this season: " + string.Join(", ", peakGenres) + ".",
                    Severity = "info"
                });

            var totalRentals = rentals.Count;
            var currentPct = totalRentals > 0
                ? Math.Round(rentalsBySeason[currentSeason].Count * 100.0 / totalRentals, 1)
                : 0;
            insights.Add(new SeasonalInsight
            {
                Icon = "📊",
                Title = "Current Season Share",
                Description = currentSeason + " accounts for " + currentPct + "% of all rentals.",
                Severity = currentPct >= 25 ? "success" : "warning"
            });

            return new SeasonalProfile
            {
                CurrentSeason = currentSeason,
                CurrentMonth = now.Month,
                GenreTrends = genreTrends,
                Recommendations = recs,
                Insights = insights,
                SeasonalActivity = seasonalActivity
            };
        }

        public static string GetSeason(int month)
        {
            if (month >= 3 && month <= 5) return "Spring";
            if (month >= 6 && month <= 8) return "Summer";
            if (month >= 9 && month <= 11) return "Fall";
            return "Winter";
        }

        public static string SeasonIcon(string season)
        {
            switch (season)
            {
                case "Spring": return "🌸";
                case "Summer": return "🌞";
                case "Fall": return "🍂";
                case "Winter": return "❄️";
                default: return "📅";
            }
        }
    }
}
