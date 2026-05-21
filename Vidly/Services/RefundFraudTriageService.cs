using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ── Models ─────────────────────────────────────────────────────

    /// <summary>
    /// Top-level verdict for a single refund request after triage.
    /// </summary>
    public enum RefundFraudVerdict
    {
        /// <summary>Low risk, small amount - safe to auto-approve.</summary>
        AutoApprove,
        /// <summary>Normal review by any clerk.</summary>
        StandardReview,
        /// <summary>Escalate to supervisor, require supporting evidence.</summary>
        EnhancedReview,
        /// <summary>Hold the refund; fraud-investigation required before any payout.</summary>
        Block
    }

    /// <summary>Risk band for a triaged refund request.</summary>
    public enum RefundFraudRiskBand
    {
        Minimal,    // 0-19
        Low,        // 20-39
        Moderate,   // 40-59
        Elevated,   // 60-79
        High        // 80-100
    }

    /// <summary>P0/P1/P2 action priority.</summary>
    public enum RefundFraudActionPriority { P0, P1, P2 }

    /// <summary>
    /// Risk-appetite knob mirroring its sibling agentic services. Modulates
    /// how aggressively the triage flags requests and which actions fire.
    /// </summary>
    public enum RefundFraudRiskAppetite
    {
        /// <summary>Bias toward protecting cash; more friction, more blocks.</summary>
        Cautious,
        /// <summary>Default.</summary>
        Balanced,
        /// <summary>Bias toward customer experience; fewer escalations.</summary>
        Aggressive
    }

    /// <summary>
    /// Tunable configuration. Defaults are reasonable for a small video store.
    /// Every knob is explicit so the service stays testable.
    /// </summary>
    public class RefundFraudTriageConfig
    {
        /// <summary>How far back to look at a customer's refund history.</summary>
        public int RefundHistoryWindowDays { get; set; } = 365;

        /// <summary>Trailing window used for "recent refund velocity".</summary>
        public int VelocityWindowDays { get; set; } = 30;

        /// <summary>3+ refund requests in the velocity window → strong velocity hit.</summary>
        public int VelocityHighCount { get; set; } = 3;

        /// <summary>2 refund requests in the velocity window → moderate hit.</summary>
        public int VelocityModerateCount { get; set; } = 2;

        /// <summary>Score above which a request is treated as Block.</summary>
        public int BlockThreshold { get; set; } = 75;

        /// <summary>Score above which a request is Enhanced review.</summary>
        public int EnhancedReviewThreshold { get; set; } = 50;

        /// <summary>Score above which a request is Standard review.</summary>
        public int StandardReviewThreshold { get; set; } = 25;

        /// <summary>Below this amount AND below StandardReviewThreshold → AutoApprove.</summary>
        public decimal AutoApproveMaxAmount { get; set; } = 10.00m;

        /// <summary>New-customer window. Refunds inside this raise risk.</summary>
        public int NewCustomerWindowDays { get; set; } = 14;

        /// <summary>"Large" refund amount tier.</summary>
        public decimal LargeAmountThreshold { get; set; } = 50.00m;

        /// <summary>"Medium" refund amount tier.</summary>
        public decimal MediumAmountThreshold { get; set; } = 25.00m;

        /// <summary>How many days after a rental a same-reason repeat counts as a pattern.</summary>
        public int RepeatReasonWindowDays { get; set; } = 180;

        /// <summary>Risk-appetite knob.</summary>
        public RefundFraudRiskAppetite RiskAppetite { get; set; } = RefundFraudRiskAppetite.Balanced;
    }

    /// <summary>
    /// One structured reasoning signal feeding the triage score.
    /// </summary>
    public class RefundFraudSignal
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public int ScoreDelta { get; set; }
        /// <summary>"info" | "warn" | "critical".</summary>
        public string Severity { get; set; }
    }

    /// <summary>A recommended action with priority and rationale.</summary>
    public class RefundFraudAction
    {
        public RefundFraudActionPriority Priority { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public string Rationale { get; set; }
    }

    /// <summary>Triage result for a single refund request.</summary>
    public class RefundFraudTriage
    {
        public int RefundRequestId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int RentalId { get; set; }
        public string MovieName { get; set; }
        public decimal RequestedAmount { get; set; }
        public RefundReason Reason { get; set; }
        public RefundType Type { get; set; }
        public DateTime EvaluatedAt { get; set; }

        public int Score { get; set; }
        public RefundFraudRiskBand RiskBand { get; set; }
        public RefundFraudVerdict Verdict { get; set; }
        public List<RefundFraudSignal> Signals { get; set; } = new List<RefundFraudSignal>();
        public List<RefundFraudAction> Actions { get; set; } = new List<RefundFraudAction>();
        public string Headline { get; set; }
    }

    /// <summary>Portfolio-level summary across multiple triages.</summary>
    public class RefundFraudPortfolioSummary
    {
        public int TotalRequests { get; set; }
        public int BlockCount { get; set; }
        public int EnhancedReviewCount { get; set; }
        public int StandardReviewCount { get; set; }
        public int AutoApproveCount { get; set; }
        public decimal AmountAtRisk { get; set; }
        public Dictionary<RefundFraudRiskBand, int> ByBand { get; set; }
            = new Dictionary<RefundFraudRiskBand, int>();
    }

    // ── Service ───────────────────────────────────────────────────

    /// <summary>
    /// Agentic refund-fraud triage advisor.
    /// Sibling to <see cref="RefundService"/> (which submits / approves) and
    /// <see cref="DisputeResolutionService"/> (which adjudicates disputes).
    /// This service does NOT mutate refund state - it scores risk, classifies
    /// each pending request, and emits a prioritized action playbook so a
    /// human reviewer can decide quickly.
    ///
    /// Inputs:
    ///   - A pending <see cref="RefundRequest"/>.
    ///   - The customer's prior refund history (via injected provider, so the
    ///     service stays decoupled from <see cref="RefundService"/>'s static
    ///     ledger and is trivially testable with a lambda).
    ///   - The customer's rental history and profile.
    ///
    /// Outputs:
    ///   - A 0-100 fraud-risk score with named signals explaining each
    ///     contribution.
    ///   - A verdict (AutoApprove / StandardReview / EnhancedReview / Block).
    ///   - A P0/P1/P2 action playbook (e.g. "Require defect photo evidence",
    ///     "Hold payout pending fraud review", "Flag account for pattern").
    /// </summary>
    public class RefundFraudTriageService
    {
        private readonly IRentalRepository _rentals;
        private readonly ICustomerRepository _customers;
        private readonly Func<int, IReadOnlyList<RefundRequest>> _refundsByCustomer;
        private readonly IClock _clock;
        private readonly RefundFraudTriageConfig _config;

        public RefundFraudTriageService(
            IRentalRepository rentals,
            ICustomerRepository customers,
            Func<int, IReadOnlyList<RefundRequest>> refundsByCustomer,
            IClock clock,
            RefundFraudTriageConfig config = null)
        {
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _refundsByCustomer = refundsByCustomer
                ?? throw new ArgumentNullException(nameof(refundsByCustomer));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? new RefundFraudTriageConfig();
        }

        /// <summary>Public read-only access to the active configuration.</summary>
        public RefundFraudTriageConfig Config => _config;

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Triage a single refund request.
        /// </summary>
        public RefundFraudTriage Triage(RefundRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var customer = _customers.GetById(request.CustomerId);
            var rental = _rentals.GetById(request.RentalId);

            var triage = new RefundFraudTriage
            {
                RefundRequestId = request.Id,
                CustomerId = request.CustomerId,
                CustomerName = customer?.Name ?? request.CustomerName ?? "(unknown)",
                RentalId = request.RentalId,
                MovieName = rental?.MovieName ?? request.MovieName,
                RequestedAmount = request.RefundAmount,
                Reason = request.Reason,
                Type = request.Type,
                EvaluatedAt = _clock.Now,
            };

            int score = 0;
            score += ScoreVelocity(request, triage);
            score += ScoreApprovalHistory(request, triage);
            score += ScoreTiming(request, rental, triage);
            score += ScoreAmount(request, triage);
            score += ScoreNewCustomer(request, customer, triage);
            score += ScoreReason(request, triage);
            score += ScoreRepeatReasonPattern(request, triage);
            score += ScoreMembership(customer, triage);
            score += ScoreRentalHistoryDepth(request, triage);

            // Appetite shift - same signals, different sensitivity.
            score = ApplyAppetite(score, triage);

            // Clamp 0..100.
            if (score < 0) score = 0;
            if (score > 100) score = 100;

            triage.Score = score;
            triage.RiskBand = ToBand(score);
            triage.Verdict = DecideVerdict(score, request);
            triage.Actions = BuildActions(triage, request, customer, rental);
            triage.Headline = BuildHeadline(triage);

            return triage;
        }

        /// <summary>Triage many requests at once.</summary>
        public IReadOnlyList<RefundFraudTriage> TriageMany(IEnumerable<RefundRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            return requests.Select(Triage).ToList();
        }

        /// <summary>Portfolio summary over a set of triage results.</summary>
        public RefundFraudPortfolioSummary Summarize(IEnumerable<RefundFraudTriage> triages)
        {
            if (triages == null) throw new ArgumentNullException(nameof(triages));
            var list = triages.ToList();
            var s = new RefundFraudPortfolioSummary
            {
                TotalRequests = list.Count,
                BlockCount = list.Count(t => t.Verdict == RefundFraudVerdict.Block),
                EnhancedReviewCount = list.Count(t => t.Verdict == RefundFraudVerdict.EnhancedReview),
                StandardReviewCount = list.Count(t => t.Verdict == RefundFraudVerdict.StandardReview),
                AutoApproveCount = list.Count(t => t.Verdict == RefundFraudVerdict.AutoApprove),
                AmountAtRisk = list
                    .Where(t => t.Verdict == RefundFraudVerdict.Block
                             || t.Verdict == RefundFraudVerdict.EnhancedReview)
                    .Sum(t => t.RequestedAmount),
            };
            foreach (RefundFraudRiskBand b in Enum.GetValues(typeof(RefundFraudRiskBand)))
                s.ByBand[b] = list.Count(t => t.RiskBand == b);
            return s;
        }

        /// <summary>Plain-text report for staff consoles / emails.</summary>
        public string RenderTextReport(IEnumerable<RefundFraudTriage> triages)
        {
            if (triages == null) throw new ArgumentNullException(nameof(triages));
            var list = triages.OrderByDescending(t => t.Score).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("Refund Fraud Triage Report");
            sb.AppendLine("Generated: " + _clock.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Risk appetite: " + _config.RiskAppetite);
            sb.AppendLine(new string('-', 60));
            foreach (var t in list)
            {
                sb.AppendLine(
                    $"#{t.RefundRequestId} {t.CustomerName} - {t.MovieName} " +
                    $"${t.RequestedAmount:F2} [{t.Verdict} / {t.RiskBand} / {t.Score}]");
                sb.AppendLine("  " + t.Headline);
                foreach (var a in t.Actions.OrderBy(a => a.Priority))
                    sb.AppendLine($"  [{a.Priority}] {a.Code}: {a.Description}");
            }
            return sb.ToString();
        }

        // ── Signal scoring ─────────────────────────────────────────

        private int ScoreVelocity(RefundRequest req, RefundFraudTriage t)
        {
            var history = _refundsByCustomer(req.CustomerId) ?? new List<RefundRequest>();
            var cutoff = _clock.Now.AddDays(-_config.VelocityWindowDays);
            int recent = history.Count(r => r.Id != req.Id && r.RequestedDate >= cutoff);

            if (recent >= _config.VelocityHighCount)
            {
                AddSignal(t, "VELOCITY_HIGH",
                    $"{recent} refund requests in past {_config.VelocityWindowDays} days",
                    25, "critical");
                return 25;
            }
            if (recent >= _config.VelocityModerateCount)
            {
                AddSignal(t, "VELOCITY_MODERATE",
                    $"{recent} refund requests in past {_config.VelocityWindowDays} days",
                    15, "warn");
                return 15;
            }
            if (recent >= 1)
            {
                AddSignal(t, "VELOCITY_LIGHT",
                    $"{recent} prior refund request in past {_config.VelocityWindowDays} days",
                    5, "info");
                return 5;
            }
            return 0;
        }

        private int ScoreApprovalHistory(RefundRequest req, RefundFraudTriage t)
        {
            var history = (_refundsByCustomer(req.CustomerId) ?? new List<RefundRequest>())
                .Where(r => r.Id != req.Id
                            && (r.Status == RefundStatus.Approved
                                || r.Status == RefundStatus.Denied
                                || r.Status == RefundStatus.Processed))
                .ToList();
            if (history.Count < 2) return 0;

            int denied = history.Count(r => r.Status == RefundStatus.Denied);
            double denialRate = (double)denied / history.Count;
            if (denialRate >= 0.5)
            {
                AddSignal(t, "HIGH_DENIAL_RATE",
                    $"{denied}/{history.Count} prior refunds denied ({denialRate:P0})",
                    20, "critical");
                return 20;
            }
            if (denialRate >= 0.25)
            {
                AddSignal(t, "ELEVATED_DENIAL_RATE",
                    $"{denied}/{history.Count} prior refunds denied ({denialRate:P0})",
                    10, "warn");
                return 10;
            }
            return 0;
        }

        private int ScoreTiming(RefundRequest req, Rental rental, RefundFraudTriage t)
        {
            if (rental == null) return 0;
            // Days between rental start and the refund request.
            var daysSinceRental = (req.RequestedDate.Date - rental.RentalDate.Date).Days;
            if (daysSinceRental >= 14)
            {
                AddSignal(t, "LATE_REFUND_REQUEST",
                    $"Refund requested {daysSinceRental} days after rental",
                    10, "warn");
                return 10;
            }
            // Same-day claim of "defective disc" on a returned, not-overdue rental is suspicious.
            if (daysSinceRental == 0
                && rental.Status == RentalStatus.Returned
                && req.Reason == RefundReason.DefectiveDisc)
            {
                AddSignal(t, "SAME_DAY_DEFECT_CLAIM",
                    "Same-day refund claim citing defective disc on a returned rental",
                    8, "warn");
                return 8;
            }
            return 0;
        }

        private int ScoreAmount(RefundRequest req, RefundFraudTriage t)
        {
            if (req.RefundAmount >= _config.LargeAmountThreshold)
            {
                AddSignal(t, "LARGE_AMOUNT",
                    $"Refund amount ${req.RefundAmount:F2} ≥ ${_config.LargeAmountThreshold:F2}",
                    12, "warn");
                return 12;
            }
            if (req.RefundAmount >= _config.MediumAmountThreshold)
            {
                AddSignal(t, "MEDIUM_AMOUNT",
                    $"Refund amount ${req.RefundAmount:F2} ≥ ${_config.MediumAmountThreshold:F2}",
                    6, "info");
                return 6;
            }
            return 0;
        }

        private int ScoreNewCustomer(RefundRequest req, Customer customer, RefundFraudTriage t)
        {
            if (customer == null) return 0;
            if (!customer.MemberSince.HasValue) return 0;
            var age = (_clock.Today - customer.MemberSince.Value.Date).Days;
            if (age <= _config.NewCustomerWindowDays && req.Type == RefundType.Full)
            {
                AddSignal(t, "NEW_CUSTOMER_FULL_REFUND",
                    $"Customer joined {age} day(s) ago and is requesting a full refund",
                    18, "critical");
                return 18;
            }
            if (age <= _config.NewCustomerWindowDays)
            {
                AddSignal(t, "NEW_CUSTOMER",
                    $"Customer joined {age} day(s) ago",
                    6, "info");
                return 6;
            }
            return 0;
        }

        private int ScoreReason(RefundRequest req, RefundFraudTriage t)
        {
            if (req.Reason == RefundReason.Other)
            {
                AddSignal(t, "VAGUE_REASON",
                    "Reason is 'Other' (no specific category)",
                    8, "info");
                return 8;
            }
            return 0;
        }

        private int ScoreRepeatReasonPattern(RefundRequest req, RefundFraudTriage t)
        {
            var history = _refundsByCustomer(req.CustomerId) ?? new List<RefundRequest>();
            var cutoff = _clock.Now.AddDays(-_config.RepeatReasonWindowDays);
            int sameReason = history.Count(r =>
                r.Id != req.Id
                && r.Reason == req.Reason
                && r.RequestedDate >= cutoff);
            if (sameReason >= 3)
            {
                AddSignal(t, "REPEAT_REASON_PATTERN",
                    $"Same reason ({req.Reason}) cited in {sameReason} prior refunds within " +
                    $"past {_config.RepeatReasonWindowDays} days",
                    12, "critical");
                return 12;
            }
            if (sameReason == 2)
            {
                AddSignal(t, "REPEAT_REASON_LIGHT",
                    $"Same reason ({req.Reason}) cited in 2 prior refunds",
                    5, "info");
                return 5;
            }
            return 0;
        }

        private int ScoreMembership(Customer customer, RefundFraudTriage t)
        {
            if (customer == null) return 0;
            switch (customer.MembershipType)
            {
                case MembershipType.Basic:
                    AddSignal(t, "MEMBERSHIP_BASIC", "Basic membership (no loyalty discount)",
                        3, "info");
                    return 3;
                case MembershipType.Platinum:
                    AddSignal(t, "MEMBERSHIP_PLATINUM",
                        "Platinum loyal customer (risk reduced)",
                        -5, "info");
                    return -5;
                case MembershipType.Gold:
                    AddSignal(t, "MEMBERSHIP_GOLD",
                        "Gold loyal customer (risk reduced)",
                        -2, "info");
                    return -2;
                default:
                    return 0;
            }
        }

        private int ScoreRentalHistoryDepth(RefundRequest req, RefundFraudTriage t)
        {
            var customerRentals = _rentals.GetByCustomer(req.CustomerId);
            int total = customerRentals?.Count ?? 0;
            if (total <= 1)
            {
                AddSignal(t, "THIN_RENTAL_HISTORY",
                    total == 0
                        ? "Customer has no prior rental history"
                        : "Customer has only 1 prior rental",
                    10, "warn");
                return 10;
            }
            return 0;
        }

        // ── Verdict / band / appetite ─────────────────────────────

        private int ApplyAppetite(int rawScore, RefundFraudTriage t)
        {
            if (_config.RiskAppetite == RefundFraudRiskAppetite.Cautious)
            {
                int bumped = (int)Math.Round(rawScore * 1.15);
                if (bumped != rawScore)
                    AddSignal(t, "APPETITE_CAUTIOUS",
                        "Risk-appetite=Cautious: score multiplied by 1.15",
                        bumped - rawScore, "info");
                return bumped;
            }
            if (_config.RiskAppetite == RefundFraudRiskAppetite.Aggressive)
            {
                int dampened = (int)Math.Round(rawScore * 0.85);
                if (dampened != rawScore)
                    AddSignal(t, "APPETITE_AGGRESSIVE",
                        "Risk-appetite=Aggressive: score multiplied by 0.85",
                        dampened - rawScore, "info");
                return dampened;
            }
            return rawScore;
        }

        private RefundFraudRiskBand ToBand(int score)
        {
            if (score >= 80) return RefundFraudRiskBand.High;
            if (score >= 60) return RefundFraudRiskBand.Elevated;
            if (score >= 40) return RefundFraudRiskBand.Moderate;
            if (score >= 20) return RefundFraudRiskBand.Low;
            return RefundFraudRiskBand.Minimal;
        }

        private RefundFraudVerdict DecideVerdict(int score, RefundRequest req)
        {
            if (score >= _config.BlockThreshold) return RefundFraudVerdict.Block;
            if (score >= _config.EnhancedReviewThreshold) return RefundFraudVerdict.EnhancedReview;
            if (score >= _config.StandardReviewThreshold) return RefundFraudVerdict.StandardReview;

            // Below standard-review threshold: small amounts auto-approve, larger
            // ones still get a standard review just so a human eyeballs the payout.
            if (req.RefundAmount <= _config.AutoApproveMaxAmount)
                return RefundFraudVerdict.AutoApprove;
            return RefundFraudVerdict.StandardReview;
        }

        // ── Action playbook ───────────────────────────────────────

        private List<RefundFraudAction> BuildActions(
            RefundFraudTriage t, RefundRequest req, Customer customer, Rental rental)
        {
            var actions = new List<RefundFraudAction>();

            switch (t.Verdict)
            {
                case RefundFraudVerdict.Block:
                    actions.Add(new RefundFraudAction
                    {
                        Priority = RefundFraudActionPriority.P0,
                        Code = "HOLD_PAYOUT",
                        Description = "Hold refund payout pending fraud-investigation review.",
                        Rationale = "Score ≥ block threshold; do not release funds.",
                    });
                    actions.Add(new RefundFraudAction
                    {
                        Priority = RefundFraudActionPriority.P0,
                        Code = "FLAG_ACCOUNT",
                        Description = "Flag customer account for fraud review.",
                        Rationale = "Persistent risk pattern across signals.",
                    });
                    actions.Add(new RefundFraudAction
                    {
                        Priority = RefundFraudActionPriority.P1,
                        Code = "REQUEST_EVIDENCE",
                        Description = "Request photo / receipt / disc-condition evidence from customer.",
                        Rationale = "High-risk claim requires verifiable proof before payout.",
                    });
                    break;

                case RefundFraudVerdict.EnhancedReview:
                    actions.Add(new RefundFraudAction
                    {
                        Priority = RefundFraudActionPriority.P0,
                        Code = "SUPERVISOR_REVIEW",
                        Description = "Escalate to supervisor before approval.",
                        Rationale = "Score in enhanced-review band.",
                    });
                    actions.Add(new RefundFraudAction
                    {
                        Priority = RefundFraudActionPriority.P1,
                        Code = "REQUEST_EVIDENCE",
                        Description = "Request supporting evidence (photo, return slip, etc).",
                        Rationale = "Reduce risk on payout decision.",
                    });
                    break;

                case RefundFraudVerdict.StandardReview:
                    actions.Add(new RefundFraudAction
                    {
                        Priority = RefundFraudActionPriority.P1,
                        Code = "STANDARD_REVIEW",
                        Description = "Standard clerk review; approve or deny per policy.",
                        Rationale = "Low-moderate risk.",
                    });
                    break;

                case RefundFraudVerdict.AutoApprove:
                    actions.Add(new RefundFraudAction
                    {
                        Priority = RefundFraudActionPriority.P2,
                        Code = "AUTO_APPROVE_ELIGIBLE",
                        Description = "Eligible for automated approval (small low-risk refund).",
                        Rationale = "Score below standard-review threshold and amount within auto-approve cap.",
                    });
                    break;
            }

            // Signal-specific add-ons.
            if (HasSignal(t, "REPEAT_REASON_PATTERN"))
            {
                actions.Add(new RefundFraudAction
                {
                    Priority = RefundFraudActionPriority.P1,
                    Code = "INVESTIGATE_REASON_PATTERN",
                    Description = $"Investigate repeated '{req.Reason}' claim pattern.",
                    Rationale = "Same reason cited multiple times in recent history.",
                });
            }
            if (HasSignal(t, "NEW_CUSTOMER_FULL_REFUND"))
            {
                actions.Add(new RefundFraudAction
                {
                    Priority = RefundFraudActionPriority.P1,
                    Code = "VERIFY_CUSTOMER_IDENTITY",
                    Description = "Verify new customer's identity before refund payout.",
                    Rationale = "New-account + full-refund is a classic chargeback fraud pattern.",
                });
            }
            if (HasSignal(t, "HIGH_DENIAL_RATE"))
            {
                actions.Add(new RefundFraudAction
                {
                    Priority = RefundFraudActionPriority.P1,
                    Code = "REVIEW_DENIAL_HISTORY",
                    Description = "Review prior denied refunds before deciding this one.",
                    Rationale = "Customer has a high denial-rate pattern.",
                });
            }
            if (rental == null)
            {
                actions.Add(new RefundFraudAction
                {
                    Priority = RefundFraudActionPriority.P0,
                    Code = "MISSING_RENTAL",
                    Description = "Underlying rental record not found - reject or investigate.",
                    Rationale = "Refund must reference an existing rental.",
                });
            }

            // Appetite-specific tweaks: Cautious adds a P2 "double-check" nudge
            // on every actionable case; Aggressive trims redundant evidence asks
            // on borderline standard-review cases.
            if (_config.RiskAppetite == RefundFraudRiskAppetite.Cautious
                && t.Verdict != RefundFraudVerdict.AutoApprove)
            {
                actions.Add(new RefundFraudAction
                {
                    Priority = RefundFraudActionPriority.P2,
                    Code = "SECOND_PAIR_OF_EYES",
                    Description = "Have a second staff member sign off before payout.",
                    Rationale = "Risk-appetite=Cautious.",
                });
            }
            if (_config.RiskAppetite == RefundFraudRiskAppetite.Aggressive
                && t.Verdict == RefundFraudVerdict.StandardReview)
            {
                actions.RemoveAll(a => a.Code == "REQUEST_EVIDENCE");
            }

            return Dedupe(actions);
        }

        // ── Helpers ───────────────────────────────────────────────

        private static void AddSignal(
            RefundFraudTriage t, string code, string desc, int delta, string sev)
        {
            t.Signals.Add(new RefundFraudSignal
            {
                Code = code,
                Description = desc,
                ScoreDelta = delta,
                Severity = sev,
            });
        }

        private static bool HasSignal(RefundFraudTriage t, string code)
        {
            foreach (var s in t.Signals)
                if (s.Code == code) return true;
            return false;
        }

        private static List<RefundFraudAction> Dedupe(List<RefundFraudAction> actions)
        {
            var seen = new HashSet<string>();
            var result = new List<RefundFraudAction>();
            foreach (var a in actions)
            {
                if (seen.Add(a.Code)) result.Add(a);
            }
            return result;
        }

        private string BuildHeadline(RefundFraudTriage t)
        {
            string verdict = t.Verdict.ToString();
            var critical = t.Signals.Where(s => s.Severity == "critical").ToList();
            if (critical.Count > 0)
                return $"{verdict} (score {t.Score}): " + critical[0].Description;
            var warn = t.Signals.Where(s => s.Severity == "warn").ToList();
            if (warn.Count > 0)
                return $"{verdict} (score {t.Score}): " + warn[0].Description;
            return $"{verdict} (score {t.Score})";
        }
    }
}
