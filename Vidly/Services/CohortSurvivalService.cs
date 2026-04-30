using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Customer Cohort Survival Engine — autonomous retention analysis using
    /// Kaplan-Meier survival estimation. Groups customers into monthly cohorts
    /// by join date, tracks rental activity over time, detects retention cliffs,
    /// computes hazard rates, and compares cohort performance.
    /// </summary>
    public class CohortSurvivalService
    {
        private readonly ICustomerRepository _customers;
        private readonly IRentalRepository _rentals;
        private readonly IClock _clock;

        public CohortSurvivalService(
            ICustomerRepository customers,
            IRentalRepository rentals,
            IClock clock)
        {
            _customers = customers;
            _rentals = rentals;
            _clock = clock;
        }

        /// <summary>
        /// Generate the full cohort survival report.
        /// </summary>
        public CohortSurvivalReport GenerateReport()
        {
            var customers = _customers.GetAll();
            var rentals = _rentals.GetAll();
            var now = _clock.UtcNow;

            var cohorts = BuildCohorts(customers, rentals, now);
            var comparisons = CompareCohorts(cohorts);
            var overallHealth = ComputeOverallHealth(cohorts);
            var insights = GenerateInsights(cohorts, comparisons);

            return new CohortSurvivalReport
            {
                GeneratedAt = now,
                Cohorts = cohorts,
                Comparisons = comparisons,
                OverallRetentionHealth = overallHealth,
                Insights = insights
            };
        }

        /// <summary>
        /// Get survival curve for a specific cohort by label (e.g. "2025-01").
        /// </summary>
        public CohortDetail GetCohortDetail(string cohortLabel)
        {
            var customers = _customers.GetAll();
            var rentals = _rentals.GetAll();
            var now = _clock.UtcNow;

            var cohorts = BuildCohorts(customers, rentals, now);
            var cohort = cohorts.FirstOrDefault(c => c.Label == cohortLabel);
            if (cohort == null) return null;

            return new CohortDetail
            {
                Cohort = cohort,
                TopRetainedCustomers = GetTopRetained(cohort, customers, rentals),
                ChurnedCustomers = GetChurned(cohort, customers, rentals, now)
            };
        }

        private List<Cohort> BuildCohorts(
            IReadOnlyList<Customer> customers,
            IReadOnlyList<Rental> rentals,
            DateTime now)
        {
            // Group customers by join month
            var grouped = customers
                .Where(c => c.MemberSince.HasValue)
                .GroupBy(c => new { c.MemberSince.Value.Year, c.MemberSince.Value.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .ToList();

            // Pre-index rentals by customer ID for O(1) per-customer lookup.
            // Eliminates the O(C × R) inner loop per month that previously
            // scanned ALL rentals for EVERY cohort member in EVERY period.
            // For M months, C customers, R rentals this reduces from
            // O(M × C × R) to O(R + M × C × avgRentalsPerCustomer).
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(rentals);

            var cohorts = new List<Cohort>();

            foreach (var group in grouped)
            {
                var label = $"{group.Key.Year:D4}-{group.Key.Month:D2}";
                var cohortStart = new DateTime(group.Key.Year, group.Key.Month, 1);
                var memberIds = group.Select(c => c.Id).ToHashSet();
                var initialSize = memberIds.Count;

                if (initialSize == 0) continue;

                // Calculate months since cohort start
                var monthsElapsed = ((now.Year - cohortStart.Year) * 12) + (now.Month - cohortStart.Month);
                if (monthsElapsed < 0) continue;

                // Build survival curve: for each month, how many are still "active"
                // A customer is "active" in month N if they have at least one rental
                // within that month period from their join date
                var survivalPoints = new List<SurvivalPoint>();
                var atRisk = initialSize;
                var survived = initialSize;

                for (int month = 0; month <= Math.Min(monthsElapsed, 24); month++)
                {
                    var periodStart = cohortStart.AddMonths(month);
                    var periodEnd = cohortStart.AddMonths(month + 1);

                    if (periodStart > now) break;

                    // Count customers who rented in this period using pre-indexed lookup
                    var activeInPeriod = 0;
                    foreach (var id in memberIds)
                    {
                        List<Rental> custRentals;
                        if (rentalsByCustomer.TryGetValue(id, out custRentals))
                        {
                            foreach (var r in custRentals)
                            {
                                if (r.RentalDate >= periodStart && r.RentalDate < periodEnd)
                                {
                                    activeInPeriod++;
                                    break; // only need to find one rental in the period
                                }
                            }
                        }
                    }

                    // For month 0, survival = 1.0 (everyone starts active)
                    if (month == 0)
                    {
                        // activeInPeriod already computed for the first month above
                        var firstMonthActive = activeInPeriod;

                        survivalPoints.Add(new SurvivalPoint
                        {
                            Month = 0,
                            AtRisk = initialSize,
                            Active = firstMonthActive > 0 ? firstMonthActive : initialSize,
                            SurvivalRate = 1.0,
                            HazardRate = 0
                        });
                        survived = firstMonthActive > 0 ? firstMonthActive : initialSize;
                        atRisk = survived;
                        continue;
                    }

                    // Kaplan-Meier: survival(t) = survival(t-1) * (1 - events/atRisk)
                    var events = atRisk - activeInPeriod;
                    if (events < 0) events = 0;
                    if (events > atRisk) events = atRisk;

                    var hazard = atRisk > 0 ? (double)events / atRisk : 0;
                    var prevSurvival = survivalPoints.Last().SurvivalRate;
                    var survival = prevSurvival * (1.0 - hazard);

                    survivalPoints.Add(new SurvivalPoint
                    {
                        Month = month,
                        AtRisk = atRisk,
                        Active = activeInPeriod,
                        SurvivalRate = Math.Round(survival, 4),
                        HazardRate = Math.Round(hazard, 4)
                    });

                    atRisk = activeInPeriod > 0 ? activeInPeriod : Math.Max(1, atRisk - events);
                }

                // Detect retention cliffs (hazard spikes > 2x average)
                var avgHazard = survivalPoints.Where(p => p.Month > 0).Select(p => p.HazardRate).DefaultIfEmpty(0).Average();
                var cliffs = survivalPoints
                    .Where(p => p.Month > 0 && p.HazardRate > avgHazard * 2 && p.HazardRate > 0.1)
                    .Select(p => new RetentionCliff
                    {
                        Month = p.Month,
                        HazardRate = p.HazardRate,
                        Severity = p.HazardRate > 0.5 ? "Critical" : p.HazardRate > 0.3 ? "High" : "Moderate",
                        Interpretation = InterpretCliff(p.Month)
                    })
                    .ToList();

                // Median survival time (month where survival drops below 0.5)
                var medianMonth = survivalPoints.FirstOrDefault(p => p.SurvivalRate < 0.5);

                cohorts.Add(new Cohort
                {
                    Label = label,
                    CohortStart = cohortStart,
                    InitialSize = initialSize,
                    MonthsTracked = survivalPoints.Count,
                    SurvivalCurve = survivalPoints,
                    RetentionCliffs = cliffs,
                    MedianSurvivalMonth = medianMonth?.Month,
                    FinalSurvivalRate = survivalPoints.LastOrDefault()?.SurvivalRate ?? 1.0,
                    HealthGrade = GradeCohort(survivalPoints)
                });
            }

            return cohorts;
        }

        private List<CohortComparison> CompareCohorts(List<Cohort> cohorts)
        {
            if (cohorts.Count < 2) return new List<CohortComparison>();

            var comparisons = new List<CohortComparison>();

            for (int i = 1; i < cohorts.Count; i++)
            {
                var prev = cohorts[i - 1];
                var curr = cohorts[i];

                // Compare at common month depth
                var commonMonths = Math.Min(prev.MonthsTracked, curr.MonthsTracked);
                if (commonMonths < 2) continue;

                var prevRate = prev.SurvivalCurve.ElementAtOrDefault(commonMonths - 1)?.SurvivalRate ?? 0;
                var currRate = curr.SurvivalCurve.ElementAtOrDefault(commonMonths - 1)?.SurvivalRate ?? 0;
                var delta = currRate - prevRate;

                comparisons.Add(new CohortComparison
                {
                    CohortA = prev.Label,
                    CohortB = curr.Label,
                    ComparedAtMonth = commonMonths - 1,
                    SurvivalA = Math.Round(prevRate, 4),
                    SurvivalB = Math.Round(currRate, 4),
                    Delta = Math.Round(delta, 4),
                    Verdict = delta > 0.05 ? "Improving" : delta < -0.05 ? "Declining" : "Stable"
                });
            }

            return comparisons;
        }

        private RetentionHealth ComputeOverallHealth(List<Cohort> cohorts)
        {
            if (!cohorts.Any())
            {
                return new RetentionHealth
                {
                    Score = 0,
                    Grade = "N/A",
                    TotalCustomersTracked = 0,
                    AverageMedianLifespan = 0,
                    TrendDirection = "Unknown",
                    Summary = "No cohort data available."
                };
            }

            var avgFinalSurvival = cohorts.Average(c => c.FinalSurvivalRate);
            var avgMedian = cohorts.Where(c => c.MedianSurvivalMonth.HasValue)
                .Select(c => (double)c.MedianSurvivalMonth.Value)
                .DefaultIfEmpty(12)
                .Average();

            // Score 0-100 based on average survival and median lifespan
            var survivalScore = avgFinalSurvival * 50; // 0-50 from survival rate
            var lifespanScore = Math.Min(avgMedian / 12.0, 1.0) * 50; // 0-50 from lifespan
            var score = (int)Math.Round(survivalScore + lifespanScore);
            score = Math.Max(0, Math.Min(100, score));

            // Trend from recent cohorts
            var recent = cohorts.TakeLast(3).ToList();
            var trend = "Stable";
            if (recent.Count >= 2)
            {
                var firstSurv = recent.First().FinalSurvivalRate;
                var lastSurv = recent.Last().FinalSurvivalRate;
                if (lastSurv > firstSurv + 0.05) trend = "Improving";
                else if (lastSurv < firstSurv - 0.05) trend = "Declining";
            }

            return new RetentionHealth
            {
                Score = score,
                Grade = score >= 80 ? "A" : score >= 60 ? "B" : score >= 40 ? "C" : score >= 20 ? "D" : "F",
                TotalCustomersTracked = cohorts.Sum(c => c.InitialSize),
                AverageMedianLifespan = Math.Round(avgMedian, 1),
                TrendDirection = trend,
                Summary = $"Tracking {cohorts.Count} cohorts. Average retention grade: {(avgFinalSurvival * 100):F0}% at final observation. Trend: {trend}."
            };
        }

        private List<string> GenerateInsights(List<Cohort> cohorts, List<CohortComparison> comparisons)
        {
            var insights = new List<string>();

            // Best performing cohort
            var best = cohorts.OrderByDescending(c => c.FinalSurvivalRate).FirstOrDefault();
            if (best != null)
                insights.Add($"Best performing cohort: {best.Label} ({best.FinalSurvivalRate * 100:F0}% survival after {best.MonthsTracked} months).");

            // Worst performing cohort
            var worst = cohorts.OrderBy(c => c.FinalSurvivalRate).FirstOrDefault();
            if (worst != null && worst.Label != best?.Label)
                insights.Add($"Weakest cohort: {worst.Label} ({worst.FinalSurvivalRate * 100:F0}% survival) — investigate onboarding quality for this period.");

            // Common cliff months
            var allCliffs = cohorts.SelectMany(c => c.RetentionCliffs).ToList();
            if (allCliffs.Any())
            {
                var commonCliffMonth = allCliffs.GroupBy(c => c.Month)
                    .OrderByDescending(g => g.Count())
                    .First();
                insights.Add($"Month {commonCliffMonth.Key} is a recurring retention cliff (seen in {commonCliffMonth.Count()} cohorts). Consider proactive engagement at this stage.");
            }

            // Trend insight
            var declining = comparisons.Count(c => c.Verdict == "Declining");
            var improving = comparisons.Count(c => c.Verdict == "Improving");
            if (declining > improving && declining > 1)
                insights.Add("⚠️ Retention is trending downward across recent cohorts. Prioritize engagement and re-activation campaigns.");
            else if (improving > declining && improving > 1)
                insights.Add("✅ Retention is improving across recent cohorts. Current strategies appear effective.");

            // Median lifespan warning
            var shortLived = cohorts.Where(c => c.MedianSurvivalMonth.HasValue && c.MedianSurvivalMonth.Value <= 2).ToList();
            if (shortLived.Any())
                insights.Add($"{shortLived.Count} cohort(s) have median survival ≤ 2 months — early churn is a critical issue.");

            if (!insights.Any())
                insights.Add("Insufficient data to generate meaningful insights. More rental history will improve analysis.");

            return insights;
        }

        private List<RetainedCustomerInfo> GetTopRetained(Cohort cohort, IReadOnlyList<Customer> customers, IReadOnlyList<Rental> rentals)
        {
            // Pre-index rentals by customer for O(C + R) instead of O(C × R)
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(rentals);

            var cohortCustomers = customers
                .Where(c => c.MemberSince.HasValue &&
                            c.MemberSince.Value.Year == cohort.CohortStart.Year &&
                            c.MemberSince.Value.Month == cohort.CohortStart.Month)
                .ToList();

            return cohortCustomers
                .Select(c => {
                    List<Rental> custRentals;
                    rentalsByCustomer.TryGetValue(c.Id, out custRentals);
                    var list = custRentals ?? new List<Rental>();
                    return new
                    {
                        Customer = c,
                        RentalCount = list.Count,
                        LastRental = list.Count > 0
                            ? list.OrderByDescending(r => r.RentalDate).First().RentalDate
                            : (DateTime?)null
                    };
                })
                .OrderByDescending(x => x.RentalCount)
                .Take(5)
                .Select(x => new RetainedCustomerInfo
                {
                    CustomerId = x.Customer.Id,
                    Name = x.Customer.Name,
                    TotalRentals = x.RentalCount,
                    LastRentalDate = x.LastRental
                })
                .ToList();
        }

        private List<ChurnedCustomerInfo> GetChurned(Cohort cohort, IReadOnlyList<Customer> customers, IReadOnlyList<Rental> rentals, DateTime now)
        {
            // Pre-index rentals by customer for O(C + R) instead of O(C × R)
            var rentalsByCustomer = CustomerRentalAnalytics.BuildRentalsByCustomer(rentals);

            var cohortCustomers = customers
                .Where(c => c.MemberSince.HasValue &&
                            c.MemberSince.Value.Year == cohort.CohortStart.Year &&
                            c.MemberSince.Value.Month == cohort.CohortStart.Month)
                .ToList();

            var inactiveThreshold = now.AddMonths(-3);

            return cohortCustomers
                .Select(c => {
                    List<Rental> custRentals;
                    rentalsByCustomer.TryGetValue(c.Id, out custRentals);
                    var list = custRentals ?? new List<Rental>();
                    return new
                    {
                        Customer = c,
                        RentalCount = list.Count,
                        LastRental = list.Count > 0
                            ? list.OrderByDescending(r => r.RentalDate).First().RentalDate
                            : (DateTime?)null
                    };
                })
                .Where(x => !x.LastRental.HasValue || x.LastRental.Value < inactiveThreshold)
                .OrderBy(x => x.LastRental ?? DateTime.MinValue)
                .Take(10)
                .Select(x => new ChurnedCustomerInfo
                {
                    CustomerId = x.Customer.Id,
                    Name = x.Customer.Name,
                    TotalRentals = x.RentalCount,
                    LastRentalDate = x.LastRental,
                    DaysSinceLastRental = x.LastRental.HasValue
                        ? (int)(now - x.LastRental.Value).TotalDays
                        : -1
                })
                .ToList();
        }

        private string InterpretCliff(int month)
        {
            if (month == 1) return "First-month drop-off: customers who tried the service once and didn't return.";
            if (month == 2) return "Second-month fade: novelty wore off. Consider a month-2 engagement campaign.";
            if (month == 3) return "Quarter boundary: customers evaluating whether to continue. Offer incentives.";
            if (month <= 6) return "Mid-term attrition: engagement declining. Loyalty programs or personalized recommendations may help.";
            if (month <= 12) return "Long-term drift: even engaged customers lose interest. Refresh catalog and offerings.";
            return "Extended tenure drop: very long-term customers leaving. Investigate service quality or life changes.";
        }

        private string GradeCohort(List<SurvivalPoint> curve)
        {
            if (curve.Count < 2) return "N/A";
            var finalSurvival = curve.Last().SurvivalRate;
            var months = curve.Count;

            // Normalize: 6-month cohort with 80% survival is better than 12-month with 80%
            // Use survival per month as quality indicator
            var monthlyDecay = months > 1 ? (1.0 - finalSurvival) / (months - 1) : 0;

            if (monthlyDecay <= 0.02) return "A+";
            if (monthlyDecay <= 0.05) return "A";
            if (monthlyDecay <= 0.08) return "B";
            if (monthlyDecay <= 0.12) return "C";
            if (monthlyDecay <= 0.18) return "D";
            return "F";
        }
    }

    #region Models

    public class CohortSurvivalReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<Cohort> Cohorts { get; set; }
        public List<CohortComparison> Comparisons { get; set; }
        public RetentionHealth OverallRetentionHealth { get; set; }
        public List<string> Insights { get; set; }
    }

    public class Cohort
    {
        public string Label { get; set; }
        public DateTime CohortStart { get; set; }
        public int InitialSize { get; set; }
        public int MonthsTracked { get; set; }
        public List<SurvivalPoint> SurvivalCurve { get; set; }
        public List<RetentionCliff> RetentionCliffs { get; set; }
        public int? MedianSurvivalMonth { get; set; }
        public double FinalSurvivalRate { get; set; }
        public string HealthGrade { get; set; }
    }

    public class SurvivalPoint
    {
        public int Month { get; set; }
        public int AtRisk { get; set; }
        public int Active { get; set; }
        public double SurvivalRate { get; set; }
        public double HazardRate { get; set; }
    }

    public class RetentionCliff
    {
        public int Month { get; set; }
        public double HazardRate { get; set; }
        public string Severity { get; set; }
        public string Interpretation { get; set; }
    }

    public class CohortComparison
    {
        public string CohortA { get; set; }
        public string CohortB { get; set; }
        public int ComparedAtMonth { get; set; }
        public double SurvivalA { get; set; }
        public double SurvivalB { get; set; }
        public double Delta { get; set; }
        public string Verdict { get; set; }
    }

    public class RetentionHealth
    {
        public int Score { get; set; }
        public string Grade { get; set; }
        public int TotalCustomersTracked { get; set; }
        public double AverageMedianLifespan { get; set; }
        public string TrendDirection { get; set; }
        public string Summary { get; set; }
    }

    public class CohortDetail
    {
        public Cohort Cohort { get; set; }
        public List<RetainedCustomerInfo> TopRetainedCustomers { get; set; }
        public List<ChurnedCustomerInfo> ChurnedCustomers { get; set; }
    }

    public class RetainedCustomerInfo
    {
        public int CustomerId { get; set; }
        public string Name { get; set; }
        public int TotalRentals { get; set; }
        public DateTime? LastRentalDate { get; set; }
    }

    public class ChurnedCustomerInfo
    {
        public int CustomerId { get; set; }
        public string Name { get; set; }
        public int TotalRentals { get; set; }
        public DateTime? LastRentalDate { get; set; }
        public int DaysSinceLastRental { get; set; }
    }

    #endregion
}
