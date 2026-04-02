using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Predicts which active rentals are at risk of being returned late.
    /// Analyzes customer history, rental timing patterns, and current rental
    /// state to produce a risk score (0-100) with actionable recommendations.
    /// </summary>
    public class LateReturnPredictorService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        public LateReturnPredictorService(
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Generates risk predictions for all active (non-returned) rentals.
        /// Builds a customer rental index once (O(R)) instead of scanning
        /// all rentals per active rental (previously O(A×R) where A = active
        /// rentals and R = total rentals). For a store with 500 active
        /// rentals and 10,000 total rentals, this reduces from ~5M to ~10K
        /// iterations.
        /// </summary>
        public List<LateReturnPrediction> PredictAll()
        {
            var allRentals = _rentalRepository.GetAll();
            var activeRentals = new List<Rental>();

            // Single pass: build customer rental index and collect active rentals
            var rentalsByCustomer = new Dictionary<int, List<Rental>>();
            foreach (var r in allRentals)
            {
                if (!rentalsByCustomer.TryGetValue(r.CustomerId, out var list))
                {
                    list = new List<Rental>();
                    rentalsByCustomer[r.CustomerId] = list;
                }
                list.Add(r);

                if (r.Status != RentalStatus.Returned)
                    activeRentals.Add(r);
            }

            var predictions = new List<LateReturnPrediction>(activeRentals.Count);

            foreach (var rental in activeRentals)
            {
                // O(1) dictionary lookup + O(H) filter for current rental ID
                // instead of O(R) linear scan over all rentals
                List<Rental> customerHistory;
                if (rentalsByCustomer.TryGetValue(rental.CustomerId, out var allCustomerRentals))
                {
                    customerHistory = new List<Rental>(allCustomerRentals.Count);
                    foreach (var r in allCustomerRentals)
                    {
                        if (r.Id != rental.Id)
                            customerHistory.Add(r);
                    }
                }
                else
                {
                    customerHistory = new List<Rental>();
                }

                predictions.Add(PredictForRental(rental, customerHistory));
            }

            predictions.Sort((a, b) => b.RiskScore.CompareTo(a.RiskScore));
            return predictions;
        }

        /// <summary>
        /// Generates a risk prediction for a single rental.
        /// </summary>
        public LateReturnPrediction PredictForRental(int rentalId)
        {
            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                throw new KeyNotFoundException($"Rental {rentalId} not found.");

            if (rental.Status == RentalStatus.Returned)
                throw new InvalidOperationException("Cannot predict for a returned rental.");

            var customerHistory = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == rental.CustomerId && r.Id != rental.Id)
                .ToList();

            return PredictForRental(rental, customerHistory);
        }

        /// <summary>
        /// Returns a summary of predictions across all active rentals.
        /// </summary>
        public PredictionSummary GetSummary()
        {
            var predictions = PredictAll();
            return new PredictionSummary
            {
                TotalActiveRentals = predictions.Count,
                LowRisk = predictions.Count(p => p.Level == RiskLevel.Low),
                MediumRisk = predictions.Count(p => p.Level == RiskLevel.Medium),
                HighRisk = predictions.Count(p => p.Level == RiskLevel.High),
                CriticalRisk = predictions.Count(p => p.Level == RiskLevel.Critical),
                TotalEstimatedLateFees = predictions.Sum(p => p.EstimatedLateFee),
                AverageRiskScore = predictions.Count > 0
                    ? predictions.Average(p => p.RiskScore) : 0
            };
        }

        private LateReturnPrediction PredictForRental(Rental rental, List<Rental> customerHistory)
        {
            var prediction = new LateReturnPrediction
            {
                RentalId = rental.Id,
                CustomerId = rental.CustomerId,
                CustomerName = rental.CustomerName,
                MovieId = rental.MovieId,
                MovieName = rental.MovieName,
                RentalDate = rental.RentalDate,
                DueDate = rental.DueDate,
                DaysRemaining = (int)(rental.DueDate - _clock.Today).TotalDays
            };

            int totalPoints = 0;

            // Factor 1: Already overdue (0-30 points)
            if (prediction.DaysRemaining < 0)
            {
                int overdueDays = Math.Abs(prediction.DaysRemaining);
                int points = Math.Min(30, overdueDays * 5);
                totalPoints += points;
                prediction.Factors.Add(new RiskFactor
                {
                    Name = "Already Overdue",
                    Description = $"{overdueDays} day(s) past due date",
                    Points = points
                });
            }
            // Factor 2: Due soon (0-15 points)
            else if (prediction.DaysRemaining <= 2)
            {
                int points = prediction.DaysRemaining == 0 ? 15 : (3 - prediction.DaysRemaining) * 5;
                totalPoints += points;
                prediction.Factors.Add(new RiskFactor
                {
                    Name = "Due Soon",
                    Description = prediction.DaysRemaining == 0
                        ? "Due today"
                        : $"Due in {prediction.DaysRemaining} day(s)",
                    Points = points
                });
            }

            // Factor 3: Customer late return history (0-25 points)
            var returnedHistory = customerHistory.Where(r => r.Status == RentalStatus.Returned).ToList();
            if (returnedHistory.Count > 0)
            {
                int lateReturns = returnedHistory.Count(r =>
                    r.ReturnDate.HasValue && r.ReturnDate.Value > r.DueDate);
                double lateRate = (double)lateReturns / returnedHistory.Count;

                if (lateRate > 0)
                {
                    int points = (int)Math.Min(25, Math.Round(lateRate * 25));
                    totalPoints += points;
                    prediction.Factors.Add(new RiskFactor
                    {
                        Name = "Late Return History",
                        Description = $"{lateReturns}/{returnedHistory.Count} previous rentals returned late ({lateRate:P0})",
                        Points = points
                    });
                }
            }

            // Factor 4: Currently overdue other rentals (0-15 points)
            var otherOverdue = customerHistory.Count(r =>
                r.Status == RentalStatus.Overdue || r.Status == RentalStatus.Active && _clock.Today > r.DueDate);
            if (otherOverdue > 0)
            {
                int points = Math.Min(15, otherOverdue * 8);
                totalPoints += points;
                prediction.Factors.Add(new RiskFactor
                {
                    Name = "Other Overdue Rentals",
                    Description = $"Customer has {otherOverdue} other overdue rental(s)",
                    Points = points
                });
            }

            // Factor 5: Long rental duration (0-10 points)
            int rentalDays = (int)(_clock.Today - rental.RentalDate).TotalDays;
            if (rentalDays > 10)
            {
                int points = Math.Min(10, (rentalDays - 10) * 2);
                totalPoints += points;
                prediction.Factors.Add(new RiskFactor
                {
                    Name = "Extended Hold",
                    Description = $"Movie held for {rentalDays} days (above average)",
                    Points = points
                });
            }

            // Factor 6: No rental history (new customer risk) (0-10 points)
            if (customerHistory.Count == 0)
            {
                totalPoints += 10;
                prediction.Factors.Add(new RiskFactor
                {
                    Name = "New Customer",
                    Description = "No rental history to assess reliability",
                    Points = 10
                });
            }

            // Cap at 100
            prediction.RiskScore = Math.Min(100, totalPoints);

            // Determine level
            prediction.Level = prediction.RiskScore switch
            {
                >= 70 => RiskLevel.Critical,
                >= 45 => RiskLevel.High,
                >= 20 => RiskLevel.Medium,
                _ => RiskLevel.Low
            };

            // Estimate late fee
            if (prediction.DaysRemaining < 0)
            {
                prediction.EstimatedLateFee = Math.Abs(prediction.DaysRemaining) * RentalPolicyConstants.LateFeePerDay;
            }
            else if (prediction.RiskScore >= 45)
            {
                // Estimate 2-3 days late based on risk
                int estimatedLateDays = prediction.RiskScore >= 70 ? 3 : 2;
                prediction.EstimatedLateFee = estimatedLateDays * RentalPolicyConstants.LateFeePerDay;
            }

            // Generate suggested actions
            prediction.SuggestedActions = GenerateActions(prediction);

            return prediction;
        }

        private List<string> GenerateActions(LateReturnPrediction prediction)
        {
            var actions = new List<string>();

            if (prediction.DaysRemaining < 0)
            {
                actions.Add("Send overdue notice immediately");
                if (Math.Abs(prediction.DaysRemaining) >= 3)
                    actions.Add("Escalate — consider phone call or account hold");
            }
            else if (prediction.DaysRemaining <= 1)
            {
                actions.Add("Send reminder: rental due " +
                    (prediction.DaysRemaining == 0 ? "today" : "tomorrow"));
            }

            if (prediction.Level >= RiskLevel.High)
            {
                actions.Add("Flag account for staff follow-up");
            }

            if (prediction.Factors.Any(f => f.Name == "Late Return History"))
            {
                actions.Add("Consider requiring deposit for future rentals");
            }

            if (prediction.Factors.Any(f => f.Name == "Other Overdue Rentals"))
            {
                actions.Add("Block new checkouts until overdue items returned");
            }

            if (prediction.Factors.Any(f => f.Name == "New Customer"))
            {
                actions.Add("Send welcome email with rental policy reminder");
            }

            if (actions.Count == 0)
            {
                actions.Add("No action needed — rental on track");
            }

            return actions;
        }
    }
}
