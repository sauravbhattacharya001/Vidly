using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages disc damage reports — filing, analytics, customer risk
    /// profiling, and replacement recommendations for the video rental store.
    /// </summary>
    public class DamageReportService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private static readonly List<DamageReport> _reports = new List<DamageReport>();

        private const int RepeatOffenderThreshold = 3;
        private const decimal ReplacementChargeMultiplier = 1.5m;

        private static readonly Dictionary<DiscCondition, decimal> ConditionChargeLookup =
            new Dictionary<DiscCondition, decimal>
            {
                { DiscCondition.Good, 0m },
                { DiscCondition.Fair, 2.99m },
                { DiscCondition.Poor, 7.99m },
                { DiscCondition.Damaged, 14.99m },
                { DiscCondition.Unplayable, 24.99m }
            };

        public DamageReportService(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Files a new damage report, auto-calculating the charge based
        /// on condition degradation.
        /// </summary>
        public DamageReport FileReport(
            int movieId,
            int customerId,
            DiscCondition conditionBefore,
            DiscCondition conditionAfter,
            DamageType damageType,
            string notes = null,
            int? rentalId = null)
        {
            if (conditionAfter <= conditionBefore)
                throw new ArgumentException(
                    "Condition after must be worse than condition before to file a damage report.");

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.", nameof(movieId));

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.", nameof(customerId));

            var charge = CalculateCharge(conditionBefore, conditionAfter, movie);

            var report = new DamageReport
            {
                MovieId = movieId,
                MovieName = movie.Name,
                CustomerId = customerId,
                CustomerName = customer.Name,
                RentalId = rentalId,
                ConditionBefore = conditionBefore,
                ConditionAfter = conditionAfter,
                DamageType = damageType,
                Notes = notes,
                DamageCharge = charge,
                ReportedOn = DateTime.Now
            };

            report.EnsureId();
            _reports.Add(report);
            return report;
        }

        /// <summary>Mark a damage charge as collected.</summary>
        public bool CollectCharge(int reportId)
        {
            var report = _reports.FirstOrDefault(r => r.Id == reportId);
            if (report == null) return false;
            report.ChargeCollected = true;
            return true;
        }

        /// <summary>Mark a damaged disc as replaced.</summary>
        public bool MarkReplaced(int reportId)
        {
            var report = _reports.FirstOrDefault(r => r.Id == reportId);
            if (report == null) return false;
            report.Replaced = true;
            return true;
        }

        /// <summary>Get all damage reports, newest first.</summary>
        public IReadOnlyList<DamageReport> GetAllReports()
            => _reports.OrderByDescending(r => r.ReportedOn).ToList();

        /// <summary>Get damage reports for a specific movie.</summary>
        public IReadOnlyList<DamageReport> GetReportsByMovie(int movieId)
            => _reports.Where(r => r.MovieId == movieId)
                       .OrderByDescending(r => r.ReportedOn).ToList();

        /// <summary>Get damage reports for a specific customer.</summary>
        public IReadOnlyList<DamageReport> GetReportsByCustomer(int customerId)
            => _reports.Where(r => r.CustomerId == customerId)
                       .OrderByDescending(r => r.ReportedOn).ToList();

        /// <summary>Get a single report by id.</summary>
        public DamageReport GetById(int reportId)
            => _reports.FirstOrDefault(r => r.Id == reportId);

        /// <summary>
        /// Generates a damage summary for a specific movie.
        /// </summary>
        public MovieDamageSummary GetMovieDamageSummary(int movieId)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null) return null;

            var movieReports = _reports.Where(r => r.MovieId == movieId).ToList();

            if (!movieReports.Any())
            {
                return new MovieDamageSummary
                {
                    MovieId = movieId,
                    MovieName = movie.Name,
                    CurrentCondition = DiscCondition.Mint,
                    RiskLevel = "Low",
                    NeedsReplacement = false
                };
            }

            var worstCondition = movieReports.Max(r => r.ConditionAfter);
            var mostCommon = movieReports
                .Where(r => r.DamageType != DamageType.None)
                .GroupBy(r => r.DamageType)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            var totalCharges = movieReports.Sum(r => r.DamageCharge);
            var outstanding = movieReports
                .Where(r => !r.ChargeCollected)
                .Sum(r => r.DamageCharge);

            var riskLevel = movieReports.Count >= 5 ? "High"
                          : movieReports.Count >= 2 ? "Medium"
                          : "Low";

            return new MovieDamageSummary
            {
                MovieId = movieId,
                MovieName = movie.Name,
                TotalReports = movieReports.Count,
                TotalCharges = totalCharges,
                OutstandingCharges = outstanding,
                CurrentCondition = worstCondition,
                MostCommonDamage = mostCommon,
                NeedsReplacement = worstCondition >= DiscCondition.Damaged,
                RiskLevel = riskLevel
            };
        }

        /// <summary>Get all movies that need replacement.</summary>
        public IReadOnlyList<MovieDamageSummary> GetMoviesNeedingReplacement()
        {
            var movieIds = _reports.Select(r => r.MovieId).Distinct();
            return movieIds
                .Select(GetMovieDamageSummary)
                .Where(s => s != null && s.NeedsReplacement)
                .OrderByDescending(s => s.TotalReports)
                .ToList();
        }

        /// <summary>
        /// Builds a damage profile for a customer.
        /// </summary>
        public CustomerDamageProfile GetCustomerProfile(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null) return null;

            var customerReports = _reports
                .Where(r => r.CustomerId == customerId).ToList();

            var avgDrop = customerReports.Any()
                ? customerReports.Average(r =>
                    (int)r.ConditionAfter - (int)r.ConditionBefore)
                : 0;

            var unpaid = customerReports
                .Where(r => !r.ChargeCollected)
                .Sum(r => r.DamageCharge);

            var isRepeat = customerReports.Count >= RepeatOffenderThreshold;

            string riskTier;
            if (isRepeat || unpaid > 50m)
                riskTier = "Flagged";
            else if (customerReports.Count >= 2 || unpaid > 20m)
                riskTier = "Watch";
            else
                riskTier = "Good";

            return new CustomerDamageProfile
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                TotalIncidents = customerReports.Count,
                TotalCharges = customerReports.Sum(r => r.DamageCharge),
                UnpaidCharges = unpaid,
                AverageConditionDrop = avgDrop,
                IsRepeatOffender = isRepeat,
                RiskTier = riskTier,
                LastIncident = customerReports
                    .OrderByDescending(r => r.ReportedOn)
                    .Select(r => (DateTime?)r.ReportedOn)
                    .FirstOrDefault()
            };
        }

        /// <summary>Get all flagged (repeat offender) customers.</summary>
        public IReadOnlyList<CustomerDamageProfile> GetFlaggedCustomers()
        {
            var customerIds = _reports.Select(r => r.CustomerId).Distinct();
            return customerIds
                .Select(GetCustomerProfile)
                .Where(p => p != null && p.RiskTier == "Flagged")
                .OrderByDescending(p => p.TotalIncidents)
                .ToList();
        }

        /// <summary>
        /// Generates store-wide damage analytics.
        /// </summary>
        public DamageAnalytics GetAnalytics()
        {
            if (!_reports.Any())
            {
                return new DamageAnalytics
                {
                    MostCommonDamageType = DamageType.None
                };
            }

            var totalCharges = _reports.Sum(r => r.DamageCharge);
            var collected = _reports
                .Where(r => r.ChargeCollected)
                .Sum(r => r.DamageCharge);

            var mostCommon = _reports
                .Where(r => r.DamageType != DamageType.None)
                .GroupBy(r => r.DamageType)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            var replacementNeeded = GetMoviesNeedingReplacement();
            var flagged = GetFlaggedCustomers();

            var mostDamaged = _reports
                .GroupBy(r => r.MovieId)
                .OrderByDescending(g => g.Count())
                .Select(g => GetMovieDamageSummary(g.Key))
                .FirstOrDefault();

            var worstOffender = _reports
                .GroupBy(r => r.CustomerId)
                .OrderByDescending(g => g.Count())
                .Select(g => GetCustomerProfile(g.Key))
                .FirstOrDefault();

            return new DamageAnalytics
            {
                TotalReports = _reports.Count,
                TotalCharges = totalCharges,
                CollectedCharges = collected,
                CollectionRate = totalCharges > 0
                    ? Math.Round(collected / totalCharges * 100, 1)
                    : 0,
                MoviesNeedingReplacement = replacementNeeded.Count,
                RepeatOffenders = flagged.Count,
                MostCommonDamageType = mostCommon,
                MostDamagedMovie = mostDamaged,
                WorstOffender = worstOffender
            };
        }

        private decimal CalculateCharge(
            DiscCondition before, DiscCondition after, Movie movie)
        {
            var baseCharge = ConditionChargeLookup.ContainsKey(after)
                ? ConditionChargeLookup[after]
                : 0m;

            if (ConditionChargeLookup.ContainsKey(before))
                baseCharge -= ConditionChargeLookup[before];

            if (baseCharge < 0) baseCharge = 0;

            if (movie.IsNewRelease)
                baseCharge *= ReplacementChargeMultiplier;

            return Math.Round(baseCharge, 2);
        }

        /// <summary>Reset all in-memory data (for testing).</summary>
        public static void Reset()
        {
            _reports.Clear();
            DamageReport.ResetIdCounter();
        }
    }
}
