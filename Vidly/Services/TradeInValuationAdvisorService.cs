using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Vidly.Services
{
    // === Enums ====================================================

    /// <summary>Per-trade-in disposition verdict, ordered by acceptance favorability.</summary>
    public enum TradeInValuationVerdict
    {
        AcceptPremiumCredit = 0,
        AcceptStandardCredit = 1,
        AcceptReducedCredit = 2,
        RouteToManualReview = 3,
        RejectAsRedundant = 4,
        RejectAsObsolete = 5,
        FlagFraudPattern = 6
    }

    public enum TradeInValuationActionPriority { P0, P1, P2, P3 }

    public enum TradeInValuationAppetite { Cautious, Balanced, Aggressive }

    public enum TradeInValuationHeadline
    {
        HealthyIntake = 0,
        BalancedIntake = 1,
        SupplyGlutWarning = 2,
        FraudSignalElevated = 3,
        CriticalIntake = 4
    }

    // === Inputs ===================================================

    /// <summary>
    /// Snapshot of a pending trade-in submission. Plain DTO - no repository or
    /// model dependency, so the service is reusable from any surface (controller,
    /// background job, CLI export).
    /// </summary>
    public class TradeInValuationSnapshot
    {
        public int TradeInId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string MovieTitle { get; set; }

        /// <summary>Format code: "DVD", "BluRay", "UHD4K", "VHS".</summary>
        public string Format { get; set; }

        /// <summary>Condition code: "LikeNew", "Good", "Fair", "Poor".</summary>
        public string Condition { get; set; }

        /// <summary>Existing copies of the same title currently in inventory.</summary>
        public int CopiesOnHand { get; set; }

        /// <summary>0..100. Caller-supplied catalog demand score (rentals/week, waitlist, etc).</summary>
        public int DemandScore { get; set; }

        /// <summary>Total trade-ins by this customer in the trailing 30 days.</summary>
        public int CustomerTradeIns30Days { get; set; }

        /// <summary>Total accepted trade-ins by this customer all-time (tenure proxy).</summary>
        public int CustomerAcceptedLifetime { get; set; }

        /// <summary>Customer lifetime trade-in rejection rate (0..1).</summary>
        public double CustomerRejectionRate { get; set; }

        /// <summary>True if a duplicate (same customer + title + format) was submitted in the trailing 14 days.</summary>
        public bool DuplicateRecentSubmission { get; set; }

        /// <summary>True if the title is on an active "wanted" list (boost).</summary>
        public bool TitleOnWantedList { get; set; }

        /// <summary>Submission time (used for batching insights only).</summary>
        public DateTime SubmittedAt { get; set; }
    }

    // === Outputs ==================================================

    public class TradeInValuationCase
    {
        public int TradeInId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string MovieTitle { get; set; }
        public string Format { get; set; }
        public string Condition { get; set; }
        public TradeInValuationVerdict Verdict { get; set; }
        public TradeInValuationActionPriority Priority { get; set; }

        /// <summary>0..100. Higher = more valuable acquisition.</summary>
        public int ValueScore { get; set; }

        /// <summary>0..100. Higher = more risk this submission is gaming the system.</summary>
        public int FraudRisk { get; set; }

        /// <summary>Recommended store credit to award (0..100).</summary>
        public decimal RecommendedCredits { get; set; }

        /// <summary>Structured reason codes (stable strings, deterministic order).</summary>
        public List<string> Reasons { get; set; } = new List<string>();

        public string RecommendedAction { get; set; }
    }

    public class TradeInValuationPlaybookAction
    {
        public string Id { get; set; }
        public TradeInValuationActionPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; }
        public int BlastRadius { get; set; }
        public string Reversibility { get; set; }
        public List<int> TargetTradeInIds { get; set; } = new List<int>();
    }

    public class TradeInValuationSummary
    {
        public int TotalSubmissions { get; set; }
        public int AcceptedCount { get; set; }
        public int ReducedCount { get; set; }
        public int ManualReviewCount { get; set; }
        public int RejectedCount { get; set; }
        public int FlaggedFraudCount { get; set; }
        public int ObsoleteFormatCount { get; set; }
        public int DuplicateCount { get; set; }
        public int WantedTitleCount { get; set; }
        public decimal TotalRecommendedCredits { get; set; }
        public decimal AvgCreditsPerAccepted { get; set; }
        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public TradeInValuationHeadline HeadlineVerdict { get; set; }
        public List<string> Insights { get; set; } = new List<string>();
    }

    public class TradeInValuationOptions
    {
        public TradeInValuationAppetite RiskAppetite { get; set; } = TradeInValuationAppetite.Balanced;
        public int TopCases { get; set; } = 25;

        /// <summary>Inventory copies above which a title is considered glutted. Default 5.</summary>
        public int SupplyGlutThreshold { get; set; } = 5;

        /// <summary>Customer trade-ins in 30 days above which fraud signals fire. Default 10.</summary>
        public int HighVolumeCustomerThreshold { get; set; } = 10;

        /// <summary>Rejection rate above which the customer is "suspect". Default 0.5.</summary>
        public double SuspectRejectionRate { get; set; } = 0.5;

        /// <summary>Base credit ladder by condition (LikeNew/Good/Fair/Poor).</summary>
        public decimal LikeNewCredit { get; set; } = 5.00m;
        public decimal GoodCredit { get; set; } = 3.50m;
        public decimal FairCredit { get; set; } = 2.00m;
        public decimal PoorCredit { get; set; } = 0.50m;
    }

    public class TradeInValuationReport
    {
        public DateTime GeneratedAt { get; set; }
        public TradeInValuationOptions Options { get; set; } = new TradeInValuationOptions();
        public List<TradeInValuationCase> Cases { get; set; } = new List<TradeInValuationCase>();
        public List<TradeInValuationPlaybookAction> Playbook { get; set; } = new List<TradeInValuationPlaybookAction>();
        public TradeInValuationSummary Summary { get; set; } = new TradeInValuationSummary();

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
            sb.AppendLine("| Total submissions | " + Summary.TotalSubmissions + " |");
            sb.AppendLine("| Accepted | " + Summary.AcceptedCount + " |");
            sb.AppendLine("| Reduced credit | " + Summary.ReducedCount + " |");
            sb.AppendLine("| Manual review | " + Summary.ManualReviewCount + " |");
            sb.AppendLine("| Rejected | " + Summary.RejectedCount + " |");
            sb.AppendLine("| Flagged fraud | " + Summary.FlaggedFraudCount + " |");
            sb.AppendLine("| Obsolete format | " + Summary.ObsoleteFormatCount + " |");
            sb.AppendLine("| Duplicate | " + Summary.DuplicateCount + " |");
            sb.AppendLine("| Wanted title | " + Summary.WantedTitleCount + " |");
            sb.AppendLine("| Total credits | " + Summary.TotalRecommendedCredits.ToString("F2", inv) + " |");
            sb.AppendLine("| Avg credit/accepted | " + Summary.AvgCreditsPerAccepted.ToString("F2", inv) + " |");
            sb.AppendLine("| Score | " + Summary.OverallScore + " (" + Summary.Grade + ") |");
            sb.AppendLine("| Verdict | " + Summary.HeadlineVerdict + " |");
            sb.AppendLine();

            sb.AppendLine(h2 + "Top cases");
            sb.AppendLine();
            sb.AppendLine("| Id | Customer | Title | Format | Cond | Verdict | Value | Fraud | Credit | Next |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var c in Cases)
            {
                sb.AppendLine("| " + c.TradeInId
                    + " | " + (c.CustomerName ?? "")
                    + " | " + (c.MovieTitle ?? "")
                    + " | " + (c.Format ?? "")
                    + " | " + (c.Condition ?? "")
                    + " | " + c.Verdict
                    + " | " + c.ValueScore
                    + " | " + c.FraudRisk
                    + " | " + c.RecommendedCredits.ToString("F2", inv)
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
                    + string.Join(",", a.TargetTradeInIds) + " |");
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
    /// Agentic trade-in valuation advisor - 10th Vidly agentic sibling joining
    /// ReviewIntelligenceService, DamageRiskForecastService, LateReturnEscalationService,
    /// RefundFraudTriageService, WaitlistConversionAdvisorService,
    /// PenaltyWaiverGovernanceAdvisorService, GiftCardBreakageAdvisorService,
    /// LostAndFoundDispositionAdvisorService, and SubscriptionDunningAdvisorService.
    ///
    /// Triages pending trade-in submissions across the portfolio: classifies each into
    /// an acceptance verdict, scores condition-adjusted value and per-customer fraud
    /// risk, recommends a credit amount, and emits a P0-first deduped playbook plus
    /// fleet insights and an A-F intake grade. Pure analytic - never grants the
    /// credit, never sends notifications, never mutates the input snapshots.
    /// </summary>
    public class TradeInValuationAdvisorService
    {
        private readonly Func<DateTime> _clock;

        public TradeInValuationAdvisorService() : this(null) { }

        public TradeInValuationAdvisorService(Func<DateTime> clock)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public TradeInValuationReport GenerateReport(
            IEnumerable<TradeInValuationSnapshot> snapshots,
            TradeInValuationOptions options = null)
        {
            var opts = options ?? new TradeInValuationOptions();
            var report = new TradeInValuationReport
            {
                GeneratedAt = _clock(),
                Options = opts
            };

            var input = (snapshots ?? Enumerable.Empty<TradeInValuationSnapshot>())
                .Where(s => s != null)
                .ToList();

            if (input.Count == 0)
            {
                report.Summary.Grade = 'A';
                report.Summary.OverallScore = 100;
                report.Summary.HeadlineVerdict = TradeInValuationHeadline.HealthyIntake;
                report.Summary.Insights.Add("EMPTY_INTAKE");
                report.Playbook.Add(new TradeInValuationPlaybookAction
                {
                    Id = "INTAKE_HEALTHY",
                    Priority = TradeInValuationActionPriority.P3,
                    Label = "Maintain intake monitoring",
                    Owner = "store_manager",
                    BlastRadius = 1,
                    Reversibility = "high",
                    Reason = "No pending trade-ins."
                });
                return report;
            }

            foreach (var snap in input)
            {
                report.Cases.Add(EvaluateOne(snap, opts));
            }

            // Deterministic ordering: priority asc, fraud desc, value desc, id asc.
            report.Cases = report.Cases
                .OrderBy(c => (int)c.Priority)
                .ThenByDescending(c => c.FraudRisk)
                .ThenByDescending(c => c.ValueScore)
                .ThenBy(c => c.TradeInId)
                .ToList();

            BuildSummary(report, input, opts);
            BuildPlaybook(report, input, opts);

            // Cap to TopCases (after playbook so insights reflect the full set).
            if (report.Cases.Count > opts.TopCases)
                report.Cases = report.Cases.Take(opts.TopCases).ToList();

            return report;
        }

        // -- Per-snapshot evaluation -----------------------------------

        private TradeInValuationCase EvaluateOne(
            TradeInValuationSnapshot s,
            TradeInValuationOptions opts)
        {
            var c = new TradeInValuationCase
            {
                TradeInId = s.TradeInId,
                CustomerId = s.CustomerId,
                CustomerName = s.CustomerName,
                MovieTitle = s.MovieTitle,
                Format = s.Format,
                Condition = s.Condition
            };

            // -- Value score (0..100) ----------------------------------

            int value = 50;
            double appetiteMul = AppetiteMultiplier(opts.RiskAppetite);

            // Format weighting.
            switch ((s.Format ?? "").Trim())
            {
                case "UHD4K": value += 18; c.Reasons.Add("FORMAT_HIGH_VALUE"); break;
                case "BluRay": value += 10; c.Reasons.Add("FORMAT_STANDARD"); break;
                case "DVD": value += 2; break;
                case "VHS": value -= 35; c.Reasons.Add("FORMAT_OBSOLETE"); break;
                default: value -= 5; c.Reasons.Add("FORMAT_UNKNOWN"); break;
            }

            // Condition.
            switch ((s.Condition ?? "").Trim())
            {
                case "LikeNew": value += 20; break;
                case "Good": value += 10; break;
                case "Fair": value -= 5; c.Reasons.Add("CONDITION_FAIR"); break;
                case "Poor": value -= 25; c.Reasons.Add("CONDITION_POOR"); break;
                default: value -= 10; c.Reasons.Add("CONDITION_UNKNOWN"); break;
            }

            // Demand and wanted-list.
            int demandClamped = Math.Max(0, Math.Min(100, s.DemandScore));
            value += (demandClamped - 50) / 4; // +/- ~12
            if (demandClamped >= 80) c.Reasons.Add("HIGH_DEMAND");
            if (s.TitleOnWantedList) { value += 12; c.Reasons.Add("WANTED_TITLE"); }

            // Supply glut penalty.
            if (s.CopiesOnHand >= opts.SupplyGlutThreshold)
            {
                int over = s.CopiesOnHand - opts.SupplyGlutThreshold + 1;
                value -= Math.Min(35, 5 * over);
                c.Reasons.Add("SUPPLY_GLUT");
            }
            else if (s.CopiesOnHand == 0)
            {
                value += 8;
                c.Reasons.Add("CATALOG_GAP");
            }

            value = Clamp(value, 0, 100);
            c.ValueScore = value;

            // -- Fraud risk (0..100) -----------------------------------

            int fraud = 0;
            if (s.DuplicateRecentSubmission) { fraud += 45; c.Reasons.Add("DUPLICATE_SUBMISSION"); }
            if (s.CustomerTradeIns30Days >= opts.HighVolumeCustomerThreshold)
            {
                fraud += 30;
                c.Reasons.Add("HIGH_VOLUME_CUSTOMER");
            }
            else if (s.CustomerTradeIns30Days >= opts.HighVolumeCustomerThreshold / 2)
            {
                fraud += 12;
            }
            if (s.CustomerRejectionRate >= opts.SuspectRejectionRate)
            {
                fraud += 25;
                c.Reasons.Add("HIGH_REJECTION_HISTORY");
            }
            // Mismatch: very high condition claim from suspect customer.
            if ((s.Condition == "LikeNew" || s.Condition == "Good")
                && s.CustomerRejectionRate >= opts.SuspectRejectionRate * 0.7)
            {
                fraud += 8;
                c.Reasons.Add("CLAIM_QUALITY_MISMATCH");
            }
            // New customer with high-value claim is mildly suspicious.
            if (s.CustomerAcceptedLifetime == 0 && s.Format == "UHD4K" && s.Condition == "LikeNew")
            {
                fraud += 8;
                c.Reasons.Add("NEW_CUSTOMER_PREMIUM_CLAIM");
            }

            // Trust dampener for established customers.
            if (s.CustomerAcceptedLifetime >= 20 && s.CustomerRejectionRate < 0.15)
            {
                fraud = Math.Max(0, fraud - 15);
                c.Reasons.Add("TRUSTED_CUSTOMER");
            }

            // Appetite shapes fraud sensitivity.
            fraud = Clamp((int)Math.Round(fraud * appetiteMul), 0, 100);
            c.FraudRisk = fraud;

            // -- Verdict ladder ----------------------------------------

            TradeInValuationVerdict verdict;
            if (fraud >= 70)
            {
                verdict = TradeInValuationVerdict.FlagFraudPattern;
            }
            else if (s.Format == "VHS" && (s.Condition == "Poor" || s.Condition == "Fair"))
            {
                verdict = TradeInValuationVerdict.RejectAsObsolete;
            }
            else if (s.CopiesOnHand >= opts.SupplyGlutThreshold + 3 && demandClamped < 30 && !s.TitleOnWantedList)
            {
                verdict = TradeInValuationVerdict.RejectAsRedundant;
            }
            else if (fraud >= 40 || c.Reasons.Contains("CLAIM_QUALITY_MISMATCH"))
            {
                verdict = TradeInValuationVerdict.RouteToManualReview;
            }
            else if (value >= 75)
            {
                verdict = TradeInValuationVerdict.AcceptPremiumCredit;
            }
            else if (value >= 45)
            {
                verdict = TradeInValuationVerdict.AcceptStandardCredit;
            }
            else
            {
                verdict = TradeInValuationVerdict.AcceptReducedCredit;
            }
            c.Verdict = verdict;

            // -- Credit recommendation ---------------------------------

            decimal credit = BaseCredit(s.Condition, opts);
            // Format multiplier.
            decimal fmtMul = FormatMultiplier(s.Format);
            credit = Math.Round(credit * fmtMul, 2);
            // Demand bonus.
            if (s.TitleOnWantedList) credit += 1.50m;
            if (demandClamped >= 80) credit += 0.75m;
            // Glut penalty.
            if (s.CopiesOnHand >= opts.SupplyGlutThreshold)
                credit = Math.Max(0.25m, credit * 0.5m);

            // Reduce per verdict.
            switch (verdict)
            {
                case TradeInValuationVerdict.AcceptPremiumCredit: credit = Math.Round(credit * 1.10m, 2); break;
                case TradeInValuationVerdict.AcceptStandardCredit: break;
                case TradeInValuationVerdict.AcceptReducedCredit: credit = Math.Round(credit * 0.65m, 2); break;
                case TradeInValuationVerdict.RouteToManualReview: credit = Math.Round(credit * 0.50m, 2); break;
                default: credit = 0m; break;
            }
            if (credit > 100m) credit = 100m;
            c.RecommendedCredits = credit;

            // -- Priority + recommended action -------------------------

            c.Priority = MapPriority(verdict, fraud, opts.RiskAppetite);
            c.RecommendedAction = RecommendedActionText(verdict);

            // Stable reason ordering.
            c.Reasons = c.Reasons.Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();
            return c;
        }

        private static decimal BaseCredit(string condition, TradeInValuationOptions opts)
        {
            switch ((condition ?? "").Trim())
            {
                case "LikeNew": return opts.LikeNewCredit;
                case "Good": return opts.GoodCredit;
                case "Fair": return opts.FairCredit;
                case "Poor": return opts.PoorCredit;
                default: return opts.PoorCredit;
            }
        }

        private static decimal FormatMultiplier(string fmt)
        {
            switch ((fmt ?? "").Trim())
            {
                case "UHD4K": return 1.50m;
                case "BluRay": return 1.20m;
                case "DVD": return 1.00m;
                case "VHS": return 0.40m;
                default: return 0.75m;
            }
        }

        private static double AppetiteMultiplier(TradeInValuationAppetite a)
        {
            switch (a)
            {
                case TradeInValuationAppetite.Cautious: return 1.15;
                case TradeInValuationAppetite.Aggressive: return 0.85;
                default: return 1.0;
            }
        }

        private static TradeInValuationActionPriority MapPriority(
            TradeInValuationVerdict v,
            int fraud,
            TradeInValuationAppetite appetite)
        {
            // Fraud always P0.
            if (v == TradeInValuationVerdict.FlagFraudPattern) return TradeInValuationActionPriority.P0;
            if (v == TradeInValuationVerdict.RouteToManualReview) return TradeInValuationActionPriority.P1;
            if (v == TradeInValuationVerdict.RejectAsRedundant
                || v == TradeInValuationVerdict.RejectAsObsolete)
                return TradeInValuationActionPriority.P2;
            if (v == TradeInValuationVerdict.AcceptPremiumCredit) return TradeInValuationActionPriority.P1;
            if (v == TradeInValuationVerdict.AcceptStandardCredit) return TradeInValuationActionPriority.P2;
            return TradeInValuationActionPriority.P3;
        }

        private static string RecommendedActionText(TradeInValuationVerdict v)
        {
            switch (v)
            {
                case TradeInValuationVerdict.AcceptPremiumCredit: return "Accept; award premium credit and shelve for catalog.";
                case TradeInValuationVerdict.AcceptStandardCredit: return "Accept at standard credit ladder.";
                case TradeInValuationVerdict.AcceptReducedCredit: return "Accept at reduced credit; flag low condition to customer.";
                case TradeInValuationVerdict.RouteToManualReview: return "Hold pending manager inspection.";
                case TradeInValuationVerdict.RejectAsRedundant: return "Reject politely; offer alternate-title trade.";
                case TradeInValuationVerdict.RejectAsObsolete: return "Reject; suggest donation/recycle channel.";
                case TradeInValuationVerdict.FlagFraudPattern: return "Freeze trade-in cycle; escalate to fraud team.";
                default: return "Review.";
            }
        }

        // -- Summary ---------------------------------------------------

        private void BuildSummary(
            TradeInValuationReport report,
            List<TradeInValuationSnapshot> input,
            TradeInValuationOptions opts)
        {
            var s = report.Summary;
            s.TotalSubmissions = input.Count;

            foreach (var c in report.Cases)
            {
                switch (c.Verdict)
                {
                    case TradeInValuationVerdict.AcceptPremiumCredit:
                    case TradeInValuationVerdict.AcceptStandardCredit:
                        s.AcceptedCount++;
                        break;
                    case TradeInValuationVerdict.AcceptReducedCredit:
                        s.ReducedCount++;
                        s.AcceptedCount++;
                        break;
                    case TradeInValuationVerdict.RouteToManualReview:
                        s.ManualReviewCount++;
                        break;
                    case TradeInValuationVerdict.RejectAsRedundant:
                    case TradeInValuationVerdict.RejectAsObsolete:
                        s.RejectedCount++;
                        if (c.Verdict == TradeInValuationVerdict.RejectAsObsolete)
                            s.ObsoleteFormatCount++;
                        break;
                    case TradeInValuationVerdict.FlagFraudPattern:
                        s.FlaggedFraudCount++;
                        break;
                }
                s.TotalRecommendedCredits += c.RecommendedCredits;
            }

            s.DuplicateCount = input.Count(i => i.DuplicateRecentSubmission);
            s.WantedTitleCount = input.Count(i => i.TitleOnWantedList);

            if (s.AcceptedCount > 0)
            {
                s.AvgCreditsPerAccepted = Math.Round(
                    s.TotalRecommendedCredits / s.AcceptedCount, 2);
            }

            // Overall intake score: 100 - 25*fraud% - 15*rejected% - 8*manual% +5*accepted%.
            double n = Math.Max(1, s.TotalSubmissions);
            double scoreF = 100.0
                - 25.0 * (s.FlaggedFraudCount / n) * 100.0 / 100.0
                - 15.0 * (s.RejectedCount / n) * 100.0 / 100.0
                - 8.0 * (s.ManualReviewCount / n) * 100.0 / 100.0
                + 5.0 * (s.AcceptedCount / n);
            // Compress to 0..100 with sensible scaling.
            scoreF = 100.0
                - (25.0 * s.FlaggedFraudCount + 12.0 * s.RejectedCount + 6.0 * s.ManualReviewCount) / n
                + (4.0 * s.AcceptedCount) / n;
            s.OverallScore = Clamp((int)Math.Round(scoreF), 0, 100);
            s.Grade = LetterGrade(s.OverallScore);

            // Headline verdict.
            double fraudPct = s.FlaggedFraudCount / n;
            double glutPct = report.Cases.Count(c => c.Reasons.Contains("SUPPLY_GLUT")) / n;
            if (fraudPct >= 0.10)
                s.HeadlineVerdict = TradeInValuationHeadline.FraudSignalElevated;
            else if (s.OverallScore < 40)
                s.HeadlineVerdict = TradeInValuationHeadline.CriticalIntake;
            else if (glutPct >= 0.40)
                s.HeadlineVerdict = TradeInValuationHeadline.SupplyGlutWarning;
            else if (s.OverallScore < 75)
                s.HeadlineVerdict = TradeInValuationHeadline.BalancedIntake;
            else
                s.HeadlineVerdict = TradeInValuationHeadline.HealthyIntake;

            // Insights (always non-empty).
            if (s.FlaggedFraudCount >= 1)
                s.Insights.Add("FRAUD_SIGNALS_PRESENT:" + s.FlaggedFraudCount);
            if (s.DuplicateCount >= 2)
                s.Insights.Add("DUPLICATE_CLUSTER:" + s.DuplicateCount);
            if (glutPct >= 0.40)
                s.Insights.Add("CATALOG_GLUT_DOMINANT");
            if (s.WantedTitleCount >= 1)
                s.Insights.Add("WANTED_TITLES_INCOMING:" + s.WantedTitleCount);
            if (s.ObsoleteFormatCount >= 2)
                s.Insights.Add("OBSOLETE_FORMAT_TREND");
            if (s.AcceptedCount == s.TotalSubmissions && s.TotalSubmissions > 0)
                s.Insights.Add("ALL_ACCEPTED");
            if (s.Insights.Count == 0)
                s.Insights.Add("INTAKE_NORMAL");
        }

        // -- Playbook --------------------------------------------------

        private void BuildPlaybook(
            TradeInValuationReport report,
            List<TradeInValuationSnapshot> input,
            TradeInValuationOptions opts)
        {
            var p = new List<TradeInValuationPlaybookAction>();
            var byId = new HashSet<string>(StringComparer.Ordinal);

            void Add(TradeInValuationPlaybookAction a)
            {
                if (a == null || byId.Contains(a.Id)) return;
                byId.Add(a.Id);
                p.Add(a);
            }

            var fraudCases = report.Cases
                .Where(c => c.Verdict == TradeInValuationVerdict.FlagFraudPattern)
                .ToList();
            if (fraudCases.Count > 0)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "FREEZE_FRAUD_CYCLE",
                    Priority = TradeInValuationActionPriority.P0,
                    Label = "Freeze trade-in cycle for flagged customers",
                    Reason = "One or more submissions hit fraud-risk threshold.",
                    Owner = "fraud_team",
                    BlastRadius = 4,
                    Reversibility = "medium",
                    TargetTradeInIds = fraudCases.Select(c => c.TradeInId).ToList()
                });
                if (fraudCases.Count >= 2)
                {
                    Add(new TradeInValuationPlaybookAction
                    {
                        Id = "OPEN_FRAUD_REVIEW_BATCH",
                        Priority = TradeInValuationActionPriority.P0,
                        Label = "Open batch fraud review across flagged trade-ins",
                        Reason = ">=2 fraud-flagged submissions in this intake.",
                        Owner = "fraud_team",
                        BlastRadius = 5,
                        Reversibility = "medium",
                        TargetTradeInIds = fraudCases.Select(c => c.TradeInId).ToList()
                    });
                }
            }

            var manual = report.Cases
                .Where(c => c.Verdict == TradeInValuationVerdict.RouteToManualReview)
                .ToList();
            if (manual.Count > 0)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "ASSIGN_MANAGER_REVIEW",
                    Priority = TradeInValuationActionPriority.P1,
                    Label = "Assign manager inspection slot for manual review queue",
                    Reason = manual.Count + " submission(s) flagged for manual review.",
                    Owner = "store_manager",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetTradeInIds = manual.Select(c => c.TradeInId).ToList()
                });
            }

            var glut = report.Cases
                .Where(c => c.Reasons.Contains("SUPPLY_GLUT"))
                .ToList();
            if (glut.Count >= 2)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "REBALANCE_CATALOG_GLUT",
                    Priority = TradeInValuationActionPriority.P2,
                    Label = "Rebalance over-supplied catalog (consider trade-out / clearance)",
                    Reason = glut.Count + " glutted titles in current intake.",
                    Owner = "inventory_manager",
                    BlastRadius = 3,
                    Reversibility = "medium",
                    TargetTradeInIds = glut.Select(c => c.TradeInId).ToList()
                });
            }

            var gaps = report.Cases
                .Where(c => c.Reasons.Contains("CATALOG_GAP"))
                .ToList();
            if (gaps.Count >= 1)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "SHELVE_CATALOG_GAP_FILLERS",
                    Priority = TradeInValuationActionPriority.P2,
                    Label = "Fast-track shelving for trade-ins that fill catalog gaps",
                    Reason = gaps.Count + " trade-in(s) fill an empty catalog slot.",
                    Owner = "inventory_manager",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetTradeInIds = gaps.Select(c => c.TradeInId).ToList()
                });
            }

            var wanted = report.Cases
                .Where(c => c.Reasons.Contains("WANTED_TITLE"))
                .ToList();
            if (wanted.Count >= 1)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "NOTIFY_WAITLIST_WANTED_TITLES",
                    Priority = TradeInValuationActionPriority.P1,
                    Label = "Notify waitlist that wanted titles are arriving",
                    Reason = wanted.Count + " incoming trade-in(s) match the wanted list.",
                    Owner = "marketing",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetTradeInIds = wanted.Select(c => c.TradeInId).ToList()
                });
            }

            var obsolete = report.Cases
                .Where(c => c.Verdict == TradeInValuationVerdict.RejectAsObsolete)
                .ToList();
            if (obsolete.Count >= 2)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "PUBLISH_OBSOLETE_FORMAT_NOTICE",
                    Priority = TradeInValuationActionPriority.P2,
                    Label = "Update trade-in policy to publish obsolete-format list",
                    Reason = "Obsolete format submissions accumulating; reduce intake friction.",
                    Owner = "store_manager",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetTradeInIds = obsolete.Select(c => c.TradeInId).ToList()
                });
            }

            var dupCustomers = input
                .Where(i => i.DuplicateRecentSubmission)
                .Select(i => i.CustomerId)
                .Distinct()
                .ToList();
            if (dupCustomers.Count >= 1)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "RATE_LIMIT_DUPLICATE_SUBMITTERS",
                    Priority = TradeInValuationActionPriority.P1,
                    Label = "Apply per-customer trade-in rate limit for duplicate submitters",
                    Reason = dupCustomers.Count + " customer(s) submitted duplicates within 14d.",
                    Owner = "fraud_team",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetTradeInIds = input
                        .Where(i => i.DuplicateRecentSubmission)
                        .Select(i => i.TradeInId)
                        .ToList()
                });
            }

            // Cautious + grade C/D/F -> schedule audit.
            if (opts.RiskAppetite == TradeInValuationAppetite.Cautious
                && (report.Summary.Grade == 'C' || report.Summary.Grade == 'D' || report.Summary.Grade == 'F'))
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "SCHEDULE_INTAKE_AUDIT",
                    Priority = TradeInValuationActionPriority.P2,
                    Label = "Schedule trade-in policy audit",
                    Reason = "Cautious appetite + intake grade " + report.Summary.Grade + ".",
                    Owner = "store_manager",
                    BlastRadius = 2,
                    Reversibility = "high"
                });
            }

            // Default fallback when nothing else fires.
            if (p.Count == 0)
            {
                Add(new TradeInValuationPlaybookAction
                {
                    Id = "INTAKE_HEALTHY",
                    Priority = TradeInValuationActionPriority.P3,
                    Label = "Maintain intake monitoring",
                    Reason = "All submissions clean.",
                    Owner = "store_manager",
                    BlastRadius = 1,
                    Reversibility = "high"
                });
            }

            // Aggressive trims lone P3 if any P0/P1 present.
            if (opts.RiskAppetite == TradeInValuationAppetite.Aggressive
                && p.Any(a => a.Priority == TradeInValuationActionPriority.P0
                           || a.Priority == TradeInValuationActionPriority.P1))
            {
                p = p.Where(a => a.Priority != TradeInValuationActionPriority.P3).ToList();
            }

            // P0-first stable order.
            report.Playbook = p
                .OrderBy(a => (int)a.Priority)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
                .Take(opts.TopCases)
                .ToList();
        }

        // -- Helpers ---------------------------------------------------

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static char LetterGrade(int score)
        {
            if (score >= 85) return 'A';
            if (score >= 70) return 'B';
            if (score >= 55) return 'C';
            if (score >= 40) return 'D';
            return 'F';
        }
    }
}
