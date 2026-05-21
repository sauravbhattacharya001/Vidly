using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ── Enums ─────────────────────────────────────────────────────

    /// <summary>
    /// Per-movie waitlist verdict, roughly ordered by severity.
    /// </summary>
    public enum WaitlistVerdict
    {
        Healthy = 0,
        ListOk = 1,
        StaleList = 2,
        DeepBacklog = 3,
        ChronicAbandonment = 4,
        LongWaiter = 5,
        UrgentNeglected = 6,
        WindowClosing = 7,
        ExpiredNotified = 8
    }

    /// <summary>
    /// Priority bucket for advisor playbook actions.
    /// Mirrors <see cref="HealthActionPriority"/> but kept separate so the
    /// two services can evolve independently.
    /// </summary>
    public enum WaitlistActionPriority { P0, P1, P2, P3 }

    /// <summary>
    /// Risk-appetite knob. Cautious flags issues earlier; Aggressive
    /// trims low-priority noise from the playbook.
    /// </summary>
    public enum WaitlistConversionAppetite { Cautious, Balanced, Aggressive }

    // ── Models ────────────────────────────────────────────────────

    /// <summary>
    /// Diagnostic for a single movie's waitlist.
    /// </summary>
    public class WaitlistConversionCase
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public WaitlistVerdict Verdict { get; set; }
        public WaitlistActionPriority Priority { get; set; }
        public int Risk { get; set; } // 0..100
        public int ActiveWaitingCount { get; set; }
        public int NotifiedCount { get; set; }
        public int ExpiredNotifiedCount { get; set; }
        public int ClosingWindowCount { get; set; }
        public int MaxWaiterDays { get; set; }
        public int AbandonRatePct { get; set; }      // 0..100 over recent N
        public int FulfillRatePct { get; set; }      // 0..100 over recent N
        public bool HasUrgentNeglected { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>
    /// Cross-movie remediation action recommended by the advisor.
    /// </summary>
    public class WaitlistPlaybookAction
    {
        public string Id { get; set; }
        public WaitlistActionPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; }
        public int BlastRadius { get; set; }
        public string Reversibility { get; set; }
        public List<int> TargetMovieIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// Portfolio-level summary across all audited waitlists.
    /// </summary>
    public class WaitlistConversionSummary
    {
        public int TotalCases { get; set; }
        public int P0Count { get; set; }
        public int P1Count { get; set; }
        public int P2Count { get; set; }
        public int ActiveWaitingCount { get; set; }
        public int NotifiedCount { get; set; }
        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public string Headline { get; set; }
        public List<string> Insights { get; set; } = new List<string>();
    }

    public class WaitlistConversionReport
    {
        public DateTime AsOfDate { get; set; }
        public WaitlistConversionAppetite RiskAppetite { get; set; }
        public WaitlistConversionSummary Summary { get; set; } = new WaitlistConversionSummary();
        public List<WaitlistConversionCase> Cases { get; set; } = new List<WaitlistConversionCase>();
        public List<WaitlistPlaybookAction> Playbook { get; set; } = new List<WaitlistPlaybookAction>();
    }

    // ── Service ───────────────────────────────────────────────────

    /// <summary>
    /// Agentic per-movie waitlist conversion advisor — 5th Vidly agentic
    /// sibling. Where <see cref="ReviewIntelligenceService"/> audits movies
    /// (reputation), <see cref="DamageRiskForecastService"/> audits active
    /// rentals (damage), <see cref="LateReturnEscalationService"/> audits
    /// overdue rentals (escalation), <see cref="RefundFraudTriageService"/>
    /// audits pending refunds (fraud), and
    /// <see cref="ReservationHealthAdvisorService"/> audits reservation
    /// queues — this one audits the *waitlist* funnel:
    /// expired notification windows, soon-to-expire holds, urgent-priority
    /// waiters that have been ignored, long-tail waiters at churn risk,
    /// chronic abandonment patterns, deep backlogs, and stale lists with
    /// no fulfillment activity. Emits a per-movie case list + cross-movie
    /// playbook + portfolio summary.
    ///
    /// Pure read-only - never mutates repositories.
    /// </summary>
    public class WaitlistConversionAdvisorService
    {
        private readonly IWaitlistRepository _waitlist;
        private readonly IMovieRepository _movies;
        private readonly IClock _clock;

        /// <summary>Daily rate at or above which a movie is treated as high-value.</summary>
        public const decimal HighValueDailyRate = 4.00m;

        /// <summary>Active waiters at or above which a list is flagged as a deep backlog.</summary>
        public const int DeepBacklogMin = 6;

        /// <summary>Waiter day count above which churn risk fires.</summary>
        public const int LongWaiterDays = 21;

        /// <summary>Urgent-priority waiter days that trigger neglect flag.</summary>
        public const int UrgentNeglectDays = 3;

        /// <summary>Closing-window threshold (hours) before notification expiry.</summary>
        public const double WindowClosingHours = 24.0;

        /// <summary>Days with zero fulfillment after which a list is "stale".</summary>
        public const int StaleListDays = 30;

        /// <summary>Recent-entry window size used for abandon / fulfill rates.</summary>
        public const int RecentWindowSize = 10;

        public WaitlistConversionAdvisorService(
            IWaitlistRepository waitlist,
            IMovieRepository movies,
            IClock clock)
        {
            _waitlist = waitlist ?? throw new ArgumentNullException(nameof(waitlist));
            _movies = movies ?? throw new ArgumentNullException(nameof(movies));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ── Report generation ────────────────────────────────────

        public WaitlistConversionReport GenerateReport(
            WaitlistConversionAppetite appetite = WaitlistConversionAppetite.Balanced)
        {
            var now = _clock.Now;
            var report = new WaitlistConversionReport
            {
                AsOfDate = now.Date,
                RiskAppetite = appetite
            };

            var all = _waitlist.GetAll()?.ToList() ?? new List<WaitlistEntry>();

            // Group by movie. Full history (incl. Fulfilled/Expired/Cancelled)
            // is needed for the abandonment + staleness signals, but a movie
            // only earns a case when it has at least one Waiting or Notified
            // entry right now.
            var byMovie = new Dictionary<int, List<WaitlistEntry>>();
            foreach (var e in all)
            {
                if (e == null) continue;
                if (!byMovie.TryGetValue(e.MovieId, out var bucket))
                {
                    bucket = new List<WaitlistEntry>();
                    byMovie[e.MovieId] = bucket;
                }
                bucket.Add(e);
            }

            foreach (var kv in byMovie)
            {
                var movieId = kv.Key;
                var bucket = kv.Value;

                var active = bucket
                    .Where(e => e.Status == WaitlistStatus.Waiting ||
                                e.Status == WaitlistStatus.Notified)
                    .ToList();

                if (active.Count == 0) continue;

                var movie = _movies != null ? _movies.GetById(movieId) : null;
                var movieName = movie?.Name ?? bucket[0].MovieName ?? ("Movie#" + movieId);
                var dailyRate = movie?.DailyRate ?? 0m;
                var isHighValue = dailyRate >= HighValueDailyRate;

                var theCase = BuildCase(movieId, movieName, isHighValue, bucket, active, now, appetite);
                report.Cases.Add(theCase);
            }

            // Deterministic case order: highest risk first, then movieId asc.
            report.Cases = report.Cases
                .OrderByDescending(c => c.Risk)
                .ThenBy(c => c.MovieId)
                .ToList();

            BuildPlaybook(report, appetite);
            BuildSummary(report, appetite);

            return report;
        }

        // ── Per-movie case construction ──────────────────────────

        private WaitlistConversionCase BuildCase(
            int movieId,
            string movieName,
            bool isHighValue,
            List<WaitlistEntry> all,
            List<WaitlistEntry> active,
            DateTime now,
            WaitlistConversionAppetite appetite)
        {
            var reasons = new List<string>();

            int waitingCount = active.Count(e => e.Status == WaitlistStatus.Waiting);
            int notifiedCount = active.Count(e => e.Status == WaitlistStatus.Notified);

            // Deep backlog.
            int depth = active.Count;
            if (depth >= DeepBacklogMin)
                reasons.Add("LIST_DEPTH_" + depth.ToString(CultureInfo.InvariantCulture));

            // Expired notifications (pickup window already passed).
            var expiredNotified = active
                .Where(e => e.Status == WaitlistStatus.Notified
                            && e.ExpiresAt.HasValue
                            && now > e.ExpiresAt.Value)
                .ToList();
            if (expiredNotified.Count > 0)
                reasons.Add("NOTIFY_EXPIRED_" + expiredNotified.Count.ToString(CultureInfo.InvariantCulture));

            // Closing notification windows.
            var closing = active
                .Where(e => e.Status == WaitlistStatus.Notified
                            && e.ExpiresAt.HasValue
                            && now <= e.ExpiresAt.Value
                            && (e.ExpiresAt.Value - now).TotalHours <= WindowClosingHours)
                .ToList();
            if (closing.Count > 0)
                reasons.Add("WINDOW_CLOSING_" + closing.Count.ToString(CultureInfo.InvariantCulture));

            // Long-waiter / churn risk (Waiting entries only).
            int maxWait = 0;
            foreach (var e in active.Where(x => x.Status == WaitlistStatus.Waiting))
            {
                var w = Math.Max(0, (int)Math.Ceiling((now - e.JoinedAt).TotalDays));
                if (w > maxWait) maxWait = w;
            }
            if (maxWait > LongWaiterDays)
                reasons.Add("LONG_WAITER_" + maxWait.ToString(CultureInfo.InvariantCulture) + "D");

            // Urgent priority waiting too long.
            var urgentNeglect = active.Any(e =>
                e.Status == WaitlistStatus.Waiting &&
                e.Priority == WaitlistPriority.Urgent &&
                (now - e.JoinedAt).TotalDays > UrgentNeglectDays);
            if (urgentNeglect)
                reasons.Add("URGENT_NEGLECTED");

            // Chronic abandonment over recent N.
            var recent = all
                .OrderByDescending(e => e.JoinedAt)
                .ThenByDescending(e => e.Id)
                .Take(RecentWindowSize)
                .ToList();
            int abandoned = recent.Count(e => e.Status == WaitlistStatus.Expired ||
                                              e.Status == WaitlistStatus.Cancelled);
            int fulfilled = recent.Count(e => e.Status == WaitlistStatus.Fulfilled);
            int abandonPct = recent.Count == 0 ? 0 : (int)Math.Round(100.0 * abandoned / recent.Count);
            int fulfillPct = recent.Count == 0 ? 0 : (int)Math.Round(100.0 * fulfilled / recent.Count);
            if (recent.Count >= 4 && abandonPct >= 50)
                reasons.Add("ABANDON_RATE_" + abandonPct.ToString(CultureInfo.InvariantCulture) + "PCT");

            // Stale list: no Fulfilled entry within StaleListDays AND oldest
            // active waiter exceeds the same threshold.
            var lastFulfillment = all
                .Where(e => e.Status == WaitlistStatus.Fulfilled)
                .Select(e => e.NotifiedAt ?? e.JoinedAt)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            var staleNoFulfill = lastFulfillment == DateTime.MinValue ||
                                 (now - lastFulfillment).TotalDays > StaleListDays;
            if (staleNoFulfill && maxWait >= StaleListDays && notifiedCount == 0)
                reasons.Add("STALE_LIST_" + maxWait.ToString(CultureInfo.InvariantCulture) + "D");

            // ── Verdict + priority + risk ────────────────────────

            var verdict = WaitlistVerdict.ListOk;
            var priority = WaitlistActionPriority.P3;
            int risk = 10;

            if (expiredNotified.Count > 0)
            {
                verdict = WaitlistVerdict.ExpiredNotified;
                priority = WaitlistActionPriority.P0;
                risk = Math.Max(risk, 90 + Math.Min(10, expiredNotified.Count));
            }
            else if (closing.Count > 0 && !urgentNeglect && maxWait <= LongWaiterDays)
            {
                verdict = WaitlistVerdict.WindowClosing;
                priority = WaitlistActionPriority.P1;
                risk = Math.Max(risk, 70 + Math.Min(15, closing.Count * 3));
            }

            if (urgentNeglect)
            {
                if (verdict < WaitlistVerdict.UrgentNeglected)
                    verdict = WaitlistVerdict.UrgentNeglected;
                if (priority > WaitlistActionPriority.P1) priority = WaitlistActionPriority.P1;
                risk = Math.Max(risk, 75);
            }

            if (maxWait > LongWaiterDays && verdict < WaitlistVerdict.LongWaiter)
            {
                verdict = WaitlistVerdict.LongWaiter;
                if (priority > WaitlistActionPriority.P2) priority = WaitlistActionPriority.P2;
                risk = Math.Max(risk, 55 + Math.Min(20, maxWait - LongWaiterDays));
            }

            if (recent.Count >= 4 && abandonPct >= 50 && verdict < WaitlistVerdict.ChronicAbandonment)
            {
                verdict = WaitlistVerdict.ChronicAbandonment;
                if (priority > WaitlistActionPriority.P2) priority = WaitlistActionPriority.P2;
                risk = Math.Max(risk, 60 + Math.Min(20, abandonPct - 50));
            }

            if (depth >= DeepBacklogMin && verdict < WaitlistVerdict.DeepBacklog)
            {
                verdict = WaitlistVerdict.DeepBacklog;
                if (priority > WaitlistActionPriority.P2) priority = WaitlistActionPriority.P2;
                risk = Math.Max(risk, 50 + Math.Min(25, (depth - DeepBacklogMin) * 4));
            }

            if (staleNoFulfill && maxWait >= StaleListDays && notifiedCount == 0
                && verdict < WaitlistVerdict.StaleList)
            {
                verdict = WaitlistVerdict.StaleList;
                if (priority > WaitlistActionPriority.P3) priority = WaitlistActionPriority.P3;
                risk = Math.Max(risk, 40);
            }

            if (isHighValue && risk >= 50) risk = Math.Min(100, risk + 5);

            // Cautious: boost risk and bump priority one notch up for non-P0.
            if (appetite == WaitlistConversionAppetite.Cautious)
            {
                risk = Math.Min(100, risk + 5);
                if (priority == WaitlistActionPriority.P2) priority = WaitlistActionPriority.P1;
                else if (priority == WaitlistActionPriority.P3) priority = WaitlistActionPriority.P2;
            }
            // Aggressive: trim low-risk noise.
            else if (appetite == WaitlistConversionAppetite.Aggressive)
            {
                if (priority == WaitlistActionPriority.P3) risk = Math.Max(0, risk - 5);
            }

            if (reasons.Count == 0)
            {
                verdict = WaitlistVerdict.ListOk;
                priority = WaitlistActionPriority.P3;
                risk = Math.Min(risk, 15);
                reasons.Add("LIST_OK");
            }

            return new WaitlistConversionCase
            {
                MovieId = movieId,
                MovieName = movieName,
                Verdict = verdict,
                Priority = priority,
                Risk = Math.Max(0, Math.Min(100, risk)),
                ActiveWaitingCount = waitingCount,
                NotifiedCount = notifiedCount,
                ExpiredNotifiedCount = expiredNotified.Count,
                ClosingWindowCount = closing.Count,
                MaxWaiterDays = maxWait,
                AbandonRatePct = abandonPct,
                FulfillRatePct = fulfillPct,
                HasUrgentNeglected = urgentNeglect,
                Reasons = reasons
            };
        }

        // ── Playbook ─────────────────────────────────────────────

        private void BuildPlaybook(WaitlistConversionReport report, WaitlistConversionAppetite appetite)
        {
            if (report.Cases.Count == 0)
            {
                report.Playbook.Add(new WaitlistPlaybookAction
                {
                    Id = "waitlist_healthy",
                    Priority = WaitlistActionPriority.P3,
                    Label = "Waitlists are healthy - no remediation required",
                    Reason = "No active waitlist entries found in the audit window.",
                    Owner = "ops",
                    BlastRadius = 0,
                    Reversibility = "n/a"
                });
                return;
            }

            void Add(string id, WaitlistActionPriority p, string label, string reason,
                     string owner, int blast, string reversibility, IEnumerable<int> targets)
            {
                var ta = targets?.OrderBy(x => x).ToList() ?? new List<int>();
                report.Playbook.Add(new WaitlistPlaybookAction
                {
                    Id = id,
                    Priority = p,
                    Label = label,
                    Reason = reason,
                    Owner = owner,
                    BlastRadius = blast,
                    Reversibility = reversibility,
                    TargetMovieIds = ta
                });
            }

            var expired = report.Cases.Where(c => c.ExpiredNotifiedCount > 0).ToList();
            if (expired.Count > 0)
            {
                Add("retire_expired_notifications",
                    WaitlistActionPriority.P0,
                    "Retire " + expired.Sum(c => c.ExpiredNotifiedCount) +
                        " expired waitlist notifications and re-notify next in line",
                    "Notifications past their pickup window block fulfillment and erode trust.",
                    "ops",
                    expired.Sum(c => c.ExpiredNotifiedCount),
                    "reversible",
                    expired.Select(c => c.MovieId));
            }

            var urgent = report.Cases.Where(c => c.HasUrgentNeglected).ToList();
            if (urgent.Count > 0)
            {
                Add("fast_track_urgent_waiters",
                    WaitlistActionPriority.P1,
                    "Fast-track " + urgent.Count + " urgent-priority waiters past SLA",
                    "Urgent (pre-order) waiters older than " + UrgentNeglectDays +
                        "d signal SLA breach for highest-priority customers.",
                    "customer-success",
                    urgent.Count,
                    "reversible",
                    urgent.Select(c => c.MovieId));
            }

            var closing = report.Cases.Where(c => c.ClosingWindowCount > 0 && c.ExpiredNotifiedCount == 0).ToList();
            if (closing.Count > 0)
            {
                Add("nudge_closing_windows",
                    WaitlistActionPriority.P1,
                    "Send pickup reminders for " + closing.Sum(c => c.ClosingWindowCount) +
                        " holds expiring within " + (int)WindowClosingHours + "h",
                    "Preempting window expiry reduces re-notification churn and abandoned holds.",
                    "ops",
                    closing.Sum(c => c.ClosingWindowCount),
                    "reversible",
                    closing.Select(c => c.MovieId));
            }

            var longWait = report.Cases.Where(c => c.MaxWaiterDays > LongWaiterDays).ToList();
            if (longWait.Count > 0)
            {
                Add("outreach_long_waiters",
                    WaitlistActionPriority.P2,
                    "Outreach to long-tail waiters on " + longWait.Count + " movie list(s)",
                    "Waiters past " + LongWaiterDays + "d are at elevated abandon / churn risk.",
                    "customer-success",
                    longWait.Count,
                    "reversible",
                    longWait.Select(c => c.MovieId));
            }

            var chronic = report.Cases.Where(c => c.AbandonRatePct >= 50).ToList();
            if (chronic.Count > 0)
            {
                Add("audit_chronic_abandonment",
                    WaitlistActionPriority.P2,
                    "Audit copy supply and notification timing on " + chronic.Count + " chronically-abandoned list(s)",
                    "Abandon rate >= 50% over recent entries suggests structural supply or comms issues.",
                    "merch",
                    chronic.Count,
                    "reversible",
                    chronic.Select(c => c.MovieId));
            }

            var deep = report.Cases.Where(c => c.ActiveWaitingCount + c.NotifiedCount >= DeepBacklogMin).ToList();
            if (deep.Count > 0 && appetite != WaitlistConversionAppetite.Aggressive)
            {
                Add("expand_copy_supply",
                    WaitlistActionPriority.P2,
                    "Evaluate adding rental copies for " + deep.Count + " deep-backlog title(s)",
                    "Active list >= " + DeepBacklogMin + " entries; backlog grows faster than fulfillment.",
                    "merch",
                    deep.Count,
                    "reversible",
                    deep.Select(c => c.MovieId));
            }

            var stale = report.Cases.Where(c => c.Verdict == WaitlistVerdict.StaleList).ToList();
            if (stale.Count > 0 && appetite != WaitlistConversionAppetite.Aggressive)
            {
                Add("review_stale_lists",
                    WaitlistActionPriority.P3,
                    "Review " + stale.Count + " stale list(s) with no recent fulfillment",
                    "No fulfilled entry within " + StaleListDays + "d while waiters linger - confirm the title is still rentable.",
                    "merch",
                    stale.Count,
                    "reversible",
                    stale.Select(c => c.MovieId));
            }

            if (report.Playbook.Count == 0)
            {
                Add("monitor_only",
                    WaitlistActionPriority.P3,
                    "Continue monitoring - no actionable signals",
                    "All active lists are within tolerance bands.",
                    "ops",
                    0,
                    "n/a",
                    Enumerable.Empty<int>());
            }

            // Sort by priority asc, then label asc for stability.
            report.Playbook = report.Playbook
                .OrderBy(a => (int)a.Priority)
                .ThenBy(a => a.Label, StringComparer.Ordinal)
                .ToList();
        }

        // ── Summary ──────────────────────────────────────────────

        private void BuildSummary(WaitlistConversionReport report, WaitlistConversionAppetite appetite)
        {
            var s = report.Summary;
            s.TotalCases = report.Cases.Count;
            s.P0Count = report.Cases.Count(c => c.Priority == WaitlistActionPriority.P0);
            s.P1Count = report.Cases.Count(c => c.Priority == WaitlistActionPriority.P1);
            s.P2Count = report.Cases.Count(c => c.Priority == WaitlistActionPriority.P2);
            s.ActiveWaitingCount = report.Cases.Sum(c => c.ActiveWaitingCount);
            s.NotifiedCount = report.Cases.Sum(c => c.NotifiedCount);

            if (report.Cases.Count == 0)
            {
                s.OverallScore = 100;
                s.Grade = 'A';
                s.Headline = "All waitlists healthy";
                s.Insights.Add("INSUFFICIENT_DATA");
                return;
            }

            // Overall score: start at 100, subtract risk-weighted penalties.
            double score = 100.0;
            foreach (var c in report.Cases)
            {
                double w = c.Priority == WaitlistActionPriority.P0 ? 1.0
                         : c.Priority == WaitlistActionPriority.P1 ? 0.6
                         : c.Priority == WaitlistActionPriority.P2 ? 0.3
                         : 0.1;
                score -= w * (c.Risk / 100.0) * 18.0;
            }
            score = Math.Max(0, Math.Min(100, score));

            // Force-F if any P0 case exists (mirrors sibling advisor convention).
            if (s.P0Count > 0) score = Math.Min(score, 55);

            s.OverallScore = (int)Math.Round(score);
            s.Grade = s.OverallScore >= 90 ? 'A'
                    : s.OverallScore >= 80 ? 'B'
                    : s.OverallScore >= 70 ? 'C'
                    : s.OverallScore >= 60 ? 'D'
                    : 'F';

            if (s.P0Count > 0)
                s.Headline = s.P0Count + " critical waitlist issue(s) need immediate action";
            else if (s.P1Count > 0)
                s.Headline = s.P1Count + " waitlist case(s) need attention soon";
            else if (s.P2Count > 0)
                s.Headline = s.P2Count + " waitlist case(s) trending unhealthy";
            else
                s.Headline = "Waitlists within tolerance";

            if (s.P0Count > 0) s.Insights.Add("EXPIRED_NOTIFICATIONS_PRESENT");
            if (report.Cases.Any(c => c.HasUrgentNeglected)) s.Insights.Add("URGENT_NEGLECTED_PRESENT");
            if (report.Cases.Any(c => c.AbandonRatePct >= 50)) s.Insights.Add("CHRONIC_ABANDONMENT");
            if (report.Cases.Any(c => c.MaxWaiterDays > LongWaiterDays)) s.Insights.Add("LONG_TAIL_WAITERS");
            if (report.Cases.Any(c => (c.ActiveWaitingCount + c.NotifiedCount) >= DeepBacklogMin))
                s.Insights.Add("DEEP_BACKLOG");
            if (report.Cases.All(c => c.Verdict == WaitlistVerdict.ListOk))
                s.Insights.Add("ALL_LISTS_OK");

            s.Insights = s.Insights.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        // ── Renderers ────────────────────────────────────────────

        public string ToText(WaitlistConversionReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("Waitlist Conversion Advisor - " +
                          report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.AppendLine("Appetite: " + report.RiskAppetite);
            sb.AppendLine("Grade: " + report.Summary.Grade + "  Score: " + report.Summary.OverallScore);
            sb.AppendLine("VERDICT: " + (report.Cases.Count == 0
                ? "all_healthy"
                : report.Cases[0].Verdict.ToString()));
            sb.AppendLine("Headline: " + (report.Summary.Headline ?? ""));
            sb.AppendLine();
            sb.AppendLine("Cases (" + report.Cases.Count + "):");
            foreach (var c in report.Cases)
            {
                sb.AppendLine("  #" + c.MovieId + " " + c.MovieName +
                              "  [" + c.Priority + "] " + c.Verdict +
                              "  risk=" + c.Risk +
                              "  wait=" + c.ActiveWaitingCount +
                              "  notif=" + c.NotifiedCount +
                              "  maxWaitD=" + c.MaxWaiterDays);
                if (c.Reasons.Count > 0)
                    sb.AppendLine("    reasons: " + string.Join(", ", c.Reasons));
            }
            sb.AppendLine();
            sb.AppendLine("Playbook (" + report.Playbook.Count + "):");
            foreach (var a in report.Playbook)
            {
                sb.AppendLine("  [" + a.Priority + "] " + a.Id + " - " + a.Label);
            }
            return sb.ToString();
        }

        public string ToMarkdown(WaitlistConversionReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("# Waitlist Conversion Advisor");
            sb.AppendLine();
            sb.AppendLine("- **As of:** " + report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.AppendLine("- **Appetite:** " + report.RiskAppetite);
            sb.AppendLine("- **Grade:** " + report.Summary.Grade + " (score " + report.Summary.OverallScore + ")");
            sb.AppendLine("- **Headline:** " + (report.Summary.Headline ?? ""));
            sb.AppendLine();
            sb.AppendLine("## Cases");
            sb.AppendLine();
            if (report.Cases.Count == 0)
            {
                sb.AppendLine("_No active waitlists._");
            }
            else
            {
                sb.AppendLine("| Movie | Verdict | Priority | Risk | Waiting | Notified | MaxWaitD | Abandon% |");
                sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");
                foreach (var c in report.Cases)
                {
                    sb.AppendLine("| #" + c.MovieId + " " + (c.MovieName ?? "") +
                                  " | " + c.Verdict +
                                  " | " + c.Priority +
                                  " | " + c.Risk +
                                  " | " + c.ActiveWaitingCount +
                                  " | " + c.NotifiedCount +
                                  " | " + c.MaxWaiterDays +
                                  " | " + c.AbandonRatePct + " |");
                }
            }
            sb.AppendLine();
            sb.AppendLine("## Playbook");
            sb.AppendLine();
            if (report.Playbook.Count == 0)
            {
                sb.AppendLine("_No actions._");
            }
            else
            {
                foreach (var a in report.Playbook)
                {
                    sb.AppendLine("- **[" + a.Priority + "]** `" + a.Id + "` - " + a.Label +
                                  " _(owner: " + a.Owner + ", blast: " + a.BlastRadius + ")_");
                }
            }
            return sb.ToString();
        }

        public string ToJson(WaitlistConversionReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"asOfDate\": " + Q(report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + ",");
            sb.AppendLine("  \"riskAppetite\": " + Q(report.RiskAppetite.ToString()) + ",");

            // Cases
            sb.AppendLine("  \"cases\": [");
            for (int i = 0; i < report.Cases.Count; i++)
            {
                var c = report.Cases[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"abandonRatePct\": " + c.AbandonRatePct.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"activeWaitingCount\": " + c.ActiveWaitingCount.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"closingWindowCount\": " + c.ClosingWindowCount.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"expiredNotifiedCount\": " + c.ExpiredNotifiedCount.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"fulfillRatePct\": " + c.FulfillRatePct.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"hasUrgentNeglected\": " + (c.HasUrgentNeglected ? "true" : "false") + ",");
                sb.AppendLine("      \"maxWaiterDays\": " + c.MaxWaiterDays.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"movieId\": " + c.MovieId.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"movieName\": " + Q(c.MovieName) + ",");
                sb.AppendLine("      \"notifiedCount\": " + c.NotifiedCount.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"priority\": " + Q(c.Priority.ToString()) + ",");
                sb.Append("      \"reasons\": [");
                for (int j = 0; j < c.Reasons.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append(Q(c.Reasons[j]));
                }
                sb.AppendLine("],");
                sb.AppendLine("      \"risk\": " + c.Risk.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"verdict\": " + Q(c.Verdict.ToString()));
                sb.AppendLine("    }" + (i == report.Cases.Count - 1 ? "" : ","));
            }
            sb.AppendLine("  ],");

            // Playbook
            sb.AppendLine("  \"playbook\": [");
            for (int i = 0; i < report.Playbook.Count; i++)
            {
                var a = report.Playbook[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"blastRadius\": " + a.BlastRadius.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"id\": " + Q(a.Id) + ",");
                sb.AppendLine("      \"label\": " + Q(a.Label) + ",");
                sb.AppendLine("      \"owner\": " + Q(a.Owner) + ",");
                sb.AppendLine("      \"priority\": " + Q(a.Priority.ToString()) + ",");
                sb.AppendLine("      \"reason\": " + Q(a.Reason) + ",");
                sb.AppendLine("      \"reversibility\": " + Q(a.Reversibility) + ",");
                sb.Append("      \"targetMovieIds\": [");
                for (int j = 0; j < a.TargetMovieIds.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append(a.TargetMovieIds[j].ToString(CultureInfo.InvariantCulture));
                }
                sb.AppendLine("]");
                sb.AppendLine("    }" + (i == report.Playbook.Count - 1 ? "" : ","));
            }
            sb.AppendLine("  ],");

            // Summary
            sb.AppendLine("  \"summary\": {");
            sb.AppendLine("    \"activeWaitingCount\": " + report.Summary.ActiveWaitingCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"grade\": " + Q(report.Summary.Grade.ToString()) + ",");
            sb.AppendLine("    \"headline\": " + Q(report.Summary.Headline) + ",");
            sb.Append("    \"insights\": [");
            for (int j = 0; j < report.Summary.Insights.Count; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append(Q(report.Summary.Insights[j]));
            }
            sb.AppendLine("],");
            sb.AppendLine("    \"notifiedCount\": " + report.Summary.NotifiedCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"overallScore\": " + report.Summary.OverallScore.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"p0Count\": " + report.Summary.P0Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"p1Count\": " + report.Summary.P1Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"p2Count\": " + report.Summary.P2Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"totalCases\": " + report.Summary.TotalCases.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  }");
            sb.Append("}");
            return sb.ToString();
        }

        private static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)ch);
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
