using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ── Model classes ──────────────────────────────────────────────────

    public class CoachingReport
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public DateTime GeneratedAt { get; set; }

        // Rhythm
        public string RhythmState { get; set; }
        public string RhythmTrend { get; set; }
        public List<RhythmWindow> RhythmHistory { get; set; } = new List<RhythmWindow>();

        // Genre diversity
        public double GenreEntropy { get; set; }
        public double MaxPossibleEntropy { get; set; }
        public double DiversityScore { get; set; }
        public bool InGenreRut { get; set; }
        public string DominantGenre { get; set; }
        public Dictionary<string, double> GenreDistribution { get; set; } = new Dictionary<string, double>();

        // Engagement
        public double EngagementScore { get; set; }
        public string EngagementTrend { get; set; }
        public double TrendSlope { get; set; }
        public List<MonthlyEngagement> MonthlyHistory { get; set; } = new List<MonthlyEngagement>();

        // Timing
        public string TimingPersona { get; set; }
        public Dictionary<string, int> DayOfWeekDistribution { get; set; } = new Dictionary<string, int>();

        // Punctuality
        public double PunctualityScore { get; set; }
        public string ReturnBehavior { get; set; }
        public int TotalReturned { get; set; }
        public int OnTimeCount { get; set; }
        public int LateCount { get; set; }
        public int EarlyCount { get; set; }

        // Spending
        public decimal AvgMonthlySpend { get; set; }
        public string SpendingTrend { get; set; }

        // Coaching
        public List<CoachingGoal> Goals { get; set; } = new List<CoachingGoal>();
        public List<CoachingNudge> Nudges { get; set; } = new List<CoachingNudge>();
        public int OverallWellnessScore { get; set; }
        public string WellnessGrade { get; set; }
    }

    public class RhythmWindow
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int RentalCount { get; set; }
        public string State { get; set; }
    }

    public class MonthlyEngagement
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int RentalCount { get; set; }
        public decimal Spend { get; set; }
    }

    public class CoachingGoal
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Rationale { get; set; }
        public string Emoji { get; set; }
        public string Difficulty { get; set; }
        public string TargetMetric { get; set; }
        public double CurrentValue { get; set; }
        public double TargetValue { get; set; }
    }

    public class CoachingNudge
    {
        public string Message { get; set; }
        public string Category { get; set; }
        public string Emoji { get; set; }
        public string Priority { get; set; }
    }

    public class HabitCoachViewModel
    {
        public List<Customer> Customers { get; set; } = new List<Customer>();
        public int? SelectedCustomerId { get; set; }
        public CoachingReport Report { get; set; }
        public string ErrorMessage { get; set; }
    }

    // ── Service ────────────────────────────────────────────────────────

    /// <summary>
    /// Autonomous Rental Habit Coach — analyzes customer rental patterns,
    /// detects behavioral signals (genre ruts, binge/drought cycles, declining
    /// engagement, timing patterns), and generates personalized coaching goals
    /// and actionable nudges.
    /// </summary>
    public class HabitCoachService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        public HabitCoachService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public CoachingReport Analyze(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new KeyNotFoundException($"Customer {customerId} not found.");

            var rentals = _rentalRepository.GetByCustomer(customerId);
            var movies = _movieRepository.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id);

            var enriched = rentals
                .Where(r => movieLookup.ContainsKey(r.MovieId))
                .Select(r => new { Rental = r, Movie = movieLookup[r.MovieId] })
                .OrderBy(x => x.Rental.RentalDate)
                .ToList();

            var report = new CoachingReport
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                GeneratedAt = _clock.Now
            };

            if (!enriched.Any())
            {
                report.RhythmState = "Dormant";
                report.RhythmTrend = "Stable";
                report.GenreEntropy = 0;
                report.MaxPossibleEntropy = 0;
                report.DiversityScore = 0;
                report.InGenreRut = false;
                report.DominantGenre = "None";
                report.EngagementScore = 0;
                report.EngagementTrend = "Stable";
                report.TimingPersona = "Unknown";
                report.PunctualityScore = 100;
                report.ReturnBehavior = "No Data";
                report.SpendingTrend = "Stable";
                report.OverallWellnessScore = 0;
                report.WellnessGrade = "F";
                report.Goals.Add(new CoachingGoal
                {
                    Title = "Get Started",
                    Description = "Rent your first movie to begin your journey!",
                    Rationale = "Every great movie buff starts somewhere.",
                    Emoji = "🎬",
                    Difficulty = "Easy",
                    TargetMetric = "Rentals",
                    CurrentValue = 0,
                    TargetValue = 1
                });
                report.Nudges.Add(new CoachingNudge
                {
                    Message = "Your movie journey awaits — browse our catalog and pick something that catches your eye!",
                    Category = "Engagement",
                    Emoji = "🌟",
                    Priority = "High"
                });
                return report;
            }

            // ── Rhythm Detection ───────────────────────────────────────
            AnalyzeRhythm(report, enriched.Select(e => e.Rental).ToList());

            // ── Genre Diversity ─────────────────────────────────────────
            AnalyzeGenreDiversity(report, enriched.Where(e => e.Movie.Genre.HasValue)
                .Select(e => e.Movie.Genre.Value).ToList());

            // ── Engagement Trend ────────────────────────────────────────
            AnalyzeEngagement(report, enriched.Select(e => e.Rental).ToList());

            // ── Timing Patterns ─────────────────────────────────────────
            AnalyzeTiming(report, enriched.Select(e => e.Rental).ToList());

            // ── Punctuality ─────────────────────────────────────────────
            AnalyzePunctuality(report, enriched.Select(e => e.Rental).ToList());

            // ── Spending ────────────────────────────────────────────────
            AnalyzeSpending(report, enriched.Select(e => e.Rental).ToList());

            // ── Coaching ────────────────────────────────────────────────
            GenerateGoals(report);
            GenerateNudges(report);

            // ── Wellness Score ──────────────────────────────────────────
            CalculateWellness(report);

            return report;
        }

        // ── Rhythm ─────────────────────────────────────────────────────

        private void AnalyzeRhythm(CoachingReport report, List<Rental> rentals)
        {
            var now = _clock.Now;
            var windows = new List<RhythmWindow>();

            // Build 30-day sliding windows for last 6 months
            for (int i = 0; i < 6; i++)
            {
                var windowEnd = now.AddDays(-30 * i);
                var windowStart = windowEnd.AddDays(-30);
                var count = rentals.Count(r => r.RentalDate >= windowStart && r.RentalDate < windowEnd);
                windows.Add(new RhythmWindow
                {
                    Start = windowStart,
                    End = windowEnd,
                    RentalCount = count,
                    State = ClassifyRhythmState(count)
                });
            }

            windows.Reverse();
            report.RhythmHistory = windows;

            var currentWindow = windows.LastOrDefault();
            report.RhythmState = currentWindow?.State ?? "Dormant";

            // Detect trend from last 3 windows
            if (windows.Count >= 3)
            {
                var recent = windows.Skip(windows.Count - 3).Select(w => w.RentalCount).ToList();
                if (recent[2] > recent[0] + 1)
                    report.RhythmTrend = "Accelerating";
                else if (recent[2] < recent[0] - 1)
                    report.RhythmTrend = "Decelerating";
                else
                    report.RhythmTrend = "Stable";
            }
            else
            {
                report.RhythmTrend = "Stable";
            }
        }

        internal static string ClassifyRhythmState(int rentalsPerMonth)
        {
            if (rentalsPerMonth > 8) return "Binge";
            if (rentalsPerMonth >= 4) return "Active";
            if (rentalsPerMonth >= 1) return "Casual";
            return "Dormant";
        }

        // ── Genre Diversity ────────────────────────────────────────────

        private void AnalyzeGenreDiversity(CoachingReport report, List<Genre> genres)
        {
            if (!genres.Any())
            {
                report.DiversityScore = 0;
                report.InGenreRut = false;
                report.DominantGenre = "None";
                return;
            }

            var counts = genres.GroupBy(g => g)
                .ToDictionary(g => g.Key.ToString(), g => (double)g.Count());
            var total = (double)genres.Count;

            report.GenreDistribution = counts.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / total * 100, 1));

            // Shannon entropy
            double entropy = 0;
            foreach (var count in counts.Values)
            {
                var p = count / total;
                if (p > 0) entropy -= p * Math.Log(p, 2);
            }
            report.GenreEntropy = Math.Round(entropy, 3);

            var distinctGenres = counts.Count;
            report.MaxPossibleEntropy = distinctGenres > 1 ? Math.Round(Math.Log(distinctGenres, 2), 3) : 0;
            report.DiversityScore = report.MaxPossibleEntropy > 0
                ? Math.Round((entropy / Math.Log(10, 2)) * 100 / (Math.Log(10, 2)) * (entropy / report.MaxPossibleEntropy), 1)
                : 0;

            // Simpler diversity score: normalized entropy 0-100
            report.DiversityScore = report.MaxPossibleEntropy > 0
                ? Math.Round(entropy / report.MaxPossibleEntropy * 100, 1)
                : 0;

            var dominant = counts.OrderByDescending(kv => kv.Value).First();
            report.DominantGenre = dominant.Key;

            // Genre rut: check last 20 (or all if fewer)
            var recentGenres = genres.Skip(Math.Max(0, genres.Count - 20)).ToList();
            var recentCounts = recentGenres.GroupBy(g => g).ToDictionary(g => g.Key, g => g.Count());
            var recentTotal = (double)recentGenres.Count;
            var topGenrePct = recentCounts.Values.Max() / recentTotal;
            report.InGenreRut = topGenrePct > 0.6;
        }

        // ── Engagement Trend ───────────────────────────────────────────

        private void AnalyzeEngagement(CoachingReport report, List<Rental> rentals)
        {
            var monthly = rentals
                .GroupBy(r => new { r.RentalDate.Year, r.RentalDate.Month })
                .Select(g => new MonthlyEngagement
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    RentalCount = g.Count(),
                    Spend = g.Sum(r => r.DailyRate * Math.Max(1, (decimal)((r.ReturnDate ?? r.DueDate) - r.RentalDate).TotalDays))
                })
                .OrderBy(m => m.Year).ThenBy(m => m.Month)
                .ToList();

            report.MonthlyHistory = monthly;

            if (monthly.Count < 2)
            {
                report.EngagementTrend = "Stable";
                report.TrendSlope = 0;
                report.EngagementScore = monthly.Any() ? Math.Min(100, monthly[0].RentalCount * 15) : 0;
                return;
            }

            // Linear regression on rental counts
            var n = monthly.Count;
            var xs = Enumerable.Range(0, n).Select(i => (double)i).ToList();
            var ys = monthly.Select(m => (double)m.RentalCount).ToList();
            var xMean = xs.Average();
            var yMean = ys.Average();
            var num = xs.Zip(ys, (x, y) => (x - xMean) * (y - yMean)).Sum();
            var den = xs.Select(x => (x - xMean) * (x - xMean)).Sum();
            var slope = den > 0 ? num / den : 0;

            report.TrendSlope = Math.Round(slope, 3);
            report.EngagementTrend = slope > 0.3 ? "Growing" : slope < -0.3 ? "Declining" : "Stable";

            // Engagement score: weighted recent activity
            var recentMonths = monthly.Skip(Math.Max(0, monthly.Count - 3)).ToList();
            var avgRecent = recentMonths.Average(m => m.RentalCount);
            report.EngagementScore = Math.Min(100, Math.Round(avgRecent * 15, 0));
        }

        // ── Timing Patterns ────────────────────────────────────────────

        private void AnalyzeTiming(CoachingReport report, List<Rental> rentals)
        {
            var dayNames = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            var dayCounts = new Dictionary<string, int>();
            foreach (var name in dayNames) dayCounts[name] = 0;

            foreach (var r in rentals)
                dayCounts[r.RentalDate.DayOfWeek.ToString()]++;

            report.DayOfWeekDistribution = dayCounts;

            var total = (double)rentals.Count;
            if (total == 0)
            {
                report.TimingPersona = "Unknown";
                return;
            }

            var weekendCount = dayCounts["Friday"] + dayCounts["Saturday"] + dayCounts["Sunday"];
            var weekendPct = weekendCount / total;

            if (weekendPct > 0.7)
                report.TimingPersona = "Weekend Warrior";
            else if (weekendPct < 0.3)
                report.TimingPersona = "Weekday Regular";
            else
                report.TimingPersona = "Balanced";
        }

        // ── Punctuality ────────────────────────────────────────────────

        private void AnalyzePunctuality(CoachingReport report, List<Rental> rentals)
        {
            var returned = rentals.Where(r => r.ReturnDate.HasValue).ToList();
            report.TotalReturned = returned.Count;

            if (!returned.Any())
            {
                report.PunctualityScore = 100;
                report.ReturnBehavior = "No Returns Yet";
                return;
            }

            int early = 0, onTime = 0, late = 0;
            foreach (var r in returned)
            {
                var diff = (r.ReturnDate.Value - r.DueDate).TotalDays;
                if (diff < -0.5) early++;
                else if (diff <= 1.0) onTime++;
                else late++;
            }

            report.EarlyCount = early;
            report.OnTimeCount = onTime;
            report.LateCount = late;

            report.PunctualityScore = Math.Round((early + onTime) / (double)returned.Count * 100, 0);

            if (late > returned.Count * 0.5)
                report.ReturnBehavior = "Chronically Late";
            else if (early > returned.Count * 0.5)
                report.ReturnBehavior = "Early Bird";
            else
                report.ReturnBehavior = "On-Time";
        }

        // ── Spending ───────────────────────────────────────────────────

        private void AnalyzeSpending(CoachingReport report, List<Rental> rentals)
        {
            if (!report.MonthlyHistory.Any())
            {
                report.AvgMonthlySpend = 0;
                report.SpendingTrend = "Stable";
                return;
            }

            report.AvgMonthlySpend = Math.Round(report.MonthlyHistory.Average(m => m.Spend), 2);

            if (report.MonthlyHistory.Count < 2)
            {
                report.SpendingTrend = "Stable";
                return;
            }

            var half = report.MonthlyHistory.Count / 2;
            var firstHalf = report.MonthlyHistory.Take(half).Average(m => m.Spend);
            var secondHalf = report.MonthlyHistory.Skip(half).Average(m => m.Spend);

            if (secondHalf > firstHalf * 1.2m)
                report.SpendingTrend = "Increasing";
            else if (secondHalf < firstHalf * 0.8m)
                report.SpendingTrend = "Decreasing";
            else
                report.SpendingTrend = "Stable";
        }

        // ── Goal Generation ────────────────────────────────────────────

        private void GenerateGoals(CoachingReport report)
        {
            var goals = new List<CoachingGoal>();

            // Genre rut goal
            if (report.InGenreRut)
            {
                goals.Add(new CoachingGoal
                {
                    Title = "Break the Genre Rut",
                    Description = $"You've been watching a lot of {report.DominantGenre}. Try exploring 2 new genres this month!",
                    Rationale = "Variety keeps your movie experience fresh and helps you discover hidden gems.",
                    Emoji = "🌈",
                    Difficulty = "Medium",
                    TargetMetric = "Genre Diversity",
                    CurrentValue = report.DiversityScore,
                    TargetValue = Math.Min(100, report.DiversityScore + 25)
                });
            }

            // Dormant reactivation
            if (report.RhythmState == "Dormant")
            {
                goals.Add(new CoachingGoal
                {
                    Title = "Rekindle the Spark",
                    Description = "You haven't rented lately. Start with just 1 movie this week!",
                    Rationale = "Getting back into the habit is easier with small steps.",
                    Emoji = "🔥",
                    Difficulty = "Easy",
                    TargetMetric = "Monthly Rentals",
                    CurrentValue = 0,
                    TargetValue = 2
                });
            }

            // Binge moderation
            if (report.RhythmState == "Binge")
            {
                goals.Add(new CoachingGoal
                {
                    Title = "Pace Yourself",
                    Description = "You're on a binge streak! Try spacing rentals out for a more sustainable rhythm.",
                    Rationale = "Sustainable habits last longer than intense bursts.",
                    Emoji = "⏳",
                    Difficulty = "Medium",
                    TargetMetric = "Monthly Rentals",
                    CurrentValue = report.RhythmHistory.LastOrDefault()?.RentalCount ?? 0,
                    TargetValue = 6
                });
            }

            // Punctuality improvement
            if (report.ReturnBehavior == "Chronically Late")
            {
                goals.Add(new CoachingGoal
                {
                    Title = "Return on Time",
                    Description = "Late returns add up! Aim to return your next 3 rentals on or before the due date.",
                    Rationale = "On-time returns save you money and keep movies available for others.",
                    Emoji = "⏰",
                    Difficulty = "Easy",
                    TargetMetric = "Punctuality Score",
                    CurrentValue = report.PunctualityScore,
                    TargetValue = 80
                });
            }

            // Declining engagement
            if (report.EngagementTrend == "Declining")
            {
                goals.Add(new CoachingGoal
                {
                    Title = "Stay Engaged",
                    Description = "Your rental activity has been dropping. Set a goal of 2 movies this month!",
                    Rationale = "A little momentum goes a long way.",
                    Emoji = "📈",
                    Difficulty = "Easy",
                    TargetMetric = "Engagement Score",
                    CurrentValue = report.EngagementScore,
                    TargetValue = Math.Min(100, report.EngagementScore + 20)
                });
            }

            // Low diversity even without rut
            if (!report.InGenreRut && report.DiversityScore > 0 && report.DiversityScore < 40)
            {
                goals.Add(new CoachingGoal
                {
                    Title = "Expand Your Horizons",
                    Description = "Try a genre you've never rented before — you might be surprised!",
                    Rationale = "Some of the best movie discoveries come from unexpected genres.",
                    Emoji = "🗺️",
                    Difficulty = "Easy",
                    TargetMetric = "Genre Diversity",
                    CurrentValue = report.DiversityScore,
                    TargetValue = 60
                });
            }

            // Take top 3 goals by priority
            report.Goals = goals.Take(3).ToList();
        }

        // ── Nudge Generation ───────────────────────────────────────────

        private void GenerateNudges(CoachingReport report)
        {
            var nudges = new List<CoachingNudge>();

            if (report.RhythmState == "Dormant")
            {
                nudges.Add(new CoachingNudge
                {
                    Message = "It's been a while! A movie night could be just what you need. 🍿",
                    Category = "Engagement",
                    Emoji = "💤",
                    Priority = "High"
                });
            }

            if (report.InGenreRut)
            {
                nudges.Add(new CoachingNudge
                {
                    Message = $"You've been on a {report.DominantGenre} streak — how about trying something different this time?",
                    Category = "Diversity",
                    Emoji = "🔄",
                    Priority = "Medium"
                });
            }

            if (report.ReturnBehavior == "Chronically Late")
            {
                nudges.Add(new CoachingNudge
                {
                    Message = "Quick reminder: returning on time saves money and earns loyalty points!",
                    Category = "Punctuality",
                    Emoji = "⏰",
                    Priority = "Medium"
                });
            }

            if (report.EngagementTrend == "Declining")
            {
                nudges.Add(new CoachingNudge
                {
                    Message = "We've noticed you're renting less lately. Need some fresh recommendations?",
                    Category = "Engagement",
                    Emoji = "📉",
                    Priority = "Medium"
                });
            }

            if (report.TimingPersona == "Weekend Warrior")
            {
                nudges.Add(new CoachingNudge
                {
                    Message = "Midweek rentals often have shorter wait times — try a Tuesday movie night!",
                    Category = "Timing",
                    Emoji = "📅",
                    Priority = "Low"
                });
            }

            if (report.RhythmState == "Binge")
            {
                nudges.Add(new CoachingNudge
                {
                    Message = "You're on fire! Just make sure you're savoring each film. Quality over quantity! 🎥",
                    Category = "Rhythm",
                    Emoji = "🔥",
                    Priority = "Low"
                });
            }

            if (report.ReturnBehavior == "Early Bird")
            {
                nudges.Add(new CoachingNudge
                {
                    Message = "Great job returning early! You're a model customer. Keep it up! ⭐",
                    Category = "Punctuality",
                    Emoji = "🌅",
                    Priority = "Low"
                });
            }

            if (report.EngagementTrend == "Growing")
            {
                nudges.Add(new CoachingNudge
                {
                    Message = "Your movie habit is growing — you're becoming a true cinephile! 🎬",
                    Category = "Engagement",
                    Emoji = "🚀",
                    Priority = "Low"
                });
            }

            report.Nudges = nudges;
        }

        // ── Wellness Score ─────────────────────────────────────────────

        private void CalculateWellness(CoachingReport report)
        {
            // Weighted composite: engagement 30%, diversity 25%, punctuality 25%, rhythm 20%
            var rhythmScore = report.RhythmState switch
            {
                "Active" => 100.0,
                "Casual" => 70.0,
                "Binge" => 50.0,
                "Dormant" => 10.0,
                _ => 50.0
            };

            var wellness = (report.EngagementScore * 0.30)
                         + (report.DiversityScore * 0.25)
                         + (report.PunctualityScore * 0.25)
                         + (rhythmScore * 0.20);

            report.OverallWellnessScore = (int)Math.Round(Math.Min(100, Math.Max(0, wellness)));

            report.WellnessGrade = report.OverallWellnessScore switch
            {
                >= 95 => "A+",
                >= 90 => "A",
                >= 85 => "A-",
                >= 80 => "B+",
                >= 75 => "B",
                >= 70 => "B-",
                >= 65 => "C+",
                >= 60 => "C",
                >= 55 => "C-",
                >= 50 => "D+",
                >= 45 => "D",
                >= 40 => "D-",
                _ => "F"
            };
        }
    }
}
