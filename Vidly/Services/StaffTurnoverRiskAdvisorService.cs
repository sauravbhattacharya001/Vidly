using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Agentic staff turnover risk advisor — proactively identifies employees at risk
    /// of voluntary departure based on performance trajectory, tenure milestones,
    /// workload imbalance, engagement decay, and schedule irregularities.
    /// Emits P0-P3 retention playbook with concrete interventions.
    /// </summary>
    public class StaffTurnoverRiskAdvisorService
    {
        private readonly IClock _clock;

        public enum RiskAppetite { Cautious, Balanced, Aggressive }

        public StaffTurnoverRiskAdvisorService(IClock clock = null)
        {
            _clock = clock ?? new SystemClock();
        }

        // ══════════════════════════════════════════════════════
        //  Input Models
        // ══════════════════════════════════════════════════════

        public class StaffSnapshot
        {
            public int StaffId { get; set; }
            public string Name { get; set; }
            public string Role { get; set; }
            public DateTime HireDate { get; set; }
            public bool IsActive { get; set; } = true;

            // Performance signals (recent period vs prior period)
            public double RecentPerformanceScore { get; set; }  // 0-100
            public double PriorPerformanceScore { get; set; }   // 0-100

            // Engagement signals
            public int RecentTransactionCount { get; set; }
            public int PriorTransactionCount { get; set; }
            public double RecentSatisfactionAvg { get; set; }   // 0-5
            public double PriorSatisfactionAvg { get; set; }    // 0-5

            // Schedule signals
            public int LateArrivalsLast30Days { get; set; }
            public int AbsencesLast30Days { get; set; }
            public int ShiftSwapRequestsLast30Days { get; set; }

            // Compensation / recognition
            public int MonthsSinceLastRaise { get; set; }
            public int MonthsSinceLastPromotion { get; set; }
            public bool HasReceivedRecognitionLast90Days { get; set; }

            // Workload
            public double WeeklyHoursAvg { get; set; }
            public double TeamWeeklyHoursAvg { get; set; }

            // External signals
            public bool HasUpdatedResumeOnline { get; set; }
            public bool HasDeclinedExtraShifts { get; set; }
        }

        // ══════════════════════════════════════════════════════
        //  Output Models
        // ══════════════════════════════════════════════════════

        public enum TurnoverVerdict
        {
            FlightRiskImminent,   // P0 — likely to leave within 30 days
            FlightRiskElevated,   // P1 — at risk within 90 days
            Disengaging,          // P2 — early warning signs
            Stable,               // P3 — low risk
            Thriving,             // P3 — actively growing, low risk
            InsufficientData      // P3 — not enough signals
        }

        public class StaffRiskAssessment
        {
            public int StaffId { get; set; }
            public string Name { get; set; }
            public string Role { get; set; }
            public TurnoverVerdict Verdict { get; set; }
            public int Priority { get; set; }  // 0-3
            public double RiskScore { get; set; }  // 0-100
            public double TenureMonths { get; set; }
            public List<string> Reasons { get; set; } = new List<string>();
            public List<string> RetentionActions { get; set; } = new List<string>();
        }

        public class PlaybookAction
        {
            public string Id { get; set; }
            public int Priority { get; set; }
            public string Label { get; set; }
            public string Reason { get; set; }
            public string Owner { get; set; }
            public int BlastRadius { get; set; }
            public string Reversibility { get; set; }
            public List<int> StaffIds { get; set; } = new List<int>();
        }

        public class TurnoverRiskReport
        {
            public DateTime GeneratedAt { get; set; }
            public RiskAppetite Appetite { get; set; }
            public string Grade { get; set; }  // A-F
            public double PortfolioRiskScore { get; set; }
            public string Headline { get; set; }
            public int TotalStaff { get; set; }
            public int P0Count { get; set; }
            public int P1Count { get; set; }
            public int P2Count { get; set; }
            public int P3Count { get; set; }
            public List<StaffRiskAssessment> Assessments { get; set; } = new List<StaffRiskAssessment>();
            public List<PlaybookAction> Playbook { get; set; } = new List<PlaybookAction>();
            public List<string> Insights { get; set; } = new List<string>();
        }

        // ══════════════════════════════════════════════════════
        //  Analysis
        // ══════════════════════════════════════════════════════

        public TurnoverRiskReport Analyze(
            IEnumerable<StaffSnapshot> staff,
            RiskAppetite appetite = RiskAppetite.Balanced)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            var staffList = staff.Where(s => s.IsActive).ToList();

            var assessments = staffList.Select(s => Assess(s, appetite)).ToList();

            // Sort by priority asc, risk desc, name asc
            assessments = assessments
                .OrderBy(a => a.Priority)
                .ThenByDescending(a => a.RiskScore)
                .ThenBy(a => a.Name)
                .ToList();

            var p0 = assessments.Count(a => a.Priority == 0);
            var p1 = assessments.Count(a => a.Priority == 1);
            var p2 = assessments.Count(a => a.Priority == 2);
            var p3 = assessments.Count(a => a.Priority == 3);

            // Portfolio risk = weighted mean of top-3 risk scores
            var topScores = assessments.OrderByDescending(a => a.RiskScore).Take(3).ToList();
            var portfolioRisk = topScores.Any()
                ? topScores[0].RiskScore * 0.5 +
                  (topScores.Count > 1 ? topScores[1].RiskScore * 0.3 : 0) +
                  (topScores.Count > 2 ? topScores[2].RiskScore * 0.2 : 0)
                : 0;

            var grade = ComputeGrade(p0, p1, portfolioRisk, staffList.Count);
            var playbook = BuildPlaybook(assessments, appetite, grade);
            var insights = BuildInsights(assessments, staffList.Count);

            return new TurnoverRiskReport
            {
                GeneratedAt = _clock.Now,
                Appetite = appetite,
                Grade = grade,
                PortfolioRiskScore = Math.Round(portfolioRisk, 1),
                Headline = $"VERDICT: grade={grade} staff={staffList.Count} P0={p0} P1={p1} portfolio_risk={portfolioRisk:F1}",
                TotalStaff = staffList.Count,
                P0Count = p0,
                P1Count = p1,
                P2Count = p2,
                P3Count = p3,
                Assessments = assessments,
                Playbook = playbook,
                Insights = insights
            };
        }

        // ══════════════════════════════════════════════════════
        //  Per-Staff Scoring
        // ══════════════════════════════════════════════════════

        private StaffRiskAssessment Assess(StaffSnapshot s, RiskAppetite appetite)
        {
            var now = _clock.Now;
            var tenureMonths = (now - s.HireDate).TotalDays / 30.44;
            var signals = new List<(string reason, double severity)>();

            // 1. Performance decline
            if (s.PriorPerformanceScore > 0)
            {
                var decline = s.PriorPerformanceScore - s.RecentPerformanceScore;
                if (decline >= 25) signals.Add(("SHARP_PERFORMANCE_DECLINE", 55));
                else if (decline >= 15) signals.Add(("MODERATE_PERFORMANCE_DECLINE", 35));
                else if (decline >= 8) signals.Add(("SLIGHT_PERFORMANCE_DECLINE", 15));
            }

            // 2. Engagement decay (transaction volume drop)
            if (s.PriorTransactionCount > 5)
            {
                var dropRatio = 1.0 - (double)s.RecentTransactionCount / s.PriorTransactionCount;
                if (dropRatio >= 0.50) signals.Add(("SEVERE_ENGAGEMENT_DROP", 50));
                else if (dropRatio >= 0.30) signals.Add(("MODERATE_ENGAGEMENT_DROP", 30));
                else if (dropRatio >= 0.15) signals.Add(("MILD_ENGAGEMENT_DROP", 12));
            }

            // 3. Satisfaction decline
            if (s.PriorSatisfactionAvg >= 3.0)
            {
                var satDrop = s.PriorSatisfactionAvg - s.RecentSatisfactionAvg;
                if (satDrop >= 1.5) signals.Add(("SATISFACTION_COLLAPSE", 45));
                else if (satDrop >= 0.8) signals.Add(("SATISFACTION_DECLINE", 25));
            }

            // 4. Schedule irregularities
            if (s.LateArrivalsLast30Days >= 8) signals.Add(("CHRONIC_TARDINESS", 40));
            else if (s.LateArrivalsLast30Days >= 4) signals.Add(("RISING_TARDINESS", 20));

            if (s.AbsencesLast30Days >= 5) signals.Add(("EXCESSIVE_ABSENCES", 45));
            else if (s.AbsencesLast30Days >= 3) signals.Add(("RISING_ABSENCES", 22));

            if (s.ShiftSwapRequestsLast30Days >= 4) signals.Add(("FREQUENT_SHIFT_SWAPS", 25));

            // 5. Compensation stagnation
            if (s.MonthsSinceLastRaise >= 24 && tenureMonths >= 24)
                signals.Add(("COMPENSATION_STAGNATION", 35));
            else if (s.MonthsSinceLastRaise >= 18 && tenureMonths >= 18)
                signals.Add(("RAISE_OVERDUE", 18));

            if (s.MonthsSinceLastPromotion >= 36 && tenureMonths >= 36)
                signals.Add(("PROMOTION_STAGNATION", 30));

            // 6. Recognition gap
            if (!s.HasReceivedRecognitionLast90Days && s.RecentPerformanceScore >= 70)
                signals.Add(("HIGH_PERFORMER_UNRECOGNIZED", 20));

            // 7. Workload imbalance
            if (s.TeamWeeklyHoursAvg > 0)
            {
                var overworkRatio = s.WeeklyHoursAvg / s.TeamWeeklyHoursAvg;
                if (overworkRatio >= 1.5) signals.Add(("SEVERE_OVERWORK", 40));
                else if (overworkRatio >= 1.25) signals.Add(("MODERATE_OVERWORK", 22));
            }

            // 8. External / behavioral signals
            if (s.HasUpdatedResumeOnline) signals.Add(("RESUME_UPDATED", 60));
            if (s.HasDeclinedExtraShifts) signals.Add(("DECLINING_EXTRA_SHIFTS", 18));

            // 9. Tenure risk milestones (18-month and 3-year itch)
            if (tenureMonths >= 16 && tenureMonths <= 22)
                signals.Add(("EIGHTEEN_MONTH_TENURE_WINDOW", 12));
            else if (tenureMonths >= 34 && tenureMonths <= 40)
                signals.Add(("THREE_YEAR_TENURE_WINDOW", 10));

            // ── Compute risk score ──
            double riskScore = 0;
            if (signals.Any())
            {
                var sorted = signals.OrderByDescending(x => x.severity).ToList();
                riskScore = sorted[0].severity +
                    0.4 * Math.Min(sorted.Skip(1).Sum(x => x.severity), 60);
            }

            // Apply appetite multiplier
            double mult = appetite == RiskAppetite.Cautious ? 1.15
                        : appetite == RiskAppetite.Aggressive ? 0.85
                        : 1.0;
            riskScore = Math.Min(100, Math.Max(0, riskScore * mult));

            // ── Determine verdict ──
            TurnoverVerdict verdict;
            int priority;
            if (signals.Count == 0 && tenureMonths < 3)
            {
                verdict = TurnoverVerdict.InsufficientData;
                priority = 3;
            }
            else if (riskScore >= 75 || (s.HasUpdatedResumeOnline && riskScore >= 55))
            {
                verdict = TurnoverVerdict.FlightRiskImminent;
                priority = 0;
            }
            else if (riskScore >= 55)
            {
                verdict = TurnoverVerdict.FlightRiskElevated;
                priority = 1;
            }
            else if (riskScore >= 35)
            {
                verdict = TurnoverVerdict.Disengaging;
                priority = 2;
            }
            else if (s.RecentPerformanceScore >= 80 && signals.Count <= 1)
            {
                verdict = TurnoverVerdict.Thriving;
                priority = 3;
            }
            else
            {
                verdict = TurnoverVerdict.Stable;
                priority = 3;
            }

            // ── Build per-person retention actions ──
            var actions = new List<string>();
            if (verdict == TurnoverVerdict.FlightRiskImminent)
            {
                actions.Add("Schedule urgent 1-on-1 with manager within 48h");
                if (signals.Any(x => x.reason.Contains("COMPENSATION") || x.reason.Contains("RAISE")))
                    actions.Add("Prepare retention offer (compensation review)");
                if (signals.Any(x => x.reason.Contains("OVERWORK")))
                    actions.Add("Redistribute workload immediately");
            }
            else if (verdict == TurnoverVerdict.FlightRiskElevated)
            {
                actions.Add("Schedule career development conversation within 2 weeks");
                if (signals.Any(x => x.reason.Contains("PROMOTION")))
                    actions.Add("Discuss growth path and promotion timeline");
            }
            else if (verdict == TurnoverVerdict.Disengaging)
            {
                actions.Add("Increase recognition frequency");
                actions.Add("Check in on job satisfaction");
            }

            return new StaffRiskAssessment
            {
                StaffId = s.StaffId,
                Name = s.Name,
                Role = s.Role,
                Verdict = verdict,
                Priority = priority,
                RiskScore = Math.Round(riskScore, 1),
                TenureMonths = Math.Round(tenureMonths, 1),
                Reasons = signals.OrderByDescending(x => x.severity).Select(x => x.reason).ToList(),
                RetentionActions = actions
            };
        }

        // ══════════════════════════════════════════════════════
        //  Grade
        // ══════════════════════════════════════════════════════

        private string ComputeGrade(int p0, int p1, double portfolioRisk, int total)
        {
            if (total == 0) return "A";
            if (p0 >= 3 || portfolioRisk >= 75) return "F";
            if (p0 >= 1 || portfolioRisk >= 55) return "D";
            if (portfolioRisk >= 35 || p1 >= 3) return "C";
            if (portfolioRisk >= 18 || p1 >= 1) return "B";
            return "A";
        }

        // ══════════════════════════════════════════════════════
        //  Playbook
        // ══════════════════════════════════════════════════════

        private List<PlaybookAction> BuildPlaybook(
            List<StaffRiskAssessment> assessments, RiskAppetite appetite, string grade)
        {
            var actions = new List<PlaybookAction>();
            var p0Staff = assessments.Where(a => a.Priority == 0).ToList();
            var p1Staff = assessments.Where(a => a.Priority == 1).ToList();
            var p2Staff = assessments.Where(a => a.Priority == 2).ToList();

            if (p0Staff.Any())
            {
                actions.Add(new PlaybookAction
                {
                    Id = "EMERGENCY_RETENTION_INTERVENTION",
                    Priority = 0,
                    Label = "Emergency retention intervention",
                    Reason = $"{p0Staff.Count} staff member(s) at imminent flight risk",
                    Owner = "hr_manager",
                    BlastRadius = 4,
                    Reversibility = "medium",
                    StaffIds = p0Staff.Select(s => s.StaffId).ToList()
                });

                if (p0Staff.Any(s => s.Reasons.Contains("COMPENSATION_STAGNATION") || s.Reasons.Contains("RAISE_OVERDUE")))
                {
                    actions.Add(new PlaybookAction
                    {
                        Id = "PREPARE_RETENTION_OFFERS",
                        Priority = 0,
                        Label = "Prepare competitive retention offers",
                        Reason = "Compensation stagnation detected in flight-risk staff",
                        Owner = "compensation_team",
                        BlastRadius = 3,
                        Reversibility = "low",
                        StaffIds = p0Staff.Where(s => s.Reasons.Contains("COMPENSATION_STAGNATION") || s.Reasons.Contains("RAISE_OVERDUE"))
                            .Select(s => s.StaffId).ToList()
                    });
                }

                if (p0Staff.Count >= 2)
                {
                    actions.Add(new PlaybookAction
                    {
                        Id = "ESCALATE_TO_LEADERSHIP",
                        Priority = 0,
                        Label = "Escalate retention crisis to senior leadership",
                        Reason = $"Multiple ({p0Staff.Count}) staff at imminent departure risk",
                        Owner = "store_director",
                        BlastRadius = 5,
                        Reversibility = "high",
                        StaffIds = p0Staff.Select(s => s.StaffId).ToList()
                    });
                }
            }

            if (p1Staff.Any())
            {
                actions.Add(new PlaybookAction
                {
                    Id = "SCHEDULE_CAREER_CONVERSATIONS",
                    Priority = 1,
                    Label = "Schedule career development conversations",
                    Reason = $"{p1Staff.Count} staff showing elevated flight risk",
                    Owner = "team_lead",
                    BlastRadius = 2,
                    Reversibility = "high",
                    StaffIds = p1Staff.Select(s => s.StaffId).ToList()
                });

                var overworked = p1Staff.Where(s => s.Reasons.Contains("SEVERE_OVERWORK") || s.Reasons.Contains("MODERATE_OVERWORK")).ToList();
                if (overworked.Any())
                {
                    actions.Add(new PlaybookAction
                    {
                        Id = "REBALANCE_WORKLOAD",
                        Priority = 1,
                        Label = "Rebalance workload distribution",
                        Reason = "Overwork detected in at-risk staff",
                        Owner = "scheduling_manager",
                        BlastRadius = 3,
                        Reversibility = "high",
                        StaffIds = overworked.Select(s => s.StaffId).ToList()
                    });
                }
            }

            if (p2Staff.Count >= 3)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "LAUNCH_ENGAGEMENT_PROGRAM",
                    Priority = 2,
                    Label = "Launch team engagement program",
                    Reason = $"{p2Staff.Count} staff showing early disengagement signs",
                    Owner = "hr_manager",
                    BlastRadius = 3,
                    Reversibility = "high",
                    StaffIds = p2Staff.Select(s => s.StaffId).ToList()
                });
            }

            var unrecognized = assessments.Where(a => a.Reasons.Contains("HIGH_PERFORMER_UNRECOGNIZED")).ToList();
            if (unrecognized.Any())
            {
                actions.Add(new PlaybookAction
                {
                    Id = "RECOGNIZE_TOP_PERFORMERS",
                    Priority = 2,
                    Label = "Recognize high performers",
                    Reason = $"{unrecognized.Count} high performers without recent recognition",
                    Owner = "team_lead",
                    BlastRadius = 1,
                    Reversibility = "high",
                    StaffIds = unrecognized.Select(s => s.StaffId).ToList()
                });
            }

            if (appetite == RiskAppetite.Cautious && (grade == "C" || grade == "D" || grade == "F"))
            {
                actions.Add(new PlaybookAction
                {
                    Id = "SCHEDULE_TURNOVER_AUDIT",
                    Priority = 2,
                    Label = "Schedule comprehensive turnover risk audit",
                    Reason = $"Portfolio grade {grade} warrants deeper investigation",
                    Owner = "hr_manager",
                    BlastRadius = 1,
                    Reversibility = "high",
                    StaffIds = new List<int>()
                });
            }

            if (!actions.Any())
            {
                actions.Add(new PlaybookAction
                {
                    Id = "MAINTAIN_CULTURE",
                    Priority = 3,
                    Label = "Maintain healthy team culture",
                    Reason = "No elevated turnover risk detected",
                    Owner = "team_lead",
                    BlastRadius = 1,
                    Reversibility = "high",
                    StaffIds = new List<int>()
                });
            }

            // Aggressive trims P3 when P0/P1 present
            if (appetite == RiskAppetite.Aggressive && actions.Any(a => a.Priority <= 1))
            {
                actions.RemoveAll(a => a.Priority == 3);
            }

            return actions.OrderBy(a => a.Priority).ThenBy(a => a.Id).ToList();
        }

        // ══════════════════════════════════════════════════════
        //  Insights
        // ══════════════════════════════════════════════════════

        private List<string> BuildInsights(List<StaffRiskAssessment> assessments, int total)
        {
            var insights = new List<string>();
            if (total == 0) { insights.Add("EMPTY_ROSTER"); return insights; }

            var p0Count = assessments.Count(a => a.Priority == 0);
            var p1Count = assessments.Count(a => a.Priority == 1);

            if (p0Count >= 3) insights.Add("RETENTION_CRISIS");
            else if (p0Count >= 1) insights.Add("IMMINENT_FLIGHT_RISK_PRESENT");

            if (assessments.Count(a => a.Reasons.Contains("SEVERE_OVERWORK") || a.Reasons.Contains("MODERATE_OVERWORK")) >= 2)
                insights.Add("WORKLOAD_IMBALANCE_PATTERN");

            if (assessments.Count(a => a.Reasons.Contains("COMPENSATION_STAGNATION") || a.Reasons.Contains("RAISE_OVERDUE")) >= 3)
                insights.Add("SYSTEMIC_COMPENSATION_STAGNATION");

            if (assessments.Count(a => a.Reasons.Contains("SHARP_PERFORMANCE_DECLINE") || a.Reasons.Contains("MODERATE_PERFORMANCE_DECLINE")) >= 3)
                insights.Add("WIDESPREAD_PERFORMANCE_DECLINE");

            if (assessments.Count(a => a.Verdict == TurnoverVerdict.Thriving) >= (total * 0.6))
                insights.Add("HEALTHY_TEAM_CULTURE");

            var tenureMilestone = assessments.Count(a =>
                a.Reasons.Contains("EIGHTEEN_MONTH_TENURE_WINDOW") || a.Reasons.Contains("THREE_YEAR_TENURE_WINDOW"));
            if (tenureMilestone >= 2) insights.Add("TENURE_MILESTONE_CLUSTER");

            if (assessments.Count(a => a.Reasons.Contains("RESUME_UPDATED")) >= 2)
                insights.Add("MULTIPLE_RESUME_UPDATES_DETECTED");

            if (!insights.Any()) insights.Add("NO_NOTABLE_SIGNALS");

            return insights;
        }

        // ══════════════════════════════════════════════════════
        //  Formatters
        // ══════════════════════════════════════════════════════

        public string FormatText(TurnoverRiskReport report)
        {
            var lines = new List<string>
            {
                report.Headline,
                "",
                $"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}",
                $"Appetite: {report.Appetite}",
                $"Staff: {report.TotalStaff} | P0={report.P0Count} P1={report.P1Count} P2={report.P2Count} P3={report.P3Count}",
                ""
            };

            foreach (var a in report.Assessments.Where(x => x.Priority <= 2))
            {
                lines.Add($"  [{a.Priority}] {a.Name} ({a.Role}) — {a.Verdict} risk={a.RiskScore} tenure={a.TenureMonths}mo");
                if (a.Reasons.Any())
                    lines.Add($"      Reasons: {string.Join(", ", a.Reasons)}");
                if (a.RetentionActions.Any())
                    lines.Add($"      Actions: {string.Join("; ", a.RetentionActions)}");
            }

            lines.Add("");
            lines.Add("Playbook:");
            foreach (var p in report.Playbook)
                lines.Add($"  [P{p.Priority}] {p.Label} — {p.Reason} (owner: {p.Owner})");

            lines.Add("");
            lines.Add($"Insights: {string.Join(", ", report.Insights)}");

            return string.Join(Environment.NewLine, lines);
        }

        public string FormatMarkdown(TurnoverRiskReport report)
        {
            var lines = new List<string>
            {
                "## Staff Turnover Risk Report",
                "",
                $"**Grade:** {report.Grade} | **Portfolio Risk:** {report.PortfolioRiskScore} | **Appetite:** {report.Appetite}",
                $"**Staff:** {report.TotalStaff} | P0={report.P0Count} P1={report.P1Count} P2={report.P2Count} P3={report.P3Count}",
                "",
                "## Assessments",
                "",
                "| Name | Role | Verdict | Priority | Risk | Tenure (mo) | Top Reason |",
                "|------|------|---------|----------|------|-------------|------------|"
            };

            foreach (var a in report.Assessments)
            {
                var topReason = a.Reasons.FirstOrDefault() ?? "-";
                lines.Add($"| {a.Name} | {a.Role} | {a.Verdict} | P{a.Priority} | {a.RiskScore} | {a.TenureMonths} | {topReason} |");
            }

            lines.Add("");
            lines.Add("## Playbook");
            lines.Add("");
            foreach (var p in report.Playbook)
                lines.Add($"- **[P{p.Priority}] {p.Label}** — {p.Reason} _(owner: {p.Owner}, blast: {p.BlastRadius})_");

            lines.Add("");
            lines.Add("## Insights");
            lines.Add("");
            foreach (var i in report.Insights)
                lines.Add($"- {i}");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
