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
    /// Risk band for a single active rental's projected return condition.
    /// </summary>
    public enum DamageRiskBand
    {
        Minimal,    // 0-19
        Low,        // 20-39
        Moderate,   // 40-59
        Elevated,   // 60-79
        High        // 80-100
    }

    /// <summary>
    /// Priority bucket for preventive actions.
    /// </summary>
    public enum DamagePreventionPriority { P0, P1, P2 }

    /// <summary>
    /// Risk-appetite knob that modulates how aggressively the forecaster
    /// raises risk scores and emits preventive actions.
    /// </summary>
    public enum DamageRiskAppetite
    {
        /// <summary>Bias toward over-protecting inventory. Adds extra friction.</summary>
        Cautious,
        /// <summary>Default behavior.</summary>
        Balanced,
        /// <summary>Bias toward fewer interventions; only surface clear risks.</summary>
        Aggressive
    }

    /// <summary>
    /// Tunable thresholds. Defaults are reasonable for a small video-rental store.
    /// All knobs are intentionally explicit so the service stays testable.
    /// </summary>
    public class DamageRiskForecastConfig
    {
        /// <summary>How far back to look at a customer's damage history.</summary>
        public int HistoryWindowDays { get; set; } = 365;

        /// <summary>Trailing window for "recent store-wide damage trend" signal.</summary>
        public int RecentTrendWindowDays { get; set; } = 30;

        /// <summary>
        /// Minimum damage reports in the recent trend window for the
        /// "store-wide damage spike" insight to fire.
        /// </summary>
        public int StoreWideDamageSpikeMin { get; set; } = 3;

        /// <summary>
        /// Score above which a rental is treated as P0 actionable.
        /// </summary>
        public int P0Threshold { get; set; } = 75;

        /// <summary>Score above which a rental is treated as P1.</summary>
        public int P1Threshold { get; set; } = 50;

        /// <summary>Risk-appetite knob.</summary>
        public DamageRiskAppetite RiskAppetite { get; set; } = DamageRiskAppetite.Balanced;

        /// <summary>
        /// Daily-rate floor above which the rental is considered "high-value"
        /// (triggers stronger deposit-hold language).
        /// </summary>
        public decimal HighValueDailyRate { get; set; } = 5.00m;
    }

    /// <summary>
    /// Structured signal contributing to a single rental's risk score.
    /// </summary>
    public class DamageRiskSignal
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public int Contribution { get; set; }
    }

    /// <summary>
    /// Per-rental projection: how likely is this rental to come back damaged,
    /// and what should we do about it before it returns?
    /// </summary>
    public class RentalDamageForecast
    {
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public DateTime DueDate { get; set; }
        public int DaysUntilDue { get; set; }
        public int RiskScore { get; set; }
        public DamageRiskBand Band { get; set; }
        public DamagePreventionPriority Priority { get; set; }
        public List<DamageRiskSignal> Signals { get; set; } = new List<DamageRiskSignal>();
        public List<string> RecommendedActions { get; set; } = new List<string>();
        /// <summary>Human-readable one-line summary used in renderers.</summary>
        public string Headline { get; set; }
    }

    /// <summary>
    /// Single preventive action in the catalogue-wide playbook.
    /// </summary>
    public class DamagePreventionAction
    {
        public DamagePreventionPriority Priority { get; set; }
        public string Action { get; set; }
        public string Owner { get; set; }
        public int RentalCount { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Top-level forecast report covering all active rentals.
    /// </summary>
    public class DamageRiskForecastReport
    {
        public DateTime AsOfDate { get; set; }
        public int ActiveRentals { get; set; }
        public int P0Count { get; set; }
        public int P1Count { get; set; }
        public int P2Count { get; set; }
        /// <summary>Weighted portfolio risk 0-100 (mean of per-rental scores).</summary>
        public int PortfolioRisk { get; set; }
        /// <summary>A-F portfolio health grade.</summary>
        public string PortfolioGrade { get; set; }
        public List<RentalDamageForecast> Forecasts { get; set; } = new List<RentalDamageForecast>();
        public List<DamagePreventionAction> Playbook { get; set; } = new List<DamagePreventionAction>();
        public List<string> Insights { get; set; } = new List<string>();
    }

    // ── Service ────────────────────────────────────────────────────

    /// <summary>
    /// Agentic forward-looking damage-risk forecaster.
    ///
    /// For every active (non-returned) rental it cross-references:
    ///   • per-customer damage history within HistoryWindowDays
    ///     (count, severity mix, total fees, waiver rate)
    ///   • rental duration so far + overdue pressure
    ///   • movie attributes (genre baseline damage rate, high daily-rate)
    ///   • customer membership tier (loyalty discount on risk)
    ///   • store-wide recent damage trend (catalogue-level multiplier)
    ///
    /// and emits a 0-100 RiskScore + band + structured Signals + ranked
    /// preventive actions. The catalogue-wide Playbook deduplicates actions
    /// across rentals and surfaces the highest-leverage interventions
    /// (P0 = act now, P1 = this week, P2 = passive monitoring).
    ///
    /// Pure read-only analysis. Deterministic for a given (rentals, damage
    /// history, customers, movies, asOfDate, config). No mutation. Sits next
    /// to ReviewIntelligenceService, ChurnPredictorService, AnomalyWatchdog,
    /// and LateReturnPredictor in the agentic-services family.
    /// </summary>
    public class DamageRiskForecastService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IDamageRepository _damageRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;
        private readonly DamageRiskForecastConfig _config;

        public DamageRiskForecastService(
            IRentalRepository rentalRepo,
            IDamageRepository damageRepo,
            ICustomerRepository customerRepo,
            IMovieRepository movieRepo,
            IClock clock,
            DamageRiskForecastConfig config = null)
        {
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _damageRepo = damageRepo ?? throw new ArgumentNullException(nameof(damageRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? new DamageRiskForecastConfig();
        }

        // ── Genre damage baselines (per-rental damage rate priors) ──
        //
        // Hand-tuned priors: action/horror/adventure titles ship in cases that
        // tend to come back chipped or scratched (high-action handling, kids
        // movies, late-night sessions). Documentaries/romance get treated
        // gently. Range 0..15 contribution.
        private static readonly Dictionary<Genre, int> GenreBaseline = new Dictionary<Genre, int>
        {
            { Genre.Action,      10 },
            { Genre.Adventure,    9 },
            { Genre.Horror,       8 },
            { Genre.SciFi,        7 },
            { Genre.Thriller,     7 },
            { Genre.Animation,   12 }, // kids handling
            { Genre.Comedy,       6 },
            { Genre.Drama,        4 },
            { Genre.Romance,      3 },
            { Genre.Documentary,  3 },
        };

        /// <summary>
        /// Produce a damage-risk forecast covering every active rental as of
        /// the given date.
        /// </summary>
        public DamageRiskForecastReport Forecast(DateTime? asOfDate = null)
        {
            var today = (asOfDate ?? _clock.Today).Date;

            var allRentals = _rentalRepo.GetAll();
            var activeRentals = allRentals
                .Where(r => r.Status != RentalStatus.Returned)
                .ToList();

            var allDamage = _damageRepo.GetAll().ToList();
            var historyCutoff = today.AddDays(-_config.HistoryWindowDays);
            var historyDamage = allDamage
                .Where(d => d.ReportedAt.Date >= historyCutoff)
                .ToList();

            // Index by customer for O(1) lookup
            var damageByCustomer = historyDamage
                .GroupBy(d => d.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Store-wide trend multiplier
            var trendCutoff = today.AddDays(-_config.RecentTrendWindowDays);
            var recentDamageCount = historyDamage.Count(d => d.ReportedAt.Date >= trendCutoff);
            var storeWideSpike = recentDamageCount >= _config.StoreWideDamageSpikeMin;

            var forecasts = new List<RentalDamageForecast>(activeRentals.Count);
            foreach (var rental in activeRentals)
            {
                var customer = _customerRepo.GetById(rental.CustomerId);
                var movie = _movieRepo.GetById(rental.MovieId);
                List<DamageReport> customerHistory;
                if (!damageByCustomer.TryGetValue(rental.CustomerId, out customerHistory))
                    customerHistory = new List<DamageReport>();

                var forecast = ScoreSingleRental(
                    rental, customer, movie, customerHistory, storeWideSpike, today);
                forecasts.Add(forecast);
            }

            // Stable ordering: highest risk first, then earliest due date.
            forecasts = forecasts
                .OrderByDescending(f => f.RiskScore)
                .ThenBy(f => f.DueDate)
                .ThenBy(f => f.RentalId)
                .ToList();

            var report = new DamageRiskForecastReport
            {
                AsOfDate = today,
                ActiveRentals = activeRentals.Count,
                Forecasts = forecasts,
                P0Count = forecasts.Count(f => f.Priority == DamagePreventionPriority.P0),
                P1Count = forecasts.Count(f => f.Priority == DamagePreventionPriority.P1),
                P2Count = forecasts.Count(f => f.Priority == DamagePreventionPriority.P2),
                PortfolioRisk = forecasts.Count == 0
                    ? 0
                    : (int)Math.Round(forecasts.Average(f => (double)f.RiskScore)),
            };
            report.PortfolioGrade = GradeFromScore(report.PortfolioRisk, report.P0Count);
            report.Playbook = BuildCatalogPlaybook(forecasts);
            report.Insights = BuildInsights(report, storeWideSpike, recentDamageCount);

            return report;
        }

        // ── Scoring ────────────────────────────────────────────────

        private RentalDamageForecast ScoreSingleRental(
            Rental rental,
            Customer customer,
            Movie movie,
            List<DamageReport> customerHistory,
            bool storeWideSpike,
            DateTime today)
        {
            var signals = new List<DamageRiskSignal>();
            int score = 0;

            // Baseline damage risk for ANY active rental.
            score += 5;
            signals.Add(new DamageRiskSignal
            {
                Code = "BASELINE",
                Description = "Baseline per-rental damage risk",
                Contribution = 5,
            });

            // 1) Customer history contribution.
            if (customerHistory.Count > 0)
            {
                int historyContribution = 0;
                int severeCount = customerHistory.Count(d =>
                    d.Severity == DamageSeverity.Severe ||
                    d.Severity == DamageSeverity.Destroyed);
                int moderateCount = customerHistory.Count(d => d.Severity == DamageSeverity.Moderate);
                int minorCount = customerHistory.Count(d => d.Severity == DamageSeverity.Minor);

                historyContribution += Math.Min(severeCount * 18, 45);
                historyContribution += Math.Min(moderateCount * 10, 25);
                historyContribution += Math.Min(minorCount * 4, 15);

                historyContribution = Math.Min(historyContribution, 55);
                score += historyContribution;
                signals.Add(new DamageRiskSignal
                {
                    Code = "CUSTOMER_HISTORY",
                    Description = $"{customerHistory.Count} prior damage report(s) " +
                                  $"(severe={severeCount}, moderate={moderateCount}, minor={minorCount})",
                    Contribution = historyContribution,
                });

                // Waiver-rate signal: a high waiver rate means we've been lenient before
                // and the behavior may not have been corrected. Worth surfacing.
                int waivedCount = customerHistory.Count(d => d.Status == DamageStatus.Waived);
                if (waivedCount >= 2 && (double)waivedCount / customerHistory.Count >= 0.5)
                {
                    score += 8;
                    signals.Add(new DamageRiskSignal
                    {
                        Code = "HIGH_WAIVER_RATE",
                        Description = $"{waivedCount}/{customerHistory.Count} prior damages were waived",
                        Contribution = 8,
                    });
                }
            }

            // 2) Rental duration + overdue pressure.
            var daysOut = Math.Max(0, (today - rental.RentalDate.Date).Days);
            if (daysOut >= 7)
            {
                var durationContrib = Math.Min(2 + (daysOut - 7), 10);
                score += durationContrib;
                signals.Add(new DamageRiskSignal
                {
                    Code = "LONG_RENTAL",
                    Description = $"Rental has been out {daysOut} days",
                    Contribution = durationContrib,
                });
            }
            if (rental.Status == RentalStatus.Overdue || today > rental.DueDate.Date)
            {
                var overdueDays = Math.Max(1, (today - rental.DueDate.Date).Days);
                var overdueContrib = Math.Min(5 + overdueDays * 2, 20);
                score += overdueContrib;
                signals.Add(new DamageRiskSignal
                {
                    Code = "OVERDUE",
                    Description = $"Overdue by {overdueDays} day(s)",
                    Contribution = overdueContrib,
                });
            }

            // 3) Movie attributes.
            if (movie != null && movie.Genre.HasValue
                && GenreBaseline.TryGetValue(movie.Genre.Value, out var genreBase))
            {
                score += genreBase;
                signals.Add(new DamageRiskSignal
                {
                    Code = "GENRE_BASELINE",
                    Description = $"Genre={movie.Genre.Value} baseline damage rate",
                    Contribution = genreBase,
                });
            }
            if (rental.DailyRate >= _config.HighValueDailyRate)
            {
                score += 4;
                signals.Add(new DamageRiskSignal
                {
                    Code = "HIGH_VALUE_TITLE",
                    Description = $"High-value title (daily rate ${rental.DailyRate:0.00})",
                    Contribution = 4,
                });
            }

            // 4) Membership tier modulates risk down for loyal customers
            //    (they've shown long-term good behavior; assume baseline).
            if (customer != null)
            {
                int loyaltyDelta = 0;
                switch (customer.MembershipType)
                {
                    case MembershipType.Platinum: loyaltyDelta = -10; break;
                    case MembershipType.Gold:     loyaltyDelta = -5; break;
                    case MembershipType.Silver:   loyaltyDelta = -2; break;
                }
                if (loyaltyDelta != 0)
                {
                    score += loyaltyDelta;
                    signals.Add(new DamageRiskSignal
                    {
                        Code = "LOYALTY_DISCOUNT",
                        Description = $"{customer.MembershipType} member loyalty adjustment",
                        Contribution = loyaltyDelta,
                    });
                }
            }

            // 5) Store-wide damage spike multiplier (small additive nudge).
            if (storeWideSpike)
            {
                score += 5;
                signals.Add(new DamageRiskSignal
                {
                    Code = "STORE_WIDE_SPIKE",
                    Description = "Catalogue-wide damage trend elevated",
                    Contribution = 5,
                });
            }

            // 6) Risk-appetite modulation.
            int appetiteDelta = 0;
            switch (_config.RiskAppetite)
            {
                case DamageRiskAppetite.Cautious:   appetiteDelta =  6; break;
                case DamageRiskAppetite.Aggressive: appetiteDelta = -6; break;
            }
            if (appetiteDelta != 0)
            {
                score += appetiteDelta;
                signals.Add(new DamageRiskSignal
                {
                    Code = "RISK_APPETITE",
                    Description = $"Risk appetite={_config.RiskAppetite}",
                    Contribution = appetiteDelta,
                });
            }

            score = Math.Max(0, Math.Min(100, score));

            var band = BandFromScore(score);
            var priority = score >= _config.P0Threshold ? DamagePreventionPriority.P0
                           : score >= _config.P1Threshold ? DamagePreventionPriority.P1
                           : DamagePreventionPriority.P2;

            var daysUntilDue = (rental.DueDate.Date - today).Days;
            var forecast = new RentalDamageForecast
            {
                RentalId = rental.Id,
                CustomerId = rental.CustomerId,
                CustomerName = rental.CustomerName ?? customer?.Name,
                MovieId = rental.MovieId,
                MovieName = rental.MovieName ?? movie?.Name,
                DueDate = rental.DueDate.Date,
                DaysUntilDue = daysUntilDue,
                RiskScore = score,
                Band = band,
                Priority = priority,
                Signals = signals,
            };
            forecast.RecommendedActions = BuildActionsForRental(forecast, rental, customer);
            forecast.Headline = BuildHeadline(forecast);
            return forecast;
        }

        // ── Per-rental action menu ─────────────────────────────────

        private List<string> BuildActionsForRental(
            RentalDamageForecast forecast, Rental rental, Customer customer)
        {
            var actions = new List<string>();
            switch (forecast.Priority)
            {
                case DamagePreventionPriority.P0:
                    actions.Add("Call customer now: verify item condition and expected return");
                    actions.Add("Place a refundable deposit hold matching daily rate");
                    actions.Add("Offer one-click rental insurance at checkout reminder");
                    if (forecast.DaysUntilDue <= 0)
                        actions.Add("Trigger overdue-recovery flow with photo-on-return requirement");
                    break;
                case DamagePreventionPriority.P1:
                    actions.Add("SMS reminder 24h before due date with handling tips");
                    actions.Add("Suggest rental insurance upsell in next checkout");
                    if (rental.DailyRate >= _config.HighValueDailyRate)
                        actions.Add("Require condition photo at return (high-value title)");
                    break;
                case DamagePreventionPriority.P2:
                    actions.Add("Passive monitor — include in weekly damage-trend dashboard");
                    break;
            }
            return actions;
        }

        // ── Catalogue-wide playbook (dedup + count) ────────────────

        private List<DamagePreventionAction> BuildCatalogPlaybook(List<RentalDamageForecast> forecasts)
        {
            // Group identical action strings, attributing the highest priority
            // and counting the rentals impacted.
            var grouped = new Dictionary<string, (DamagePreventionPriority p, int count, string reason)>(
                StringComparer.Ordinal);
            foreach (var f in forecasts)
            {
                foreach (var a in f.RecommendedActions)
                {
                    if (grouped.TryGetValue(a, out var prev))
                    {
                        var bumped = (DamagePreventionPriority)Math.Min((int)prev.p, (int)f.Priority);
                        grouped[a] = (bumped, prev.count + 1, prev.reason);
                    }
                    else
                    {
                        grouped[a] = (f.Priority, 1, AssignOwner(a));
                    }
                }
            }

            return grouped
                .Select(kv => new DamagePreventionAction
                {
                    Priority = kv.Value.p,
                    Action = kv.Key,
                    Owner = AssignOwner(kv.Key),
                    RentalCount = kv.Value.count,
                    Reason = $"Applies to {kv.Value.count} active rental(s)",
                })
                .OrderBy(a => (int)a.Priority)
                .ThenByDescending(a => a.RentalCount)
                .ThenBy(a => a.Action, StringComparer.Ordinal)
                .ToList();
        }

        private static string AssignOwner(string action)
        {
            if (action.StartsWith("Call ", StringComparison.OrdinalIgnoreCase)) return "store_manager";
            if (action.IndexOf("deposit", StringComparison.OrdinalIgnoreCase) >= 0) return "billing";
            if (action.IndexOf("insurance", StringComparison.OrdinalIgnoreCase) >= 0) return "front_desk";
            if (action.StartsWith("SMS ", StringComparison.OrdinalIgnoreCase)) return "marketing_automation";
            if (action.IndexOf("photo", StringComparison.OrdinalIgnoreCase) >= 0) return "returns_desk";
            if (action.StartsWith("Trigger ", StringComparison.OrdinalIgnoreCase)) return "ops_automation";
            return "ops_automation";
        }

        // ── Cross-rental autonomous insights ───────────────────────

        private List<string> BuildInsights(
            DamageRiskForecastReport report, bool storeWideSpike, int recentDamageCount)
        {
            var insights = new List<string>();
            if (report.ActiveRentals == 0)
            {
                insights.Add("No active rentals — nothing to forecast.");
                return insights;
            }
            if (report.P0Count > 0)
                insights.Add($"{report.P0Count} P0 rental(s) need immediate attention.");
            if (storeWideSpike)
                insights.Add($"Store-wide damage spike: {recentDamageCount} reports in the last " +
                             $"{_config.RecentTrendWindowDays} days — review handling SOPs.");
            // Cluster: same customer with multiple high-risk active rentals.
            var multiActive = report.Forecasts
                .Where(f => f.Priority != DamagePreventionPriority.P2)
                .GroupBy(f => f.CustomerId)
                .Where(g => g.Count() >= 2)
                .ToList();
            if (multiActive.Count > 0)
                insights.Add($"{multiActive.Count} customer(s) hold 2+ at-risk active rentals — " +
                             "consider per-customer cap or pre-emptive call.");
            // Portfolio risk band insight.
            if (report.PortfolioRisk >= 60)
                insights.Add("Portfolio damage risk is elevated — recommend tightening " +
                             "return-photo policy this week.");
            else if (report.PortfolioRisk <= 20)
                insights.Add("Portfolio damage risk is calm — safe to relax photo-on-return policy.");
            return insights;
        }

        // ── Bands, grades, headlines ───────────────────────────────

        private static DamageRiskBand BandFromScore(int score)
        {
            if (score >= 80) return DamageRiskBand.High;
            if (score >= 60) return DamageRiskBand.Elevated;
            if (score >= 40) return DamageRiskBand.Moderate;
            if (score >= 20) return DamageRiskBand.Low;
            return DamageRiskBand.Minimal;
        }

        private static string GradeFromScore(int portfolioRisk, int p0Count)
        {
            if (p0Count >= 5 || portfolioRisk >= 80) return "F";
            if (p0Count >= 2 || portfolioRisk >= 65) return "D";
            if (portfolioRisk >= 50) return "C";
            if (portfolioRisk >= 30) return "B";
            return "A";
        }

        private static string BuildHeadline(RentalDamageForecast f)
        {
            var dueText = f.DaysUntilDue < 0
                ? $"overdue by {-f.DaysUntilDue}d"
                : f.DaysUntilDue == 0 ? "due today" : $"due in {f.DaysUntilDue}d";
            return $"[{f.Priority}] {f.CustomerName ?? "Customer"} / " +
                   $"{f.MovieName ?? "Movie"} — risk {f.RiskScore}/100 ({f.Band}, {dueText})";
        }

        // ── Renderers ──────────────────────────────────────────────

        /// <summary>
        /// Render the report as a paste-into-Slack plain-text brief.
        /// </summary>
        public string FormatText(DamageRiskForecastReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine($"DAMAGE RISK FORECAST  ({report.AsOfDate:yyyy-MM-dd})");
            sb.AppendLine($"Active rentals: {report.ActiveRentals}");
            sb.AppendLine($"Portfolio risk: {report.PortfolioRisk}/100  Grade: {report.PortfolioGrade}");
            sb.AppendLine($"P0/P1/P2: {report.P0Count}/{report.P1Count}/{report.P2Count}");
            if (report.Insights.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("INSIGHTS");
                foreach (var ins in report.Insights) sb.AppendLine($"  • {ins}");
            }
            if (report.Forecasts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("TOP AT-RISK RENTALS");
                foreach (var f in report.Forecasts.Take(10))
                    sb.AppendLine($"  • {f.Headline}");
            }
            if (report.Playbook.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("PLAYBOOK");
                foreach (var a in report.Playbook)
                    sb.AppendLine($"  [{a.Priority}] {a.Action}  (owner={a.Owner}, x{a.RentalCount})");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Render the report as Markdown for dashboards / email digests.
        /// </summary>
        public string FormatMarkdown(DamageRiskForecastReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.AppendLine($"# Damage Risk Forecast — {report.AsOfDate:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine($"- Active rentals: **{report.ActiveRentals}**");
            sb.AppendLine($"- Portfolio risk: **{report.PortfolioRisk}/100** (grade **{report.PortfolioGrade}**)");
            sb.AppendLine($"- P0 / P1 / P2: **{report.P0Count}** / **{report.P1Count}** / **{report.P2Count}**");
            if (report.Insights.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Insights");
                foreach (var ins in report.Insights) sb.AppendLine($"- {ins}");
            }
            if (report.Forecasts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Top At-Risk Rentals");
                foreach (var f in report.Forecasts.Take(10))
                    sb.AppendLine($"- {f.Headline}");
            }
            if (report.Playbook.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Playbook");
                foreach (var a in report.Playbook)
                    sb.AppendLine($"- **[{a.Priority}]** {a.Action} — owner `{a.Owner}` · applies to {a.RentalCount} rental(s)");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
