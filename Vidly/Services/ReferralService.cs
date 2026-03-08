using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages the customer referral program: create referrals, convert them when
    /// referred friends sign up, track rewards, and provide analytics.
    /// </summary>
    public class ReferralService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly List<Referral> _referrals = new List<Referral>();
        private readonly object _lock = new object();
        private int _nextId = 1;

        /// <summary>Points awarded to referrer when a referral converts.</summary>
        public const int ConversionBonusPoints = 200;

        /// <summary>Points awarded to referred customer as welcome bonus.</summary>
        public const int WelcomeBonusPoints = 100;

        /// <summary>Days before a pending referral expires.</summary>
        public const int ExpirationDays = 30;

        /// <summary>Max active (pending) referrals per customer.</summary>
        public const int MaxPendingPerCustomer = 10;

        public ReferralService(ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Generates a unique referral code for a customer.
        /// Format: REF-{CustomerId}-{random alphanumeric}.
        /// Uses cryptographically secure random generation to prevent
        /// code prediction attacks. System.Random is deterministic — an
        /// attacker who knows the approximate generation time can predict
        /// future codes and hijack referral bonuses.
        /// </summary>
        public string GenerateReferralCode(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            const int suffixLength = 6;
            var randomBytes = new byte[suffixLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            var suffix = new string(randomBytes
                .Select(b => chars[b % chars.Length]).ToArray());

            return $"REF-{customerId}-{suffix}";
        }

        /// <summary>
        /// Creates a new referral invitation.
        /// </summary>
        public Referral CreateReferral(int referrerId, string referredName, string referredEmail)
        {
            if (string.IsNullOrWhiteSpace(referredName))
                throw new ArgumentException("Referred name is required.");
            if (string.IsNullOrWhiteSpace(referredEmail))
                throw new ArgumentException("Referred email is required.");

            var customer = _customerRepository.GetById(referrerId);
            if (customer == null)
                throw new ArgumentException($"Customer {referrerId} not found.");

            lock (_lock)
            {
                // Check pending limit
                var pendingCount = _referrals.Count(r =>
                    r.ReferrerId == referrerId && r.Status == ReferralStatus.Pending);
                if (pendingCount >= MaxPendingPerCustomer)
                    throw new InvalidOperationException(
                        $"Maximum of {MaxPendingPerCustomer} pending referrals reached.");

                // Check for duplicate email from same referrer
                var duplicate = _referrals.Any(r =>
                    r.ReferrerId == referrerId &&
                    r.ReferredEmail.Equals(referredEmail, StringComparison.OrdinalIgnoreCase) &&
                    r.Status == ReferralStatus.Pending);
                if (duplicate)
                    throw new InvalidOperationException(
                        "You already have a pending referral for this email.");

                var referral = new Referral
                {
                    Id = _nextId++,
                    ReferrerId = referrerId,
                    ReferredName = referredName.Trim(),
                    ReferredEmail = referredEmail.Trim().ToLowerInvariant(),
                    ReferralCode = GenerateReferralCode(referrerId),
                    CreatedDate = DateTime.Now,
                    Status = ReferralStatus.Pending,
                    PointsAwarded = 0
                };

                _referrals.Add(referral);
                return referral;
            }
        }

        /// <summary>
        /// Converts a referral when the referred person signs up.
        /// Awards points to both referrer and new customer.
        /// </summary>
        public Referral ConvertReferral(string referralCode, int newCustomerId)
        {
            if (string.IsNullOrWhiteSpace(referralCode))
                throw new ArgumentException("Referral code is required.");

            var newCustomer = _customerRepository.GetById(newCustomerId);
            if (newCustomer == null)
                throw new ArgumentException($"Customer {newCustomerId} not found.");

            lock (_lock)
            {
                var referral = _referrals.FirstOrDefault(r =>
                    r.ReferralCode.Equals(referralCode, StringComparison.OrdinalIgnoreCase));

                if (referral == null)
                    throw new ArgumentException("Invalid referral code.");

                if (referral.Status != ReferralStatus.Pending)
                    throw new InvalidOperationException(
                        $"Referral is {referral.Status}, not Pending.");

                // Check expiration
                if ((DateTime.Now - referral.CreatedDate).TotalDays > ExpirationDays)
                {
                    referral.Status = ReferralStatus.Expired;
                    throw new InvalidOperationException("Referral code has expired.");
                }

                referral.Status = ReferralStatus.Converted;
                referral.ConvertedDate = DateTime.Now;
                referral.ReferredCustomerId = newCustomerId;
                referral.PointsAwarded = ConversionBonusPoints;

                return referral;
            }
        }

        /// <summary>
        /// Marks expired referrals. Call periodically.
        /// </summary>
        public int ExpireOldReferrals()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var expired = _referrals.Where(r =>
                    r.Status == ReferralStatus.Pending &&
                    (now - r.CreatedDate).TotalDays > ExpirationDays).ToList();

                foreach (var r in expired)
                    r.Status = ReferralStatus.Expired;

                return expired.Count;
            }
        }

        /// <summary>
        /// Gets all referrals for a customer (as referrer).
        /// </summary>
        public IReadOnlyList<Referral> GetReferralsByCustomer(int customerId)
        {
            lock (_lock)
            {
                return _referrals
                    .Where(r => r.ReferrerId == customerId)
                    .OrderByDescending(r => r.CreatedDate)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Looks up a referral by its code.
        /// </summary>
        public Referral GetByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            lock (_lock)
            {
                return _referrals.FirstOrDefault(r =>
                    r.ReferralCode.Equals(code, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Gets referral summary for a specific customer.
        /// </summary>
        public ReferralSummary GetCustomerSummary(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            lock (_lock)
            {
                var refs = _referrals.Where(r => r.ReferrerId == customerId).ToList();
                var converted = refs.Count(r => r.Status == ReferralStatus.Converted ||
                                                r.Status == ReferralStatus.RewardClaimed);
                var total = refs.Count;

                return new ReferralSummary
                {
                    CustomerId = customerId,
                    CustomerName = customer.Name,
                    TotalReferrals = total,
                    ConvertedCount = converted,
                    PendingCount = refs.Count(r => r.Status == ReferralStatus.Pending),
                    ExpiredCount = refs.Count(r => r.Status == ReferralStatus.Expired),
                    TotalPointsEarned = refs.Sum(r => r.PointsAwarded),
                    ConversionRate = total > 0 ? Math.Round((double)converted / total * 100, 1) : 0,
                    ReferralCode = refs.LastOrDefault()?.ReferralCode ?? GenerateReferralCode(customerId)
                };
            }
        }

        /// <summary>
        /// Gets program-wide stats including leaderboard and monthly trends.
        /// </summary>
        public ReferralProgramStats GetProgramStats()
        {
            lock (_lock)
            {
                var all = _referrals.ToList();
                var converted = all.Count(r => r.Status == ReferralStatus.Converted ||
                                               r.Status == ReferralStatus.RewardClaimed);
                var total = all.Count;

                // Leaderboard: top referrers by conversions
                var leaderboard = all
                    .Where(r => r.Status == ReferralStatus.Converted ||
                                r.Status == ReferralStatus.RewardClaimed)
                    .GroupBy(r => r.ReferrerId)
                    .Select(g =>
                    {
                        var customer = _customerRepository.GetById(g.Key);
                        var count = g.Count();
                        return new ReferralLeaderboardEntry
                        {
                            CustomerId = g.Key,
                            CustomerName = customer?.Name ?? "Unknown",
                            ConvertedReferrals = count,
                            TotalPointsEarned = g.Sum(r => r.PointsAwarded),
                            Tier = count >= 10 ? "Ambassador" :
                                   count >= 5 ? "Champion" :
                                   count >= 3 ? "Advocate" : "Starter"
                        };
                    })
                    .OrderByDescending(e => e.ConvertedReferrals)
                    .ThenByDescending(e => e.TotalPointsEarned)
                    .Take(10)
                    .ToList();

                // Assign ranks
                for (int i = 0; i < leaderboard.Count; i++)
                    leaderboard[i].Rank = i + 1;

                // Monthly trends (last 6 months)
                var sixMonthsAgo = DateTime.Now.AddMonths(-6);
                var trends = all
                    .Where(r => r.CreatedDate >= sixMonthsAgo)
                    .GroupBy(r => new { r.CreatedDate.Year, r.CreatedDate.Month })
                    .Select(g => new MonthlyReferralTrend
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        Label = new DateTime(g.Key.Year, g.Key.Month, 1)
                            .ToString("MMM yyyy", CultureInfo.InvariantCulture),
                        Sent = g.Count(),
                        Converted = g.Count(r => r.Status == ReferralStatus.Converted ||
                                                  r.Status == ReferralStatus.RewardClaimed)
                    })
                    .OrderBy(t => t.Year).ThenBy(t => t.Month)
                    .ToList();

                return new ReferralProgramStats
                {
                    TotalReferrals = total,
                    TotalConverted = converted,
                    TotalPending = all.Count(r => r.Status == ReferralStatus.Pending),
                    TotalExpired = all.Count(r => r.Status == ReferralStatus.Expired),
                    OverallConversionRate = total > 0 ? Math.Round((double)converted / total * 100, 1) : 0,
                    TotalPointsAwarded = all.Sum(r => r.PointsAwarded),
                    ActiveReferrers = all.Select(r => r.ReferrerId).Distinct().Count(),
                    Leaderboard = leaderboard.AsReadOnly(),
                    MonthlyTrends = trends.AsReadOnly()
                };
            }
        }

        /// <summary>
        /// Gets all referrals, optionally filtered by status.
        /// </summary>
        public IReadOnlyList<Referral> GetAll(ReferralStatus? status = null)
        {
            lock (_lock)
            {
                var query = _referrals.AsEnumerable();
                if (status.HasValue)
                    query = query.Where(r => r.Status == status.Value);

                return query.OrderByDescending(r => r.CreatedDate).ToList().AsReadOnly();
            }
        }
    }
}
