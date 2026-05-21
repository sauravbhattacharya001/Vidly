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
    /// Per-card breakage-risk verdict, roughly ordered from healthy → most concerning.
    /// </summary>
    public enum GiftCardBreakageVerdict
    {
        Healthy = 0,
        Active = 1,
        PartiallyRedeemed = 2,
        ExpiringSoon = 3,
        Dormant = 4,
        AbandonedHighValue = 5,
        Expired = 6
    }

    /// <summary>Priority bucket for playbook actions.</summary>
    public enum GiftCardBreakageActionPriority { P0, P1, P2, P3 }

    /// <summary>Risk-appetite knob. Cautious inflates risk; Aggressive trims it.</summary>
    public enum GiftCardBreakageAppetite { Cautious, Balanced, Aggressive }

    /// <summary>Portfolio headline verdict bands.</summary>
    public enum GiftCardBreakageHeadline
    {
        PortfolioHealthy = 0,
        WatchPortfolio = 1,
        BreakageElevated = 2,
        BreakageHigh = 3,
        BreakageCritical = 4
    }

    // ── Models ────────────────────────────────────────────────────

    /// <summary>Per-card breakage diagnostic.</summary>
    public class GiftCardBreakageCase
    {
        public int CardId { get; set; }
        public string Code { get; set; }
        public GiftCardBreakageVerdict Verdict { get; set; }
        public GiftCardBreakageActionPriority Priority { get; set; }
        public int BreakageRisk { get; set; } // 0..100
        public decimal Balance { get; set; }
        public decimal OriginalValue { get; set; }
        public decimal LiabilityAmount { get; set; }
        public double RedemptionRatio { get; set; }
        public int AgeDays { get; set; }
        public int? DaysSinceLastActivity { get; set; } // null when never used
        public int? DaysToExpiration { get; set; }      // null when no expiration
        public bool HasEverRedeemed { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>Cross-portfolio remediation action.</summary>
    public class GiftCardBreakagePlaybookAction
    {
        public string Id { get; set; }
        public GiftCardBreakageActionPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; }
        public int BlastRadius { get; set; }
        public string Reversibility { get; set; }
        public List<int> TargetCardIds { get; set; } = new List<int>();
    }

    /// <summary>Portfolio-level summary.</summary>
    public class GiftCardBreakageSummary
    {
        public int TotalCards { get; set; }
        public int ActiveCards { get; set; }
        public int ExpiredCards { get; set; }
        public int DormantCards { get; set; }
        public int AbandonedCards { get; set; }
        public int ExpiringSoonCards { get; set; }
        public int PartiallyRedeemedCards { get; set; }
        public decimal TotalOutstandingLiability { get; set; }
        public decimal ProjectedBreakageAmount { get; set; }
        public double BreakageRate { get; set; }
        public double AvgRedemptionRatio { get; set; }
        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public GiftCardBreakageHeadline HeadlineVerdict { get; set; }
        public List<string> Insights { get; set; } = new List<string>();
    }

    /// <summary>Caller-supplied knobs.</summary>
    public class GiftCardBreakageOptions
    {
        public GiftCardBreakageAppetite RiskAppetite { get; set; } = GiftCardBreakageAppetite.Balanced;
        public int TopCases { get; set; } = 25;
    }

    /// <summary>Full report bundle.</summary>
    public class GiftCardBreakageReport
    {
        public DateTime GeneratedAt { get; set; }
        public GiftCardBreakageOptions Options { get; set; } = new GiftCardBreakageOptions();
        public List<GiftCardBreakageCase> Cases { get; set; } = new List<GiftCardBreakageCase>();
        public List<GiftCardBreakagePlaybookAction> Playbook { get; set; } =
            new List<GiftCardBreakagePlaybookAction>();
        public GiftCardBreakageSummary Summary { get; set; } = new GiftCardBreakageSummary();

        /// <summary>Plain-text renderer.</summary>
        public string ToText() => Render(markdown: false);

        /// <summary>Markdown renderer (Summary / Top cases / Playbook / Insights).</summary>
        public string ToMarkdown() => Render(markdown: true);

        private string Render(bool markdown)
        {
            var sb = new StringBuilder();
            string h2 = markdown ? "## " : "";
            sb.AppendLine(h2 + "Summary");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Total cards | " + Summary.TotalCards + " |");
            sb.AppendLine("| Active cards | " + Summary.ActiveCards + " |");
            sb.AppendLine("| Outstanding liability | $" +
                Summary.TotalOutstandingLiability.ToString("F2", CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| Projected breakage | $" +
                Summary.ProjectedBreakageAmount.ToString("F2", CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| Breakage rate | " +
                (Summary.BreakageRate * 100.0).ToString("F1", CultureInfo.InvariantCulture) + "% |");
            sb.AppendLine("| Score | " + Summary.OverallScore + " (" + Summary.Grade + ") |");
            sb.AppendLine("| Verdict | " + Summary.HeadlineVerdict + " |");
            sb.AppendLine();

            sb.AppendLine(h2 + "Top cases");
            sb.AppendLine();
            sb.AppendLine("| Id | Code | Verdict | Risk | Liability | Reasons |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var c in Cases)
            {
                sb.AppendLine("| " + c.CardId + " | " + (c.Code ?? "") + " | " + c.Verdict
                    + " | " + c.BreakageRisk + " | $" +
                    c.LiabilityAmount.ToString("F2", CultureInfo.InvariantCulture)
                    + " | " + string.Join(",", c.Reasons) + " |");
            }
            sb.AppendLine();

            sb.AppendLine(h2 + "Playbook");
            sb.AppendLine();
            sb.AppendLine("| Priority | Id | Label | Owner | Targets |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var a in Playbook)
            {
                sb.AppendLine("| " + a.Priority + " | " + a.Id + " | " + a.Label
                    + " | " + a.Owner + " | "
                    + string.Join(",", a.TargetCardIds) + " |");
            }
            sb.AppendLine();

            sb.AppendLine(h2 + "Insights");
            sb.AppendLine();
            foreach (var i in Summary.Insights)
                sb.AppendLine("- " + i);
            return sb.ToString();
        }
    }

    // ── Service ───────────────────────────────────────────────────

    /// <summary>
    /// Agentic gift-card "breakage" advisor — 7th Vidly agentic sibling to
    /// <see cref="ReviewIntelligenceService"/>, <see cref="DamageRiskForecastService"/>,
    /// <see cref="LateReturnEscalationService"/>, <see cref="RefundFraudTriageService"/>,
    /// <see cref="WaitlistConversionAdvisorService"/>, and
    /// <see cref="PenaltyWaiverGovernanceAdvisorService"/>.
    ///
    /// "Breakage" is the share of gift-card balance that will never be redeemed.
    /// The advisor scores every card 0..100 (Expired/Dormant/AbandonedHighValue/
    /// ExpiringSoon/PartiallyRedeemed/Active/Healthy), projects total expected
    /// breakage liability, and recommends operator actions: extend expirations,
    /// write off expired balances, outreach to abandoned high-value cards,
    /// dormant-reminder marketing, partial-redemption nudges, and breakage
    /// policy review when posture is poor.
    ///
    /// Pure read-only — never mutates the repository or its cards.
    /// </summary>
    public class GiftCardBreakageAdvisorService
    {
        private readonly IGiftCardRepository _repo;
        private readonly IClock _clock;

        /// <summary>Balance threshold for AbandonedHighValue verdict.</summary>
        public const decimal AbandonedHighValueBalance = 50.00m;

        /// <summary>No-activity days for AbandonedHighValue verdict.</summary>
        public const int AbandonedHighValueDays = 270;

        /// <summary>No-activity days for Dormant verdict.</summary>
        public const int DormantDays = 180;

        /// <summary>Days-to-expiration cutoff for ExpiringSoon verdict.</summary>
        public const int ExpiringSoonDays = 30;

        /// <summary>Recently-active cutoff that protects Active/Healthy verdicts.</summary>
        public const int RecentActivityDays = 60;

        public GiftCardBreakageAdvisorService()
            : this(new InMemoryGiftCardRepository())
        {
        }

        public GiftCardBreakageAdvisorService(IGiftCardRepository repo, IClock clock = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>Build a full breakage report.</summary>
        public GiftCardBreakageReport GenerateReport(GiftCardBreakageOptions options = null)
        {
            options = options ?? new GiftCardBreakageOptions();
            int topCases = Math.Max(0, options.TopCases);
            var report = new GiftCardBreakageReport
            {
                GeneratedAt = _clock.Now,
                Options = options
            };

            var today = _clock.Today;
            var cards = _repo.GetAll() ?? (IReadOnlyList<GiftCard>)new List<GiftCard>();

            foreach (var card in cards)
            {
                if (card == null) continue;
                report.Cases.Add(BuildCase(card, today, options.RiskAppetite));
            }

            // Deterministic order: Priority asc, BreakageRisk desc, Id asc.
            var ordered = report.Cases
                .OrderBy(c => c.Priority)
                .ThenByDescending(c => c.BreakageRisk)
                .ThenBy(c => c.CardId)
                .ToList();
            report.Cases = ordered.Take(topCases).ToList();

            BuildSummary(report, ordered, cards);
            BuildPlaybook(report, ordered, options.RiskAppetite);
            return report;
        }

        // ── Per-card analysis ─────────────────────────────────────

        private GiftCardBreakageCase BuildCase(
            GiftCard card,
            DateTime today,
            GiftCardBreakageAppetite appetite)
        {
            var c = new GiftCardBreakageCase
            {
                CardId = card.Id,
                Code = card.Code,
                Balance = card.Balance,
                OriginalValue = card.OriginalValue,
                AgeDays = Math.Max(0, (int)Math.Floor((today - card.CreatedDate.Date).TotalDays))
            };

            var txns = card.Transactions ?? new List<GiftCardTransaction>();
            var lastActivity = txns
                .Where(t => t != null && t.Type != GiftCardTransactionType.InitialLoad)
                .Select(t => (DateTime?)t.Date.Date)
                .OrderByDescending(d => d)
                .FirstOrDefault();
            c.DaysSinceLastActivity = lastActivity.HasValue
                ? (int?)Math.Max(0, (int)Math.Floor((today - lastActivity.Value).TotalDays))
                : null;

            c.DaysToExpiration = card.ExpirationDate.HasValue
                ? (int?)(int)Math.Floor((card.ExpirationDate.Value.Date - today).TotalDays)
                : null;

            c.HasEverRedeemed = txns.Any(t => t != null && t.Type == GiftCardTransactionType.Redemption);

            c.RedemptionRatio = card.OriginalValue > 0
                ? (double)((card.OriginalValue - card.Balance) / card.OriginalValue)
                : 0.0;
            if (c.RedemptionRatio < 0) c.RedemptionRatio = 0;
            if (c.RedemptionRatio > 1) c.RedemptionRatio = 1;

            // ── Verdict ladder (highest wins) ────────────────────
            bool isExpired = c.DaysToExpiration.HasValue && c.DaysToExpiration.Value < 0 && card.Balance > 0;
            bool isAbandonedHighValue =
                card.Balance >= AbandonedHighValueBalance
                && (!c.DaysSinceLastActivity.HasValue || c.DaysSinceLastActivity.Value >= AbandonedHighValueDays)
                && (!c.DaysToExpiration.HasValue || c.DaysToExpiration.Value > 90)
                && !isExpired;
            bool isDormant =
                (!c.DaysSinceLastActivity.HasValue || c.DaysSinceLastActivity.Value >= DormantDays)
                && card.Balance > 0
                && !isExpired
                && !isAbandonedHighValue;
            bool isExpiringSoon = c.DaysToExpiration.HasValue
                && c.DaysToExpiration.Value >= 0
                && c.DaysToExpiration.Value <= ExpiringSoonDays
                && card.Balance > 0
                && !isExpired
                && !isAbandonedHighValue
                && !isDormant;
            bool isPartiallyRedeemed =
                c.HasEverRedeemed
                && card.Balance > 0
                && c.RedemptionRatio >= 0.10
                && c.RedemptionRatio <= 0.90
                && !isExpired && !isAbandonedHighValue && !isDormant && !isExpiringSoon;
            bool isActive =
                c.DaysSinceLastActivity.HasValue
                && c.DaysSinceLastActivity.Value <= RecentActivityDays
                && card.Balance > 0
                && !isExpired && !isAbandonedHighValue && !isDormant
                && !isExpiringSoon && !isPartiallyRedeemed;
            bool isHealthy = !isExpired && !isAbandonedHighValue && !isDormant
                && !isExpiringSoon && !isPartiallyRedeemed && !isActive
                && (card.Balance == 0 || c.AgeDays < 30);

            if (isExpired) c.Verdict = GiftCardBreakageVerdict.Expired;
            else if (isAbandonedHighValue) c.Verdict = GiftCardBreakageVerdict.AbandonedHighValue;
            else if (isDormant) c.Verdict = GiftCardBreakageVerdict.Dormant;
            else if (isExpiringSoon) c.Verdict = GiftCardBreakageVerdict.ExpiringSoon;
            else if (isPartiallyRedeemed) c.Verdict = GiftCardBreakageVerdict.PartiallyRedeemed;
            else if (isActive) c.Verdict = GiftCardBreakageVerdict.Active;
            else c.Verdict = GiftCardBreakageVerdict.Healthy;

            // ── Reasons ──────────────────────────────────────────
            if (isExpired)
            {
                int daysPast = -(c.DaysToExpiration ?? 0);
                c.Reasons.Add("EXPIRED_" + daysPast.ToString(CultureInfo.InvariantCulture) + "_DAYS_AGO");
            }
            if (isExpiringSoon)
                c.Reasons.Add("EXPIRES_IN_" + c.DaysToExpiration.Value.ToString(CultureInfo.InvariantCulture)
                              + "_DAYS");
            if (c.DaysSinceLastActivity.HasValue && c.DaysSinceLastActivity.Value >= AbandonedHighValueDays)
                c.Reasons.Add("NO_ACTIVITY_" + c.DaysSinceLastActivity.Value.ToString(CultureInfo.InvariantCulture)
                              + "D");
            else if (c.DaysSinceLastActivity.HasValue && c.DaysSinceLastActivity.Value >= DormantDays)
                c.Reasons.Add("NO_ACTIVITY_" + c.DaysSinceLastActivity.Value.ToString(CultureInfo.InvariantCulture)
                              + "D");
            if (!c.HasEverRedeemed && c.AgeDays >= 180)
                c.Reasons.Add("NEVER_REDEEMED");
            if (card.OriginalValue > 0 && card.Balance >= 0.75m * card.OriginalValue && c.AgeDays >= 365)
                c.Reasons.Add("HIGH_BALANCE_LEFT");
            if (c.DaysSinceLastActivity.HasValue && c.DaysSinceLastActivity.Value <= 30 && c.HasEverRedeemed)
                c.Reasons.Add("RECENTLY_USED");

            // ── Risk score ───────────────────────────────────────
            double raw = 0;
            if (isExpired) raw += 35;
            if (isDormant || isAbandonedHighValue) raw += 25;
            if (isExpiringSoon) raw += 20;
            if (!c.HasEverRedeemed && c.AgeDays >= 180) raw += 15;
            if (card.OriginalValue > 0 && card.Balance >= 0.75m * card.OriginalValue && c.AgeDays >= 365) raw += 10;
            if (c.DaysSinceLastActivity.HasValue && c.DaysSinceLastActivity.Value >= 365) raw += 10;
            if (c.DaysSinceLastActivity.HasValue && c.DaysSinceLastActivity.Value <= 30 && c.HasEverRedeemed)
                raw -= 15;

            double mult = appetite == GiftCardBreakageAppetite.Cautious ? 1.15
                        : appetite == GiftCardBreakageAppetite.Aggressive ? 0.85
                        : 1.0;
            int score = (int)Math.Round(raw * mult);
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            c.BreakageRisk = score;

            // ── Priority ─────────────────────────────────────────
            switch (c.Verdict)
            {
                case GiftCardBreakageVerdict.Expired:
                case GiftCardBreakageVerdict.AbandonedHighValue:
                    c.Priority = GiftCardBreakageActionPriority.P0; break;
                case GiftCardBreakageVerdict.Dormant:
                case GiftCardBreakageVerdict.ExpiringSoon:
                    c.Priority = GiftCardBreakageActionPriority.P1; break;
                case GiftCardBreakageVerdict.PartiallyRedeemed:
                    c.Priority = GiftCardBreakageActionPriority.P2; break;
                default:
                    c.Priority = GiftCardBreakageActionPriority.P3; break;
            }

            c.LiabilityAmount = c.Verdict == GiftCardBreakageVerdict.Healthy ? 0m : card.Balance;
            return c;
        }

        // ── Portfolio summary ─────────────────────────────────────

        private void BuildSummary(
            GiftCardBreakageReport report,
            List<GiftCardBreakageCase> allCases,
            IReadOnlyList<GiftCard> cards)
        {
            var s = report.Summary;
            s.TotalCards = allCases.Count;
            s.ExpiredCards = allCases.Count(c => c.Verdict == GiftCardBreakageVerdict.Expired);
            s.DormantCards = allCases.Count(c => c.Verdict == GiftCardBreakageVerdict.Dormant);
            s.AbandonedCards = allCases.Count(c => c.Verdict == GiftCardBreakageVerdict.AbandonedHighValue);
            s.ExpiringSoonCards = allCases.Count(c => c.Verdict == GiftCardBreakageVerdict.ExpiringSoon);
            s.PartiallyRedeemedCards = allCases.Count(c => c.Verdict == GiftCardBreakageVerdict.PartiallyRedeemed);
            s.ActiveCards = cards.Count(card => card != null && card.IsActive && card.Balance > 0);
            s.TotalOutstandingLiability = cards.Where(card => card != null && card.IsActive).Sum(card => card.Balance);
            s.ProjectedBreakageAmount = allCases
                .Where(c => c.Priority == GiftCardBreakageActionPriority.P0
                         || c.Priority == GiftCardBreakageActionPriority.P1)
                .Sum(c => c.LiabilityAmount);

            decimal denom = s.TotalOutstandingLiability;
            if (denom < 0.01m) denom = 0.01m;
            s.BreakageRate = (double)(s.ProjectedBreakageAmount / denom);
            if (s.BreakageRate < 0) s.BreakageRate = 0;

            var withValue = allCases.Where(c => c.OriginalValue > 0).ToList();
            s.AvgRedemptionRatio = withValue.Count == 0
                ? 0.0
                : withValue.Average(c => c.RedemptionRatio);

            int rawScore = 100 - (int)Math.Round(s.BreakageRate * 100.0);
            if (rawScore < 0) rawScore = 0;
            if (rawScore > 100) rawScore = 100;
            s.OverallScore = rawScore;

            s.Grade = rawScore >= 90 ? 'A'
                    : rawScore >= 75 ? 'B'
                    : rawScore >= 55 ? 'C'
                    : rawScore >= 35 ? 'D'
                    : 'F';

            s.HeadlineVerdict = rawScore >= 90 ? GiftCardBreakageHeadline.PortfolioHealthy
                              : rawScore >= 75 ? GiftCardBreakageHeadline.WatchPortfolio
                              : rawScore >= 55 ? GiftCardBreakageHeadline.BreakageElevated
                              : rawScore >= 35 ? GiftCardBreakageHeadline.BreakageHigh
                              : GiftCardBreakageHeadline.BreakageCritical;

            // Insights — always at least one.
            if (s.TotalCards == 0)
            {
                s.Insights.Add("INSUFFICIENT_DATA");
                return;
            }

            if (s.BreakageRate > 0.30)
                s.Insights.Add("BREAKAGE_RATE_HIGH:" + (s.BreakageRate * 100.0).ToString("F1", CultureInfo.InvariantCulture) + "%");
            if (s.TotalOutstandingLiability > 1000m)
                s.Insights.Add("LARGE_LIABILITY:$" + s.TotalOutstandingLiability.ToString("F2", CultureInfo.InvariantCulture));
            if (s.ExpiringSoonCards >= 3)
                s.Insights.Add("HEAVY_EXPIRY_PRESSURE:" + s.ExpiringSoonCards);
            if (s.AvgRedemptionRatio > 0.70)
                s.Insights.Add("STRONG_REDEMPTION:" + (s.AvgRedemptionRatio * 100.0).ToString("F0", CultureInfo.InvariantCulture) + "%");
            if (s.TotalCards > 0 && s.DormantCards * 2 > s.TotalCards)
                s.Insights.Add("MOSTLY_DORMANT:" + s.DormantCards + "/" + s.TotalCards);

            if (s.Insights.Count == 0)
                s.Insights.Add("HEALTHY_PORTFOLIO");
        }

        // ── Playbook ─────────────────────────────────────────────

        private void BuildPlaybook(
            GiftCardBreakageReport report,
            List<GiftCardBreakageCase> allCases,
            GiftCardBreakageAppetite appetite)
        {
            var pb = report.Playbook;

            var expiringSoon = allCases
                .Where(c => c.Verdict == GiftCardBreakageVerdict.ExpiringSoon)
                .Select(c => c.CardId).OrderBy(i => i).ToList();
            if (expiringSoon.Count > 0)
            {
                pb.Add(new GiftCardBreakagePlaybookAction
                {
                    Id = "EXTEND_EXPIRY_EXPIRING_SOON",
                    Priority = GiftCardBreakageActionPriority.P0,
                    Label = "Extend expiration on cards expiring within " + ExpiringSoonDays + " days",
                    Reason = expiringSoon.Count + " cards within the soon-to-expire window.",
                    Owner = "cards_ops",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetCardIds = expiringSoon
                });
            }

            var expired = allCases
                .Where(c => c.Verdict == GiftCardBreakageVerdict.Expired)
                .Select(c => c.CardId).OrderBy(i => i).ToList();
            if (expired.Count > 0)
            {
                pb.Add(new GiftCardBreakagePlaybookAction
                {
                    Id = "WRITE_OFF_EXPIRED",
                    Priority = GiftCardBreakageActionPriority.P0,
                    Label = "Write off expired card balances",
                    Reason = expired.Count + " expired cards with residual balance.",
                    Owner = "finance",
                    BlastRadius = 3,
                    Reversibility = "low",
                    TargetCardIds = expired
                });
            }

            var abandoned = allCases
                .Where(c => c.Verdict == GiftCardBreakageVerdict.AbandonedHighValue)
                .Select(c => c.CardId).OrderBy(i => i).ToList();
            if (abandoned.Count > 0)
            {
                pb.Add(new GiftCardBreakagePlaybookAction
                {
                    Id = "OUTREACH_ABANDONED_HIGH_VALUE",
                    Priority = GiftCardBreakageActionPriority.P0,
                    Label = "Personal outreach for abandoned high-value cards",
                    Reason = abandoned.Count + " high-value cards with no activity in 270+ days.",
                    Owner = "cs",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetCardIds = abandoned
                });
            }

            var dormant = allCases
                .Where(c => c.Verdict == GiftCardBreakageVerdict.Dormant)
                .Select(c => c.CardId).OrderBy(i => i).ToList();
            if (dormant.Count >= 3)
            {
                pb.Add(new GiftCardBreakagePlaybookAction
                {
                    Id = "SEND_DORMANT_REMINDER_EMAIL",
                    Priority = GiftCardBreakageActionPriority.P1,
                    Label = "Send dormant-card reminder email campaign",
                    Reason = dormant.Count + " dormant cards (>=180d inactive).",
                    Owner = "marketing",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetCardIds = dormant
                });
            }

            var partial = allCases
                .Where(c => c.Verdict == GiftCardBreakageVerdict.PartiallyRedeemed)
                .Select(c => c.CardId).OrderBy(i => i).ToList();
            if (partial.Count >= 5)
            {
                pb.Add(new GiftCardBreakagePlaybookAction
                {
                    Id = "PROMOTE_PARTIAL_REDEMPTION",
                    Priority = GiftCardBreakageActionPriority.P2,
                    Label = "Promote use-up of partially-redeemed cards",
                    Reason = partial.Count + " cards in the 10-90% redeemed band.",
                    Owner = "marketing",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetCardIds = partial
                });
            }

            if (appetite == GiftCardBreakageAppetite.Cautious
                && (report.Summary.Grade == 'C' || report.Summary.Grade == 'D' || report.Summary.Grade == 'F'))
            {
                pb.Add(new GiftCardBreakagePlaybookAction
                {
                    Id = "REVIEW_BREAKAGE_POLICY",
                    Priority = GiftCardBreakageActionPriority.P2,
                    Label = "Review gift-card breakage policy and accruals",
                    Reason = "Portfolio grade " + report.Summary.Grade
                              + " with cautious risk appetite warrants a finance review.",
                    Owner = "finance",
                    BlastRadius = 3,
                    Reversibility = "medium"
                });
            }

            // Dedupe by Id (defensive — should not happen).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<GiftCardBreakagePlaybookAction>();
            foreach (var a in pb)
            {
                if (seen.Add(a.Id)) deduped.Add(a);
            }
            pb.Clear();
            pb.AddRange(deduped);

            bool anyP0orP1 = pb.Any(a => a.Priority == GiftCardBreakageActionPriority.P0
                                      || a.Priority == GiftCardBreakageActionPriority.P1);
            if (pb.Count == 0)
            {
                pb.Add(new GiftCardBreakagePlaybookAction
                {
                    Id = "PORTFOLIO_HEALTHY",
                    Priority = GiftCardBreakageActionPriority.P3,
                    Label = "No action — gift-card portfolio is healthy",
                    Reason = "No P0/P1/P2 signals across the portfolio.",
                    Owner = "noop",
                    BlastRadius = 0,
                    Reversibility = "high"
                });
            }
            else if (appetite == GiftCardBreakageAppetite.Aggressive && anyP0orP1)
            {
                // Aggressive: trim P3 fallback noise. (None added above, but defensive.)
                pb.RemoveAll(a => a.Priority == GiftCardBreakageActionPriority.P3);
            }

            // Final order: Priority asc, Id asc.
            var ordered = pb.OrderBy(a => a.Priority).ThenBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList();
            pb.Clear();
            pb.AddRange(ordered);
        }
    }
}
