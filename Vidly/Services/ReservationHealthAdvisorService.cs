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
    /// Per-movie reservation queue verdict, ordered roughly by severity.
    /// </summary>
    public enum QueueVerdict
    {
        Healthy = 0,
        QueueOk = 1,
        ChurnRisk = 2,
        ChronicAbandonment = 3,
        LongQueue = 4,
        QueueStarved = 5,
        BlockedByOverdueRental = 6,
        StalePickupReady = 7
    }

    /// <summary>
    /// Priority bucket for advisor playbook actions.
    /// </summary>
    public enum HealthActionPriority { P0, P1, P2, P3 }

    /// <summary>
    /// Risk-appetite knob. Cautious flags issues earlier; Aggressive trims
    /// low-priority noise from the playbook.
    /// </summary>
    public enum ReservationHealthAppetite { Cautious, Balanced, Aggressive }

    // ── Models ────────────────────────────────────────────────────

    /// <summary>
    /// Diagnostic for a single movie's reservation queue.
    /// </summary>
    public class MovieQueueCase
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public QueueVerdict Verdict { get; set; }
        public HealthActionPriority Priority { get; set; }
        public int Risk { get; set; } // 0..100
        public int ActiveReservationCount { get; set; }
        public int MaxWaiterDays { get; set; }
        public int AbandonRatePct { get; set; } // 0..100 across last 10 reservations
        public bool IsHighValueMovie { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>
    /// Cross-movie remediation action recommended by the advisor.
    /// </summary>
    public class HealthPlaybookAction
    {
        public string Id { get; set; }
        public HealthActionPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; }
        public int BlastRadius { get; set; }
        public string Reversibility { get; set; }
        public List<int> TargetMovieIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// Portfolio-level summary.
    /// </summary>
    public class ReservationHealthSummary
    {
        public int TotalCases { get; set; }
        public int P0Count { get; set; }
        public int P1Count { get; set; }
        public int P2Count { get; set; }
        public int ActiveReservationCount { get; set; }
        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public string Headline { get; set; }
        public List<string> Insights { get; set; } = new List<string>();
    }

    public class ReservationHealthReport
    {
        public DateTime AsOfDate { get; set; }
        public ReservationHealthAppetite RiskAppetite { get; set; }
        public ReservationHealthSummary Summary { get; set; } = new ReservationHealthSummary();
        public List<MovieQueueCase> Cases { get; set; } = new List<MovieQueueCase>();
        public List<HealthPlaybookAction> Playbook { get; set; } = new List<HealthPlaybookAction>();
    }

    // ── Service ───────────────────────────────────────────────────

    /// <summary>
    /// Agentic per-movie reservation queue health advisor.
    ///
    /// Sibling to <see cref="ReviewIntelligenceService"/>,
    /// <see cref="DamageRiskForecastService"/>, and
    /// <see cref="LateReturnEscalationService"/>. Where those services audit
    /// movies (reputation), active rentals (damage), and overdue rentals
    /// (escalation), this one audits the reservation queue: stale Ready holds,
    /// queues blocked behind heavily-overdue rentals, long-waiter churn risk,
    /// chronic abandonment patterns, and over-deep queues. Emits a per-movie
    /// case list + cross-movie playbook + portfolio summary.
    ///
    /// Pure read-only - never mutates repositories.
    /// </summary>
    public class ReservationHealthAdvisorService
    {
        private readonly IReservationRepository _reservations;
        private readonly IRentalRepository _rentals;
        private readonly IMovieRepository _movies;
        private readonly ICustomerRepository _customers;
        private readonly IClock _clock;

        /// <summary>Daily rate at or above which a movie is treated as high-value.</summary>
        public const decimal HighValueDailyRate = 4.00m;

        /// <summary>Queue depth at or above which a queue is flagged as long.</summary>
        public const int LongQueueDepthMin = 6;

        /// <summary>Waiter day count above which churn risk fires.</summary>
        public const int ChurnRiskWaiterDays = 21;

        /// <summary>Days an active rental must be overdue before it blocks the queue.</summary>
        public const int BlockingOverdueRentalDays = 7;

        /// <summary>Pickup-window remaining (days) below which the queue is starved.</summary>
        public const int QueueStarvedWindowDays = 1;

        public ReservationHealthAdvisorService(
            IReservationRepository reservations,
            IRentalRepository rentals,
            IMovieRepository movies,
            ICustomerRepository customers,
            IClock clock)
        {
            _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
            _movies = movies ?? throw new ArgumentNullException(nameof(movies));
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ── Report generation ────────────────────────────────────

        public ReservationHealthReport GenerateReport(
            ReservationHealthAppetite appetite = ReservationHealthAppetite.Balanced)
        {
            var today = _clock.Today;
            var report = new ReservationHealthReport
            {
                AsOfDate = today,
                RiskAppetite = appetite
            };

            var all = _reservations.GetAll() ?? (IReadOnlyList<Reservation>)new List<Reservation>().AsReadOnly();

            // Group all reservations by movie. We need full history (incl.
            // Expired/Cancelled/Fulfilled) for abandonment detection, but
            // a movie only earns a "case" when it has at least one active
            // (Waiting or Ready) reservation right now.
            var byMovie = new Dictionary<int, List<Reservation>>();
            foreach (var r in all)
            {
                if (r == null) continue;
                if (!byMovie.TryGetValue(r.MovieId, out var bucket))
                {
                    bucket = new List<Reservation>();
                    byMovie[r.MovieId] = bucket;
                }
                bucket.Add(r);
            }

            foreach (var kv in byMovie)
            {
                var movieId = kv.Key;
                var bucket = kv.Value;

                var active = bucket
                    .Where(r => r.Status == ReservationStatus.Waiting ||
                                r.Status == ReservationStatus.Ready)
                    .ToList();

                if (active.Count == 0) continue;

                var movie = _movies.GetById(movieId);
                var movieName = movie?.Name ?? bucket[0].MovieName ?? ("Movie#" + movieId);
                var dailyRate = movie?.DailyRate ?? 0m;
                var isHighValue = dailyRate >= HighValueDailyRate;

                var theCase = BuildCase(movieId, movieName, isHighValue, bucket, active, today, appetite);
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

        private MovieQueueCase BuildCase(
            int movieId,
            string movieName,
            bool isHighValue,
            List<Reservation> all,
            List<Reservation> active,
            DateTime today,
            ReservationHealthAppetite appetite)
        {
            var reasons = new List<string>();

            // Queue depth.
            var depth = active.Count;
            if (depth >= LongQueueDepthMin)
                reasons.Add("QUEUE_DEPTH_" + depth.ToString(CultureInfo.InvariantCulture));

            // Stale Ready (pickup window already passed).
            var staleReady = active
                .Where(r => r.Status == ReservationStatus.Ready
                            && r.ExpiresDate.HasValue
                            && today > r.ExpiresDate.Value)
                .ToList();
            if (staleReady.Count > 0)
                reasons.Add("READY_EXPIRED_" + staleReady.Count.ToString(CultureInfo.InvariantCulture));

            // Pickup-window closing soon (Ready, expires within QueueStarvedWindowDays).
            var starvedReady = active
                .Where(r => r.Status == ReservationStatus.Ready
                            && r.ExpiresDate.HasValue
                            && today <= r.ExpiresDate.Value
                            && (r.ExpiresDate.Value - today).TotalDays <= QueueStarvedWindowDays)
                .ToList();
            if (starvedReady.Count > 0)
                reasons.Add("PICKUP_WINDOW_CLOSING_" + starvedReady.Count.ToString(CultureInfo.InvariantCulture));

            // Long-waiter / churn risk.
            var maxWait = 0;
            foreach (var r in active)
            {
                var w = Math.Max(0, (int)Math.Ceiling((today - r.ReservedDate).TotalDays));
                if (w > maxWait) maxWait = w;
            }
            if (maxWait > ChurnRiskWaiterDays)
                reasons.Add("LONG_WAITER_" + maxWait.ToString(CultureInfo.InvariantCulture) + "D");

            // Blocked by deeply-overdue rental.
            var rentals = _rentals.GetByMovie(movieId) ?? (IReadOnlyList<Rental>)new List<Rental>().AsReadOnly();
            var blockingRental = rentals
                .Where(r => r.Status != RentalStatus.Returned
                            && today > r.DueDate
                            && (today - r.DueDate).TotalDays >= BlockingOverdueRentalDays)
                .OrderByDescending(r => (today - r.DueDate).TotalDays)
                .FirstOrDefault();
            if (blockingRental != null)
            {
                var od = (int)Math.Ceiling((today - blockingRental.DueDate).TotalDays);
                reasons.Add("RENTAL_OVERDUE_" + od.ToString(CultureInfo.InvariantCulture) + "D");
            }

            // Chronic abandonment over last 10 reservations.
            var recent = all
                .OrderByDescending(r => r.ReservedDate)
                .ThenByDescending(r => r.Id)
                .Take(10)
                .ToList();
            var abandoned = recent.Count(r => r.Status == ReservationStatus.Expired ||
                                              r.Status == ReservationStatus.Cancelled);
            var abandonPct = recent.Count == 0 ? 0 : (int)Math.Round(100.0 * abandoned / recent.Count);
            var chronicAbandon = recent.Count >= 5 && abandonPct >= 40;
            if (chronicAbandon)
                reasons.Add("ABANDON_RATE_" + abandonPct.ToString(CultureInfo.InvariantCulture) + "PCT");

            // Pick primary verdict (worst wins).
            QueueVerdict verdict;
            if (staleReady.Count > 0)
                verdict = QueueVerdict.StalePickupReady;
            else if (blockingRental != null)
                verdict = QueueVerdict.BlockedByOverdueRental;
            else if (starvedReady.Count > 0)
                verdict = QueueVerdict.QueueStarved;
            else if (depth >= LongQueueDepthMin)
                verdict = QueueVerdict.LongQueue;
            else if (chronicAbandon)
                verdict = QueueVerdict.ChronicAbandonment;
            else if (maxWait > ChurnRiskWaiterDays)
                verdict = QueueVerdict.ChurnRisk;
            else
                verdict = QueueVerdict.QueueOk;

            if (isHighValue) reasons.Add("HIGH_VALUE_TITLE");

            // Score.
            int baseRisk;
            switch (verdict)
            {
                case QueueVerdict.StalePickupReady: baseRisk = 80; break;
                case QueueVerdict.BlockedByOverdueRental: baseRisk = 75; break;
                case QueueVerdict.QueueStarved: baseRisk = 60; break;
                case QueueVerdict.ChronicAbandonment: baseRisk = 55; break;
                case QueueVerdict.LongQueue: baseRisk = 45; break;
                case QueueVerdict.ChurnRisk: baseRisk = 45; break;
                case QueueVerdict.QueueOk: baseRisk = 5; break;
                default: baseRisk = 5; break;
            }
            var raw = baseRisk + Math.Min(15, depth * 2) + (isHighValue ? 8 : 0);
            double mult;
            switch (appetite)
            {
                case ReservationHealthAppetite.Cautious: mult = 1.15; break;
                case ReservationHealthAppetite.Aggressive: mult = 0.85; break;
                default: mult = 1.0; break;
            }
            var risk = (int)Math.Round(raw * mult);
            if (risk < 0) risk = 0;
            if (risk > 100) risk = 100;

            // Per-case priority.
            HealthActionPriority priority;
            switch (verdict)
            {
                case QueueVerdict.StalePickupReady:
                case QueueVerdict.BlockedByOverdueRental:
                    priority = HealthActionPriority.P0; break;
                case QueueVerdict.QueueStarved:
                case QueueVerdict.LongQueue:
                case QueueVerdict.ChronicAbandonment:
                case QueueVerdict.ChurnRisk:
                    priority = HealthActionPriority.P1; break;
                case QueueVerdict.QueueOk:
                    priority = risk >= 20 ? HealthActionPriority.P2 : HealthActionPriority.P3; break;
                default:
                    priority = HealthActionPriority.P3; break;
            }

            // Deterministic reason order.
            reasons.Sort(StringComparer.Ordinal);

            return new MovieQueueCase
            {
                MovieId = movieId,
                MovieName = movieName,
                Verdict = verdict,
                Priority = priority,
                Risk = risk,
                ActiveReservationCount = depth,
                MaxWaiterDays = maxWait,
                AbandonRatePct = abandonPct,
                IsHighValueMovie = isHighValue,
                Reasons = reasons
            };
        }

        // ── Playbook ─────────────────────────────────────────────

        private void BuildPlaybook(ReservationHealthReport report, ReservationHealthAppetite appetite)
        {
            var actions = new List<HealthPlaybookAction>();
            var byVerdict = new Dictionary<QueueVerdict, List<int>>();
            foreach (var c in report.Cases)
            {
                if (!byVerdict.TryGetValue(c.Verdict, out var list))
                {
                    list = new List<int>();
                    byVerdict[c.Verdict] = list;
                }
                list.Add(c.MovieId);
            }

            List<int> Movies(QueueVerdict v) =>
                byVerdict.TryGetValue(v, out var l) ? l.OrderBy(i => i).ToList() : null;

            void Add(string id, HealthActionPriority pri, string label, string reason,
                    string owner, int blast, string reversibility, List<int> targets)
            {
                actions.Add(new HealthPlaybookAction
                {
                    Id = id,
                    Priority = pri,
                    Label = label,
                    Reason = reason,
                    Owner = owner,
                    BlastRadius = blast,
                    Reversibility = reversibility,
                    TargetMovieIds = targets ?? new List<int>()
                });
            }

            var stale = Movies(QueueVerdict.StalePickupReady);
            if (stale != null)
                Add("expire_stale_holds", HealthActionPriority.P0,
                    "Expire stale Ready holds and notify next in queue",
                    "Ready reservations are past their pickup window on " +
                        stale.Count.ToString(CultureInfo.InvariantCulture) + " movie(s).",
                    "ops", 2, "high", stale);

            var blocked = Movies(QueueVerdict.BlockedByOverdueRental);
            if (blocked != null)
                Add("escalate_blocked_queues", HealthActionPriority.P0,
                    "Escalate overdue rentals blocking active reservation queues",
                    "Waiting reservations are stuck behind rentals overdue >= " +
                        BlockingOverdueRentalDays.ToString(CultureInfo.InvariantCulture) +
                        "d on " + blocked.Count.ToString(CultureInfo.InvariantCulture) + " movie(s).",
                    "store_manager", 3, "medium", blocked);

            var starved = Movies(QueueVerdict.QueueStarved);
            if (starved != null)
                Add("notify_pickup_window_closing", HealthActionPriority.P1,
                    "Notify customers their pickup window is closing soon",
                    "Pickup window expires within " +
                        QueueStarvedWindowDays.ToString(CultureInfo.InvariantCulture) +
                        "d on " + starved.Count.ToString(CultureInfo.InvariantCulture) + " movie(s).",
                    "ops", 1, "high", starved);

            var chronic = Movies(QueueVerdict.ChronicAbandonment);
            if (chronic != null)
                Add("purge_chronic_abandoners", HealthActionPriority.P1,
                    "Audit and notify chronic reservation abandoners",
                    ">=40% of the last 10 reservations were expired or cancelled on " +
                        chronic.Count.ToString(CultureInfo.InvariantCulture) + " movie(s).",
                    "marketing", 2, "high", chronic);

            var churn = Movies(QueueVerdict.ChurnRisk);
            if (churn != null)
                Add("proactive_outreach_long_waiters", HealthActionPriority.P1,
                    "Reach out to customers who have been waiting for weeks",
                    "Waiters have been queued > " +
                        ChurnRiskWaiterDays.ToString(CultureInfo.InvariantCulture) +
                        "d on " + churn.Count.ToString(CultureInfo.InvariantCulture) + " movie(s).",
                    "customer_success", 2, "high", churn);

            var longq = Movies(QueueVerdict.LongQueue);
            if (longq != null)
                Add("add_inventory_for_long_queues", HealthActionPriority.P1,
                    "Procure additional copies for high-demand titles",
                    "Active queue depth >= " +
                        LongQueueDepthMin.ToString(CultureInfo.InvariantCulture) +
                        " on " + longq.Count.ToString(CultureInfo.InvariantCulture) + " movie(s).",
                    "procurement", 3, "low", longq);

            // Compute provisional grade so cautious can decide whether to add audit.
            var provisionalGrade = ProvisionalGrade(report, actions);
            if (appetite == ReservationHealthAppetite.Cautious &&
                (provisionalGrade == 'C' || provisionalGrade == 'D' || provisionalGrade == 'F'))
            {
                Add("audit_reservation_caps", HealthActionPriority.P2,
                    "Schedule an audit of reservation caps and pickup-window length",
                    "Cautious appetite + grade " + provisionalGrade +
                        " - schedule a tuning review of reservation policy.",
                    "ops", 1, "high", null);
            }

            if (actions.Count == 0)
            {
                Add("queues_healthy", HealthActionPriority.P3,
                    "Reservation queues healthy - maintain monitoring",
                    "No P0/P1/P2 reservation health issues detected.",
                    "ops", 1, "high", null);
            }

            if (appetite == ReservationHealthAppetite.Aggressive)
            {
                var hasP0orP1 = actions.Any(a => a.Priority == HealthActionPriority.P0 ||
                                                  a.Priority == HealthActionPriority.P1);
                if (hasP0orP1)
                {
                    // Trim P3 fallback and any lone P2.
                    actions.RemoveAll(a => a.Priority == HealthActionPriority.P3);
                    var p2s = actions.Where(a => a.Priority == HealthActionPriority.P2).ToList();
                    if (p2s.Count == 1) actions.Remove(p2s[0]);
                }
            }

            // Deterministic order: priority asc, id asc.
            report.Playbook = actions
                .OrderBy(a => (int)a.Priority)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static char ProvisionalGrade(ReservationHealthReport report,
                                             List<HealthPlaybookAction> actions)
        {
            // Mirror the same scoring used in BuildSummary so cautious audit
            // gating lines up with the published grade.
            if (report.Cases.Count == 0) return 'A';
            var mean = report.Cases.Average(c => (double)c.Risk);
            var score = (int)Math.Round(100 - mean);
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            var anyP0 = actions.Any(a => a.Priority == HealthActionPriority.P0)
                        || report.Cases.Any(c => c.Priority == HealthActionPriority.P0);
            if (anyP0) return 'F';
            if (score >= 85) return 'A';
            if (score >= 70) return 'B';
            if (score >= 55) return 'C';
            if (score >= 40) return 'D';
            return 'F';
        }

        // ── Summary ──────────────────────────────────────────────

        private void BuildSummary(ReservationHealthReport report, ReservationHealthAppetite appetite)
        {
            var s = report.Summary;
            s.TotalCases = report.Cases.Count;
            s.P0Count = report.Cases.Count(c => c.Priority == HealthActionPriority.P0);
            s.P1Count = report.Cases.Count(c => c.Priority == HealthActionPriority.P1);
            s.P2Count = report.Cases.Count(c => c.Priority == HealthActionPriority.P2);
            s.ActiveReservationCount = report.Cases.Sum(c => c.ActiveReservationCount);

            int score;
            if (report.Cases.Count == 0)
            {
                score = 100;
            }
            else
            {
                var mean = report.Cases.Average(c => (double)c.Risk);
                var appetiteShift = appetite == ReservationHealthAppetite.Cautious ? -5
                                  : appetite == ReservationHealthAppetite.Aggressive ? 5
                                  : 0;
                score = (int)Math.Round(100 - mean) + appetiteShift;
                if (score < 0) score = 0;
                if (score > 100) score = 100;
            }
            s.OverallScore = score;

            char grade;
            if (s.P0Count > 0) grade = 'F';
            else if (score >= 85) grade = 'A';
            else if (score >= 70) grade = 'B';
            else if (score >= 55) grade = 'C';
            else if (score >= 40) grade = 'D';
            else grade = 'F';
            s.Grade = grade;

            // Insights.
            var insights = new HashSet<string>(StringComparer.Ordinal);
            int countV(QueueVerdict v) => report.Cases.Count(c => c.Verdict == v);

            if (report.Cases.Count == 0)
            {
                insights.Add("INSUFFICIENT_DATA");
            }
            else
            {
                if (countV(QueueVerdict.BlockedByOverdueRental) >= 2) insights.Add("MULTIPLE_BLOCKED_QUEUES");
                if (countV(QueueVerdict.StalePickupReady) >= 2) insights.Add("STALE_PICKUP_CLUSTER");
                if (countV(QueueVerdict.ChurnRisk) >= 3) insights.Add("LONG_WAITER_PATTERN");
                if (countV(QueueVerdict.ChronicAbandonment) >= 2) insights.Add("HIGH_ABANDONMENT_TREND");
                var anyIssue = report.Cases.Any(c => (int)c.Verdict > (int)QueueVerdict.QueueOk);
                if (!anyIssue) insights.Add("HEALTHY_QUEUES");
            }

            s.Insights = insights.OrderBy(x => x, StringComparer.Ordinal).ToList();

            s.Headline = string.Format(
                CultureInfo.InvariantCulture,
                "VERDICT: grade={0} N={1} P0={2} P1={3} score={4}",
                grade, s.TotalCases, s.P0Count, s.P1Count, score);
        }

        // ── Renderers ────────────────────────────────────────────

        public string ToText(ReservationHealthReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("Reservation Health Advisor");
            sb.AppendLine("As of: " + report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.AppendLine("Risk appetite: " + report.RiskAppetite);
            sb.AppendLine(report.Summary.Headline);
            sb.AppendLine();
            sb.AppendLine("Cases:");
            if (report.Cases.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var c in report.Cases)
                {
                    sb.AppendLine("  - [" + c.Priority + "] " + c.MovieName +
                        " (#" + c.MovieId.ToString(CultureInfo.InvariantCulture) + ")" +
                        " verdict=" + c.Verdict +
                        " risk=" + c.Risk.ToString(CultureInfo.InvariantCulture) +
                        " active=" + c.ActiveReservationCount.ToString(CultureInfo.InvariantCulture) +
                        " maxWait=" + c.MaxWaiterDays.ToString(CultureInfo.InvariantCulture) + "d" +
                        (c.Reasons.Count > 0 ? " reasons=" + string.Join(",", c.Reasons) : ""));
                }
            }
            sb.AppendLine();
            sb.AppendLine("Playbook:");
            foreach (var a in report.Playbook)
            {
                sb.AppendLine("  - [" + a.Priority + "] " + a.Id + " :: " + a.Label +
                              " (owner=" + a.Owner + ", blast=" + a.BlastRadius +
                              ", reversibility=" + a.Reversibility + ")");
            }
            if (report.Summary.Insights.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Insights: " + string.Join(", ", report.Summary.Insights));
            }
            return sb.ToString();
        }

        public string ToMarkdown(ReservationHealthReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("# Reservation Health Advisor");
            sb.AppendLine();
            sb.AppendLine("- **As of:** " + report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.AppendLine("- **Risk appetite:** " + report.RiskAppetite);
            sb.AppendLine("- **Headline:** " + report.Summary.Headline);
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Total cases | " + report.Summary.TotalCases.ToString(CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| Active reservations | " + report.Summary.ActiveReservationCount.ToString(CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| P0 | " + report.Summary.P0Count.ToString(CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| P1 | " + report.Summary.P1Count.ToString(CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| P2 | " + report.Summary.P2Count.ToString(CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| Overall score | " + report.Summary.OverallScore.ToString(CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| Grade | " + report.Summary.Grade + " |");
            sb.AppendLine();
            sb.AppendLine("## Cases");
            sb.AppendLine();
            if (report.Cases.Count == 0)
            {
                sb.AppendLine("_No movies with active reservations._");
            }
            else
            {
                sb.AppendLine("| Movie | Verdict | Priority | Risk | Active | MaxWait | Abandon% | Reasons |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var c in report.Cases)
                {
                    sb.Append("| ");
                    sb.Append(c.MovieName + " (#" + c.MovieId.ToString(CultureInfo.InvariantCulture) + ")");
                    sb.Append(" | ").Append(c.Verdict);
                    sb.Append(" | ").Append(c.Priority);
                    sb.Append(" | ").Append(c.Risk.ToString(CultureInfo.InvariantCulture));
                    sb.Append(" | ").Append(c.ActiveReservationCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(" | ").Append(c.MaxWaiterDays.ToString(CultureInfo.InvariantCulture) + "d");
                    sb.Append(" | ").Append(c.AbandonRatePct.ToString(CultureInfo.InvariantCulture) + "%");
                    sb.Append(" | ").Append(c.Reasons.Count == 0 ? "-" : string.Join(", ", c.Reasons));
                    sb.AppendLine(" |");
                }
            }
            sb.AppendLine();
            sb.AppendLine("## Playbook");
            sb.AppendLine();
            sb.AppendLine("| Priority | Id | Label | Owner | Blast | Reversibility | Targets |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var a in report.Playbook)
            {
                sb.Append("| ").Append(a.Priority);
                sb.Append(" | ").Append(a.Id);
                sb.Append(" | ").Append(a.Label);
                sb.Append(" | ").Append(a.Owner);
                sb.Append(" | ").Append(a.BlastRadius.ToString(CultureInfo.InvariantCulture));
                sb.Append(" | ").Append(a.Reversibility);
                sb.Append(" | ").Append(a.TargetMovieIds.Count == 0
                    ? "-"
                    : string.Join(",", a.TargetMovieIds.Select(i => i.ToString(CultureInfo.InvariantCulture))));
                sb.AppendLine(" |");
            }
            if (report.Summary.Insights.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Insights");
                sb.AppendLine();
                foreach (var i in report.Summary.Insights) sb.AppendLine("- " + i);
            }
            return sb.ToString();
        }

        public string ToJson(ReservationHealthReport report)
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
                sb.AppendLine("      \"activeReservationCount\": " + c.ActiveReservationCount.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"isHighValueMovie\": " + (c.IsHighValueMovie ? "true" : "false") + ",");
                sb.AppendLine("      \"maxWaiterDays\": " + c.MaxWaiterDays.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"movieId\": " + c.MovieId.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"movieName\": " + Q(c.MovieName) + ",");
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
            sb.AppendLine("    \"activeReservationCount\": " + report.Summary.ActiveReservationCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"grade\": " + Q(report.Summary.Grade.ToString()) + ",");
            sb.AppendLine("    \"headline\": " + Q(report.Summary.Headline) + ",");
            sb.Append("    \"insights\": [");
            for (int j = 0; j < report.Summary.Insights.Count; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append(Q(report.Summary.Insights[j]));
            }
            sb.AppendLine("],");
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
