using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Vidly.Services
{
    // === Enums ====================================================

    /// <summary>Per-subscription dunning verdict, ordered healthy -> terminal.</summary>
    public enum SubscriptionDunningVerdict
    {
        Current = 0,
        SoftReminder = 1,
        ActiveDunning = 2,
        UrgentRetry = 3,
        DowngradeOffer = 4,
        PauseHold = 5,
        TerminalCharge = 6,
        ForceCancel = 7
    }

    /// <summary>Priority bucket for portfolio playbook actions.</summary>
    public enum SubscriptionDunningActionPriority { P0, P1, P2, P3 }

    /// <summary>Risk-appetite knob. Cautious inflates urgency; Aggressive trims it.</summary>
    public enum SubscriptionDunningAppetite { Cautious, Balanced, Aggressive }

    /// <summary>Why the most recent retry failed (payment-processor reason buckets).</summary>
    public enum SubscriptionDunningFailureReason
    {
        Unknown = 0,
        InsufficientFunds = 1,
        ExpiredCard = 2,
        InvalidCard = 3,
        IssuerDecline = 4,
        FraudBlock = 5,
        ProcessorError = 6,
        Disputed = 7
    }

    /// <summary>Tier of the subscription plan (drives downgrade offer logic).</summary>
    public enum SubscriptionDunningTier { Basic = 0, Standard = 1, Premium = 2 }

    /// <summary>Portfolio headline.</summary>
    public enum SubscriptionDunningHeadline
    {
        PortfolioHealthy = 0,
        WatchPortfolio = 1,
        DunningElevated = 2,
        DunningHigh = 3,
        DunningCritical = 4
    }

    // === Inputs ===================================================

    /// <summary>
    /// Snapshot of a subscription whose most recent renewal charge failed (or is
    /// currently delinquent). Plain DTO - no repository or model dependency, so
    /// the service is trivially testable and reusable by other surfaces.
    /// </summary>
    public class SubscriptionDunningSnapshot
    {
        public int SubscriptionId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public SubscriptionDunningTier Tier { get; set; }

        /// <summary>Monthly recurring revenue in whatever currency the caller uses.</summary>
        public decimal MonthlyRevenue { get; set; }

        /// <summary>Customer lifetime value to date.</summary>
        public decimal LifetimeRevenue { get; set; }

        /// <summary>Total months the customer has been an active subscriber.</summary>
        public int TenureMonths { get; set; }

        /// <summary>When the most recent retry attempt was made.</summary>
        public DateTime LastFailedAt { get; set; }

        /// <summary>Number of consecutive failed attempts in the current dunning cycle.</summary>
        public int FailedAttempts { get; set; }

        /// <summary>Reason from the most recent attempt.</summary>
        public SubscriptionDunningFailureReason LastFailureReason { get; set; }

        /// <summary>Whether the saved card on file is expired (or expires within 14 days).</summary>
        public bool CardExpired { get; set; }

        /// <summary>Whether the customer has a viable backup payment method on file.</summary>
        public bool HasBackupPaymentMethod { get; set; }

        /// <summary>Whether the customer has opened/clicked any of the dunning emails sent so far.</summary>
        public bool RecentEmailEngagement { get; set; }

        /// <summary>Rentals (or comparable engagement events) in the last 30 days.</summary>
        public int RentalsLast30Days { get; set; }

        /// <summary>Any open support ticket related to billing (caller decides scope).</summary>
        public bool HasOpenBillingTicket { get; set; }

        /// <summary>Whether the account has previously been auto-pause-recovered. Avoid second auto-pause.</summary>
        public bool PreviouslyAutoPauseRecovered { get; set; }
    }

    // === Outputs ==================================================

    /// <summary>Per-subscription diagnostic + recommended next move.</summary>
    public class SubscriptionDunningCase
    {
        public int SubscriptionId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public SubscriptionDunningTier Tier { get; set; }
        public SubscriptionDunningVerdict Verdict { get; set; }
        public SubscriptionDunningActionPriority Priority { get; set; }

        /// <summary>0..100. Higher = more urgent / more likely to churn this cycle.</summary>
        public int DunningRisk { get; set; }

        /// <summary>0..100. Higher = larger forecasted dollar impact (MRR + churn proxy).</summary>
        public int RevenueAtRisk { get; set; }

        public int DaysSinceFirstFailure { get; set; }
        public int FailedAttempts { get; set; }
        public SubscriptionDunningFailureReason LastFailureReason { get; set; }

        /// <summary>Structured reason codes (stable strings, deterministic order).</summary>
        public List<string> Reasons { get; set; } = new List<string>();

        /// <summary>Short imperative next-step recommendation for the agent on the case.</summary>
        public string RecommendedAction { get; set; }
    }

    /// <summary>Cross-portfolio remediation action.</summary>
    public class SubscriptionDunningPlaybookAction
    {
        public string Id { get; set; }
        public SubscriptionDunningActionPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; }
        public int BlastRadius { get; set; }
        public string Reversibility { get; set; }
        public List<int> TargetSubscriptionIds { get; set; } = new List<int>();
    }

    /// <summary>Portfolio summary.</summary>
    public class SubscriptionDunningSummary
    {
        public int TotalSubscriptions { get; set; }
        public int CurrentSubscriptions { get; set; }
        public int ActiveDunningCount { get; set; }
        public int UrgentRetryCount { get; set; }
        public int DowngradeOfferCount { get; set; }
        public int PauseHoldCount { get; set; }
        public int TerminalCount { get; set; }
        public int ForceCancelCount { get; set; }
        public int CardIssuesCount { get; set; }

        public decimal TotalMrrAtRisk { get; set; }
        public decimal RecoverableMrrEstimate { get; set; }
        public double DunningRecoveryProbability { get; set; }

        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public SubscriptionDunningHeadline HeadlineVerdict { get; set; }

        public List<string> Insights { get; set; } = new List<string>();
    }

    /// <summary>Caller-supplied knobs.</summary>
    public class SubscriptionDunningOptions
    {
        public SubscriptionDunningAppetite RiskAppetite { get; set; } = SubscriptionDunningAppetite.Balanced;
        public int TopCases { get; set; } = 25;

        /// <summary>Attempts threshold for active dunning (>=). Default 2.</summary>
        public int ActiveDunningAttempts { get; set; } = 2;

        /// <summary>Attempts threshold for urgent retry (>=). Default 3.</summary>
        public int UrgentRetryAttempts { get; set; } = 3;

        /// <summary>Attempts threshold for terminal-charge classification (>=). Default 5.</summary>
        public int TerminalAttempts { get; set; } = 5;

        /// <summary>Days after first failure to declare ForceCancel candidate. Default 14.</summary>
        public int ForceCancelAfterDays { get; set; } = 14;

        /// <summary>Recovery probability baseline (0..1) when no signals push it. Default 0.55.</summary>
        public double BaselineRecoveryProbability { get; set; } = 0.55;
    }

    /// <summary>Full report bundle.</summary>
    public class SubscriptionDunningReport
    {
        public DateTime GeneratedAt { get; set; }
        public SubscriptionDunningOptions Options { get; set; } = new SubscriptionDunningOptions();
        public List<SubscriptionDunningCase> Cases { get; set; } = new List<SubscriptionDunningCase>();
        public List<SubscriptionDunningPlaybookAction> Playbook { get; set; } =
            new List<SubscriptionDunningPlaybookAction>();
        public SubscriptionDunningSummary Summary { get; set; } = new SubscriptionDunningSummary();

        public string ToText() { return Render(false); }
        public string ToMarkdown() { return Render(true); }

        private string Render(bool markdown)
        {
            var sb = new StringBuilder();
            string h2 = markdown ? "## " : "";
            var inv = CultureInfo.InvariantCulture;

            sb.AppendLine(h2 + "Summary");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Total subscriptions | " + Summary.TotalSubscriptions + " |");
            sb.AppendLine("| Current | " + Summary.CurrentSubscriptions + " |");
            sb.AppendLine("| Active dunning | " + Summary.ActiveDunningCount + " |");
            sb.AppendLine("| Urgent retry | " + Summary.UrgentRetryCount + " |");
            sb.AppendLine("| Downgrade offer | " + Summary.DowngradeOfferCount + " |");
            sb.AppendLine("| Pause/hold | " + Summary.PauseHoldCount + " |");
            sb.AppendLine("| Terminal | " + Summary.TerminalCount + " |");
            sb.AppendLine("| Force-cancel | " + Summary.ForceCancelCount + " |");
            sb.AppendLine("| Card issues | " + Summary.CardIssuesCount + " |");
            sb.AppendLine("| MRR at risk | " + Summary.TotalMrrAtRisk.ToString("F2", inv) + " |");
            sb.AppendLine("| Recoverable MRR estimate | " + Summary.RecoverableMrrEstimate.ToString("F2", inv) + " |");
            sb.AppendLine("| Recovery probability | " +
                (Summary.DunningRecoveryProbability * 100.0).ToString("F1", inv) + "% |");
            sb.AppendLine("| Score | " + Summary.OverallScore + " (" + Summary.Grade + ") |");
            sb.AppendLine("| Verdict | " + Summary.HeadlineVerdict + " |");
            sb.AppendLine();

            sb.AppendLine(h2 + "Top cases");
            sb.AppendLine();
            sb.AppendLine("| Id | Customer | Tier | Verdict | Risk | $@Risk | Attempts | Reason | Next |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var c in Cases)
            {
                sb.AppendLine("| " + c.SubscriptionId
                    + " | " + (c.CustomerName ?? "")
                    + " | " + c.Tier
                    + " | " + c.Verdict
                    + " | " + c.DunningRisk
                    + " | " + c.RevenueAtRisk
                    + " | " + c.FailedAttempts
                    + " | " + c.LastFailureReason
                    + " | " + (c.RecommendedAction ?? "") + " |");
            }
            sb.AppendLine();

            sb.AppendLine(h2 + "Playbook");
            sb.AppendLine();
            sb.AppendLine("| Priority | Id | Label | Owner | Targets |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var a in Playbook)
            {
                sb.AppendLine("| " + a.Priority + " | " + a.Id + " | " + a.Label
                    + " | " + (a.Owner ?? "") + " | "
                    + string.Join(",", a.TargetSubscriptionIds) + " |");
            }
            sb.AppendLine();

            sb.AppendLine(h2 + "Insights");
            sb.AppendLine();
            foreach (var i in Summary.Insights)
                sb.AppendLine("- " + i);
            return sb.ToString();
        }
    }

    // === Service ==================================================

    /// <summary>
    /// Agentic subscription dunning advisor - 9th Vidly agentic sibling to
    /// ReviewIntelligenceService, DamageRiskForecastService, LateReturnEscalationService,
    /// RefundFraudTriageService, WaitlistConversionAdvisorService,
    /// PenaltyWaiverGovernanceAdvisorService, GiftCardBreakageAdvisorService, and
    /// LostAndFoundDispositionAdvisorService.
    ///
    /// Triages delinquent subscriptions across the portfolio: classifies each into a
    /// dunning verdict ladder, scores cycle-recovery probability and revenue-at-risk,
    /// recommends a per-account next step, and emits a P0-first deduped playbook plus
    /// insights and an A-F grade. Pure analytic - never charges the card, never sends
    /// the email, never mutates the input snapshots.
    /// </summary>
    public class SubscriptionDunningAdvisorService
    {
        private readonly Func<DateTime> _now;

        public SubscriptionDunningAdvisorService() : this(null) { }

        /// <summary>Injectable clock for deterministic testing.</summary>
        public SubscriptionDunningAdvisorService(Func<DateTime> now)
        {
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// <summary>Build a full dunning report.</summary>
        public SubscriptionDunningReport GenerateReport(
            IEnumerable<SubscriptionDunningSnapshot> snapshots,
            SubscriptionDunningOptions options = null)
        {
            options = options ?? new SubscriptionDunningOptions();
            int topCases = Math.Max(0, options.TopCases);

            var report = new SubscriptionDunningReport
            {
                GeneratedAt = _now(),
                Options = options
            };

            var input = (snapshots ?? Enumerable.Empty<SubscriptionDunningSnapshot>())
                .Where(s => s != null)
                .ToList();

            var allCases = new List<SubscriptionDunningCase>(input.Count);
            foreach (var snap in input)
                allCases.Add(BuildCase(snap, options));

            var ordered = allCases
                .OrderBy(c => c.Priority)
                .ThenByDescending(c => c.DunningRisk)
                .ThenByDescending(c => c.RevenueAtRisk)
                .ThenBy(c => c.SubscriptionId)
                .ToList();

            report.Cases = ordered.Take(topCases).ToList();
            BuildSummary(report, ordered, input, options);
            BuildPlaybook(report, ordered, options);
            return report;
        }

        // === Per-case ================================================

        private SubscriptionDunningCase BuildCase(
            SubscriptionDunningSnapshot s,
            SubscriptionDunningOptions options)
        {
            var now = _now();
            int days = (int)Math.Max(0, Math.Round((now - s.LastFailedAt).TotalDays));
            var c = new SubscriptionDunningCase
            {
                SubscriptionId = s.SubscriptionId,
                CustomerId = s.CustomerId,
                CustomerName = s.CustomerName,
                Tier = s.Tier,
                DaysSinceFirstFailure = days,
                FailedAttempts = Math.Max(0, s.FailedAttempts),
                LastFailureReason = s.LastFailureReason
            };

            // ---- Verdict ladder ----
            if (s.FailedAttempts <= 0)
                c.Verdict = SubscriptionDunningVerdict.Current;
            else if (s.FailedAttempts == 1 &&
                     days < options.ActiveDunningAttempts &&
                     s.LastFailureReason != SubscriptionDunningFailureReason.FraudBlock)
                c.Verdict = SubscriptionDunningVerdict.SoftReminder;
            else if (s.FailedAttempts >= options.TerminalAttempts ||
                     days >= options.ForceCancelAfterDays)
                c.Verdict = SubscriptionDunningVerdict.ForceCancel;
            else if (s.FailedAttempts >= options.UrgentRetryAttempts)
                c.Verdict = SubscriptionDunningVerdict.UrgentRetry;
            else if (s.FailedAttempts >= options.ActiveDunningAttempts)
                c.Verdict = SubscriptionDunningVerdict.ActiveDunning;
            else
                c.Verdict = SubscriptionDunningVerdict.SoftReminder;

            // ---- Reason codes (deterministic, dedup) ----
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            if (s.CardExpired) reasons.Add("CARD_EXPIRED");
            if (s.LastFailureReason == SubscriptionDunningFailureReason.InsufficientFunds)
                reasons.Add("INSUFFICIENT_FUNDS");
            if (s.LastFailureReason == SubscriptionDunningFailureReason.FraudBlock)
                reasons.Add("FRAUD_BLOCK");
            if (s.LastFailureReason == SubscriptionDunningFailureReason.IssuerDecline)
                reasons.Add("ISSUER_DECLINE");
            if (s.LastFailureReason == SubscriptionDunningFailureReason.ProcessorError)
                reasons.Add("PROCESSOR_ERROR");
            if (s.LastFailureReason == SubscriptionDunningFailureReason.Disputed)
                reasons.Add("DISPUTED");
            if (s.HasOpenBillingTicket) reasons.Add("OPEN_BILLING_TICKET");
            if (s.PreviouslyAutoPauseRecovered) reasons.Add("PREVIOUSLY_AUTO_PAUSED");
            if (s.RentalsLast30Days >= 4) reasons.Add("HIGH_RECENT_ENGAGEMENT");
            if (s.TenureMonths >= 24) reasons.Add("LONG_TENURE");
            if (s.Tier == SubscriptionDunningTier.Premium) reasons.Add("PREMIUM_TIER");
            if (!s.HasBackupPaymentMethod && s.CardExpired) reasons.Add("NO_BACKUP_PAYMENT");
            if (days >= options.ForceCancelAfterDays) reasons.Add("DUNNING_CYCLE_EXHAUSTED");

            // ---- Verdict refinements based on reasons ----
            // Fraud always escalates straight to PauseHold for human review.
            if (s.LastFailureReason == SubscriptionDunningFailureReason.FraudBlock &&
                c.Verdict < SubscriptionDunningVerdict.PauseHold)
            {
                c.Verdict = SubscriptionDunningVerdict.PauseHold;
            }
            // Disputed charge -> TerminalCharge (no further retries).
            if (s.LastFailureReason == SubscriptionDunningFailureReason.Disputed &&
                c.Verdict < SubscriptionDunningVerdict.TerminalCharge)
            {
                c.Verdict = SubscriptionDunningVerdict.TerminalCharge;
            }
            // Insufficient-funds + premium + long tenure + already 2+ attempts ->
            // route to downgrade-offer instead of pure escalation.
            if (s.LastFailureReason == SubscriptionDunningFailureReason.InsufficientFunds &&
                s.Tier >= SubscriptionDunningTier.Standard &&
                s.TenureMonths >= 12 &&
                s.FailedAttempts >= options.ActiveDunningAttempts &&
                c.Verdict != SubscriptionDunningVerdict.ForceCancel &&
                c.Verdict != SubscriptionDunningVerdict.TerminalCharge &&
                c.Verdict != SubscriptionDunningVerdict.PauseHold)
            {
                c.Verdict = SubscriptionDunningVerdict.DowngradeOffer;
                reasons.Add("PRICE_SENSITIVITY_SIGNAL");
            }
            // Auto-pause guard: never auto-pause twice.
            if (c.Verdict == SubscriptionDunningVerdict.PauseHold && s.PreviouslyAutoPauseRecovered &&
                s.LastFailureReason != SubscriptionDunningFailureReason.FraudBlock)
            {
                c.Verdict = SubscriptionDunningVerdict.UrgentRetry;
                reasons.Add("PAUSE_REUSE_BLOCKED");
            }

            c.Reasons = reasons.ToList();

            // ---- Risk score 0..100 ----
            double risk =
                Math.Min(40, s.FailedAttempts * 10.0)
                + Math.Min(20, days * 1.5)
                + (s.CardExpired ? 8.0 : 0.0)
                + (!s.HasBackupPaymentMethod ? 5.0 : 0.0)
                + (s.RecentEmailEngagement ? -6.0 : 4.0)
                + (s.RentalsLast30Days >= 4 ? -8.0 : 0.0)
                + (s.HasOpenBillingTicket ? 5.0 : 0.0)
                + (s.LastFailureReason == SubscriptionDunningFailureReason.FraudBlock ? 15.0 : 0.0)
                + (s.LastFailureReason == SubscriptionDunningFailureReason.Disputed ? 18.0 : 0.0)
                + (s.LastFailureReason == SubscriptionDunningFailureReason.ExpiredCard ? 5.0 : 0.0);

            // Appetite multiplier: cautious +12%, aggressive -12%.
            double appetiteMult =
                options.RiskAppetite == SubscriptionDunningAppetite.Cautious ? 1.12 :
                options.RiskAppetite == SubscriptionDunningAppetite.Aggressive ? 0.88 :
                1.0;
            risk = risk * appetiteMult;
            if (risk < 0) risk = 0;
            if (risk > 100) risk = 100;
            c.DunningRisk = (int)Math.Round(risk);

            // ---- Revenue-at-risk 0..100 ----
            // Proxy: monthly revenue scaled, tier bump, tenure boost capped.
            double rev = (double)s.MonthlyRevenue * 0.6
                       + Math.Min(40.0, (double)s.LifetimeRevenue * 0.01)
                       + (s.Tier == SubscriptionDunningTier.Premium ? 12.0 :
                          s.Tier == SubscriptionDunningTier.Standard ? 6.0 : 0.0)
                       + Math.Min(10.0, s.TenureMonths * 0.2);
            if (rev < 0) rev = 0;
            if (rev > 100) rev = 100;
            c.RevenueAtRisk = (int)Math.Round(rev);

            // ---- Priority ----
            if (c.Verdict == SubscriptionDunningVerdict.ForceCancel ||
                c.Verdict == SubscriptionDunningVerdict.TerminalCharge ||
                c.Verdict == SubscriptionDunningVerdict.PauseHold ||
                c.DunningRisk >= 75)
                c.Priority = SubscriptionDunningActionPriority.P0;
            else if (c.Verdict == SubscriptionDunningVerdict.UrgentRetry ||
                     c.Verdict == SubscriptionDunningVerdict.DowngradeOffer ||
                     c.DunningRisk >= 55)
                c.Priority = SubscriptionDunningActionPriority.P1;
            else if (c.Verdict == SubscriptionDunningVerdict.ActiveDunning ||
                     c.DunningRisk >= 35)
                c.Priority = SubscriptionDunningActionPriority.P2;
            else
                c.Priority = SubscriptionDunningActionPriority.P3;

            // ---- Recommended action (paste-ready one-liner) ----
            c.RecommendedAction = NextAction(c, s, options);
            return c;
        }

        private static string NextAction(
            SubscriptionDunningCase c,
            SubscriptionDunningSnapshot s,
            SubscriptionDunningOptions options)
        {
            switch (c.Verdict)
            {
                case SubscriptionDunningVerdict.Current:
                    return "No action - subscription current.";
                case SubscriptionDunningVerdict.SoftReminder:
                    return s.CardExpired
                        ? "Email reminder to update expired card on file."
                        : "Send soft reminder; auto-retry in 48h.";
                case SubscriptionDunningVerdict.ActiveDunning:
                    return s.HasBackupPaymentMethod
                        ? "Charge backup payment method; notify customer."
                        : "Retry primary card; queue update-card email + in-app banner.";
                case SubscriptionDunningVerdict.UrgentRetry:
                    return "Manual retry today; CSM personal outreach if revenue at risk >= 60.";
                case SubscriptionDunningVerdict.DowngradeOffer:
                    return "Offer one-tier downgrade or 25% discount for 2 cycles to preserve relationship.";
                case SubscriptionDunningVerdict.PauseHold:
                    return s.LastFailureReason == SubscriptionDunningFailureReason.FraudBlock
                        ? "Pause subscription; route to fraud-review queue before any further charge."
                        : "Auto-pause for 7 days; resume only after customer confirms updated payment.";
                case SubscriptionDunningVerdict.TerminalCharge:
                    return "Stop all retries; escalate to dispute team and write down MRR.";
                case SubscriptionDunningVerdict.ForceCancel:
                    return "Cancel subscription; send graceful goodbye email with reactivation link.";
                default:
                    return "Review case.";
            }
        }

        // === Summary =================================================

        private void BuildSummary(
            SubscriptionDunningReport report,
            List<SubscriptionDunningCase> all,
            List<SubscriptionDunningSnapshot> input,
            SubscriptionDunningOptions options)
        {
            var sum = report.Summary;
            sum.TotalSubscriptions = all.Count;
            sum.CurrentSubscriptions = all.Count(c => c.Verdict == SubscriptionDunningVerdict.Current);
            sum.ActiveDunningCount = all.Count(c => c.Verdict == SubscriptionDunningVerdict.ActiveDunning);
            sum.UrgentRetryCount = all.Count(c => c.Verdict == SubscriptionDunningVerdict.UrgentRetry);
            sum.DowngradeOfferCount = all.Count(c => c.Verdict == SubscriptionDunningVerdict.DowngradeOffer);
            sum.PauseHoldCount = all.Count(c => c.Verdict == SubscriptionDunningVerdict.PauseHold);
            sum.TerminalCount = all.Count(c => c.Verdict == SubscriptionDunningVerdict.TerminalCharge);
            sum.ForceCancelCount = all.Count(c => c.Verdict == SubscriptionDunningVerdict.ForceCancel);
            sum.CardIssuesCount = input.Count(s =>
                s.CardExpired ||
                s.LastFailureReason == SubscriptionDunningFailureReason.ExpiredCard ||
                s.LastFailureReason == SubscriptionDunningFailureReason.InvalidCard);

            // Money at risk = MRR of every non-current snapshot.
            // Snapshots are looked up by SubscriptionId (cases may be sorted differently than input).
            var snapsById = new Dictionary<int, SubscriptionDunningSnapshot>(input.Count);
            foreach (var s in input)
            {
                if (!snapsById.ContainsKey(s.SubscriptionId))
                    snapsById[s.SubscriptionId] = s;
            }
            decimal mrrAtRisk = 0m;
            decimal recoverable = 0m;
            foreach (var c in all)
            {
                if (c.Verdict == SubscriptionDunningVerdict.Current) continue;
                SubscriptionDunningSnapshot s;
                if (!snapsById.TryGetValue(c.SubscriptionId, out s)) continue;
                mrrAtRisk += s.MonthlyRevenue;

                // Recoverable: anything not terminal and not force-cancel.
                if (c.Verdict != SubscriptionDunningVerdict.TerminalCharge &&
                    c.Verdict != SubscriptionDunningVerdict.ForceCancel)
                {
                    recoverable += s.MonthlyRevenue;
                }
            }
            sum.TotalMrrAtRisk = Math.Round(mrrAtRisk, 2);
            sum.RecoverableMrrEstimate = Math.Round(recoverable, 2);

            sum.DunningRecoveryProbability = ComputeRecoveryProbability(all, input, options);

            // ---- Score 0..100 ----
            int delinquent = all.Count - sum.CurrentSubscriptions;
            double delinquentShare = sum.TotalSubscriptions == 0
                ? 0.0
                : (double)delinquent / sum.TotalSubscriptions;
            double avgRisk = all.Count == 0 ? 0.0 : all.Average(c => (double)c.DunningRisk);

            // Health 0..100 = 100 - (50 * delinquentShare) - (0.5 * avgRisk) - terminal/cancel penalty.
            double penalty = (sum.TerminalCount + sum.ForceCancelCount) * 4.0
                           + sum.PauseHoldCount * 2.5
                           + sum.UrgentRetryCount * 1.5;
            double score = 100.0 - 50.0 * delinquentShare - 0.5 * avgRisk - penalty;
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            sum.OverallScore = (int)Math.Round(score);

            if (sum.OverallScore >= 85) { sum.Grade = 'A'; sum.HeadlineVerdict = SubscriptionDunningHeadline.PortfolioHealthy; }
            else if (sum.OverallScore >= 70) { sum.Grade = 'B'; sum.HeadlineVerdict = SubscriptionDunningHeadline.WatchPortfolio; }
            else if (sum.OverallScore >= 55) { sum.Grade = 'C'; sum.HeadlineVerdict = SubscriptionDunningHeadline.DunningElevated; }
            else if (sum.OverallScore >= 40) { sum.Grade = 'D'; sum.HeadlineVerdict = SubscriptionDunningHeadline.DunningHigh; }
            else { sum.Grade = 'F'; sum.HeadlineVerdict = SubscriptionDunningHeadline.DunningCritical; }

            // ---- Insights (deterministic order) ----
            var insights = new List<string>();
            if (sum.TotalSubscriptions == 0)
                insights.Add("EMPTY_PORTFOLIO");
            if (sum.CardIssuesCount >= Math.Max(2, sum.TotalSubscriptions / 4))
                insights.Add("CARD_HYGIENE_CAMPAIGN_NEEDED");
            if (sum.PauseHoldCount >= 2)
                insights.Add("MULTIPLE_PAUSE_HOLDS");
            if (sum.ForceCancelCount + sum.TerminalCount >= Math.Max(2, sum.TotalSubscriptions / 5))
                insights.Add("ACCEPTABLE_LOSSES_THIS_CYCLE");
            if (sum.RecoverableMrrEstimate >= sum.TotalMrrAtRisk * 0.75m && sum.TotalMrrAtRisk > 0m)
                insights.Add("RECOVERY_LEVERAGE_HIGH");
            if (all.Count(c => c.LastFailureReason == SubscriptionDunningFailureReason.InsufficientFunds) >=
                Math.Max(2, sum.TotalSubscriptions / 3))
                insights.Add("PRICE_PRESSURE_SIGNAL");
            if (all.Any(c => c.LastFailureReason == SubscriptionDunningFailureReason.FraudBlock))
                insights.Add("FRAUD_REVIEW_REQUIRED");
            if (sum.TotalSubscriptions > 0 && delinquent == 0)
                insights.Add("ALL_CURRENT");
            sum.Insights = insights;
        }

        private static double ComputeRecoveryProbability(
            List<SubscriptionDunningCase> all,
            List<SubscriptionDunningSnapshot> input,
            SubscriptionDunningOptions options)
        {
            if (all.Count == 0) return 1.0;
            double baseProb = options.BaselineRecoveryProbability;
            if (baseProb < 0.0) baseProb = 0.0;
            if (baseProb > 1.0) baseProb = 1.0;

            var snapsById = new Dictionary<int, SubscriptionDunningSnapshot>(input.Count);
            foreach (var s2 in input)
            {
                if (!snapsById.ContainsKey(s2.SubscriptionId))
                    snapsById[s2.SubscriptionId] = s2;
            }
            double sum = 0.0;
            int count = 0;
            foreach (var c in all)
            {
                if (c.Verdict == SubscriptionDunningVerdict.Current) continue;
                SubscriptionDunningSnapshot s;
                if (!snapsById.TryGetValue(c.SubscriptionId, out s)) continue;
                double p = baseProb;
                if (s.RecentEmailEngagement) p += 0.20;
                if (s.HasBackupPaymentMethod) p += 0.12;
                if (s.RentalsLast30Days >= 4) p += 0.10;
                if (s.CardExpired) p -= 0.15;
                if (s.LastFailureReason == SubscriptionDunningFailureReason.FraudBlock) p -= 0.30;
                if (s.LastFailureReason == SubscriptionDunningFailureReason.Disputed) p -= 0.40;
                if (s.FailedAttempts >= options.UrgentRetryAttempts) p -= 0.10;
                if (s.FailedAttempts >= options.TerminalAttempts) p -= 0.25;
                if (p < 0.0) p = 0.0;
                if (p > 1.0) p = 1.0;
                sum += p;
                count++;
            }
            return count == 0 ? 1.0 : Math.Round(sum / count, 4);
        }

        // === Playbook ================================================

        private static void BuildPlaybook(
            SubscriptionDunningReport report,
            List<SubscriptionDunningCase> all,
            SubscriptionDunningOptions options)
        {
            var actions = new List<SubscriptionDunningPlaybookAction>();

            // ---- P0 ----
            var fraudIds = all
                .Where(c => c.LastFailureReason == SubscriptionDunningFailureReason.FraudBlock)
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (fraudIds.Count > 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "FRAUD_REVIEW",
                    Priority = SubscriptionDunningActionPriority.P0,
                    Label = "Route fraud-blocked subscriptions to manual review",
                    Reason = "Charge processor flagged fraud; no retries until cleared.",
                    Owner = "fraud_ops",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetSubscriptionIds = fraudIds
                });
            }

            var cancelIds = all
                .Where(c => c.Verdict == SubscriptionDunningVerdict.ForceCancel)
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (cancelIds.Count > 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "FORCE_CANCEL_CYCLE_EXHAUSTED",
                    Priority = SubscriptionDunningActionPriority.P0,
                    Label = "Cancel subscriptions past dunning window",
                    Reason = "Cycle exhausted (" + options.ForceCancelAfterDays + "+ days). Send goodbye email + reactivation link.",
                    Owner = "billing_ops",
                    BlastRadius = 3,
                    Reversibility = "medium",
                    TargetSubscriptionIds = cancelIds
                });
            }

            var terminalIds = all
                .Where(c => c.Verdict == SubscriptionDunningVerdict.TerminalCharge)
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (terminalIds.Count > 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "WRITE_DOWN_TERMINAL",
                    Priority = SubscriptionDunningActionPriority.P0,
                    Label = "Stop retries on disputed/terminal charges",
                    Reason = "Further retries will only generate chargeback fees.",
                    Owner = "finance",
                    BlastRadius = 2,
                    Reversibility = "medium",
                    TargetSubscriptionIds = terminalIds
                });
            }

            // ---- P1 ----
            var urgentIds = all
                .Where(c => c.Verdict == SubscriptionDunningVerdict.UrgentRetry && c.RevenueAtRisk >= 60)
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (urgentIds.Count > 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "CSM_OUTREACH_HIGH_VALUE",
                    Priority = SubscriptionDunningActionPriority.P1,
                    Label = "CSM personal outreach to high-revenue urgent-retry accounts",
                    Reason = "Revenue-at-risk >= 60 and retry urgency high; relationship save is cheaper than reacquisition.",
                    Owner = "customer_success",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetSubscriptionIds = urgentIds
                });
            }

            var downgradeIds = all
                .Where(c => c.Verdict == SubscriptionDunningVerdict.DowngradeOffer)
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (downgradeIds.Count > 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "OFFER_DOWNGRADE",
                    Priority = SubscriptionDunningActionPriority.P1,
                    Label = "Send downgrade/discount offer to price-sensitive accounts",
                    Reason = "Insufficient-funds pattern + long tenure; preserve relationship at lower MRR.",
                    Owner = "lifecycle_marketing",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetSubscriptionIds = downgradeIds
                });
            }

            var cardIds = all
                .Where(c => c.Reasons.Contains("CARD_EXPIRED"))
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (cardIds.Count >= 2)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "CARD_UPDATER_CAMPAIGN",
                    Priority = SubscriptionDunningActionPriority.P1,
                    Label = "Bulk card-updater email + in-app prompt",
                    Reason = "Multiple expired cards. Batch outreach amortizes cost.",
                    Owner = "lifecycle_marketing",
                    BlastRadius = 3,
                    Reversibility = "high",
                    TargetSubscriptionIds = cardIds
                });
            }

            // ---- P2 ----
            var activeIds = all
                .Where(c => c.Verdict == SubscriptionDunningVerdict.ActiveDunning &&
                            !c.Reasons.Contains("CARD_EXPIRED"))
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (activeIds.Count > 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "AUTO_RETRY_BATCH",
                    Priority = SubscriptionDunningActionPriority.P2,
                    Label = "Run scheduled auto-retry batch",
                    Reason = "Standard dunning cadence; no manual touch needed yet.",
                    Owner = "billing_ops",
                    BlastRadius = 3,
                    Reversibility = "high",
                    TargetSubscriptionIds = activeIds
                });
            }

            var softIds = all
                .Where(c => c.Verdict == SubscriptionDunningVerdict.SoftReminder)
                .OrderBy(c => c.SubscriptionId)
                .Select(c => c.SubscriptionId)
                .ToList();
            if (softIds.Count > 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "SEND_SOFT_REMINDERS",
                    Priority = SubscriptionDunningActionPriority.P2,
                    Label = "Send soft email reminders",
                    Reason = "First failure - majority recover on self-serve update.",
                    Owner = "lifecycle_marketing",
                    BlastRadius = 3,
                    Reversibility = "high",
                    TargetSubscriptionIds = softIds
                });
            }

            // ---- P3 ----
            if (actions.Count == 0)
            {
                actions.Add(new SubscriptionDunningPlaybookAction
                {
                    Id = "PORTFOLIO_HEALTHY",
                    Priority = SubscriptionDunningActionPriority.P3,
                    Label = "Portfolio healthy - schedule weekly check",
                    Reason = "No delinquent subscriptions today.",
                    Owner = "billing_ops",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetSubscriptionIds = new List<int>()
                });
            }

            // Appetite shaping: aggressive trims P2 items when P0/P1 present;
            // cautious appends a portfolio-review action when grade is C/D/F.
            if (options.RiskAppetite == SubscriptionDunningAppetite.Aggressive &&
                actions.Any(a => a.Priority == SubscriptionDunningActionPriority.P0 ||
                                 a.Priority == SubscriptionDunningActionPriority.P1))
            {
                actions = actions
                    .Where(a => a.Priority != SubscriptionDunningActionPriority.P2)
                    .ToList();
            }
            if (options.RiskAppetite == SubscriptionDunningAppetite.Cautious &&
                (report.Summary.Grade == 'C' || report.Summary.Grade == 'D' || report.Summary.Grade == 'F'))
            {
                if (!actions.Any(a => a.Id == "SCHEDULE_PORTFOLIO_REVIEW"))
                {
                    actions.Add(new SubscriptionDunningPlaybookAction
                    {
                        Id = "SCHEDULE_PORTFOLIO_REVIEW",
                        Priority = SubscriptionDunningActionPriority.P2,
                        Label = "Schedule cross-functional dunning portfolio review",
                        Reason = "Grade " + report.Summary.Grade + " warrants billing/CSM/finance sync.",
                        Owner = "billing_ops",
                        BlastRadius = 1,
                        Reversibility = "high",
                        TargetSubscriptionIds = new List<int>()
                    });
                }
            }

            // Stable order: priority asc, id asc.
            report.Playbook = actions
                .OrderBy(a => a.Priority)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
                .ToList();
        }
    }
}
