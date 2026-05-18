using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ── Enums ──────────────────────────────────────────────────────

    /// <summary>
    /// Per-rental escalation verdict, ordered by severity.
    /// </summary>
    public enum EscalationVerdict
    {
        None = 0,
        GentleReminder = 1,
        StandardFollowUp = 2,
        FirmReminder = 3,
        ServiceFreeze = 4,
        CollectionsHandoff = 5,
        WriteOff = 6
    }

    /// <summary>
    /// Priority bucket for recommended actions.
    /// </summary>
    public enum EscalationPriority { P0, P1, P2, P3 }

    /// <summary>
    /// Risk-appetite knob. Cautious escalates earlier; Aggressive escalates later
    /// and trims low-priority playbook actions.
    /// </summary>
    public enum LateReturnRiskAppetite { Cautious, Balanced, Aggressive }

    // ── Config ─────────────────────────────────────────────────────

    /// <summary>
    /// Tunable thresholds for <see cref="LateReturnEscalationService"/>.
    /// </summary>
    public class LateReturnEscalationConfig
    {
        public int GracePeriodDays { get; set; } = 0;
        public int HistoryWindowDays { get; set; } = 365;
        public int RepeatOffenderWindowDays { get; set; } = 90;
        public int RepeatOffenderMin { get; set; } = 2;
        public int ChronicOffenderMin { get; set; } = 4;
        public int NewCustomerWindowDays { get; set; } = 30;
        public decimal HighValueDollarsAtRisk { get; set; } = 25.00m;
        public decimal AccumulatedLateFeeMin { get; set; } = 10.00m;
        public int ColdTrailMinDaysOverdue { get; set; } = 21;
        public LateReturnRiskAppetite RiskAppetite { get; set; } = LateReturnRiskAppetite.Balanced;
    }

    // ── Models ─────────────────────────────────────────────────────

    public class EscalationSignal
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string Severity { get; set; } // "info" / "warn" / "critical"
        public string Reason { get; set; }
    }

    public class EscalationCase
    {
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string MovieName { get; set; }
        public int DaysOverdue { get; set; }
        public decimal DollarsAtRisk { get; set; }
        public EscalationVerdict Verdict { get; set; }
        public EscalationPriority Priority { get; set; }
        public int Score { get; set; }
        public List<EscalationSignal> Signals { get; set; } = new List<EscalationSignal>();
        public string RecommendedActionId { get; set; }
        public string SuggestedContactChannel { get; set; }
        public string SuggestedMessageTemplate { get; set; }
        public DateTime DueDate { get; set; }
    }

    public class PlaybookAction
    {
        public string Id { get; set; }
        public EscalationPriority Priority { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Owner { get; set; } // "staff" | "manager" | "collections" | "system"
        public int BlastRadius { get; set; } // 1..5
        public string Reversibility { get; set; } // "low" | "medium" | "high"
        public List<int> TargetRentalIds { get; set; } = new List<int>();
        public string SuggestedValue { get; set; }
    }

    public class PortfolioSummary
    {
        public int TotalOverdueCount { get; set; }
        public decimal TotalDollarsAtRisk { get; set; }
        public double MeanDaysOverdue { get; set; }
        public int ChronicOffenderCount { get; set; }
        public decimal HighValueAtRiskDollars { get; set; }
        public int ServiceFreezeCount { get; set; }
        public int CollectionsHandoffCount { get; set; }
        public int WriteOffCount { get; set; }
        public int OverallScore { get; set; }
        public char Grade { get; set; }
        public string Headline { get; set; }
        public List<string> Insights { get; set; } = new List<string>();
    }

    public class EscalationReport
    {
        public DateTime AsOfDate { get; set; }
        public LateReturnRiskAppetite RiskAppetite { get; set; }
        public PortfolioSummary Summary { get; set; } = new PortfolioSummary();
        public List<EscalationCase> Cases { get; set; } = new List<EscalationCase>();
        public List<PlaybookAction> Playbook { get; set; } = new List<PlaybookAction>();
    }

    // ── Service ────────────────────────────────────────────────────

    /// <summary>
    /// Agentic per-overdue-rental escalation advisor.
    ///
    /// Complements <see cref="LateReturnPredictorService"/> (predicts future
    /// late returns) and <see cref="LateFeeService"/> (charges fees) by deciding
    /// the human/store action to take on rentals that are already overdue:
    /// reminder → follow-up → firm contact → service freeze → collections → write-off.
    ///
    /// Pure read-only — never mutates repositories.
    /// </summary>
    public class LateReturnEscalationService
    {
        private readonly IRentalRepository _rentals;
        private readonly ICustomerRepository _customers;
        private readonly IClock _clock;
        private readonly LateReturnEscalationConfig _config;

        public LateReturnEscalationService(
            IRentalRepository rentals,
            ICustomerRepository customers,
            IClock clock,
            LateReturnEscalationConfig config = null)
        {
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? new LateReturnEscalationConfig();
        }

        // ── Single-rental evaluation ──────────────────────────────

        /// <summary>
        /// Evaluate a single rental in isolation against the customer's history.
        /// Returns a case with verdict <see cref="EscalationVerdict.None"/> when
        /// the rental is not overdue past the grace period.
        /// </summary>
        public EscalationCase Evaluate(Rental rental)
        {
            if (rental == null) throw new ArgumentNullException(nameof(rental));

            var today = _clock.Today;
            var customer = _customers.GetById(rental.CustomerId);
            var history = _rentals.GetByCustomer(rental.CustomerId) ?? new List<Rental>().AsReadOnly();
            return BuildCase(rental, customer, history, today, 0);
        }

        // ── Portfolio report ──────────────────────────────────────

        public EscalationReport GenerateReport()
        {
            var today = _clock.Today;
            var all = _rentals.GetAll() ?? new List<Rental>().AsReadOnly();

            // Index rentals by customer once (avoid O(N*M)).
            var byCustomer = new Dictionary<int, List<Rental>>();
            foreach (var r in all)
            {
                if (!byCustomer.TryGetValue(r.CustomerId, out var list))
                {
                    list = new List<Rental>();
                    byCustomer[r.CustomerId] = list;
                }
                list.Add(r);
            }

            // Count active overdues per customer for MULTIPLE_ACTIVE_OVERDUES signal.
            var activeOverdueCountByCustomer = new Dictionary<int, int>();
            foreach (var r in all)
            {
                if (IsOverdueNow(r, today))
                {
                    activeOverdueCountByCustomer.TryGetValue(r.CustomerId, out var c);
                    activeOverdueCountByCustomer[r.CustomerId] = c + 1;
                }
            }

            var cases = new List<EscalationCase>();
            foreach (var r in all)
            {
                if (!IsOverdueNow(r, today)) continue;

                Customer cust = _customers.GetById(r.CustomerId);
                List<Rental> history;
                if (!byCustomer.TryGetValue(r.CustomerId, out history))
                    history = new List<Rental>();

                int activeOverdues;
                activeOverdueCountByCustomer.TryGetValue(r.CustomerId, out activeOverdues);

                var ec = BuildCase(r, cust, history, today, activeOverdues);
                if (ec.Verdict != EscalationVerdict.None)
                    cases.Add(ec);
            }

            // Deterministic ordering.
            cases = cases
                .OrderBy(c => (int)c.Priority)
                .ThenByDescending(c => c.Score)
                .ThenBy(c => c.RentalId)
                .ToList();

            var report = new EscalationReport
            {
                AsOfDate = today,
                RiskAppetite = _config.RiskAppetite,
                Cases = cases
            };
            report.Summary = BuildSummary(cases);
            report.Playbook = BuildPlaybook(cases, report.Summary);
            return report;
        }

        // ── Internals ─────────────────────────────────────────────

        private bool IsOverdueNow(Rental r, DateTime today)
        {
            if (r == null) return false;
            if (r.Status == RentalStatus.Returned) return false;
            var effectiveDue = r.DueDate.AddDays(_config.GracePeriodDays);
            return today > effectiveDue;
        }

        private EscalationCase BuildCase(
            Rental r, Customer customer, IReadOnlyList<Rental> history,
            DateTime today, int activeOverduesForCustomer)
        {
            var ec = new EscalationCase
            {
                RentalId = r.Id,
                CustomerId = r.CustomerId,
                CustomerName = customer != null ? customer.Name : (r.CustomerName ?? "Unknown"),
                MovieName = r.MovieName ?? "Unknown",
                DueDate = r.DueDate
            };

            if (!IsOverdueNow(r, today))
            {
                ec.Verdict = EscalationVerdict.None;
                ec.Priority = EscalationPriority.P3;
                ec.Score = 0;
                ec.RecommendedActionId = "NO_ACTION";
                ec.SuggestedContactChannel = "none";
                ec.SuggestedMessageTemplate = "";
                return ec;
            }

            int daysOver = (int)Math.Ceiling((today - r.DueDate).TotalDays);
            if (daysOver < 1) daysOver = 1;
            ec.DaysOverdue = daysOver;
            ec.DollarsAtRisk = Math.Round(r.DailyRate * daysOver + r.LateFee, 2);

            // ── Signals ──────────────────────────────────────────
            AddSignal(ec, "DAYS_OVERDUE",
                "Rental is " + daysOver + " day" + (daysOver == 1 ? "" : "s") + " past due",
                daysOver >= 15 ? "critical" : (daysOver >= 8 ? "warn" : "info"),
                "Primary escalation driver.");

            decimal projectedAtRisk = r.DailyRate * daysOver * 3m;
            if (projectedAtRisk >= _config.HighValueDollarsAtRisk)
            {
                AddSignal(ec, "HIGH_VALUE_RENTAL",
                    "Projected $" + projectedAtRisk.ToString("F2", CultureInfo.InvariantCulture) +
                    " at risk if return delays continue",
                    "warn",
                    "DailyRate * DaysOverdue * 3 ≥ $" + _config.HighValueDollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture));
            }

            // History-derived signals.
            int lateInWindow = 0;
            int lateInYear = 0;
            DateTime windowCutoff = today.AddDays(-_config.RepeatOffenderWindowDays);
            DateTime yearCutoff = today.AddDays(-_config.HistoryWindowDays);
            foreach (var h in history)
            {
                if (h.Id == r.Id) continue;
                if (h.Status != RentalStatus.Returned) continue;
                if (!h.ReturnDate.HasValue) continue;
                if (h.ReturnDate.Value <= h.DueDate) continue; // not late
                if (h.ReturnDate.Value >= windowCutoff) lateInWindow++;
                if (h.ReturnDate.Value >= yearCutoff) lateInYear++;
            }

            bool chronic = lateInYear >= _config.ChronicOffenderMin;
            bool repeat = lateInWindow >= _config.RepeatOffenderMin;

            if (repeat)
            {
                AddSignal(ec, "REPEAT_OFFENDER",
                    lateInWindow + " late returns in past " + _config.RepeatOffenderWindowDays + " days",
                    "warn", "Customer has a recent pattern of late returns.");
            }
            if (chronic)
            {
                AddSignal(ec, "CHRONIC_OFFENDER",
                    lateInYear + " late returns in past " + _config.HistoryWindowDays + " days",
                    "critical", "Persistent late-return pattern. Forces P0.");
            }

            bool isNew = customer != null && customer.MemberSince.HasValue &&
                         (today - customer.MemberSince.Value).TotalDays <= _config.NewCustomerWindowDays;
            if (isNew)
            {
                AddSignal(ec, "NEW_CUSTOMER",
                    "Member since " + customer.MemberSince.Value.ToString("yyyy-MM-dd"),
                    "warn", "New customers with first overdues carry elevated risk.");
            }

            bool loyalGrace = false;
            if (customer != null &&
                (customer.MembershipType == MembershipType.Gold ||
                 customer.MembershipType == MembershipType.Platinum) &&
                lateInYear == 0 && !repeat && !chronic)
            {
                loyalGrace = true;
                AddSignal(ec, "LOYAL_CUSTOMER_GRACE",
                    customer.MembershipType + " member, first-time late",
                    "info", "Soften initial response for loyal customers.");
            }

            if (activeOverduesForCustomer >= 2)
            {
                AddSignal(ec, "MULTIPLE_ACTIVE_OVERDUES",
                    "Customer has " + activeOverduesForCustomer + " active overdue rentals",
                    "critical", "Forces minimum ServiceFreeze.");
            }

            bool noContact = customer != null &&
                             string.IsNullOrWhiteSpace(customer.Email) &&
                             string.IsNullOrWhiteSpace(customer.Phone);
            if (noContact)
            {
                AddSignal(ec, "NO_CONTACT_INFO",
                    "No email or phone on file",
                    "warn", "Escalates sooner because reminders cannot reach customer.");
            }

            if (r.LateFee >= _config.AccumulatedLateFeeMin)
            {
                AddSignal(ec, "LATE_FEE_ACCUMULATED",
                    "Accumulated late fee $" + r.LateFee.ToString("F2", CultureInfo.InvariantCulture),
                    "info", "Late fees already meaningful; collections risk rising.");
            }

            if (daysOver >= _config.ColdTrailMinDaysOverdue && r.LateFee == 0m)
            {
                AddSignal(ec, "COLD_TRAIL",
                    daysOver + " days overdue with $0 late fee — fee pipeline likely broken",
                    "warn", "Indicates broken collection or fee-assessment process.");
            }

            // ── Score ────────────────────────────────────────────
            double score = 0;
            // Days-overdue band (primary).
            if (daysOver <= 3) score += 18;
            else if (daysOver <= 7) score += 35;
            else if (daysOver <= 14) score += 52;
            else if (daysOver <= 29) score += 70;
            else if (daysOver <= 89) score += 85;
            else score += 95;

            if (HasSignal(ec, "HIGH_VALUE_RENTAL")) score += 10;
            if (repeat) score += 12;
            if (chronic) score += 20;
            if (isNew) score += 6;
            if (activeOverduesForCustomer >= 2) score += 10;
            if (noContact) score += 8;
            if (HasSignal(ec, "LATE_FEE_ACCUMULATED")) score += 4;
            if (HasSignal(ec, "COLD_TRAIL")) score += 6;
            if (loyalGrace)
            {
                double mult = _config.RiskAppetite == LateReturnRiskAppetite.Cautious ? 1.0
                            : _config.RiskAppetite == LateReturnRiskAppetite.Balanced ? 0.8
                            : 0.6;
                score *= mult;
            }

            // Risk appetite global multiplier.
            double appetiteMult = _config.RiskAppetite == LateReturnRiskAppetite.Cautious ? 1.15
                                : _config.RiskAppetite == LateReturnRiskAppetite.Aggressive ? 0.85
                                : 1.0;
            score *= appetiteMult;

            if (score < 0) score = 0;
            if (score > 100) score = 100;
            ec.Score = (int)Math.Round(score, MidpointRounding.AwayFromZero);

            // ── Verdict from days-overdue band, modulated by appetite ──
            EscalationVerdict baseVerdict;
            if (daysOver <= 3) baseVerdict = EscalationVerdict.GentleReminder;
            else if (daysOver <= 7) baseVerdict = EscalationVerdict.StandardFollowUp;
            else if (daysOver <= 14) baseVerdict = EscalationVerdict.FirmReminder;
            else if (daysOver <= 29) baseVerdict = EscalationVerdict.ServiceFreeze;
            else if (daysOver <= 89) baseVerdict = EscalationVerdict.CollectionsHandoff;
            else baseVerdict = EscalationVerdict.WriteOff;

            int verdictShift = 0;
            if (_config.RiskAppetite == LateReturnRiskAppetite.Cautious) verdictShift = +1;
            else if (_config.RiskAppetite == LateReturnRiskAppetite.Aggressive) verdictShift = -1;
            if (loyalGrace) verdictShift -= 1;

            int verdictIdx = (int)baseVerdict + verdictShift;
            if (verdictIdx < (int)EscalationVerdict.GentleReminder) verdictIdx = (int)EscalationVerdict.GentleReminder;
            if (verdictIdx > (int)EscalationVerdict.WriteOff) verdictIdx = (int)EscalationVerdict.WriteOff;
            var verdict = (EscalationVerdict)verdictIdx;

            // Forced minimums.
            if (chronic && verdict < EscalationVerdict.FirmReminder)
                verdict = EscalationVerdict.FirmReminder;
            if (activeOverduesForCustomer >= 2 && verdict < EscalationVerdict.ServiceFreeze)
                verdict = EscalationVerdict.ServiceFreeze;

            ec.Verdict = verdict;

            // ── Priority ─────────────────────────────────────────
            EscalationPriority priority;
            if (verdict >= EscalationVerdict.CollectionsHandoff || chronic) priority = EscalationPriority.P0;
            else if (verdict >= EscalationVerdict.FirmReminder) priority = EscalationPriority.P1;
            else if (verdict >= EscalationVerdict.StandardFollowUp) priority = EscalationPriority.P2;
            else priority = EscalationPriority.P3;
            ec.Priority = priority;

            // ── Action, channel, template ─────────────────────────
            ec.RecommendedActionId = VerdictActionId(verdict);
            ec.SuggestedContactChannel = SuggestChannel(verdict, customer, noContact);
            ec.SuggestedMessageTemplate = BuildTemplate(verdict);

            return ec;
        }

        private static void AddSignal(EscalationCase ec, string code, string label, string severity, string reason)
        {
            ec.Signals.Add(new EscalationSignal
            {
                Code = code,
                Label = label,
                Severity = severity,
                Reason = reason
            });
        }

        private static bool HasSignal(EscalationCase ec, string code)
        {
            foreach (var s in ec.Signals) if (s.Code == code) return true;
            return false;
        }

        private static string VerdictActionId(EscalationVerdict v)
        {
            switch (v)
            {
                case EscalationVerdict.GentleReminder: return "SEND_GENTLE_REMINDER";
                case EscalationVerdict.StandardFollowUp: return "SEND_STANDARD_FOLLOWUP";
                case EscalationVerdict.FirmReminder: return "SEND_FIRM_REMINDER";
                case EscalationVerdict.ServiceFreeze: return "APPLY_SERVICE_FREEZE";
                case EscalationVerdict.CollectionsHandoff: return "HANDOFF_TO_COLLECTIONS";
                case EscalationVerdict.WriteOff: return "WRITE_OFF_RENTAL";
                default: return "NO_ACTION";
            }
        }

        private static string SuggestChannel(EscalationVerdict v, Customer c, bool noContact)
        {
            if (noContact) return v >= EscalationVerdict.ServiceFreeze ? "mail" : "in_person";
            bool hasEmail = c != null && !string.IsNullOrWhiteSpace(c.Email);
            bool hasPhone = c != null && !string.IsNullOrWhiteSpace(c.Phone);
            switch (v)
            {
                case EscalationVerdict.GentleReminder:
                    return hasEmail ? "email" : (hasPhone ? "sms" : "in_person");
                case EscalationVerdict.StandardFollowUp:
                    return hasEmail ? "email" : (hasPhone ? "sms" : "in_person");
                case EscalationVerdict.FirmReminder:
                    return hasPhone ? "phone" : (hasEmail ? "email" : "in_person");
                case EscalationVerdict.ServiceFreeze:
                    return hasPhone ? "phone" : (hasEmail ? "email" : "mail");
                case EscalationVerdict.CollectionsHandoff:
                    return "mail";
                case EscalationVerdict.WriteOff:
                    return "none";
                default:
                    return "none";
            }
        }

        private static string BuildTemplate(EscalationVerdict v)
        {
            switch (v)
            {
                case EscalationVerdict.GentleReminder:
                    return "Hi {{Customer}}, just a friendly reminder that \"{{Movie}}\" was due {{DueDate}} and is {{DaysOverdue}} day(s) overdue. Drop it by when you can — thanks!";
                case EscalationVerdict.StandardFollowUp:
                    return "Hi {{Customer}}, our records show \"{{Movie}}\" is {{DaysOverdue}} days past its {{DueDate}} due date. Please return it within 48 hours to avoid additional late fees.";
                case EscalationVerdict.FirmReminder:
                    return "{{Customer}} — \"{{Movie}}\" is now {{DaysOverdue}} days overdue (due {{DueDate}}). Late fees are accruing. Return today or contact us to arrange a plan.";
                case EscalationVerdict.ServiceFreeze:
                    return "{{Customer}}: \"{{Movie}}\" is {{DaysOverdue}} days overdue. Your rental privileges are suspended until this title is returned and outstanding charges resolved.";
                case EscalationVerdict.CollectionsHandoff:
                    return "Final notice: \"{{Movie}}\" (rented {{DueDate}}) is being referred to collections after {{DaysOverdue}} days. Contact us immediately to resolve.";
                case EscalationVerdict.WriteOff:
                    return "Internal: rental of \"{{Movie}}\" by {{Customer}} ({{DaysOverdue}} days past due) written off.";
                default:
                    return "";
            }
        }

        // ── Summary ───────────────────────────────────────────────

        private PortfolioSummary BuildSummary(List<EscalationCase> cases)
        {
            var s = new PortfolioSummary();
            s.TotalOverdueCount = cases.Count;

            if (cases.Count == 0)
            {
                s.OverallScore = 0;
                s.Grade = 'A';
                s.Headline = "Portfolio healthy: no overdue rentals.";
                s.Insights.Add("HEALTHY_PORTFOLIO");
                return s;
            }

            decimal totalAtRisk = 0;
            long daySum = 0;
            decimal highValueAtRisk = 0;
            int chronicCustomers = 0;
            var chronicSeen = new HashSet<int>();
            int freezeCount = 0, collectionsCount = 0, writeOffCount = 0;
            int contactGapCount = 0;
            int coldTrailCount = 0;
            double scoreSum = 0;

            foreach (var c in cases)
            {
                totalAtRisk += c.DollarsAtRisk;
                daySum += c.DaysOverdue;
                if (HasSignal(c, "HIGH_VALUE_RENTAL")) highValueAtRisk += c.DollarsAtRisk;
                if (HasSignal(c, "CHRONIC_OFFENDER") && chronicSeen.Add(c.CustomerId)) chronicCustomers++;
                if (c.Verdict == EscalationVerdict.ServiceFreeze) freezeCount++;
                if (c.Verdict == EscalationVerdict.CollectionsHandoff) collectionsCount++;
                if (c.Verdict == EscalationVerdict.WriteOff) writeOffCount++;
                if (HasSignal(c, "NO_CONTACT_INFO")) contactGapCount++;
                if (HasSignal(c, "COLD_TRAIL")) coldTrailCount++;
                scoreSum += c.Score;
            }

            s.TotalDollarsAtRisk = Math.Round(totalAtRisk, 2);
            s.MeanDaysOverdue = Math.Round((double)daySum / cases.Count, 2);
            s.HighValueAtRiskDollars = Math.Round(highValueAtRisk, 2);
            s.ChronicOffenderCount = chronicCustomers;
            s.ServiceFreezeCount = freezeCount;
            s.CollectionsHandoffCount = collectionsCount;
            s.WriteOffCount = writeOffCount;
            s.OverallScore = (int)Math.Round(scoreSum / cases.Count, MidpointRounding.AwayFromZero);

            // Grade.
            char grade;
            if (s.OverallScore < 15) grade = 'A';
            else if (s.OverallScore < 30) grade = 'B';
            else if (s.OverallScore < 50) grade = 'C';
            else if (s.OverallScore < 70) grade = 'D';
            else grade = 'F';
            if (chronicCustomers >= 3 || collectionsCount >= 5) grade = 'F';
            s.Grade = grade;

            // Insights.
            if (chronicCustomers >= 3) s.Insights.Add("MANY_CHRONIC_OFFENDERS");
            if (collectionsCount >= 5) s.Insights.Add("COLLECTIONS_BACKLOG_GROWING");
            if (totalAtRisk > 200m) s.Insights.Add("HIGH_DOLLARS_AT_RISK");
            if (coldTrailCount >= 3) s.Insights.Add("BROKEN_LATE_FEE_PIPELINE");
            if (cases.Count > 0 && ((double)contactGapCount / cases.Count) >= 0.20) s.Insights.Add("CONTACT_INFO_GAP");

            s.Headline = "Grade " + grade + ": " + cases.Count + " overdue rental" +
                         (cases.Count == 1 ? "" : "s") + ", $" +
                         s.TotalDollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture) + " at risk.";
            return s;
        }

        // ── Playbook ──────────────────────────────────────────────

        private List<PlaybookAction> BuildPlaybook(List<EscalationCase> cases, PortfolioSummary summary)
        {
            var actions = new List<PlaybookAction>();

            if (cases.Count == 0)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "PORTFOLIO_HEALTHY",
                    Priority = EscalationPriority.P3,
                    Label = "No action needed",
                    Reason = "No overdue rentals in the portfolio.",
                    Owner = "staff",
                    BlastRadius = 1,
                    Reversibility = "high"
                });
                return actions;
            }

            var collectionsIds = cases.Where(c => c.Verdict == EscalationVerdict.CollectionsHandoff).Select(c => c.RentalId).ToList();
            if (collectionsIds.Count >= 3)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "OPEN_COLLECTIONS_BATCH",
                    Priority = EscalationPriority.P0,
                    Label = "Open a collections batch for " + collectionsIds.Count + " rentals",
                    Reason = collectionsIds.Count + " rentals reached collections threshold (30+ days overdue).",
                    Owner = "collections",
                    BlastRadius = 4,
                    Reversibility = "low",
                    TargetRentalIds = collectionsIds.OrderBy(i => i).ToList()
                });
            }

            var chronicCustomerIds = cases
                .Where(c => HasSignal(c, "CHRONIC_OFFENDER"))
                .Select(c => c.CustomerId)
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            if (chronicCustomerIds.Count > 0)
            {
                var rentalIds = cases.Where(c => chronicCustomerIds.Contains(c.CustomerId))
                                     .Select(c => c.RentalId).OrderBy(i => i).ToList();
                actions.Add(new PlaybookAction
                {
                    Id = "FREEZE_REPEAT_OFFENDERS",
                    Priority = EscalationPriority.P0,
                    Label = "Freeze rentals for " + chronicCustomerIds.Count + " chronic offender" + (chronicCustomerIds.Count == 1 ? "" : "s"),
                    Reason = "Customers with 4+ late returns in the past year require service suspension.",
                    Owner = "manager",
                    BlastRadius = 3,
                    Reversibility = "medium",
                    TargetRentalIds = rentalIds,
                    SuggestedValue = string.Join(",", chronicCustomerIds)
                });
            }

            var highValueIds = cases
                .Where(c => HasSignal(c, "HIGH_VALUE_RENTAL"))
                .OrderByDescending(c => c.DollarsAtRisk).ThenBy(c => c.RentalId)
                .Take(5)
                .Select(c => c.RentalId)
                .OrderBy(i => i)
                .ToList();
            if (highValueIds.Count > 0)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "CONTACT_HIGH_VALUE_TODAY",
                    Priority = EscalationPriority.P0,
                    Label = "Contact top " + highValueIds.Count + " high-value overdue rental" + (highValueIds.Count == 1 ? "" : "s") + " today",
                    Reason = "Largest dollar exposures benefit most from same-day human contact.",
                    Owner = "staff",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetRentalIds = highValueIds
                });
            }

            var reminderIds = cases
                .Where(c => c.Verdict == EscalationVerdict.GentleReminder ||
                            c.Verdict == EscalationVerdict.StandardFollowUp)
                .Select(c => c.RentalId).OrderBy(i => i).ToList();
            if (reminderIds.Count > 0)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "SEND_REMINDER_WAVE",
                    Priority = EscalationPriority.P1,
                    Label = "Send batched reminder wave to " + reminderIds.Count + " customer" + (reminderIds.Count == 1 ? "" : "s"),
                    Reason = "Early-stage overdues respond well to templated reminders.",
                    Owner = "system",
                    BlastRadius = 5,
                    Reversibility = "high",
                    TargetRentalIds = reminderIds
                });
            }

            var freezeIds = cases.Where(c => c.Verdict == EscalationVerdict.ServiceFreeze).Select(c => c.RentalId).OrderBy(i => i).ToList();
            if (freezeIds.Count > 0)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "SUSPEND_SERVICE_FOR_FROZEN",
                    Priority = EscalationPriority.P1,
                    Label = "Apply service freeze to " + freezeIds.Count + " account" + (freezeIds.Count == 1 ? "" : "s"),
                    Reason = "Mid-stage escalations require suspended rental privileges.",
                    Owner = "manager",
                    BlastRadius = 3,
                    Reversibility = "medium",
                    TargetRentalIds = freezeIds
                });
            }

            var noContactIds = cases.Where(c => HasSignal(c, "NO_CONTACT_INFO")).Select(c => c.RentalId).OrderBy(i => i).ToList();
            if (noContactIds.Count >= 2)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "ESCALATE_NO_CONTACT",
                    Priority = EscalationPriority.P1,
                    Label = "Update contact info for " + noContactIds.Count + " unreachable customer" + (noContactIds.Count == 1 ? "" : "s"),
                    Reason = "Reminders cannot reach these customers without email or phone on file.",
                    Owner = "staff",
                    BlastRadius = 2,
                    Reversibility = "high",
                    TargetRentalIds = noContactIds
                });
            }

            var coldTrailIds = cases.Where(c => HasSignal(c, "COLD_TRAIL")).Select(c => c.RentalId).OrderBy(i => i).ToList();
            if (coldTrailIds.Count >= 3)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "AUDIT_COLD_TRAIL",
                    Priority = EscalationPriority.P2,
                    Label = "Audit late-fee pipeline (" + coldTrailIds.Count + " cold-trail rentals)",
                    Reason = "Multiple long-overdue rentals with $0 late fee suggest the assessment pipeline is broken.",
                    Owner = "manager",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetRentalIds = coldTrailIds
                });
            }

            var newCustIds = cases.Where(c => HasSignal(c, "NEW_CUSTOMER")).Select(c => c.RentalId).OrderBy(i => i).ToList();
            if (newCustIds.Count >= 3)
            {
                actions.Add(new PlaybookAction
                {
                    Id = "REVIEW_NEW_CUSTOMER_ONBOARDING",
                    Priority = EscalationPriority.P2,
                    Label = "Review new-customer onboarding (" + newCustIds.Count + " new customers already overdue)",
                    Reason = "Cluster of new-customer overdues suggests onboarding is not setting expectations.",
                    Owner = "manager",
                    BlastRadius = 1,
                    Reversibility = "high",
                    TargetRentalIds = newCustIds
                });
            }

            // Cautious append: schedule follow-up review for any C/D/F grade.
            if (_config.RiskAppetite == LateReturnRiskAppetite.Cautious &&
                (summary.Grade == 'C' || summary.Grade == 'D' || summary.Grade == 'F'))
            {
                actions.Add(new PlaybookAction
                {
                    Id = "SCHEDULE_FOLLOWUP_REVIEW",
                    Priority = EscalationPriority.P2,
                    Label = "Schedule a 7-day follow-up portfolio review",
                    Reason = "Cautious appetite: re-evaluate escalations next week.",
                    Owner = "manager",
                    BlastRadius = 1,
                    Reversibility = "high"
                });
            }

            // Aggressive trim: drop P3, and drop lone P2 actions when P0/P1 exist.
            if (_config.RiskAppetite == LateReturnRiskAppetite.Aggressive)
            {
                actions = actions.Where(a => a.Priority != EscalationPriority.P3).ToList();
                bool hasP0OrP1 = actions.Any(a => a.Priority == EscalationPriority.P0 || a.Priority == EscalationPriority.P1);
                if (hasP0OrP1)
                {
                    actions = actions.Where(a => a.Priority != EscalationPriority.P2).ToList();
                }
            }

            // Dedupe by Id (keep first).
            var seen = new HashSet<string>();
            var deduped = new List<PlaybookAction>();
            foreach (var a in actions.OrderBy(a => (int)a.Priority).ThenBy(a => a.Id, StringComparer.Ordinal))
            {
                if (seen.Add(a.Id)) deduped.Add(a);
            }

            if (deduped.Count == 0)
            {
                deduped.Add(new PlaybookAction
                {
                    Id = "PORTFOLIO_HEALTHY",
                    Priority = EscalationPriority.P3,
                    Label = "No action needed",
                    Reason = "No actionable escalations after appetite filtering.",
                    Owner = "staff",
                    BlastRadius = 1,
                    Reversibility = "high"
                });
            }

            return deduped;
        }

        // ── Renderers ─────────────────────────────────────────────

        public string RenderText(EscalationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("Late Return Escalation Report");
            sb.AppendLine("As of: " + report.AsOfDate.ToString("yyyy-MM-dd"));
            sb.AppendLine("Appetite: " + report.RiskAppetite);
            sb.AppendLine();
            sb.AppendLine(report.Summary.Headline);
            sb.AppendLine("Overall score: " + report.Summary.OverallScore + " (Grade " + report.Summary.Grade + ")");
            sb.AppendLine("$ at risk: " + report.Summary.TotalDollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture));
            sb.AppendLine("Mean days overdue: " + report.Summary.MeanDaysOverdue.ToString("F2", CultureInfo.InvariantCulture));
            if (report.Summary.Insights.Count > 0)
            {
                sb.AppendLine("Insights: " + string.Join(", ", report.Summary.Insights));
            }
            sb.AppendLine();
            sb.AppendLine("Playbook:");
            foreach (var a in report.Playbook)
            {
                sb.AppendLine("  [" + a.Priority + "] " + a.Id + " — " + a.Label + " (owner=" + a.Owner + ")");
            }
            sb.AppendLine();
            sb.AppendLine("Cases (" + report.Cases.Count + "):");
            foreach (var c in report.Cases)
            {
                sb.AppendLine("  [" + c.Priority + "] R#" + c.RentalId + " " + c.MovieName +
                              " — " + c.Verdict + " (score " + c.Score + ", " + c.DaysOverdue + "d, $" +
                              c.DollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture) + ")");
            }
            return sb.ToString();
        }

        public string RenderMarkdown(EscalationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("# Late Return Escalation");
            sb.AppendLine();
            sb.AppendLine("- As of: " + report.AsOfDate.ToString("yyyy-MM-dd"));
            sb.AppendLine("- Appetite: " + report.RiskAppetite);
            sb.AppendLine("- " + report.Summary.Headline);
            sb.AppendLine("- Overall score: **" + report.Summary.OverallScore + "** (Grade **" + report.Summary.Grade + "**)");
            if (report.Summary.Insights.Count > 0)
            {
                sb.AppendLine("- Insights: " + string.Join(", ", report.Summary.Insights));
            }
            sb.AppendLine();
            sb.AppendLine("## Playbook");
            sb.AppendLine();
            sb.AppendLine("| Priority | Id | Owner | Label |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var a in report.Playbook)
            {
                sb.AppendLine("| " + a.Priority + " | " + EscapePipe(a.Id) + " | " + a.Owner + " | " + EscapePipe(a.Label) + " |");
            }
            sb.AppendLine();
            sb.AppendLine("## Cases");
            sb.AppendLine();
            sb.AppendLine("| Priority | Rental | Customer | Movie | Days | $@Risk | Verdict | Action |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var c in report.Cases)
            {
                sb.AppendLine("| " + c.Priority + " | " + c.RentalId + " | " + EscapePipe(c.CustomerName) +
                              " | " + EscapePipe(c.MovieName) + " | " + c.DaysOverdue + " | $" +
                              c.DollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture) + " | " + c.Verdict +
                              " | " + EscapePipe(c.RecommendedActionId) + " |");
            }
            return sb.ToString();
        }

        private static string EscapePipe(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("|", "\\|");
        }

        public string RenderJson(EscalationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"asOfDate\": " + Q(report.AsOfDate.ToString("yyyy-MM-dd")) + ",");
            sb.AppendLine("  \"cases\": [");
            for (int i = 0; i < report.Cases.Count; i++)
            {
                var c = report.Cases[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"customerId\": " + c.CustomerId + ",");
                sb.AppendLine("      \"customerName\": " + Q(c.CustomerName) + ",");
                sb.AppendLine("      \"daysOverdue\": " + c.DaysOverdue + ",");
                sb.AppendLine("      \"dollarsAtRisk\": " + c.DollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"dueDate\": " + Q(c.DueDate.ToString("yyyy-MM-dd")) + ",");
                sb.AppendLine("      \"movieName\": " + Q(c.MovieName) + ",");
                sb.AppendLine("      \"priority\": " + Q(c.Priority.ToString()) + ",");
                sb.AppendLine("      \"recommendedActionId\": " + Q(c.RecommendedActionId) + ",");
                sb.AppendLine("      \"rentalId\": " + c.RentalId + ",");
                sb.AppendLine("      \"score\": " + c.Score + ",");
                sb.Append("      \"signals\": [");
                var sortedSignals = c.Signals.OrderBy(s => s.Code, StringComparer.Ordinal).ToList();
                if (sortedSignals.Count == 0) sb.AppendLine("],");
                else
                {
                    sb.AppendLine();
                    for (int j = 0; j < sortedSignals.Count; j++)
                    {
                        var s = sortedSignals[j];
                        sb.AppendLine("        {");
                        sb.AppendLine("          \"code\": " + Q(s.Code) + ",");
                        sb.AppendLine("          \"label\": " + Q(s.Label) + ",");
                        sb.AppendLine("          \"reason\": " + Q(s.Reason) + ",");
                        sb.AppendLine("          \"severity\": " + Q(s.Severity));
                        sb.AppendLine("        }" + (j == sortedSignals.Count - 1 ? "" : ","));
                    }
                    sb.AppendLine("      ],");
                }
                sb.AppendLine("      \"suggestedContactChannel\": " + Q(c.SuggestedContactChannel) + ",");
                sb.AppendLine("      \"suggestedMessageTemplate\": " + Q(c.SuggestedMessageTemplate) + ",");
                sb.AppendLine("      \"verdict\": " + Q(c.Verdict.ToString()));
                sb.AppendLine("    }" + (i == report.Cases.Count - 1 ? "" : ","));
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"playbook\": [");
            for (int i = 0; i < report.Playbook.Count; i++)
            {
                var a = report.Playbook[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"blastRadius\": " + a.BlastRadius + ",");
                sb.AppendLine("      \"id\": " + Q(a.Id) + ",");
                sb.AppendLine("      \"label\": " + Q(a.Label) + ",");
                sb.AppendLine("      \"owner\": " + Q(a.Owner) + ",");
                sb.AppendLine("      \"priority\": " + Q(a.Priority.ToString()) + ",");
                sb.AppendLine("      \"reason\": " + Q(a.Reason) + ",");
                sb.AppendLine("      \"reversibility\": " + Q(a.Reversibility) + ",");
                sb.AppendLine("      \"suggestedValue\": " + (a.SuggestedValue == null ? "null" : Q(a.SuggestedValue)) + ",");
                sb.Append("      \"targetRentalIds\": [");
                for (int j = 0; j < a.TargetRentalIds.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append(a.TargetRentalIds[j]);
                }
                sb.AppendLine("]");
                sb.AppendLine("    }" + (i == report.Playbook.Count - 1 ? "" : ","));
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"riskAppetite\": " + Q(report.RiskAppetite.ToString()) + ",");
            sb.AppendLine("  \"summary\": {");
            sb.AppendLine("    \"chronicOffenderCount\": " + report.Summary.ChronicOffenderCount + ",");
            sb.AppendLine("    \"collectionsHandoffCount\": " + report.Summary.CollectionsHandoffCount + ",");
            sb.AppendLine("    \"grade\": " + Q(report.Summary.Grade.ToString()) + ",");
            sb.AppendLine("    \"headline\": " + Q(report.Summary.Headline) + ",");
            sb.AppendLine("    \"highValueAtRiskDollars\": " + report.Summary.HighValueAtRiskDollars.ToString("F2", CultureInfo.InvariantCulture) + ",");
            sb.Append("    \"insights\": [");
            var sortedInsights = report.Summary.Insights.OrderBy(s => s, StringComparer.Ordinal).ToList();
            for (int j = 0; j < sortedInsights.Count; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append(Q(sortedInsights[j]));
            }
            sb.AppendLine("],");
            sb.AppendLine("    \"meanDaysOverdue\": " + report.Summary.MeanDaysOverdue.ToString("F2", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"overallScore\": " + report.Summary.OverallScore + ",");
            sb.AppendLine("    \"serviceFreezeCount\": " + report.Summary.ServiceFreezeCount + ",");
            sb.AppendLine("    \"totalDollarsAtRisk\": " + report.Summary.TotalDollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"totalOverdueCount\": " + report.Summary.TotalOverdueCount + ",");
            sb.AppendLine("    \"writeOffCount\": " + report.Summary.WriteOffCount);
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
