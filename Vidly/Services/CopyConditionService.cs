using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Tracks the physical condition of movie copies across their rental
    /// lifecycle. Records disc and case condition at checkout and return,
    /// calculates deterioration rates, flags copies needing replacement,
    /// and identifies high-risk renters who frequently return damaged items.
    ///
    /// Usage:
    ///   var service = new CopyConditionService(movieRepo, rentalRepo);
    ///   service.RecordCheckout(movieId, rentalId, customerId,
    ///       ConditionGrade.Good, ConditionGrade.Good, "Staff A");
    ///   service.RecordReturn(movieId, rentalId, customerId,
    ///       ConditionGrade.Fair, ConditionGrade.Good, "Staff B",
    ///       "Light scratches on disc surface");
    ///   var report = service.GenerateReport();
    /// </summary>
    public class CopyConditionService
    {
        private readonly List<ConditionInspection> _inspections = new List<ConditionInspection>();
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly IClock _clock;
        private int _nextId = 1;

        /// <summary>
        /// Damage rate threshold: customers returning items in worse
        /// condition more than this fraction of the time are flagged.
        /// </summary>
        public const double HighRiskDamageRate = 0.30;

        /// <summary>
        /// Medium risk threshold.
        /// </summary>
        public const double MediumRiskDamageRate = 0.15;

        /// <summary>
        /// Maximum number of worst copies in the report.
        /// </summary>
        public const int MaxWorstCopies = 10;

        public CopyConditionService(IMovieRepository movieRepo,
                                     IRentalRepository rentalRepo,
            IClock clock = null)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _clock = clock ?? new SystemClock();
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _clock = clock ?? new SystemClock();
        }

        // ── Recording inspections ───────────────────────────────────

        /// <summary>
        /// Record the condition of a movie copy at checkout time.
        /// </summary>
        public ConditionInspection RecordCheckout(int movieId, int rentalId,
            int customerId, ConditionGrade discGrade, ConditionGrade caseGrade,
            string inspectorName, string notes = null)
        {
            ValidateMovie(movieId);
            ValidateRental(rentalId);
            ValidateGrade(discGrade, nameof(discGrade));
            ValidateGrade(caseGrade, nameof(caseGrade));
            if (string.IsNullOrWhiteSpace(inspectorName))
                throw new ArgumentException("Inspector name is required.", nameof(inspectorName));

            // Prevent duplicate checkout inspections for the same rental
            if (_inspections.Any(i => i.RentalId == rentalId && i.Type == InspectionType.Checkout))
                throw new InvalidOperationException(
                    $"Checkout inspection already recorded for rental {rentalId}.");

            var inspection = new ConditionInspection
            {
                Id = _nextId++,
                MovieId = movieId,
                RentalId = rentalId,
                CustomerId = customerId,
                Type = InspectionType.Checkout,
                DiscGrade = discGrade,
                CaseGrade = caseGrade,
                Notes = notes,
                InspectorName = inspectorName,
                InspectedAt = _clock.Now
            };
            _inspections.Add(inspection);
            return inspection;
        }

        /// <summary>
        /// Record the condition of a movie copy at return time.
        /// </summary>
        public ConditionInspection RecordReturn(int movieId, int rentalId,
            int customerId, ConditionGrade discGrade, ConditionGrade caseGrade,
            string inspectorName, string notes = null)
        {
            ValidateMovie(movieId);
            ValidateRental(rentalId);
            ValidateGrade(discGrade, nameof(discGrade));
            ValidateGrade(caseGrade, nameof(caseGrade));
            if (string.IsNullOrWhiteSpace(inspectorName))
                throw new ArgumentException("Inspector name is required.", nameof(inspectorName));

            if (_inspections.Any(i => i.RentalId == rentalId && i.Type == InspectionType.Return))
                throw new InvalidOperationException(
                    $"Return inspection already recorded for rental {rentalId}.");

            var inspection = new ConditionInspection
            {
                Id = _nextId++,
                MovieId = movieId,
                RentalId = rentalId,
                CustomerId = customerId,
                Type = InspectionType.Return,
                DiscGrade = discGrade,
                CaseGrade = caseGrade,
                Notes = notes,
                InspectorName = inspectorName,
                InspectedAt = _clock.Now
            };
            _inspections.Add(inspection);
            return inspection;
        }

        /// <summary>
        /// Record an ad-hoc audit inspection (not tied to checkout/return).
        /// </summary>
        public ConditionInspection RecordAudit(int movieId,
            ConditionGrade discGrade, ConditionGrade caseGrade,
            string inspectorName, string notes = null)
        {
            ValidateMovie(movieId);
            ValidateGrade(discGrade, nameof(discGrade));
            ValidateGrade(caseGrade, nameof(caseGrade));
            if (string.IsNullOrWhiteSpace(inspectorName))
                throw new ArgumentException("Inspector name is required.", nameof(inspectorName));

            var inspection = new ConditionInspection
            {
                Id = _nextId++,
                MovieId = movieId,
                RentalId = 0,
                CustomerId = 0,
                Type = InspectionType.Audit,
                DiscGrade = discGrade,
                CaseGrade = caseGrade,
                Notes = notes,
                InspectorName = inspectorName,
                InspectedAt = _clock.Now
            };
            _inspections.Add(inspection);
            return inspection;
        }

        // ── Queries ─────────────────────────────────────────────────

        /// <summary>
        /// Get the condition delta for a specific rental (checkout vs return).
        /// Returns null if both inspections are not yet recorded.
        /// </summary>
        public RentalConditionDelta GetRentalDelta(int rentalId)
        {
            var checkout = _inspections.FirstOrDefault(
                i => i.RentalId == rentalId && i.Type == InspectionType.Checkout);
            var returnInsp = _inspections.FirstOrDefault(
                i => i.RentalId == rentalId && i.Type == InspectionType.Return);

            if (checkout == null || returnInsp == null)
                return null;

            return new RentalConditionDelta
            {
                RentalId = rentalId,
                MovieId = checkout.MovieId,
                CustomerId = checkout.CustomerId,
                DiscBefore = checkout.DiscGrade,
                DiscAfter = returnInsp.DiscGrade,
                CaseBefore = checkout.CaseGrade,
                CaseAfter = returnInsp.CaseGrade
            };
        }

        /// <summary>
        /// Get all deltas where the copy deteriorated.
        /// </summary>
        public IReadOnlyList<RentalConditionDelta> GetDeteriorationHistory(int movieId)
        {
            var rentalIds = _inspections
                .Where(i => i.MovieId == movieId && i.Type == InspectionType.Checkout)
                .Select(i => i.RentalId)
                .Distinct()
                .ToList();

            var deltas = new List<RentalConditionDelta>();
            foreach (var rid in rentalIds)
            {
                var delta = GetRentalDelta(rid);
                if (delta != null && delta.Deteriorated)
                    deltas.Add(delta);
            }

            return deltas;
        }

        /// <summary>
        /// Get the current condition status of a specific movie copy.
        /// </summary>
        public CopyConditionStatus GetCopyStatus(int movieId)
        {
            var movie = _movieRepo.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.", nameof(movieId));

            var movieInspections = _inspections
                .Where(i => i.MovieId == movieId)
                .OrderByDescending(i => i.InspectedAt)
                .ToList();

            if (!movieInspections.Any())
            {
                return new CopyConditionStatus
                {
                    MovieId = movieId,
                    MovieName = movie.Name,
                    CurrentDiscGrade = ConditionGrade.Mint,
                    CurrentCaseGrade = ConditionGrade.Mint,
                    TotalRentals = 0,
                    DeteriorationEvents = 0,
                    DeteriorationRate = 0,
                    LastInspection = null
                };
            }

            var latest = movieInspections.First();
            var rentalIds = movieInspections
                .Where(i => i.Type == InspectionType.Checkout)
                .Select(i => i.RentalId)
                .Distinct()
                .ToList();

            int deteriorationCount = 0;
            double totalDiscDrop = 0;
            foreach (var rid in rentalIds)
            {
                var delta = GetRentalDelta(rid);
                if (delta != null)
                {
                    if (delta.Deteriorated)
                        deteriorationCount++;
                    totalDiscDrop += delta.DiscChange;
                }
            }

            double detRate = rentalIds.Count > 0
                ? totalDiscDrop / rentalIds.Count
                : 0;

            return new CopyConditionStatus
            {
                MovieId = movieId,
                MovieName = movie.Name,
                CurrentDiscGrade = latest.DiscGrade,
                CurrentCaseGrade = latest.CaseGrade,
                TotalRentals = rentalIds.Count,
                DeteriorationEvents = deteriorationCount,
                DeteriorationRate = Math.Round(detRate, 3),
                LastInspection = latest.InspectedAt
            };
        }

        /// <summary>
        /// Get all copies that need replacement (disc or case in Poor/Damaged).
        /// </summary>
        public IReadOnlyList<CopyConditionStatus> GetCopiesNeedingReplacement()
        {
            var movieIds = _inspections
                .Select(i => i.MovieId)
                .Distinct()
                .ToList();

            return movieIds
                .Select(id => GetCopyStatus(id))
                .Where(s => s.NeedsReplacement)
                .OrderBy(s => Math.Min((int)s.CurrentDiscGrade, (int)s.CurrentCaseGrade))
                .ToList();
        }

        // ── Renter risk assessment ──────────────────────────────────

        /// <summary>
        /// Build a risk profile for a specific customer.
        /// </summary>
        public RenterRiskProfile GetRenterProfile(int customerId)
        {
            var customerCheckouts = _inspections
                .Where(i => i.CustomerId == customerId && i.Type == InspectionType.Checkout)
                .ToList();

            if (!customerCheckouts.Any())
            {
                return new RenterRiskProfile
                {
                    CustomerId = customerId,
                    TotalRentals = 0,
                    DamageEvents = 0,
                    DamageRate = 0,
                    AverageDiscReturn = 5,
                    AverageCaseReturn = 5,
                    RiskLevel = RenterRiskLevel.Low
                };
            }

            int damageEvents = 0;
            double discSum = 0, caseSum = 0;
            int returnCount = 0;

            foreach (var checkout in customerCheckouts)
            {
                var delta = GetRentalDelta(checkout.RentalId);
                if (delta != null)
                {
                    returnCount++;
                    discSum += (int)delta.DiscAfter;
                    caseSum += (int)delta.CaseAfter;
                    if (delta.Deteriorated)
                        damageEvents++;
                }
            }

            double damageRate = returnCount > 0
                ? (double)damageEvents / returnCount
                : 0;

            double avgDisc = returnCount > 0 ? discSum / returnCount : 5;
            double avgCase = returnCount > 0 ? caseSum / returnCount : 5;

            RenterRiskLevel risk;
            if (damageRate >= HighRiskDamageRate)
                risk = RenterRiskLevel.High;
            else if (damageRate >= MediumRiskDamageRate)
                risk = RenterRiskLevel.Medium;
            else
                risk = RenterRiskLevel.Low;

            return new RenterRiskProfile
            {
                CustomerId = customerId,
                TotalRentals = customerCheckouts.Count,
                DamageEvents = damageEvents,
                DamageRate = Math.Round(damageRate, 3),
                AverageDiscReturn = Math.Round(avgDisc, 2),
                AverageCaseReturn = Math.Round(avgCase, 2),
                RiskLevel = risk
            };
        }

        /// <summary>
        /// Get all high-risk renters.
        /// </summary>
        public IReadOnlyList<RenterRiskProfile> GetHighRiskRenters()
        {
            var customerIds = _inspections
                .Where(i => i.Type == InspectionType.Checkout)
                .Select(i => i.CustomerId)
                .Distinct()
                .ToList();

            return customerIds
                .Select(id => GetRenterProfile(id))
                .Where(p => p.RiskLevel == RenterRiskLevel.High)
                .OrderByDescending(p => p.DamageRate)
                .ToList();
        }

        // ── Reporting ───────────────────────────────────────────────

        /// <summary>
        /// Generate a comprehensive condition report for the inventory.
        /// </summary>
        public ConditionReport GenerateReport()
        {
            var movieIds = _inspections
                .Select(i => i.MovieId)
                .Distinct()
                .ToList();

            var statuses = movieIds
                .Select(id => GetCopyStatus(id))
                .ToList();

            var report = new ConditionReport
            {
                TotalCopies = statuses.Count,
                MintCount = statuses.Count(s => s.CurrentDiscGrade == ConditionGrade.Mint),
                GoodCount = statuses.Count(s => s.CurrentDiscGrade == ConditionGrade.Good),
                FairCount = statuses.Count(s => s.CurrentDiscGrade == ConditionGrade.Fair),
                PoorCount = statuses.Count(s => s.CurrentDiscGrade == ConditionGrade.Poor),
                DamagedCount = statuses.Count(s => s.CurrentDiscGrade == ConditionGrade.Damaged),
                NeedingReplacement = statuses.Count(s => s.NeedsReplacement),
                AverageDiscGrade = statuses.Any()
                    ? Math.Round(statuses.Average(s => (int)s.CurrentDiscGrade), 2)
                    : 0,
                AverageCaseGrade = statuses.Any()
                    ? Math.Round(statuses.Average(s => (int)s.CurrentCaseGrade), 2)
                    : 0,
                WorstCopies = statuses
                    .OrderBy(s => Math.Min((int)s.CurrentDiscGrade, (int)s.CurrentCaseGrade))
                    .ThenByDescending(s => s.DeteriorationEvents)
                    .Take(MaxWorstCopies)
                    .ToList(),
                HighRiskRenters = GetHighRiskRenters().ToList(),
                GeneratedAt = _clock.Now
            };

            return report;
        }

        /// <summary>
        /// Get inspection history for a specific movie copy.
        /// </summary>
        public IReadOnlyList<ConditionInspection> GetInspectionHistory(int movieId)
        {
            return _inspections
                .Where(i => i.MovieId == movieId)
                .OrderByDescending(i => i.InspectedAt)
                .ToList();
        }

        /// <summary>
        /// Get all inspections for a specific rental.
        /// </summary>
        public IReadOnlyList<ConditionInspection> GetRentalInspections(int rentalId)
        {
            return _inspections
                .Where(i => i.RentalId == rentalId)
                .OrderBy(i => i.Type)
                .ToList();
        }

        /// <summary>
        /// Get the total number of recorded inspections.
        /// </summary>
        public int GetInspectionCount() => _inspections.Count;

        // ── Validation ──────────────────────────────────────────────

        private void ValidateMovie(int movieId)
        {
            if (_movieRepo.GetById(movieId) == null)
                throw new ArgumentException($"Movie {movieId} not found.", nameof(movieId));
        }

        private void ValidateRental(int rentalId)
        {
            if (_rentalRepo.GetById(rentalId) == null)
                throw new ArgumentException($"Rental {rentalId} not found.", nameof(rentalId));
        }

        private static void ValidateGrade(ConditionGrade grade, string paramName)
        {
            if (!Enum.IsDefined(typeof(ConditionGrade), grade))
                throw new ArgumentException(
                    $"Invalid condition grade: {grade}.", paramName);
        }
    }
}
