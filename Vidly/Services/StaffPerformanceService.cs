using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Tracks and analyzes staff member performance across rental transactions.
    /// Provides individual reports, team summaries, leaderboard rankings, and
    /// improvement recommendations.
    /// </summary>
    public class StaffPerformanceService
    {
        private readonly List<StaffMember> _staff = new List<StaffMember>();
        private readonly List<StaffTransaction> _transactions = new List<StaffTransaction>();
        private readonly IClock _clock;
        private int _nextStaffId = 1;
        private int _nextTransactionId = 1;

        // ── Scoring weights (sum = 1.0) ─────────────────────

        private readonly double _volumeWeight;
        private readonly double _revenueWeight;
        private readonly double _satisfactionWeight;
        private readonly double _upsellWeight;
        private readonly double _speedWeight;
        private readonly IClock _clock;

        /// <summary>
        /// Creates a StaffPerformanceService with configurable scoring weights.
        /// All weights must be non-negative and sum to a positive number
        /// (they are normalized internally).
        /// </summary>
        public StaffPerformanceService(
            double volumeWeight = 0.20,
            double revenueWeight = 0.25,
            double satisfactionWeight = 0.30,
            double upsellWeight = 0.15,
            double speedWeight = 0.10,
            IClock clock = null)
        {
            if (volumeWeight < 0 || revenueWeight < 0 || satisfactionWeight < 0 ||
                upsellWeight < 0 || speedWeight < 0)
                throw new ArgumentException("Weights must be non-negative.");

            var total = volumeWeight + revenueWeight + satisfactionWeight + upsellWeight + speedWeight;
            if (total <= 0)
                throw new ArgumentException("At least one weight must be positive.");

            _volumeWeight = volumeWeight / total;
            _revenueWeight = revenueWeight / total;
            _satisfactionWeight = satisfactionWeight / total;
            _upsellWeight = upsellWeight / total;
            _speedWeight = speedWeight / total;
        }

        // ══════════════════════════════════════════════════════
        //  Staff CRUD
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Register a new staff member.
        /// </summary>
        public StaffMember AddStaff(string name, StaffRole role, DateTime? hireDate = null, string email = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Staff name is required.", nameof(name));

            var member = new StaffMember
            {
                Id = _nextStaffId++,
                Name = name.Trim(),
                Role = role,
                HireDate = hireDate ?? DateTime.Today,
                Email = email,
                IsActive = true
            };
            _staff.Add(member);
            return member;
        }

        /// <summary>
        /// Get a staff member by ID.
        /// </summary>
        public StaffMember GetStaff(int staffId)
        {
            return _staff.FirstOrDefault(s => s.Id == staffId);
        }

        /// <summary>
        /// List all staff members, optionally filtering to active only.
        /// </summary>
        public IReadOnlyList<StaffMember> ListStaff(bool activeOnly = true)
        {
            var query = activeOnly ? _staff.Where(s => s.IsActive) : _staff.AsEnumerable();
            return query.OrderBy(s => s.Name).ToList();
        }

        /// <summary>
        /// Deactivate a staff member (soft delete).
        /// </summary>
        public bool DeactivateStaff(int staffId)
        {
            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null) return false;
            member.IsActive = false;
            return true;
        }

        /// <summary>
        /// Reactivate a previously deactivated staff member.
        /// </summary>
        public bool ReactivateStaff(int staffId)
        {
            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null) return false;
            member.IsActive = true;
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  Transaction Recording
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Record a transaction processed by a staff member.
        /// </summary>
        public StaffTransaction RecordTransaction(
            int staffId,
            int customerId,
            StaffTransactionType type,
            decimal revenue = 0m,
            int durationSeconds = 0,
            int? movieId = null,
            bool upsellAttempted = false,
            bool upsellAccepted = false,
            int? satisfactionRating = null,
            string feedbackComment = null,
            DateTime? timestamp = null)
        {
            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null)
                throw new ArgumentException($"Staff member {staffId} not found.", nameof(staffId));

            if (revenue < 0)
                throw new ArgumentException("Revenue cannot be negative.", nameof(revenue));
            if (durationSeconds < 0)
                throw new ArgumentException("Duration cannot be negative.", nameof(durationSeconds));
            if (satisfactionRating.HasValue && (satisfactionRating.Value < 1 || satisfactionRating.Value > 5))
                throw new ArgumentException("Satisfaction rating must be between 1 and 5.", nameof(satisfactionRating));

            var txn = new StaffTransaction
            {
                Id = _nextTransactionId++,
                StaffId = staffId,
                StaffName = member.Name,
                CustomerId = customerId,
                CustomerName = null,
                MovieId = movieId,
                Type = type,
                Timestamp = timestamp ?? _clock.Now,
                Revenue = revenue,
                DurationSeconds = durationSeconds,
                UpsellAttempted = upsellAttempted,
                UpsellAccepted = upsellAccepted && upsellAttempted,
                SatisfactionRating = satisfactionRating,
                FeedbackComment = feedbackComment?.Trim()
            };

            _transactions.Add(txn);
            return txn;
        }

        /// <summary>
        /// Get all transactions for a specific staff member.
        /// </summary>
        public IReadOnlyList<StaffTransaction> GetTransactions(int staffId,
            DateTime? from = null, DateTime? to = null)
        {
            var query = _transactions.Where(t => t.StaffId == staffId);
            if (from.HasValue) query = query.Where(t => t.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(t => t.Timestamp < to.Value);
            return query.OrderByDescending(t => t.Timestamp).ToList();
        }

        // ══════════════════════════════════════════════════════
        //  Individual Performance Report
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Generate a comprehensive performance report for a staff member.
        /// </summary>
        public StaffPerformanceReport GenerateReport(int staffId,
            DateTime periodStart, DateTime periodEnd)
        {
            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null)
                throw new ArgumentException($"Staff member {staffId} not found.", nameof(staffId));

            if (periodEnd <= periodStart)
                throw new ArgumentException("Period end must be after period start.");

            var txns = _transactions
                .Where(t => t.StaffId == staffId && t.Timestamp >= periodStart && t.Timestamp < periodEnd)
                .ToList();

            var report = new StaffPerformanceReport
            {
                StaffId = staffId,
                StaffName = member.Name,
                Role = member.Role,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
            };

            if (txns.Count == 0)
            {
                report.Grade = "N/A";
                report.ImprovementAreas.Add("No transactions recorded in this period.");
                return report;
            }

            // Volume
            report.TotalTransactions = txns.Count;
            report.RentalCount = txns.Count(t => t.Type == StaffTransactionType.Rental);
            report.ReturnCount = txns.Count(t => t.Type == StaffTransactionType.Return);
            report.ReservationCount = txns.Count(t => t.Type == StaffTransactionType.Reservation);
            report.GiftCardSaleCount = txns.Count(t => t.Type == StaffTransactionType.GiftCardSale);
            report.MembershipUpgradeCount = txns.Count(t => t.Type == StaffTransactionType.MembershipUpgrade);

            // Revenue
            report.TotalRevenue = txns.Sum(t => t.Revenue);
            report.AverageRevenuePerTransaction = report.TotalRevenue / txns.Count;

            // Speed
            var durations = txns.Where(t => t.DurationSeconds > 0).Select(t => t.DurationSeconds).ToList();
            if (durations.Count > 0)
            {
                report.AverageTransactionSeconds = durations.Average();
                report.FastestTransactionSeconds = durations.Min();
                report.SlowestTransactionSeconds = durations.Max();
            }

            // Upsell
            report.UpsellAttempts = txns.Count(t => t.UpsellAttempted);
            report.UpsellSuccesses = txns.Count(t => t.UpsellAccepted);
            report.UpsellConversionRate = report.UpsellAttempts > 0
                ? (double)report.UpsellSuccesses / report.UpsellAttempts
                : 0;

            // Satisfaction
            var rated = txns.Where(t => t.SatisfactionRating.HasValue).ToList();
            report.TotalRatings = rated.Count;
            if (rated.Count > 0)
            {
                report.AverageSatisfactionRating = rated.Average(t => t.SatisfactionRating.Value);
                report.FiveStarCount = rated.Count(t => t.SatisfactionRating.Value == 5);
                report.OneStarCount = rated.Count(t => t.SatisfactionRating.Value == 1);
            }

            // Composite score
            report.PerformanceScore = CalculateScore(txns);
            report.Grade = ScoreToGrade(report.PerformanceScore);

            // Strengths & improvement areas
            IdentifyStrengthsAndAreas(report, txns);

            return report;
        }

        // ══════════════════════════════════════════════════════
        //  Leaderboard / Rankings
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Generate ranked leaderboard for all active staff over a period.
        /// </summary>
        public List<StaffRankingEntry> GetLeaderboard(DateTime periodStart, DateTime periodEnd)
        {
            if (periodEnd <= periodStart)
                throw new ArgumentException("Period end must be after period start.");

            var activeStaff = _staff.Where(s => s.IsActive).ToList();
            var entries = new List<StaffRankingEntry>();

            foreach (var member in activeStaff)
            {
                var txns = _transactions
                    .Where(t => t.StaffId == member.Id && t.Timestamp >= periodStart && t.Timestamp < periodEnd)
                    .ToList();

                if (txns.Count == 0) continue;

                var rated = txns.Where(t => t.SatisfactionRating.HasValue).ToList();
                var score = CalculateScore(txns);

                entries.Add(new StaffRankingEntry
                {
                    StaffId = member.Id,
                    StaffName = member.Name,
                    Role = member.Role,
                    Score = Math.Round(score, 1),
                    Grade = ScoreToGrade(score),
                    Transactions = txns.Count,
                    Revenue = txns.Sum(t => t.Revenue),
                    SatisfactionAvg = rated.Count > 0 ? Math.Round(rated.Average(t => t.SatisfactionRating.Value), 2) : 0
                });
            }

            entries = entries.OrderByDescending(e => e.Score).ToList();
            for (int i = 0; i < entries.Count; i++)
                entries[i].Rank = i + 1;

            return entries;
        }

        // ══════════════════════════════════════════════════════
        //  Team Summary
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Generate an aggregate team performance summary.
        /// </summary>
        public TeamPerformanceSummary GetTeamSummary(DateTime periodStart, DateTime periodEnd)
        {
            if (periodEnd <= periodStart)
                throw new ArgumentException("Period end must be after period start.");

            var txns = _transactions
                .Where(t => t.Timestamp >= periodStart && t.Timestamp < periodEnd)
                .ToList();

            var activeStaffInPeriod = txns.Select(t => t.StaffId).Distinct().Count();
            var rankings = GetLeaderboard(periodStart, periodEnd);
            var rated = txns.Where(t => t.SatisfactionRating.HasValue).ToList();
            var upsellAttempts = txns.Count(t => t.UpsellAttempted);

            var summary = new TeamPerformanceSummary
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                ActiveStaffCount = activeStaffInPeriod,
                TotalTransactions = txns.Count,
                TotalRevenue = txns.Sum(t => t.Revenue),
                AvgTransactionsPerStaff = activeStaffInPeriod > 0 ? (double)txns.Count / activeStaffInPeriod : 0,
                AvgRevenuePerStaff = activeStaffInPeriod > 0 ? (double)(txns.Sum(t => t.Revenue) / activeStaffInPeriod) : 0,
                TeamSatisfactionAvg = rated.Count > 0 ? Math.Round(rated.Average(t => t.SatisfactionRating.Value), 2) : 0,
                TeamUpsellRate = upsellAttempts > 0
                    ? (double)txns.Count(t => t.UpsellAccepted) / upsellAttempts : 0,
                AvgTransactionSeconds = txns.Where(t => t.DurationSeconds > 0).Any()
                    ? txns.Where(t => t.DurationSeconds > 0).Average(t => t.DurationSeconds) : 0,
                Rankings = rankings,
                TopPerformer = rankings.FirstOrDefault(),
            };

            // Transaction breakdown
            foreach (StaffTransactionType type in Enum.GetValues(typeof(StaffTransactionType)))
            {
                var count = txns.Count(t => t.Type == type);
                if (count > 0)
                    summary.TransactionBreakdown[type] = count;
            }

            return summary;
        }

        // ══════════════════════════════════════════════════════
        //  Period Comparison
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Compare a staff member's performance across two periods.
        /// Returns deltas for key metrics.
        /// </summary>
        public Dictionary<string, object> ComparePerformance(
            int staffId, DateTime period1Start, DateTime period1End,
            DateTime period2Start, DateTime period2End)
        {
            var r1 = GenerateReport(staffId, period1Start, period1End);
            var r2 = GenerateReport(staffId, period2Start, period2End);

            return new Dictionary<string, object>
            {
                ["staffId"] = staffId,
                ["staffName"] = r1.StaffName,
                ["period1"] = $"{period1Start:yyyy-MM-dd} to {period1End:yyyy-MM-dd}",
                ["period2"] = $"{period2Start:yyyy-MM-dd} to {period2End:yyyy-MM-dd}",
                ["transactionsDelta"] = r2.TotalTransactions - r1.TotalTransactions,
                ["revenueDelta"] = r2.TotalRevenue - r1.TotalRevenue,
                ["satisfactionDelta"] = Math.Round(r2.AverageSatisfactionRating - r1.AverageSatisfactionRating, 2),
                ["upsellRateDelta"] = Math.Round(r2.UpsellConversionRate - r1.UpsellConversionRate, 4),
                ["speedDelta"] = Math.Round(r2.AverageTransactionSeconds - r1.AverageTransactionSeconds, 1),
                ["scoreDelta"] = Math.Round(r2.PerformanceScore - r1.PerformanceScore, 1),
                ["gradeBefore"] = r1.Grade,
                ["gradeAfter"] = r2.Grade,
                ["improved"] = r2.PerformanceScore > r1.PerformanceScore
            };
        }

        // ══════════════════════════════════════════════════════
        //  Daily Activity Log
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Get hourly activity breakdown for a staff member on a specific date.
        /// </summary>
        public Dictionary<int, int> GetHourlyActivity(int staffId, DateTime date)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            var hourly = new Dictionary<int, int>();
            for (int h = 0; h < 24; h++) hourly[h] = 0;

            foreach (var txn in _transactions.Where(t =>
                t.StaffId == staffId && t.Timestamp >= dayStart && t.Timestamp < dayEnd))
            {
                hourly[txn.Timestamp.Hour]++;
            }

            return hourly;
        }

        /// <summary>
        /// Get peak hours for a staff member over a date range.
        /// Returns top N hours by transaction count.
        /// </summary>
        public List<KeyValuePair<int, int>> GetPeakHours(int staffId,
            DateTime from, DateTime to, int topN = 3)
        {
            var hourCounts = new Dictionary<int, int>();
            for (int h = 0; h < 24; h++) hourCounts[h] = 0;

            foreach (var txn in _transactions.Where(t =>
                t.StaffId == staffId && t.Timestamp >= from && t.Timestamp < to))
            {
                hourCounts[txn.Timestamp.Hour]++;
            }

            return hourCounts
                .OrderByDescending(kv => kv.Value)
                .Take(topN)
                .ToList();
        }

        // ══════════════════════════════════════════════════════
        //  Customer Feedback Analysis
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Get all feedback comments for a staff member, sorted by date.
        /// </summary>
        public IReadOnlyList<StaffTransaction> GetFeedback(int staffId,
            DateTime? from = null, DateTime? to = null, int? minRating = null, int? maxRating = null)
        {
            var query = _transactions
                .Where(t => t.StaffId == staffId && t.SatisfactionRating.HasValue);

            if (from.HasValue) query = query.Where(t => t.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(t => t.Timestamp < to.Value);
            if (minRating.HasValue) query = query.Where(t => t.SatisfactionRating.Value >= minRating.Value);
            if (maxRating.HasValue) query = query.Where(t => t.SatisfactionRating.Value <= maxRating.Value);

            return query.OrderByDescending(t => t.Timestamp).ToList();
        }

        /// <summary>
        /// Get satisfaction rating distribution for a staff member.
        /// Returns counts for ratings 1-5.
        /// </summary>
        public Dictionary<int, int> GetRatingDistribution(int staffId,
            DateTime? from = null, DateTime? to = null)
        {
            var dist = new Dictionary<int, int>();
            for (int r = 1; r <= 5; r++) dist[r] = 0;

            var query = _transactions
                .Where(t => t.StaffId == staffId && t.SatisfactionRating.HasValue);

            if (from.HasValue) query = query.Where(t => t.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(t => t.Timestamp < to.Value);

            foreach (var txn in query)
                dist[txn.SatisfactionRating.Value]++;

            return dist;
        }

        // ══════════════════════════════════════════════════════
        //  Streaks & Milestones
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Calculate the current streak of consecutive 5-star ratings.
        /// </summary>
        public int GetFiveStarStreak(int staffId)
        {
            var rated = _transactions
                .Where(t => t.StaffId == staffId && t.SatisfactionRating.HasValue)
                .OrderByDescending(t => t.Timestamp)
                .ToList();

            int streak = 0;
            foreach (var txn in rated)
            {
                if (txn.SatisfactionRating.Value == 5)
                    streak++;
                else
                    break;
            }
            return streak;
        }

        /// <summary>
        /// Get daily transaction counts for a staff member (for trend charts).
        /// </summary>
        public List<KeyValuePair<DateTime, int>> GetDailyTrend(int staffId,
            DateTime from, DateTime to)
        {
            var trend = new List<KeyValuePair<DateTime, int>>();
            var date = from.Date;
            var endDate = to.Date;

            while (date <= endDate)
            {
                var count = _transactions.Count(t =>
                    t.StaffId == staffId &&
                    t.Timestamp >= date && t.Timestamp < date.AddDays(1));
                trend.Add(new KeyValuePair<DateTime, int>(date, count));
                date = date.AddDays(1);
            }

            return trend;
        }

        // ══════════════════════════════════════════════════════
        //  Private Helpers
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Calculate a composite performance score (0-100) from transactions.
        /// Dimensions: volume, revenue, satisfaction, upsell rate, speed.
        /// Each dimension is normalized to 0-100 using reasonable benchmarks.
        /// </summary>
        private double CalculateScore(List<StaffTransaction> txns)
        {
            if (txns.Count == 0) return 0;

            // Volume: benchmark = 10 transactions/day -> 100
            var days = Math.Max(1, (txns.Max(t => t.Timestamp) - txns.Min(t => t.Timestamp)).TotalDays + 1);
            var dailyAvg = txns.Count / days;
            var volumeScore = Math.Min(100, dailyAvg / 10.0 * 100);

            // Revenue: benchmark = $50/transaction -> 100
            var avgRevenue = txns.Average(t => (double)t.Revenue);
            var revenueScore = Math.Min(100, avgRevenue / 50.0 * 100);

            // Satisfaction: 5.0 -> 100, 1.0 -> 0
            var rated = txns.Where(t => t.SatisfactionRating.HasValue).ToList();
            var satisfactionScore = rated.Count > 0
                ? (rated.Average(t => t.SatisfactionRating.Value) - 1.0) / 4.0 * 100
                : 50; // neutral if no ratings

            // Upsell: 50% conversion -> 100
            var upsellAttempts = txns.Count(t => t.UpsellAttempted);
            double upsellScore;
            if (upsellAttempts == 0)
                upsellScore = 30; // penalize not attempting
            else
            {
                var conversionRate = (double)txns.Count(t => t.UpsellAccepted) / upsellAttempts;
                upsellScore = Math.Min(100, conversionRate / 0.5 * 100);
            }

            // Speed: benchmark = 120s average -> 100, faster is better
            var durations = txns.Where(t => t.DurationSeconds > 0).ToList();
            double speedScore;
            if (durations.Count == 0)
                speedScore = 50; // neutral
            else
            {
                var avgDuration = durations.Average(t => t.DurationSeconds);
                // 60s = 100, 120s = 75, 240s = 37.5, 480s = 18.75
                speedScore = avgDuration > 0 ? Math.Min(100, 120.0 / avgDuration * 75) : 50;
            }

            return Math.Round(
                volumeScore * _volumeWeight +
                revenueScore * _revenueWeight +
                satisfactionScore * _satisfactionWeight +
                upsellScore * _upsellWeight +
                speedScore * _speedWeight,
                1);
        }

        private static string ScoreToGrade(double score)
        {
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }

        private void IdentifyStrengthsAndAreas(StaffPerformanceReport report,
            List<StaffTransaction> txns)
        {
            // Satisfaction
            if (report.TotalRatings > 0)
            {
                if (report.AverageSatisfactionRating >= 4.5)
                    report.Strengths.Add("Excellent customer satisfaction");
                else if (report.AverageSatisfactionRating < 3.0)
                    report.ImprovementAreas.Add("Customer satisfaction needs improvement");
            }
            else
            {
                report.ImprovementAreas.Add("No customer ratings collected — encourage feedback");
            }

            // Upsell
            if (report.UpsellAttempts > 0 && report.UpsellConversionRate >= 0.4)
                report.Strengths.Add("Strong upsell conversion");
            else if (report.UpsellAttempts == 0)
                report.ImprovementAreas.Add("No upsell attempts — try suggesting bundles or upgrades");
            else if (report.UpsellConversionRate < 0.15)
                report.ImprovementAreas.Add("Low upsell conversion — review pitch approach");

            // Speed
            if (report.AverageTransactionSeconds > 0 && report.AverageTransactionSeconds <= 90)
                report.Strengths.Add("Fast transaction processing");
            else if (report.AverageTransactionSeconds > 300)
                report.ImprovementAreas.Add("Slow transaction times — look for workflow optimizations");

            // Volume
            var days = Math.Max(1, (report.PeriodEnd - report.PeriodStart).TotalDays);
            var dailyAvg = report.TotalTransactions / days;
            if (dailyAvg >= 8)
                report.Strengths.Add("High transaction volume");
            else if (dailyAvg < 3)
                report.ImprovementAreas.Add("Low transaction volume");

            // Revenue per transaction
            if (report.AverageRevenuePerTransaction >= 30)
                report.Strengths.Add("High revenue per transaction");
            else if (report.AverageRevenuePerTransaction < 10 && report.TotalTransactions > 5)
                report.ImprovementAreas.Add("Low average revenue — suggest premium rentals");

            // Five-star streak
            if (report.FiveStarCount >= 5)
                report.Strengths.Add($"Five-star ratings: {report.FiveStarCount}");

            // One-star awareness
            if (report.OneStarCount >= 3)
                report.ImprovementAreas.Add($"Multiple 1-star ratings ({report.OneStarCount}) — review customer interactions");
        }
    }
}

