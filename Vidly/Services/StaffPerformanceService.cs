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
            _clock = clock ?? new SystemClock();
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
                HireDate = hireDate ?? _clock.Today,
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

            // Single pass: accumulate all metrics instead of 13+ separate
            // LINQ scans over the same list (was O(13N), now O(N)).
            decimal totalRevenue = 0m;
            int rentalCount = 0, returnCount = 0, reservationCount = 0,
                giftCardCount = 0, upgradeCount = 0;
            int upsellAttempts = 0, upsellSuccesses = 0;
            long durationSum = 0;
            int durationCount = 0, fastestDuration = int.MaxValue, slowestDuration = 0;
            int ratedCount = 0, ratingSum = 0, fiveStarCount = 0, oneStarCount = 0;

            foreach (var t in txns)
            {
                totalRevenue += t.Revenue;

                switch (t.Type)
                {
                    case StaffTransactionType.Rental:            rentalCount++; break;
                    case StaffTransactionType.Return:            returnCount++; break;
                    case StaffTransactionType.Reservation:       reservationCount++; break;
                    case StaffTransactionType.GiftCardSale:      giftCardCount++; break;
                    case StaffTransactionType.MembershipUpgrade: upgradeCount++; break;
                }

                if (t.DurationSeconds > 0)
                {
                    durationSum += t.DurationSeconds;
                    durationCount++;
                    if (t.DurationSeconds < fastestDuration) fastestDuration = t.DurationSeconds;
                    if (t.DurationSeconds > slowestDuration) slowestDuration = t.DurationSeconds;
                }

                if (t.UpsellAttempted) { upsellAttempts++; if (t.UpsellAccepted) upsellSuccesses++; }

                if (t.SatisfactionRating.HasValue)
                {
                    var rating = t.SatisfactionRating.Value;
                    ratedCount++;
                    ratingSum += rating;
                    if (rating == 5) fiveStarCount++;
                    else if (rating == 1) oneStarCount++;
                }
            }

            // Volume
            report.TotalTransactions = txns.Count;
            report.RentalCount = rentalCount;
            report.ReturnCount = returnCount;
            report.ReservationCount = reservationCount;
            report.GiftCardSaleCount = giftCardCount;
            report.MembershipUpgradeCount = upgradeCount;

            // Revenue
            report.TotalRevenue = totalRevenue;
            report.AverageRevenuePerTransaction = totalRevenue / txns.Count;

            // Speed
            if (durationCount > 0)
            {
                report.AverageTransactionSeconds = (double)durationSum / durationCount;
                report.FastestTransactionSeconds = fastestDuration;
                report.SlowestTransactionSeconds = slowestDuration;
            }

            // Upsell
            report.UpsellAttempts = upsellAttempts;
            report.UpsellSuccesses = upsellSuccesses;
            report.UpsellConversionRate = upsellAttempts > 0
                ? (double)upsellSuccesses / upsellAttempts
                : 0;

            // Satisfaction
            report.TotalRatings = ratedCount;
            if (ratedCount > 0)
            {
                report.AverageSatisfactionRating = (double)ratingSum / ratedCount;
                report.FiveStarCount = fiveStarCount;
                report.OneStarCount = oneStarCount;
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

            // Pre-group transactions by staffId so each member lookup
            // is O(Ti) instead of scanning the full O(T) list.
            var txnsByStaff = new Dictionary<int, List<StaffTransaction>>();
            foreach (var t in _transactions)
            {
                if (t.Timestamp < periodStart || t.Timestamp >= periodEnd) continue;
                if (!txnsByStaff.TryGetValue(t.StaffId, out var list))
                {
                    list = new List<StaffTransaction>();
                    txnsByStaff[t.StaffId] = list;
                }
                list.Add(t);
            }

            foreach (var member in activeStaff)
            {
                if (!txnsByStaff.TryGetValue(member.Id, out var txns) || txns.Count == 0)
                    continue;

                // Reuse ScoreComponents to get both composite score and
                // satisfaction avg in a single pass (was 2 separate scans).
                var comp = ComputeScoreComponents(txns);
                var score = Math.Round(
                    comp.Volume * _volumeWeight +
                    comp.Revenue * _revenueWeight +
                    comp.Satisfaction * _satisfactionWeight +
                    comp.Upsell * _upsellWeight +
                    comp.Speed * _speedWeight,
                    1);

                entries.Add(new StaffRankingEntry
                {
                    StaffId = member.Id,
                    StaffName = member.Name,
                    Role = member.Role,
                    Score = score,
                    Grade = ScoreToGrade(score),
                    Transactions = txns.Count,
                    Revenue = txns.Aggregate(0m, (sum, t) => sum + t.Revenue),
                    SatisfactionAvg = comp.RatedCount > 0 ? comp.SatisfactionAvg : 0
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

            var rankings = GetLeaderboard(periodStart, periodEnd);

            // Single pass: accumulate team stats instead of 8+ separate LINQ
            // scans over the same list (was O(8N + T×N), now O(N)).
            var staffIds = new HashSet<int>();
            decimal teamRevenue = 0m;
            int teamRatingSum = 0, teamRatedCount = 0;
            int teamUpsellAttempts = 0, teamUpsellSuccesses = 0;
            long teamDurationSum = 0;
            int teamDurationCount = 0;
            var typeCounts = new Dictionary<StaffTransactionType, int>();

            foreach (var t in txns)
            {
                staffIds.Add(t.StaffId);
                teamRevenue += t.Revenue;

                if (t.SatisfactionRating.HasValue)
                {
                    teamRatedCount++;
                    teamRatingSum += t.SatisfactionRating.Value;
                }

                if (t.UpsellAttempted) { teamUpsellAttempts++; if (t.UpsellAccepted) teamUpsellSuccesses++; }

                if (t.DurationSeconds > 0)
                {
                    teamDurationSum += t.DurationSeconds;
                    teamDurationCount++;
                }

                if (!typeCounts.TryGetValue(t.Type, out var cnt))
                    cnt = 0;
                typeCounts[t.Type] = cnt + 1;
            }

            var activeStaffInPeriod = staffIds.Count;

            var summary = new TeamPerformanceSummary
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                ActiveStaffCount = activeStaffInPeriod,
                TotalTransactions = txns.Count,
                TotalRevenue = teamRevenue,
                AvgTransactionsPerStaff = activeStaffInPeriod > 0 ? (double)txns.Count / activeStaffInPeriod : 0,
                AvgRevenuePerStaff = activeStaffInPeriod > 0 ? (double)(teamRevenue / activeStaffInPeriod) : 0,
                TeamSatisfactionAvg = teamRatedCount > 0 ? Math.Round((double)teamRatingSum / teamRatedCount, 2) : 0,
                TeamUpsellRate = teamUpsellAttempts > 0
                    ? (double)teamUpsellSuccesses / teamUpsellAttempts : 0,
                AvgTransactionSeconds = teamDurationCount > 0
                    ? (double)teamDurationSum / teamDurationCount : 0,
                Rankings = rankings,
                TopPerformer = rankings.FirstOrDefault(),
                TransactionBreakdown = typeCounts
            };

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
            var startDate = from.Date;
            var endDate = to.Date;

            // Single pass O(T) to bucket counts by day, replacing O(D×T)
            // nested scan that re-iterated all transactions per day.
            var dayCounts = new Dictionary<DateTime, int>();
            foreach (var t in _transactions)
            {
                if (t.StaffId != staffId) continue;
                var day = t.Timestamp.Date;
                if (day < startDate || day > endDate) continue;
                if (!dayCounts.TryGetValue(day, out var c))
                    c = 0;
                dayCounts[day] = c + 1;
            }

            // Emit contiguous day entries (including zero-count days)
            var trend = new List<KeyValuePair<DateTime, int>>();
            var date = startDate;
            while (date <= endDate)
            {
                dayCounts.TryGetValue(date, out var count);
                trend.Add(new KeyValuePair<DateTime, int>(date, count));
                date = date.AddDays(1);
            }

            return trend;
        }

        // ══════════════════════════════════════════════════════
        //  Private Helpers
        // ══════════════════════════════════════════════════════

        // ── Score components ────────────────────────────
        // Extracted so callers (GetLeaderboard, etc.) can reuse
        // intermediate metrics (e.g. satisfactionAvg) without
        // re-scanning the transaction list.

        private struct ScoreComponents
        {
            public double Volume, Revenue, Satisfaction, Upsell, Speed;
            public double SatisfactionAvg;
            public int RatedCount;
        }

        /// <summary>
        /// Single-pass computation of all scoring dimensions plus
        /// commonly-needed aggregates (satisfactionAvg, ratedCount).
        /// Replaces 8+ separate LINQ scans with one O(N) traversal.
        /// </summary>
        private ScoreComponents ComputeScoreComponents(List<StaffTransaction> txns)
        {
            if (txns.Count == 0) return default;

            decimal totalRevenue = 0m;
            DateTime minTs = DateTime.MaxValue, maxTs = DateTime.MinValue;
            int ratedCount = 0, ratingSum = 0;
            int upsellAttempts = 0, upsellAccepted = 0;
            long durationSum = 0;
            int durationCount = 0;

            foreach (var t in txns)
            {
                totalRevenue += t.Revenue;
                if (t.Timestamp < minTs) minTs = t.Timestamp;
                if (t.Timestamp > maxTs) maxTs = t.Timestamp;

                if (t.SatisfactionRating.HasValue)
                {
                    ratedCount++;
                    ratingSum += t.SatisfactionRating.Value;
                }

                if (t.UpsellAttempted)
                {
                    upsellAttempts++;
                    if (t.UpsellAccepted) upsellAccepted++;
                }

                if (t.DurationSeconds > 0)
                {
                    durationSum += t.DurationSeconds;
                    durationCount++;
                }
            }

            // Volume: benchmark = 10 transactions/day -> 100
            var days = Math.Max(1, (maxTs - minTs).TotalDays + 1);
            var volumeScore = Math.Min(100, (txns.Count / days) / 10.0 * 100);

            // Revenue: benchmark = $50/transaction -> 100
            var avgRevenue = (double)totalRevenue / txns.Count;
            var revenueScore = Math.Min(100, avgRevenue / 50.0 * 100);

            // Satisfaction: 5.0 -> 100, 1.0 -> 0
            double satAvg = ratedCount > 0 ? (double)ratingSum / ratedCount : 0;
            var satisfactionScore = ratedCount > 0
                ? (satAvg - 1.0) / 4.0 * 100
                : 50;

            // Upsell: 50% conversion -> 100
            double upsellScore;
            if (upsellAttempts == 0)
                upsellScore = 30;
            else
                upsellScore = Math.Min(100, ((double)upsellAccepted / upsellAttempts) / 0.5 * 100);

            // Speed: 60s = 100, 120s = 75
            double speedScore;
            if (durationCount == 0)
                speedScore = 50;
            else
            {
                var avgDuration = (double)durationSum / durationCount;
                speedScore = avgDuration > 0 ? Math.Min(100, 120.0 / avgDuration * 75) : 50;
            }

            return new ScoreComponents
            {
                Volume = volumeScore,
                Revenue = revenueScore,
                Satisfaction = satisfactionScore,
                Upsell = upsellScore,
                Speed = speedScore,
                SatisfactionAvg = Math.Round(satAvg, 2),
                RatedCount = ratedCount
            };
        }

        /// <summary>
        /// Calculate a composite performance score (0-100) from transactions.
        /// Delegates to <see cref="ComputeScoreComponents"/> for the heavy lifting.
        /// </summary>
        private double CalculateScore(List<StaffTransaction> txns)
        {
            var c = ComputeScoreComponents(txns);
            return Math.Round(
                c.Volume * _volumeWeight +
                c.Revenue * _revenueWeight +
                c.Satisfaction * _satisfactionWeight +
                c.Upsell * _upsellWeight +
                c.Speed * _speedWeight,
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

