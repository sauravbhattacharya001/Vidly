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
    /// Per-customer waiver-governance verdict, roughly ordered by severity.
    /// </summary>
    public enum WaiverGovernanceVerdict
    {
        Healthy = 0,
        LightUse = 1,
        RepeatRequester = 2,
        FullWaiverConcentration = 3,
        HighDollarPattern = 4,
        SystemErrorPattern = 5,
        ChronicAbuser = 6
    }

    /// <summary>Priority bucket for governance playbook actions.</summary>
    public enum WaiverGovernanceActionPriority { P0, P1, P2, P3 }

    /// <summary>
    /// Risk-appetite knob. Cautious flags issues earlier; Aggressive
    /// trims low-priority noise from the playbook.
    /// </summary>
    public enum WaiverGovernanceAppetite { Cautious, Balanced, Aggressive }

    // ── Models ────────────────────────────────────────────────────

    /// <summary>
    /// Per-customer diagnostic for late-fee waivers granted in the audit window.
    /// </summary>
    public class WaiverGovernanceCase
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public WaiverGovernanceVerdict Verdict { get; set; }
        public WaiverGovernanceActionPriority Priority { get; set; }
        public int Risk { get; set; } // 0..100
        public int WaiverCount { get; set; }
        public int FullWaiverCount { get; set; }
        public int GoodwillCount { get; set; }
        public int SystemErrorCount { get; set; }
        public int FirstOffenseCount { get; set; }
        public decimal TotalAmountWaived { get; set; }
        public int DistinctRentals { get; set; }
        public int DaysSinceFirstWaiver { get; set; }
        public int DaysSinceLastWaiver { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>Cross-portfolio remediation action recommended by the advisor.</summary>
    public class WaiverGovernancePlaybookAction
    {
        public string Id { get; set; }
        public WaiverGovernanceActionPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; }
        public int BlastRadius { get; set; }
        public string Reversibility { get; set; }
        public List<int> TargetCustomerIds { get; set; } = new List<int>();
    }

    /// <summary>Portfolio-level summary across all audited customers.</summary>
    public class WaiverGovernanceSummary
    {
        public int TotalCases { get; set; }
        public int P0Count { get; set; }
        public int P1Count { get; set; }
        public int P2Count { get; set; }
        public int TotalWaivers { get; set; }
        public decimal TotalAmountWaived { get; set; }
        public int RecentSpikeWaivers { get; set; }
        public int TrailingBaselineWaivers { get; set; }
        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public string Headline { get; set; }
        public List<string> Insights { get; set; } = new List<string>();
        public Dictionary<string, int> ApproverConcentration { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public class WaiverGovernanceReport
    {
        public DateTime AsOfDate { get; set; }
        public WaiverGovernanceAppetite RiskAppetite { get; set; }
        public WaiverGovernanceSummary Summary { get; set; } = new WaiverGovernanceSummary();
        public List<WaiverGovernanceCase> Cases { get; set; } = new List<WaiverGovernanceCase>();
        public List<WaiverGovernancePlaybookAction> Playbook { get; set; } =
            new List<WaiverGovernancePlaybookAction>();
    }

    // ── Service ───────────────────────────────────────────────────

    /// <summary>
    /// Agentic late-fee waiver governance advisor — 6th Vidly agentic
    /// sibling. Where <see cref="ReviewIntelligenceService"/> audits movies
    /// (reputation), <see cref="DamageRiskForecastService"/> audits active
    /// rentals (damage), <see cref="LateReturnEscalationService"/> audits
    /// overdue rentals (escalation), <see cref="RefundFraudTriageService"/>
    /// audits pending refunds (fraud),
    /// <see cref="ReservationHealthAdvisorService"/> audits reservation
    /// queues, and <see cref="WaitlistConversionAdvisorService"/> audits
    /// waitlist funnels — this one audits the *late-fee waiver* funnel:
    /// repeat-requester clustering, full-waiver concentration, high-dollar
    /// patterns, recurring "SystemError" defect signals, goodwill overuse,
    /// approver concentration, and aggregate volume spikes vs the trailing
    /// baseline. Emits a per-customer case list + cross-portfolio playbook
    /// + summary with approver concentration metrics.
    ///
    /// Pure read-only — never mutates repositories.
    /// </summary>
    public class PenaltyWaiverGovernanceAdvisorService
    {
        private readonly IPenaltyWaiverRepository _waivers;
        private readonly IRentalRepository _rentals;
        private readonly IClock _clock;

        /// <summary>Audit window for per-customer signals (days).</summary>
        public const int AuditWindowDays = 90;

        /// <summary>Recent-spike window (days).</summary>
        public const int RecentSpikeWindowDays = 7;

        /// <summary>Trailing baseline window used for spike comparison (days).</summary>
        public const int TrailingBaselineDays = 30;

        /// <summary>Waiver count over window at or above which a customer is "repeat".</summary>
        public const int RepeatRequesterMin = 3;

        /// <summary>Waiver count over window at or above which a customer is "chronic".</summary>
        public const int ChronicAbuserMin = 5;

        /// <summary>Dollar total at or above which a customer is "high dollar".</summary>
        public const decimal HighDollarThreshold = 50.00m;

        /// <summary>Full-waiver share (0..100) at or above which concentration fires.</summary>
        public const int FullWaiverConcentrationPct = 70;

        /// <summary>SystemError count over window at or above which a defect pattern fires.</summary>
        public const int SystemErrorPatternMin = 2;

        /// <summary>Approver share (0..100) at or above which approver concentration fires.</summary>
        public const int ApproverConcentrationPct = 60;

        /// <summary>Min total waivers required before approver-concentration insight fires.</summary>
        public const int ApproverConcentrationMinTotal = 5;

        /// <summary>Spike multiplier (recent_per_day / baseline_per_day) at or above which spike fires.</summary>
        public const double SpikeMultiplier = 2.0;

        public PenaltyWaiverGovernanceAdvisorService(
            IPenaltyWaiverRepository waivers,
            IRentalRepository rentals,
            IClock clock)
        {
            _waivers = waivers ?? throw new ArgumentNullException(nameof(waivers));
            _rentals = rentals; // optional, may be null
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ── Report generation ────────────────────────────────────

        public WaiverGovernanceReport GenerateReport(
            WaiverGovernanceAppetite appetite = WaiverGovernanceAppetite.Balanced)
        {
            var now = _clock.Now;
            var report = new WaiverGovernanceReport
            {
                AsOfDate = now.Date,
                RiskAppetite = appetite
            };

            var all = _waivers.GetAll()?.ToList() ?? new List<PenaltyWaiver>();
            var auditCutoff = now.Date.AddDays(-AuditWindowDays);
            var inWindow = all.Where(w => w != null && w.GrantedDate >= auditCutoff).ToList();

            // Group by resolved customer id (fall back to rental lookup, else name hash).
            var byCustomer = new Dictionary<int, List<PenaltyWaiver>>();
            var nameByCustomer = new Dictionary<int, string>();
            foreach (var w in inWindow)
            {
                int customerId = ResolveCustomerId(w);
                if (!byCustomer.TryGetValue(customerId, out var bucket))
                {
                    bucket = new List<PenaltyWaiver>();
                    byCustomer[customerId] = bucket;
                }
                bucket.Add(w);
                if (!nameByCustomer.ContainsKey(customerId))
                    nameByCustomer[customerId] = w.CustomerName ?? ("Customer#" + customerId);
            }

            foreach (var kv in byCustomer)
            {
                var customerId = kv.Key;
                var bucket = kv.Value;
                var name = nameByCustomer.TryGetValue(customerId, out var n) ? n : ("Customer#" + customerId);
                var c = BuildCase(customerId, name, bucket, now, appetite);
                report.Cases.Add(c);
            }

            // Deterministic order: highest risk first, then customerId asc.
            report.Cases = report.Cases
                .OrderByDescending(c => c.Risk)
                .ThenBy(c => c.CustomerId)
                .ToList();

            BuildPlaybook(report, appetite);
            BuildSummary(report, all, inWindow, now, appetite);

            return report;
        }

        private int ResolveCustomerId(PenaltyWaiver w)
        {
            // PenaltyWaiver carries CustomerName but not CustomerId, so try the
            // rental lookup first; otherwise fall back to a stable name-based id
            // (still scoped to this report). RentalId is always present.
            if (_rentals != null)
            {
                var r = _rentals.GetById(w.RentalId);
                if (r != null && r.CustomerId > 0) return r.CustomerId;
            }
            if (!string.IsNullOrEmpty(w.CustomerName))
                return -Math.Abs(w.CustomerName.GetHashCode());
            return -w.RentalId; // last resort: group per rental
        }

        // ── Per-customer case construction ───────────────────────

        private WaiverGovernanceCase BuildCase(
            int customerId,
            string customerName,
            List<PenaltyWaiver> bucket,
            DateTime now,
            WaiverGovernanceAppetite appetite)
        {
            var reasons = new List<string>();

            int total = bucket.Count;
            int full = bucket.Count(w => w.Type == WaiverType.Full);
            int goodwill = bucket.Count(w => w.Type == WaiverType.Goodwill);
            int sysErr = bucket.Count(w => w.Type == WaiverType.SystemError);
            int firstOff = bucket.Count(w => w.Type == WaiverType.FirstOffense);
            decimal totalAmount = bucket.Sum(w => w.AmountWaived);
            int distinctRentals = bucket.Select(w => w.RentalId).Distinct().Count();

            var sorted = bucket.OrderBy(w => w.GrantedDate).ToList();
            int daysSinceFirst = Math.Max(0, (int)Math.Floor((now.Date - sorted.First().GrantedDate.Date).TotalDays));
            int daysSinceLast = Math.Max(0, (int)Math.Floor((now.Date - sorted.Last().GrantedDate.Date).TotalDays));

            // Repeat / chronic.
            if (total >= ChronicAbuserMin)
                reasons.Add("CHRONIC_WAIVERS_" + total.ToString(CultureInfo.InvariantCulture));
            else if (total >= RepeatRequesterMin)
                reasons.Add("REPEAT_WAIVERS_" + total.ToString(CultureInfo.InvariantCulture));

            // Full-waiver concentration.
            int fullPct = total == 0 ? 0 : (int)Math.Round(100.0 * full / total);
            if (total >= RepeatRequesterMin && fullPct >= FullWaiverConcentrationPct)
                reasons.Add("FULL_WAIVER_PCT_" + fullPct.ToString(CultureInfo.InvariantCulture));

            // High dollar.
            if (totalAmount >= HighDollarThreshold)
                reasons.Add("HIGH_DOLLAR_" + ((int)Math.Round(totalAmount)).ToString(CultureInfo.InvariantCulture));

            // SystemError defect pattern.
            if (sysErr >= SystemErrorPatternMin)
                reasons.Add("SYSTEM_ERROR_" + sysErr.ToString(CultureInfo.InvariantCulture));

            // Goodwill overuse.
            if (goodwill >= RepeatRequesterMin)
                reasons.Add("GOODWILL_OVERUSE_" + goodwill.ToString(CultureInfo.InvariantCulture));

            // ── Verdict + priority + risk ────────────────────────

            var verdict = WaiverGovernanceVerdict.LightUse;
            var priority = WaiverGovernanceActionPriority.P3;
            int risk = 10;

            if (sysErr >= SystemErrorPatternMin)
            {
                verdict = WaiverGovernanceVerdict.SystemErrorPattern;
                priority = WaiverGovernanceActionPriority.P0;
                risk = Math.Max(risk, 80 + Math.Min(15, sysErr * 3));
            }

            if (total >= ChronicAbuserMin)
            {
                if (verdict < WaiverGovernanceVerdict.ChronicAbuser)
                    verdict = WaiverGovernanceVerdict.ChronicAbuser;
                if (priority > WaiverGovernanceActionPriority.P1)
                    priority = WaiverGovernanceActionPriority.P1;
                risk = Math.Max(risk, 70 + Math.Min(20, (total - ChronicAbuserMin) * 4));
            }
            else if (total >= RepeatRequesterMin && verdict < WaiverGovernanceVerdict.RepeatRequester)
            {
                verdict = WaiverGovernanceVerdict.RepeatRequester;
                if (priority > WaiverGovernanceActionPriority.P2)
                    priority = WaiverGovernanceActionPriority.P2;
                risk = Math.Max(risk, 50 + Math.Min(15, total * 3));
            }

            if (totalAmount >= HighDollarThreshold && verdict < WaiverGovernanceVerdict.HighDollarPattern)
            {
                verdict = WaiverGovernanceVerdict.HighDollarPattern;
                if (priority > WaiverGovernanceActionPriority.P2)
                    priority = WaiverGovernanceActionPriority.P2;
                risk = Math.Max(risk, 55 + Math.Min(20, (int)((totalAmount - HighDollarThreshold) / 10m) * 2));
            }

            if (total >= RepeatRequesterMin && fullPct >= FullWaiverConcentrationPct
                && verdict < WaiverGovernanceVerdict.FullWaiverConcentration)
            {
                verdict = WaiverGovernanceVerdict.FullWaiverConcentration;
                if (priority > WaiverGovernanceActionPriority.P2)
                    priority = WaiverGovernanceActionPriority.P2;
                risk = Math.Max(risk, 45 + Math.Min(20, fullPct - FullWaiverConcentrationPct));
            }

            // Cautious: bump risk slightly and promote priority one notch up for non-P0.
            if (appetite == WaiverGovernanceAppetite.Cautious)
            {
                risk = Math.Min(100, risk + 5);
                if (priority == WaiverGovernanceActionPriority.P2)
                    priority = WaiverGovernanceActionPriority.P1;
                else if (priority == WaiverGovernanceActionPriority.P3)
                    priority = WaiverGovernanceActionPriority.P2;
            }
            // Aggressive: trim low-priority noise.
            else if (appetite == WaiverGovernanceAppetite.Aggressive)
            {
                if (priority == WaiverGovernanceActionPriority.P3)
                    risk = Math.Max(0, risk - 5);
            }

            if (reasons.Count == 0)
            {
                verdict = total > 0 ? WaiverGovernanceVerdict.LightUse : WaiverGovernanceVerdict.Healthy;
                priority = WaiverGovernanceActionPriority.P3;
                risk = Math.Min(risk, 15);
                reasons.Add(total > 0 ? "LIGHT_USE" : "HEALTHY");
            }

            return new WaiverGovernanceCase
            {
                CustomerId = customerId,
                CustomerName = customerName,
                Verdict = verdict,
                Priority = priority,
                Risk = Math.Max(0, Math.Min(100, risk)),
                WaiverCount = total,
                FullWaiverCount = full,
                GoodwillCount = goodwill,
                SystemErrorCount = sysErr,
                FirstOffenseCount = firstOff,
                TotalAmountWaived = totalAmount,
                DistinctRentals = distinctRentals,
                DaysSinceFirstWaiver = daysSinceFirst,
                DaysSinceLastWaiver = daysSinceLast,
                Reasons = reasons
            };
        }

        // ── Playbook ─────────────────────────────────────────────

        private void BuildPlaybook(WaiverGovernanceReport report, WaiverGovernanceAppetite appetite)
        {
            if (report.Cases.Count == 0)
            {
                report.Playbook.Add(new WaiverGovernancePlaybookAction
                {
                    Id = "waivers_healthy",
                    Priority = WaiverGovernanceActionPriority.P3,
                    Label = "Waiver activity is healthy — no remediation required",
                    Reason = "No late-fee waivers granted in the audit window.",
                    Owner = "ops",
                    BlastRadius = 0,
                    Reversibility = "n/a"
                });
                return;
            }

            void Add(string id, WaiverGovernanceActionPriority p, string label, string reason,
                     string owner, int blast, string reversibility, IEnumerable<int> targets)
            {
                var ta = targets?.OrderBy(x => x).ToList() ?? new List<int>();
                report.Playbook.Add(new WaiverGovernancePlaybookAction
                {
                    Id = id,
                    Priority = p,
                    Label = label,
                    Reason = reason,
                    Owner = owner,
                    BlastRadius = blast,
                    Reversibility = reversibility,
                    TargetCustomerIds = ta
                });
            }

            var sysErr = report.Cases.Where(c => c.SystemErrorCount >= SystemErrorPatternMin).ToList();
            if (sysErr.Count > 0)
            {
                Add("investigate_system_error_pattern",
                    WaiverGovernanceActionPriority.P0,
                    "Investigate SystemError waiver pattern across " + sysErr.Count + " customer(s)",
                    "Repeated SystemError-typed waivers suggest a billing or late-fee calculation defect.",
                    "engineering",
                    sysErr.Sum(c => c.SystemErrorCount),
                    "reversible",
                    sysErr.Select(c => c.CustomerId));
            }

            var chronic = report.Cases.Where(c => c.WaiverCount >= ChronicAbuserMin).ToList();
            if (chronic.Count > 0)
            {
                Add("review_chronic_waiver_requesters",
                    WaiverGovernanceActionPriority.P1,
                    "Review policy fit for " + chronic.Count + " chronic waiver requester(s)",
                    "Customers with ≥ " + ChronicAbuserMin + " waivers in " + AuditWindowDays +
                        "d may need a hold or a coaching touch, not another waiver.",
                    "customer-success",
                    chronic.Count,
                    "reversible",
                    chronic.Select(c => c.CustomerId));
            }

            var bigDollar = report.Cases.Where(c => c.TotalAmountWaived >= HighDollarThreshold).ToList();
            if (bigDollar.Count > 0)
            {
                Add("audit_high_dollar_waivers",
                    WaiverGovernanceActionPriority.P2,
                    "Audit high-dollar waiver totals on " + bigDollar.Count + " customer(s)",
                    "Cumulative waived amount ≥ $" + HighDollarThreshold +
                        " per customer warrants a manager-level review.",
                    "finance",
                    bigDollar.Count,
                    "reversible",
                    bigDollar.Select(c => c.CustomerId));
            }

            var fullConc = report.Cases
                .Where(c => c.WaiverCount >= RepeatRequesterMin
                            && c.FullWaiverCount * 100 >= c.WaiverCount * FullWaiverConcentrationPct)
                .ToList();
            if (fullConc.Count > 0)
            {
                Add("calibrate_full_waiver_defaulting",
                    WaiverGovernanceActionPriority.P2,
                    "Calibrate full-waiver defaulting across " + fullConc.Count + " customer(s)",
                    "Full waivers dominate (≥ " + FullWaiverConcentrationPct +
                        "% of repeat requests) — partial waivers may be the better default.",
                    "ops",
                    fullConc.Count,
                    "reversible",
                    fullConc.Select(c => c.CustomerId));
            }

            var goodwill = report.Cases.Where(c => c.GoodwillCount >= RepeatRequesterMin).ToList();
            if (goodwill.Count > 0 && appetite != WaiverGovernanceAppetite.Aggressive)
            {
                Add("review_goodwill_overuse",
                    WaiverGovernanceActionPriority.P3,
                    "Review goodwill-typed waiver overuse for " + goodwill.Count + " customer(s)",
                    "Goodwill gestures should be rare; clustering suggests a process gap.",
                    "ops",
                    goodwill.Count,
                    "reversible",
                    goodwill.Select(c => c.CustomerId));
            }

            if (report.Playbook.Count == 0)
            {
                Add("monitor_only",
                    WaiverGovernanceActionPriority.P3,
                    "Continue monitoring — no actionable signals",
                    "All waiver activity is within tolerance bands.",
                    "ops",
                    0,
                    "n/a",
                    Enumerable.Empty<int>());
            }

            report.Playbook = report.Playbook
                .OrderBy(a => (int)a.Priority)
                .ThenBy(a => a.Label, StringComparer.Ordinal)
                .ToList();
        }

        // ── Summary ──────────────────────────────────────────────

        private void BuildSummary(
            WaiverGovernanceReport report,
            List<PenaltyWaiver> all,
            List<PenaltyWaiver> inWindow,
            DateTime now,
            WaiverGovernanceAppetite appetite)
        {
            var s = report.Summary;
            s.TotalCases = report.Cases.Count;
            s.P0Count = report.Cases.Count(c => c.Priority == WaiverGovernanceActionPriority.P0);
            s.P1Count = report.Cases.Count(c => c.Priority == WaiverGovernanceActionPriority.P1);
            s.P2Count = report.Cases.Count(c => c.Priority == WaiverGovernanceActionPriority.P2);
            s.TotalWaivers = inWindow.Count;
            s.TotalAmountWaived = inWindow.Sum(w => w.AmountWaived);

            // Volume spike: recent vs baseline per-day rate.
            var recentCutoff = now.Date.AddDays(-RecentSpikeWindowDays);
            var baselineCutoff = now.Date.AddDays(-(RecentSpikeWindowDays + TrailingBaselineDays));
            s.RecentSpikeWaivers = all.Count(w => w.GrantedDate >= recentCutoff);
            s.TrailingBaselineWaivers = all.Count(w =>
                w.GrantedDate >= baselineCutoff && w.GrantedDate < recentCutoff);

            double recentRate = s.RecentSpikeWaivers / (double)RecentSpikeWindowDays;
            double baselineRate = s.TrailingBaselineWaivers / (double)TrailingBaselineDays;
            if (s.RecentSpikeWaivers >= 3 && baselineRate > 0 && recentRate >= baselineRate * SpikeMultiplier)
                s.Insights.Add("VOLUME_SPIKE_" + ((int)Math.Round(recentRate * 7)).ToString(CultureInfo.InvariantCulture) + "_PER_WEEK");

            // Approver concentration.
            var byApprover = inWindow
                .Where(w => !string.IsNullOrWhiteSpace(w.ApprovedBy))
                .GroupBy(w => w.ApprovedBy.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var a in byApprover) s.ApproverConcentration[a.Name] = a.Count;
            if (inWindow.Count >= ApproverConcentrationMinTotal && byApprover.Count > 0)
            {
                var top = byApprover[0];
                int pct = (int)Math.Round(100.0 * top.Count / inWindow.Count);
                if (pct >= ApproverConcentrationPct && byApprover.Count >= 2)
                    s.Insights.Add("APPROVER_CONCENTRATION_" + pct.ToString(CultureInfo.InvariantCulture) + "PCT_" + top.Name);
            }

            if (report.Cases.Count == 0)
            {
                s.OverallScore = 100;
                s.Grade = 'A';
                s.Headline = "No late-fee waivers granted in audit window";
                s.Insights.Add("INSUFFICIENT_DATA");
                return;
            }

            double score = 100.0;
            foreach (var c in report.Cases)
            {
                double w = c.Priority == WaiverGovernanceActionPriority.P0 ? 1.0
                         : c.Priority == WaiverGovernanceActionPriority.P1 ? 0.6
                         : c.Priority == WaiverGovernanceActionPriority.P2 ? 0.3
                         : 0.1;
                score -= w * (c.Risk / 100.0) * 18.0;
            }
            score = Math.Max(0, Math.Min(100, score));

            // Force grade ceiling if any P0 case exists (mirrors sibling advisors).
            if (s.P0Count > 0) score = Math.Min(score, 55);

            s.OverallScore = (int)Math.Round(score);
            s.Grade = s.OverallScore >= 90 ? 'A'
                    : s.OverallScore >= 80 ? 'B'
                    : s.OverallScore >= 70 ? 'C'
                    : s.OverallScore >= 60 ? 'D'
                    : 'F';

            if (s.P0Count > 0)
                s.Headline = s.P0Count + " critical waiver-governance issue(s) need immediate action";
            else if (s.P1Count > 0)
                s.Headline = s.P1Count + " chronic waiver requester(s) need policy review";
            else if (s.P2Count > 0)
                s.Headline = s.P2Count + " waiver pattern(s) warrant a closer look";
            else
                s.Headline = "Waiver activity within tolerance";
        }

        // ── Text rendering ───────────────────────────────────────

        /// <summary>
        /// Renders a compact text report suitable for ops dashboards or logs.
        /// </summary>
        public string RenderTextReport(WaiverGovernanceReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("Late-Fee Waiver Governance Advisor — " +
                          report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                          " (" + report.RiskAppetite + ")");
            sb.AppendLine("Score: " + report.Summary.OverallScore + " [" + report.Summary.Grade + "]  " +
                          report.Summary.Headline);
            sb.AppendLine("Window: last " + AuditWindowDays + "d  Waivers: " + report.Summary.TotalWaivers +
                          "  Amount: $" + report.Summary.TotalAmountWaived.ToString("F2", CultureInfo.InvariantCulture));
            if (report.Summary.Insights.Count > 0)
                sb.AppendLine("Insights: " + string.Join(", ", report.Summary.Insights));
            if (report.Summary.ApproverConcentration.Count > 0)
            {
                sb.AppendLine("Approvers:");
                foreach (var kv in report.Summary.ApproverConcentration)
                    sb.AppendLine("  - " + kv.Key + ": " + kv.Value);
            }
            sb.AppendLine();
            sb.AppendLine("Cases (" + report.Cases.Count + "):");
            foreach (var c in report.Cases)
            {
                sb.AppendLine("  [" + c.Priority + " R" + c.Risk + "] " + c.CustomerName +
                              " (#" + c.CustomerId + ") " + c.Verdict +
                              "  waivers=" + c.WaiverCount +
                              " full=" + c.FullWaiverCount +
                              " amt=$" + c.TotalAmountWaived.ToString("F2", CultureInfo.InvariantCulture) +
                              "  reasons=[" + string.Join(",", c.Reasons) + "]");
            }
            sb.AppendLine();
            sb.AppendLine("Playbook (" + report.Playbook.Count + "):");
            foreach (var a in report.Playbook)
            {
                sb.AppendLine("  [" + a.Priority + "] " + a.Label + "  (owner=" + a.Owner +
                              ", blast=" + a.BlastRadius + ", " + a.Reversibility + ")");
                sb.AppendLine("      reason: " + a.Reason);
            }
            return sb.ToString();
        }
    }
}
