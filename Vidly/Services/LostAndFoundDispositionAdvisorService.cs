using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // === Enums ====================================================

    /// <summary>
    /// Per-item disposition verdict, roughly ordered from healthy to most concerning.
    /// </summary>
    public enum LostFoundDispositionVerdict
    {
        Resolved = 0,
        NewlyFound = 1,
        PendingClaim = 2,
        ExpiringSoon = 3,
        StaleUnclaimed = 4,
        UnverifiedClaimStale = 5,
        OverdueDisposal = 6
    }

    /// <summary>Priority bucket for playbook actions.</summary>
    public enum LostFoundDispositionActionPriority { P0, P1, P2, P3 }

    /// <summary>Risk-appetite knob. Cautious inflates urgency; Aggressive trims it.</summary>
    public enum LostFoundDispositionAppetite { Cautious, Balanced, Aggressive }

    /// <summary>Portfolio headline verdict bands.</summary>
    public enum LostFoundDispositionHeadline
    {
        PortfolioHealthy = 0,
        WatchPortfolio = 1,
        BacklogElevated = 2,
        BacklogHigh = 3,
        BacklogCritical = 4
    }

    // === Models ===================================================

    /// <summary>Per-item disposition diagnostic.</summary>
    public class LostFoundDispositionCase
    {
        public int ItemId { get; set; }
        public string Description { get; set; }
        public LostItemCategory Category { get; set; }
        public LostItemStatus Status { get; set; }
        public LostFoundDispositionVerdict Verdict { get; set; }
        public LostFoundDispositionActionPriority Priority { get; set; }
        public int DispositionRisk { get; set; } // 0..100
        public int AgeDays { get; set; }
        public int RetentionDays { get; set; }
        public int DaysToRetention { get; set; } // negative when overdue
        public int? DaysSincePendingClaim { get; set; }
        public string StorageBin { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>Cross-portfolio remediation action.</summary>
    public class LostFoundDispositionPlaybookAction
    {
        public string Id { get; set; }
        public LostFoundDispositionActionPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; }
        public int BlastRadius { get; set; }
        public string Reversibility { get; set; }
        public List<int> TargetItemIds { get; set; } = new List<int>();
    }

    /// <summary>Portfolio-level summary.</summary>
    public class LostFoundDispositionSummary
    {
        public int TotalItems { get; set; }
        public int UnclaimedItems { get; set; }
        public int PendingClaimItems { get; set; }
        public int OverdueItems { get; set; }
        public int StaleUnverifiedClaims { get; set; }
        public int ExpiringSoonItems { get; set; }
        public int ResolvedItems { get; set; }
        public double AvgAgeDays { get; set; }
        public double ResolutionRate { get; set; }
        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public LostFoundDispositionHeadline HeadlineVerdict { get; set; }
        public List<string> Insights { get; set; } = new List<string>();
    }

    /// <summary>Caller-supplied knobs.</summary>
    public class LostFoundDispositionOptions
    {
        public LostFoundDispositionAppetite RiskAppetite { get; set; } = LostFoundDispositionAppetite.Balanced;
        public int TopCases { get; set; } = 25;
        /// <summary>Days a pending claim may sit before becoming "stale". Default 7.</summary>
        public int StaleClaimDays { get; set; } = 7;
        /// <summary>How close to the retention deadline counts as ExpiringSoon. Default 3.</summary>
        public int ExpiringSoonDays { get; set; } = 3;
        /// <summary>High-value categories that warrant extra reconnect outreach.</summary>
        public HashSet<LostItemCategory> HighValueCategories { get; set; } =
            new HashSet<LostItemCategory>
            {
                LostItemCategory.Electronics,
                LostItemCategory.Wallet,
                LostItemCategory.Keys,
                LostItemCategory.Jewelry,
            };
    }

    /// <summary>Full report bundle.</summary>
    public class LostFoundDispositionReport
    {
        public DateTime GeneratedAt { get; set; }
        public LostFoundDispositionOptions Options { get; set; } = new LostFoundDispositionOptions();
        public List<LostFoundDispositionCase> Cases { get; set; } = new List<LostFoundDispositionCase>();
        public List<LostFoundDispositionPlaybookAction> Playbook { get; set; } =
            new List<LostFoundDispositionPlaybookAction>();
        public LostFoundDispositionSummary Summary { get; set; } = new LostFoundDispositionSummary();

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
            sb.AppendLine("| Total items | " + Summary.TotalItems + " |");
            sb.AppendLine("| Unclaimed | " + Summary.UnclaimedItems + " |");
            sb.AppendLine("| Pending claims | " + Summary.PendingClaimItems + " |");
            sb.AppendLine("| Overdue | " + Summary.OverdueItems + " |");
            sb.AppendLine("| Stale unverified | " + Summary.StaleUnverifiedClaims + " |");
            sb.AppendLine("| Expiring soon | " + Summary.ExpiringSoonItems + " |");
            sb.AppendLine("| Resolved | " + Summary.ResolvedItems + " |");
            sb.AppendLine("| Avg age (days) | " +
                Summary.AvgAgeDays.ToString("F1", CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| Resolution rate | " +
                (Summary.ResolutionRate * 100.0).ToString("F1", CultureInfo.InvariantCulture) + "% |");
            sb.AppendLine("| Score | " + Summary.OverallScore + " (" + Summary.Grade + ") |");
            sb.AppendLine("| Verdict | " + Summary.HeadlineVerdict + " |");
            sb.AppendLine();

            sb.AppendLine(h2 + "Top cases");
            sb.AppendLine();
            sb.AppendLine("| Id | Category | Verdict | Risk | Age | Bin | Reasons |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var c in Cases)
            {
                sb.AppendLine("| " + c.ItemId + " | " + c.Category + " | " + c.Verdict
                    + " | " + c.DispositionRisk + " | " + c.AgeDays
                    + " | " + (c.StorageBin ?? "") + " | "
                    + string.Join(",", c.Reasons) + " |");
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
                    + string.Join(",", a.TargetItemIds) + " |");
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
    /// Agentic lost-and-found disposition advisor - 8th Vidly agentic sibling to
    /// <see cref="ReviewIntelligenceService"/>, <see cref="DamageRiskForecastService"/>,
    /// <see cref="LateReturnEscalationService"/>, <see cref="RefundFraudTriageService"/>,
    /// <see cref="WaitlistConversionAdvisorService"/>,
    /// <see cref="PenaltyWaiverGovernanceAdvisorService"/>, and
    /// <see cref="GiftCardBreakageAdvisorService"/>.
    ///
    /// Audits the lost-and-found portfolio for items piling up past their retention
    /// deadline, pending claims that never got verified, and high-value items that
    /// warrant proactive owner outreach. Pure read-only - never mutates the
    /// repository or its items/claims.
    /// </summary>
    public class LostAndFoundDispositionAdvisorService
    {
        private readonly ILostAndFoundRepository _repo;
        private readonly IClock _clock;

        public LostAndFoundDispositionAdvisorService()
            : this(new InMemoryLostAndFoundRepository())
        {
        }

        public LostAndFoundDispositionAdvisorService(ILostAndFoundRepository repo, IClock clock = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>Build a full disposition report.</summary>
        public LostFoundDispositionReport GenerateReport(LostFoundDispositionOptions options = null)
        {
            options = options ?? new LostFoundDispositionOptions();
            int topCases = Math.Max(0, options.TopCases);
            int staleClaimDays = Math.Max(1, options.StaleClaimDays);
            int expiringSoonDays = Math.Max(0, options.ExpiringSoonDays);

            var report = new LostFoundDispositionReport
            {
                GeneratedAt = _clock.Now,
                Options = options
            };

            var today = _clock.Today;
            var items = (_repo.GetAll() ?? Enumerable.Empty<LostItem>()).Where(i => i != null).ToList();

            foreach (var item in items)
            {
                var pendingClaims = (_repo.GetClaimsForItem(item.Id) ?? Enumerable.Empty<LostItemClaim>())
                    .Where(c => c != null && !c.Verified && !c.Rejected)
                    .ToList();
                report.Cases.Add(BuildCase(item, pendingClaims, today, options));
            }

            // Deterministic order: Priority asc, DispositionRisk desc, Id asc.
            var ordered = report.Cases
                .OrderBy(c => c.Priority)
                .ThenByDescending(c => c.DispositionRisk)
                .ThenBy(c => c.ItemId)
                .ToList();
            report.Cases = ordered.Take(topCases).ToList();

            BuildSummary(report, ordered, items, options);
            BuildPlaybook(report, ordered, options);
            return report;
        }

        // === Per-item analysis ====================================

        private LostFoundDispositionCase BuildCase(
            LostItem item,
            List<LostItemClaim> pendingClaims,
            DateTime today,
            LostFoundDispositionOptions options)
        {
            var c = new LostFoundDispositionCase
            {
                ItemId = item.Id,
                Description = item.Description,
                Category = item.Category,
                Status = item.Status,
                StorageBin = item.StorageBin,
                RetentionDays = item.RetentionDays > 0 ? item.RetentionDays : 30,
                AgeDays = Math.Max(0, (int)Math.Floor((today - item.FoundAt.Date).TotalDays))
            };

            c.DaysToRetention = c.RetentionDays - c.AgeDays;

            var freshestPending = pendingClaims
                .OrderByDescending(p => p.ClaimDate)
                .FirstOrDefault();
            if (freshestPending != null)
            {
                c.DaysSincePendingClaim = Math.Max(0,
                    (int)Math.Floor((today - freshestPending.ClaimDate.Date).TotalDays));
            }

            // === Verdict ladder (highest wins) ====================
            bool isResolved =
                item.Status == LostItemStatus.Claimed
                || item.Status == LostItemStatus.Disposed
                || item.Status == LostItemStatus.Donated;

            bool isOverdue =
                !isResolved
                && c.DaysToRetention < 0
                && (item.Status == LostItemStatus.Found || item.Status == LostItemStatus.ClaimPending);

            bool isStaleUnverified =
                !isResolved && !isOverdue
                && item.Status == LostItemStatus.ClaimPending
                && c.DaysSincePendingClaim.HasValue
                && c.DaysSincePendingClaim.Value >= options.StaleClaimDays;

            bool isExpiringSoon =
                !isResolved && !isOverdue && !isStaleUnverified
                && c.DaysToRetention >= 0
                && c.DaysToRetention <= options.ExpiringSoonDays
                && item.Status == LostItemStatus.Found;

            bool isStaleUnclaimed =
                !isResolved && !isOverdue && !isStaleUnverified && !isExpiringSoon
                && item.Status == LostItemStatus.Found
                && c.AgeDays >= Math.Max(7, c.RetentionDays / 2);

            bool isPendingClaim =
                !isResolved && !isOverdue && !isStaleUnverified
                && item.Status == LostItemStatus.ClaimPending;

            bool isNewlyFound =
                !isResolved && !isOverdue && !isStaleUnverified && !isExpiringSoon
                && !isStaleUnclaimed && !isPendingClaim
                && item.Status == LostItemStatus.Found;

            if (isOverdue) c.Verdict = LostFoundDispositionVerdict.OverdueDisposal;
            else if (isStaleUnverified) c.Verdict = LostFoundDispositionVerdict.UnverifiedClaimStale;
            else if (isStaleUnclaimed) c.Verdict = LostFoundDispositionVerdict.StaleUnclaimed;
            else if (isExpiringSoon) c.Verdict = LostFoundDispositionVerdict.ExpiringSoon;
            else if (isPendingClaim) c.Verdict = LostFoundDispositionVerdict.PendingClaim;
            else if (isNewlyFound) c.Verdict = LostFoundDispositionVerdict.NewlyFound;
            else c.Verdict = LostFoundDispositionVerdict.Resolved;

            // === Risk scoring 0..100 ==============================
            int risk = 0;
            if (isOverdue)
            {
                risk += 55 + Math.Min(25, Math.Abs(c.DaysToRetention) * 2);
                c.Reasons.Add("overdue_by_" + Math.Abs(c.DaysToRetention) + "d");
            }
            if (isStaleUnverified)
            {
                risk += 45 + Math.Min(15, (c.DaysSincePendingClaim ?? 0));
                c.Reasons.Add("pending_claim_stale_" + c.DaysSincePendingClaim + "d");
            }
            if (isExpiringSoon)
            {
                risk += 30 + Math.Max(0, options.ExpiringSoonDays - c.DaysToRetention) * 3;
                c.Reasons.Add("expires_in_" + c.DaysToRetention + "d");
            }
            if (isStaleUnclaimed)
            {
                risk += 20 + Math.Min(20, c.AgeDays / 2);
                c.Reasons.Add("unclaimed_age_" + c.AgeDays + "d");
            }
            if (isPendingClaim && !isStaleUnverified)
            {
                risk += 10;
                c.Reasons.Add("pending_claim_open");
            }
            if (options.HighValueCategories != null && options.HighValueCategories.Contains(item.Category)
                && !isResolved)
            {
                risk += 10;
                c.Reasons.Add("high_value_category");
            }
            if (isResolved)
            {
                c.Reasons.Add(item.Status.ToString().ToLowerInvariant());
            }

            double mult = options.RiskAppetite == LostFoundDispositionAppetite.Cautious ? 1.15
                       : options.RiskAppetite == LostFoundDispositionAppetite.Aggressive ? 0.85
                       : 1.0;
            risk = (int)Math.Round(risk * mult, MidpointRounding.AwayFromZero);
            if (risk < 0) risk = 0;
            if (risk > 100) risk = 100;
            c.DispositionRisk = risk;

            // === Priority =========================================
            if (isOverdue) c.Priority = LostFoundDispositionActionPriority.P0;
            else if (isStaleUnverified) c.Priority = LostFoundDispositionActionPriority.P1;
            else if (isExpiringSoon) c.Priority = LostFoundDispositionActionPriority.P1;
            else if (isStaleUnclaimed) c.Priority = LostFoundDispositionActionPriority.P2;
            else if (isPendingClaim) c.Priority = LostFoundDispositionActionPriority.P2;
            else c.Priority = LostFoundDispositionActionPriority.P3;

            return c;
        }

        // === Summary / scoring ====================================

        private static void BuildSummary(
            LostFoundDispositionReport report,
            List<LostFoundDispositionCase> allCases,
            List<LostItem> rawItems,
            LostFoundDispositionOptions options)
        {
            var s = report.Summary;
            s.TotalItems = allCases.Count;
            s.UnclaimedItems = allCases.Count(c => c.Status == LostItemStatus.Found);
            s.PendingClaimItems = allCases.Count(c => c.Status == LostItemStatus.ClaimPending);
            s.OverdueItems = allCases.Count(c => c.Verdict == LostFoundDispositionVerdict.OverdueDisposal);
            s.StaleUnverifiedClaims = allCases.Count(c => c.Verdict == LostFoundDispositionVerdict.UnverifiedClaimStale);
            s.ExpiringSoonItems = allCases.Count(c => c.Verdict == LostFoundDispositionVerdict.ExpiringSoon);
            s.ResolvedItems = allCases.Count(c => c.Verdict == LostFoundDispositionVerdict.Resolved);

            s.AvgAgeDays = allCases.Count > 0 ? allCases.Average(c => c.AgeDays) : 0.0;
            int touched = rawItems.Count(i => i.Status != LostItemStatus.Found
                                              && i.Status != LostItemStatus.ClaimPending);
            s.ResolutionRate = rawItems.Count > 0 ? (double)touched / rawItems.Count : 0.0;

            // Score: start at 100, subtract for overdue/stale/expiring backlog.
            double score = 100.0;
            score -= s.OverdueItems * 12.0;
            score -= s.StaleUnverifiedClaims * 8.0;
            score -= s.ExpiringSoonItems * 4.0;
            score -= Math.Max(0, s.PendingClaimItems - 2) * 2.0;
            score += Math.Min(10.0, s.ResolutionRate * 20.0);

            double mult = options.RiskAppetite == LostFoundDispositionAppetite.Cautious ? 0.92
                       : options.RiskAppetite == LostFoundDispositionAppetite.Aggressive ? 1.08
                       : 1.0;
            score *= mult;
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            s.OverallScore = (int)Math.Round(score, MidpointRounding.AwayFromZero);

            if (s.OverallScore >= 85) s.Grade = 'A';
            else if (s.OverallScore >= 70) s.Grade = 'B';
            else if (s.OverallScore >= 55) s.Grade = 'C';
            else if (s.OverallScore >= 40) s.Grade = 'D';
            else s.Grade = 'F';

            // Headline ladder.
            if (s.OverdueItems >= 5 || s.OverallScore < 40)
                s.HeadlineVerdict = LostFoundDispositionHeadline.BacklogCritical;
            else if (s.OverdueItems >= 2 || s.OverallScore < 55)
                s.HeadlineVerdict = LostFoundDispositionHeadline.BacklogHigh;
            else if (s.OverdueItems >= 1 || s.StaleUnverifiedClaims >= 1 || s.OverallScore < 70)
                s.HeadlineVerdict = LostFoundDispositionHeadline.BacklogElevated;
            else if (s.ExpiringSoonItems >= 1 || s.OverallScore < 85)
                s.HeadlineVerdict = LostFoundDispositionHeadline.WatchPortfolio;
            else
                s.HeadlineVerdict = LostFoundDispositionHeadline.PortfolioHealthy;

            // Insights.
            if (s.OverdueItems > 0)
                s.Insights.Add("OVERDUE_BACKLOG (" + s.OverdueItems + ")");
            if (s.StaleUnverifiedClaims > 0)
                s.Insights.Add("STALE_UNVERIFIED_CLAIMS (" + s.StaleUnverifiedClaims + ")");
            if (s.ExpiringSoonItems > 0)
                s.Insights.Add("UPCOMING_DISPOSAL_WAVE (" + s.ExpiringSoonItems + ")");
            int highValueAtRisk = allCases.Count(c =>
                options.HighValueCategories != null
                && options.HighValueCategories.Contains(c.Category)
                && c.Verdict != LostFoundDispositionVerdict.Resolved);
            if (highValueAtRisk >= 1)
                s.Insights.Add("HIGH_VALUE_ITEMS_AT_RISK (" + highValueAtRisk + ")");
            if (s.ResolutionRate >= 0.50)
                s.Insights.Add("HEALTHY_RESOLUTION_RATE");
            if (s.OverallScore >= 85)
                s.Insights.Add("PORTFOLIO_HEALTHY");
            if (s.TotalItems == 0)
                s.Insights.Add("NO_LOST_ITEMS");
        }

        // === Playbook =============================================

        private static void BuildPlaybook(
            LostFoundDispositionReport report,
            List<LostFoundDispositionCase> allCases,
            LostFoundDispositionOptions options)
        {
            var pb = report.Playbook;

            var overdue = allCases
                .Where(c => c.Verdict == LostFoundDispositionVerdict.OverdueDisposal)
                .Select(c => c.ItemId).OrderBy(i => i).ToList();
            if (overdue.Count > 0)
            {
                pb.Add(new LostFoundDispositionPlaybookAction
                {
                    Id = "DISPOSE_OR_DONATE_OVERDUE_ITEMS",
                    Priority = LostFoundDispositionActionPriority.P0,
                    Label = "Dispose or donate items past retention deadline",
                    Reason = overdue.Count + " item(s) exceed their retention deadline.",
                    Owner = "operations",
                    BlastRadius = 2,
                    Reversibility = "low",
                    TargetItemIds = overdue
                });
            }

            var staleClaims = allCases
                .Where(c => c.Verdict == LostFoundDispositionVerdict.UnverifiedClaimStale)
                .Select(c => c.ItemId).OrderBy(i => i).ToList();
            if (staleClaims.Count > 0)
            {
                pb.Add(new LostFoundDispositionPlaybookAction
                {
                    Id = "EXPEDITE_CLAIM_VERIFICATION",
                    Priority = LostFoundDispositionActionPriority.P1,
                    Label = "Expedite stale unverified customer claims",
                    Reason = staleClaims.Count + " pending claim(s) sat >= "
                              + options.StaleClaimDays + "d without verification.",
                    Owner = "front_desk",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetItemIds = staleClaims
                });
            }

            var expiring = allCases
                .Where(c => c.Verdict == LostFoundDispositionVerdict.ExpiringSoon)
                .Select(c => c.ItemId).OrderBy(i => i).ToList();
            if (expiring.Count > 0)
            {
                pb.Add(new LostFoundDispositionPlaybookAction
                {
                    Id = "RUN_FINAL_OWNER_OUTREACH",
                    Priority = LostFoundDispositionActionPriority.P1,
                    Label = "Run final owner outreach before disposal",
                    Reason = expiring.Count + " item(s) reach retention deadline within "
                              + options.ExpiringSoonDays + "d.",
                    Owner = "front_desk",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetItemIds = expiring
                });
            }

            var highValueAtRisk = allCases
                .Where(c =>
                    options.HighValueCategories != null
                    && options.HighValueCategories.Contains(c.Category)
                    && (c.Verdict == LostFoundDispositionVerdict.StaleUnclaimed
                        || c.Verdict == LostFoundDispositionVerdict.ExpiringSoon
                        || c.Verdict == LostFoundDispositionVerdict.OverdueDisposal))
                .Select(c => c.ItemId).OrderBy(i => i).ToList();
            if (highValueAtRisk.Count > 0)
            {
                pb.Add(new LostFoundDispositionPlaybookAction
                {
                    Id = "EXTEND_RETENTION_HIGH_VALUE",
                    Priority = LostFoundDispositionActionPriority.P2,
                    Label = "Extend retention window for high-value categories",
                    Reason = highValueAtRisk.Count
                              + " high-value item(s) (electronics/wallet/keys/jewelry) at disposition risk.",
                    Owner = "operations",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetItemIds = highValueAtRisk
                });
            }

            var stale = allCases
                .Where(c => c.Verdict == LostFoundDispositionVerdict.StaleUnclaimed)
                .Select(c => c.ItemId).OrderBy(i => i).ToList();
            if (stale.Count >= 3)
            {
                pb.Add(new LostFoundDispositionPlaybookAction
                {
                    Id = "RUN_RECONNECT_OUTREACH_CAMPAIGN",
                    Priority = LostFoundDispositionActionPriority.P2,
                    Label = "Run reconnect outreach for stale unclaimed items",
                    Reason = stale.Count + " items sitting past midpoint of retention.",
                    Owner = "marketing",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetItemIds = stale
                });
            }

            if (options.RiskAppetite == LostFoundDispositionAppetite.Cautious
                && (report.Summary.Grade == 'C' || report.Summary.Grade == 'D' || report.Summary.Grade == 'F'))
            {
                pb.Add(new LostFoundDispositionPlaybookAction
                {
                    Id = "REVIEW_DISPOSITION_POLICY",
                    Priority = LostFoundDispositionActionPriority.P2,
                    Label = "Review lost-and-found disposition policy and SLAs",
                    Reason = "Portfolio grade " + report.Summary.Grade
                              + " with cautious risk appetite warrants a policy review.",
                    Owner = "operations",
                    BlastRadius = 3,
                    Reversibility = "medium"
                });
            }

            // Dedupe by Id (defensive).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<LostFoundDispositionPlaybookAction>();
            foreach (var a in pb)
            {
                if (seen.Add(a.Id)) deduped.Add(a);
            }
            pb.Clear();
            pb.AddRange(deduped);

            bool anyP0orP1 = pb.Any(a => a.Priority == LostFoundDispositionActionPriority.P0
                                      || a.Priority == LostFoundDispositionActionPriority.P1);
            if (pb.Count == 0)
            {
                pb.Add(new LostFoundDispositionPlaybookAction
                {
                    Id = "PORTFOLIO_HEALTHY",
                    Priority = LostFoundDispositionActionPriority.P3,
                    Label = "No action - lost-and-found portfolio is healthy",
                    Reason = "No P0/P1/P2 signals across the portfolio.",
                    Owner = "noop",
                    BlastRadius = 0,
                    Reversibility = "high"
                });
            }
            else if (options.RiskAppetite == LostFoundDispositionAppetite.Aggressive && anyP0orP1)
            {
                // Aggressive: trim P3 fallback noise.
                pb.RemoveAll(a => a.Priority == LostFoundDispositionActionPriority.P3);
            }

            // Final order: Priority asc, Id asc.
            var ordered = pb.OrderBy(a => a.Priority)
                .ThenBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            pb.Clear();
            pb.AddRange(ordered);
        }
    }
}
