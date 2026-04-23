using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    public class NegotiationService
    {
        private readonly IMovieRepository _movies;
        private readonly IRentalRepository _rentals;
        private readonly ICustomerRepository _customers;
        private static readonly Random Rng = new Random();

        private static readonly string[] LoyalKeywords = { "loyal", "years", "always", "regular", "faithful", "long time" };
        private static readonly string[] UrgencyKeywords = { "competitor", "cancel", "expensive", "netflix", "streaming", "too much", "overpriced" };
        private static readonly string[] PoliteKeywords = { "please", "appreciate", "thank", "grateful", "kind" };

        public NegotiationService(IMovieRepository movies, IRentalRepository rentals, ICustomerRepository customers)
        {
            _movies = movies ?? throw new ArgumentNullException(nameof(movies));
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        }

        public NegotiationStrategy ClassifyCustomer(int customerId)
        {
            var rentals = _rentals.GetByCustomer(customerId);
            var count = rentals.Count;
            if (count < 3) return NegotiationStrategy.FirstTime;

            var lateRate = rentals.Count(r => r.IsOverdue || r.LateFee > 0) / (double)count;
            var avgRate = rentals.Average(r => r.DailyRate);

            if (count >= 20 && lateRate < 0.10) return NegotiationStrategy.Loyal;
            if (avgRate > 3.5m) return NegotiationStrategy.Premium;
            if (count >= 10) return NegotiationStrategy.Bulk;
            return NegotiationStrategy.Casual;
        }

        public int CalculateLoyaltyScore(int customerId)
        {
            var rentals = _rentals.GetByCustomer(customerId);
            if (!rentals.Any()) return 0;

            // Rental count: max 30pts (1pt per rental, cap 30)
            var countPts = Math.Min(rentals.Count, 30);

            // On-time return rate: max 25pts
            var returned = rentals.Where(r => r.ReturnDate.HasValue).ToList();
            var onTimeRate = returned.Any()
                ? returned.Count(r => r.LateFee == 0) / (double)returned.Count
                : 1.0;
            var onTimePts = (int)(onTimeRate * 25);

            // Genre diversity: max 15pts (1.5pt per distinct genre, cap 15)
            var genres = rentals.Select(r =>
            {
                var movie = _movies.GetById(r.MovieId);
                return movie?.Genre;
            }).Where(g => g.HasValue).Distinct().Count();
            var genrePts = Math.Min((int)(genres * 1.5), 15);

            // Total spending: max 20pts (1pt per $5 spent, cap 20)
            var totalSpent = rentals.Sum(r => r.TotalCost);
            var spendPts = Math.Min((int)(totalSpent / 5m), 20);

            // Membership length: max 10pts (1pt per month)
            var earliest = rentals.Min(r => r.RentalDate);
            var months = (int)((DateTime.Now - earliest).TotalDays / 30);
            var monthPts = Math.Min(months, 10);

            return Math.Min(countPts + onTimePts + genrePts + spendPts + monthPts, 100);
        }

        public NegotiationSession StartNegotiation(int customerId, int movieId)
        {
            var customer = _customers.GetById(customerId);
            if (customer == null) throw new KeyNotFoundException("Customer not found.");
            var movie = _movies.GetById(movieId);
            if (movie == null) throw new KeyNotFoundException("Movie not found.");

            var strategy = ClassifyCustomer(customerId);
            var loyalty = CalculateLoyaltyScore(customerId);
            var originalRate = movie.DailyRate ?? 2.99m;

            var baseDiscount = GetBaseDiscount(strategy);
            var loyaltyBonus = loyalty / 200.0m; // up to 0.5 (50%) extra
            var totalDiscount = Math.Min(baseDiscount + loyaltyBonus * baseDiscount, 0.40m);

            var offer = new NegotiationOffer
            {
                MovieId = movieId,
                MovieName = movie.Name,
                OriginalRate = originalRate,
                OfferedRate = Math.Round(originalRate * (1 - totalDiscount), 2),
                DiscountPct = Math.Round(totalDiscount * 100, 1),
                ExtendedDays = loyalty > 60 ? 2 : (loyalty > 30 ? 1 : 0),
                Confidence = 0.95,
                ExpiresAt = DateTime.Now.AddHours(24),
                BonusPerks = GetInitialPerks(strategy, loyalty)
            };

            var session = new NegotiationSession
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                Strategy = strategy,
                LoyaltyScore = loyalty,
                InitialOffer = offer,
                FinalOffer = CloneOffer(offer),
                Status = NegotiationStatus.Negotiating
            };

            session.ProactiveInsights = GenerateInsights(customerId, session);
            return session;
        }

        public NegotiationSession Negotiate(NegotiationSession session, string customerArgument)
        {
            if (session.Status != NegotiationStatus.Negotiating)
                return session;

            if (session.Rounds.Count >= 5)
            {
                session.Status = NegotiationStatus.Expired;
                return session;
            }

            var roundNum = session.Rounds.Count + 1;
            var arg = (customerArgument ?? "").ToLowerInvariant();

            // Calculate adjustments based on argument
            var extraDiscount = 0.01m + (decimal)(Rng.NextDouble() * 0.02); // 1-3% base
            if (LoyalKeywords.Any(k => arg.Contains(k))) extraDiscount += 0.02m;
            if (UrgencyKeywords.Any(k => arg.Contains(k))) extraDiscount += 0.015m;
            if (PoliteKeywords.Any(k => arg.Contains(k))) extraDiscount += 0.005m;

            // Diminishing returns after round 3
            if (roundNum > 3) extraDiscount *= 0.3m;

            var currentOffer = session.FinalOffer;
            var newDiscountPct = Math.Min(currentOffer.DiscountPct + extraDiscount * 100, 40m);
            var newRate = Math.Round(currentOffer.OriginalRate * (1 - newDiscountPct / 100m), 2);
            var newConfidence = Math.Max(currentOffer.Confidence - 0.12, 0.3);

            // Maybe add a perk
            var newPerks = new List<string>(currentOffer.BonusPerks);
            if (roundNum <= 3 && Rng.NextDouble() > 0.4)
            {
                var possiblePerks = new[]
                {
                    "Free extra rental day", "No late fee guarantee",
                    "Priority reservation next time", "10% off next rental",
                    "Free genre upgrade", "Buddy rental discount (bring a friend)"
                };
                var newPerk = possiblePerks.FirstOrDefault(p => !newPerks.Contains(p));
                if (newPerk != null) newPerks.Add(newPerk);
            }

            var extDays = currentOffer.ExtendedDays;
            if (roundNum == 2 && extDays < 3) extDays++;

            var newOffer = new NegotiationOffer
            {
                MovieId = currentOffer.MovieId,
                MovieName = currentOffer.MovieName,
                OriginalRate = currentOffer.OriginalRate,
                OfferedRate = newRate,
                DiscountPct = Math.Round(newDiscountPct, 1),
                ExtendedDays = extDays,
                BonusPerks = newPerks,
                Confidence = newConfidence,
                ExpiresAt = currentOffer.ExpiresAt
            };

            var response = GenerateResponse(roundNum, newOffer, arg, session.Strategy);

            session.Rounds.Add(new NegotiationRound
            {
                RoundNumber = roundNum,
                CustomerArgument = customerArgument,
                SystemResponse = response,
                OfferAdjustment = currentOffer.OfferedRate - newRate,
                NewOffer = newOffer
            });

            session.FinalOffer = newOffer;
            return session;
        }

        private string GenerateResponse(int round, NegotiationOffer offer, string arg, NegotiationStrategy strategy)
        {
            if (round >= 4)
                return string.Format(
                    "I appreciate your persistence! This is truly my best offer: ${0}/day with {1}% off. " +
                    "I've stretched as far as I can go. What do you say?",
                    offer.OfferedRate, offer.DiscountPct);

            if (round == 1)
            {
                if (strategy == NegotiationStrategy.Loyal)
                    return string.Format(
                        "As a valued loyal customer, I'm happy to improve your deal! " +
                        "I can offer ${0}/day ({1}% off) with {2} bonus day(s). Your loyalty truly matters to us.",
                        offer.OfferedRate, offer.DiscountPct, offer.ExtendedDays);

                return string.Format(
                    "I hear you! Let me see what I can do... I've adjusted the rate to ${0}/day ({1}% off). " +
                    "That's a solid deal for \"{2}\".",
                    offer.OfferedRate, offer.DiscountPct, offer.MovieName);
            }

            if (UrgencyKeywords.Any(k => arg.Contains(k)))
                return string.Format(
                    "We definitely want to keep you as a customer! I've bumped it to ${0}/day ({1}% off). " +
                    "We can't match streaming prices, but we offer something they can't — curated personal service.",
                    offer.OfferedRate, offer.DiscountPct);

            return string.Format(
                "You make a fair point. I've improved the offer to ${0}/day ({1}% off) " +
                "with {2} perk(s) included. Getting close to my limit though!",
                offer.OfferedRate, offer.DiscountPct, offer.BonusPerks.Count);
        }

        private decimal GetBaseDiscount(NegotiationStrategy strategy)
        {
            switch (strategy)
            {
                case NegotiationStrategy.Loyal: return 0.15m;
                case NegotiationStrategy.Premium: return 0.10m;
                case NegotiationStrategy.Bulk: return 0.20m;
                case NegotiationStrategy.FirstTime: return 0.08m;
                default: return 0.05m;
            }
        }

        private List<string> GetInitialPerks(NegotiationStrategy strategy, int loyalty)
        {
            var perks = new List<string>();
            if (loyalty > 50) perks.Add("No late fee guarantee");
            if (strategy == NegotiationStrategy.Loyal || strategy == NegotiationStrategy.Premium)
                perks.Add("Priority reservation next time");
            if (strategy == NegotiationStrategy.FirstTime)
                perks.Add("Welcome bonus: free extra rental day");
            return perks;
        }

        private List<string> GenerateInsights(int customerId, NegotiationSession session)
        {
            var insights = new List<string>();
            var rentals = _rentals.GetByCustomer(customerId);

            if (rentals.Any())
            {
                // Favorite genre
                var genreCounts = rentals
                    .Select(r => _movies.GetById(r.MovieId)?.Genre)
                    .Where(g => g.HasValue)
                    .GroupBy(g => g.Value)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                if (genreCounts.Any())
                    insights.Add(string.Format(
                        "You watch mostly {0} movies — we've factored that into your perks.",
                        genreCounts.First().Key));

                // On-time rate
                var returned = rentals.Where(r => r.ReturnDate.HasValue).ToList();
                if (returned.Any())
                {
                    var onTimeRate = (int)(returned.Count(r => r.LateFee == 0) / (double)returned.Count * 100);
                    if (onTimeRate >= 80)
                        insights.Add(string.Format(
                            "Your on-time return rate of {0}% qualifies you for our reliability bonus.",
                            onTimeRate));
                }

                // Spending insight
                var totalSpent = rentals.Sum(r => r.TotalCost);
                if (totalSpent > 50)
                    insights.Add(string.Format(
                        "You've spent ${0:F2} with us — that's top-tier customer territory!",
                        totalSpent));
            }

            insights.Add("Tip: Mentioning your loyalty or asking politely can unlock better deals!");

            if (session.LoyaltyScore > 60)
                insights.Add("Your high loyalty score gives you stronger negotiating leverage.");

            return insights;
        }

        private static NegotiationOffer CloneOffer(NegotiationOffer o)
        {
            return new NegotiationOffer
            {
                MovieId = o.MovieId,
                MovieName = o.MovieName,
                OriginalRate = o.OriginalRate,
                OfferedRate = o.OfferedRate,
                DiscountPct = o.DiscountPct,
                ExtendedDays = o.ExtendedDays,
                BonusPerks = new List<string>(o.BonusPerks),
                ExpiresAt = o.ExpiresAt,
                Confidence = o.Confidence
            };
        }
    }
}
