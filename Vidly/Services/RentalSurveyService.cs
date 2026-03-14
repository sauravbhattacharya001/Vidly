using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages post-rental satisfaction surveys: submission, validation,
    /// NPS calculation, reporting, trend analysis, and actionable insights.
    /// </summary>
    public class RentalSurveyService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;

        private readonly List<RentalSurvey> _surveys = new List<RentalSurvey>();
        private readonly List<SurveyInvitation> _invitations = new List<SurveyInvitation>();
        private readonly IClock _clock;
        private int _nextId = 1;

        /// <summary>Days after return before survey invitation is sent.</summary>
        public const int InvitationDelayDays = 1;

        /// <summary>Days before an unanswered invitation expires.</summary>
        public const int InvitationExpiryDays = 14;

        /// <summary>Minimum responses needed for meaningful NPS calculation.</summary>
        public const int MinResponsesForNps = 5;

        public RentalSurveyService(
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository),
            IClock clock = null)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Submit a survey response for a completed rental.
        /// </summary>
        public RentalSurvey Submit(int customerId, int rentalId, int npsScore,
            int overallSatisfaction, Dictionary<SurveyCategory, int> categoryRatings = null,
            string comments = null, RentAgainResponse wouldRentAgain = RentAgainResponse.Yes)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                throw new ArgumentException($"Rental {rentalId} not found.");

            if (rental.CustomerId != customerId)
                throw new InvalidOperationException("Rental does not belong to this customer.");

            if (rental.ReturnDate == null)
                throw new InvalidOperationException("Cannot survey a rental that hasn't been returned.");

            if (_surveys.Any(s => s.RentalId == rentalId))
                throw new InvalidOperationException("Survey already submitted for this rental.");

            if (npsScore < 0 || npsScore > 10)
                throw new ArgumentOutOfRangeException(nameof(npsScore), "NPS score must be 0-10.");

            if (overallSatisfaction < 1 || overallSatisfaction > 5)
                throw new ArgumentOutOfRangeException(nameof(overallSatisfaction), "Satisfaction must be 1-5.");

            if (categoryRatings != null)
            {
                foreach (var kvp in categoryRatings)
                {
                    if (kvp.Value < 1 || kvp.Value > 5)
                        throw new ArgumentOutOfRangeException(
                            $"Category rating for {kvp.Key} must be 1-5.");
                }
            }

            var survey = new RentalSurvey
            {
                Id = _nextId++,
                CustomerId = customerId,
                RentalId = rentalId,
                SubmittedAt = _clock.Now,
                NpsScore = npsScore,
                OverallSatisfaction = overallSatisfaction,
                CategoryRatings = categoryRatings ?? new Dictionary<SurveyCategory, int>(),
                Comments = comments?.Trim(),
                WouldRentAgain = wouldRentAgain
            };

            _surveys.Add(survey);

            // Mark invitation as completed if one exists
            var invitation = _invitations.FirstOrDefault(
                i => i.RentalId == rentalId && i.CustomerId == customerId);
            if (invitation != null)
                invitation.IsCompleted = true;

            return survey;
        }

        /// <summary>
        /// Get a customer's survey history.
        /// </summary>
        public List<RentalSurvey> GetCustomerSurveys(int customerId)
        {
            return _surveys
                .Where(s => s.CustomerId == customerId)
                .OrderByDescending(s => s.SubmittedAt)
                .ToList();
        }

        /// <summary>
        /// Calculate NPS score: % Promoters - % Detractors.
        /// Returns null if insufficient responses.
        /// </summary>
        public double? CalculateNps(List<RentalSurvey> surveys = null)
        {
            var data = surveys ?? _surveys;
            if (data.Count < MinResponsesForNps) return null;

            double promoters = data.Count(s => s.NpsCategory == NpsCategory.Promoter);
            double detractors = data.Count(s => s.NpsCategory == NpsCategory.Detractor);
            double total = data.Count;

            return Math.Round((promoters / total - detractors / total) * 100, 1);
        }

        /// <summary>
        /// Generate a comprehensive survey report.
        /// </summary>
        public SurveyReport GenerateReport(DateTime? from = null, DateTime? to = null)
        {
            var filtered = _surveys.AsEnumerable();
            if (from.HasValue) filtered = filtered.Where(s => s.SubmittedAt >= from.Value);
            if (to.HasValue) filtered = filtered.Where(s => s.SubmittedAt <= to.Value);

            var data = filtered.ToList();
            var totalRentals = _rentalRepository.GetAll().Count(r => r.ReturnDate != null);

            var report = new SurveyReport
            {
                TotalResponses = data.Count,
                ResponseRate = totalRentals > 0
                    ? Math.Round((double)data.Count / totalRentals * 100, 1)
                    : 0
            };

            if (data.Count == 0) return report;

            // NPS metrics
            report.Promoters = data.Count(s => s.NpsCategory == NpsCategory.Promoter);
            report.Passives = data.Count(s => s.NpsCategory == NpsCategory.Passive);
            report.Detractors = data.Count(s => s.NpsCategory == NpsCategory.Detractor);
            report.AverageNps = Math.Round(data.Average(s => s.NpsScore), 1);
            report.NpsScore = CalculateNps(data) ?? 0;

            // Satisfaction
            report.AverageSatisfaction = Math.Round(data.Average(s => s.OverallSatisfaction), 1);

            // Category breakdown
            var allCategories = Enum.GetValues(typeof(SurveyCategory)).Cast<SurveyCategory>();
            foreach (var cat in allCategories)
            {
                var ratings = data
                    .Where(s => s.CategoryRatings.ContainsKey(cat))
                    .Select(s => s.CategoryRatings[cat])
                    .ToList();
                if (ratings.Count > 0)
                    report.CategoryAverages[cat] = Math.Round(ratings.Average(), 1);
            }

            if (report.CategoryAverages.Count > 0)
            {
                report.StrongestCategory = report.CategoryAverages
                    .OrderByDescending(kvp => kvp.Value).First().Key;
                report.WeakestCategory = report.CategoryAverages
                    .OrderBy(kvp => kvp.Value).First().Key;
            }

            // Rent again
            report.WouldRentAgainPercent = Math.Round(
                (double)data.Count(s => s.WouldRentAgain == RentAgainResponse.Yes) / data.Count * 100, 1);

            // Grade
            report.OverallGrade = GetGrade(report.AverageSatisfaction, report.NpsScore);

            // Insights
            report.KeyInsights = GenerateInsights(report, data);

            // Monthly trends
            report.MonthlyTrends = data
                .GroupBy(s => new { s.SubmittedAt.Year, s.SubmittedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g =>
                {
                    var monthData = g.ToList();
                    var promoters = monthData.Count(s => s.NpsCategory == NpsCategory.Promoter);
                    var detractors = monthData.Count(s => s.NpsCategory == NpsCategory.Detractor);
                    return new SurveyTrend
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        ResponseCount = monthData.Count,
                        AverageNps = Math.Round(monthData.Average(s => s.NpsScore), 1),
                        NpsScore = Math.Round(
                            ((double)promoters / monthData.Count - (double)detractors / monthData.Count) * 100, 1),
                        AverageSatisfaction = Math.Round(monthData.Average(s => s.OverallSatisfaction), 1)
                    };
                })
                .ToList();

            return report;
        }

        /// <summary>
        /// Get at-risk customers (detractors who said No to renting again).
        /// </summary>
        public List<AtRiskCustomer> GetAtRiskCustomers()
        {
            return _surveys
                .Where(s => s.NpsCategory == NpsCategory.Detractor ||
                           s.WouldRentAgain == RentAgainResponse.No)
                .GroupBy(s => s.CustomerId)
                .Select(g =>
                {
                    var customer = _customerRepository.GetById(g.Key);
                    var surveys = g.OrderByDescending(s => s.SubmittedAt).ToList();
                    var latest = surveys.First();
                    return new AtRiskCustomer
                    {
                        CustomerId = g.Key,
                        CustomerName = customer?.Name ?? "Unknown",
                        MembershipType = customer?.MembershipType ?? MembershipType.Basic,
                        LatestNpsScore = latest.NpsScore,
                        AverageNps = Math.Round(surveys.Average(s => s.NpsScore), 1),
                        NegativeSurveyCount = surveys.Count,
                        LatestComments = latest.Comments,
                        RiskLevel = CalculateRiskLevel(latest, surveys.Count),
                        WeakestCategories = GetWeakestCategories(surveys)
                    };
                })
                .OrderByDescending(c => c.RiskLevel)
                .ToList();
        }

        /// <summary>
        /// Identify top improvement opportunities from low-rated categories.
        /// </summary>
        public List<ImprovementOpportunity> GetImprovementOpportunities()
        {
            var allCategories = Enum.GetValues(typeof(SurveyCategory)).Cast<SurveyCategory>();
            var opportunities = new List<ImprovementOpportunity>();

            foreach (var cat in allCategories)
            {
                var ratings = _surveys
                    .Where(s => s.CategoryRatings.ContainsKey(cat))
                    .Select(s => s.CategoryRatings[cat])
                    .ToList();

                if (ratings.Count < 3) continue;

                var avg = ratings.Average();
                if (avg < 3.5)
                {
                    var mentionCount = _surveys.Count(s =>
                        s.CategoryRatings.ContainsKey(cat) && s.CategoryRatings[cat] <= 2);

                    opportunities.Add(new ImprovementOpportunity
                    {
                        Category = cat,
                        AverageRating = Math.Round(avg, 1),
                        TotalRatings = ratings.Count,
                        LowRatingCount = mentionCount,
                        ImpactScore = Math.Round((3.5 - avg) * ratings.Count, 1),
                        Priority = avg < 2.0 ? "Critical" : avg < 2.5 ? "High" : avg < 3.0 ? "Medium" : "Low",
                        Suggestion = GetImprovementSuggestion(cat, avg)
                    });
                }
            }

            return opportunities.OrderByDescending(o => o.ImpactScore).ToList();
        }

        /// <summary>
        /// Send survey invitations for recently returned rentals.
        /// </summary>
        public List<SurveyInvitation> GenerateInvitations()
        {
            var recentReturns = _rentalRepository.GetAll()
                .Where(r => r.ReturnDate.HasValue &&
                           (_clock.Now - r.ReturnDate.Value).TotalDays >= InvitationDelayDays &&
                           (_clock.Now - r.ReturnDate.Value).TotalDays <= InvitationDelayDays + InvitationExpiryDays)
                .ToList();

            var newInvitations = new List<SurveyInvitation>();

            foreach (var rental in recentReturns)
            {
                // Skip if already surveyed or already invited
                if (_surveys.Any(s => s.RentalId == rental.Id)) continue;
                if (_invitations.Any(i => i.RentalId == rental.Id)) continue;

                var customer = _customerRepository.GetById(rental.CustomerId);
                var movie = _movieRepository.GetById(rental.MovieId);

                var invitation = new SurveyInvitation
                {
                    RentalId = rental.Id,
                    CustomerId = rental.CustomerId,
                    CustomerName = customer?.Name ?? "Customer",
                    MovieName = movie?.Name ?? rental.MovieName ?? "Movie",
                    RentalDate = rental.RentalDate,
                    ReturnDate = rental.ReturnDate.Value,
                    InvitationSentAt = _clock.Now,
                    IsCompleted = false
                };

                _invitations.Add(invitation);
                newInvitations.Add(invitation);
            }

            return newInvitations;
        }

        /// <summary>
        /// Get pending (unanswered) invitations.
        /// </summary>
        public List<SurveyInvitation> GetPendingInvitations()
        {
            return _invitations
                .Where(i => !i.IsCompleted &&
                           (_clock.Now - i.InvitationSentAt).TotalDays <= InvitationExpiryDays)
                .OrderBy(i => i.InvitationSentAt)
                .ToList();
        }

        /// <summary>
        /// Get all surveys.
        /// </summary>
        public List<RentalSurvey> GetAll() => _surveys.ToList();

        /// <summary>
        /// Get a specific survey by ID.
        /// </summary>
        public RentalSurvey GetById(int id) => _surveys.FirstOrDefault(s => s.Id == id);

        // --- Private Helpers ---

        private string GetGrade(double satisfaction, double nps)
        {
            if (satisfaction >= 4.5 && nps >= 50) return "A+";
            if (satisfaction >= 4.0 && nps >= 30) return "A";
            if (satisfaction >= 3.5 && nps >= 10) return "B";
            if (satisfaction >= 3.0 && nps >= 0) return "C";
            if (satisfaction >= 2.5) return "D";
            return "F";
        }

        private List<string> GenerateInsights(SurveyReport report, List<RentalSurvey> data)
        {
            var insights = new List<string>();

            if (report.NpsScore >= 50)
                insights.Add("Excellent NPS! Customers are strong advocates.");
            else if (report.NpsScore >= 0)
                insights.Add("Positive NPS, but room for improvement with passives.");
            else
                insights.Add("Negative NPS — urgent attention needed to reduce detractors.");

            if (report.WeakestCategory.HasValue && report.CategoryAverages.ContainsKey(report.WeakestCategory.Value))
            {
                var weakAvg = report.CategoryAverages[report.WeakestCategory.Value];
                if (weakAvg < 3.0)
                    insights.Add($"{report.WeakestCategory.Value} is critically low ({weakAvg}/5). Prioritize improvement.");
            }

            if (report.WouldRentAgainPercent < 70)
                insights.Add($"Only {report.WouldRentAgainPercent}% would rent again — retention risk.");

            if (report.ResponseRate < 20)
                insights.Add("Low response rate — consider incentivizing survey completion.");

            var commentCount = data.Count(s => !string.IsNullOrWhiteSpace(s.Comments));
            if (commentCount > 0)
                insights.Add($"{commentCount} customers left written feedback — review for themes.");

            if (report.MonthlyTrends.Count >= 2)
            {
                var recent = report.MonthlyTrends.Last();
                var previous = report.MonthlyTrends[report.MonthlyTrends.Count - 2];
                var delta = recent.NpsScore - previous.NpsScore;
                if (Math.Abs(delta) >= 5)
                    insights.Add(delta > 0
                        ? $"NPS trending up ({delta:+0.0} pts) — keep up the good work!"
                        : $"NPS trending down ({delta:+0.0} pts) — investigate recent changes.");
            }

            return insights;
        }

        private int CalculateRiskLevel(RentalSurvey latest, int negativeCount)
        {
            int risk = 0;
            if (latest.NpsScore <= 3) risk += 3;
            else if (latest.NpsScore <= 5) risk += 2;
            else risk += 1;

            if (latest.WouldRentAgain == RentAgainResponse.No) risk += 3;
            else if (latest.WouldRentAgain == RentAgainResponse.Maybe) risk += 1;

            if (latest.OverallSatisfaction <= 2) risk += 2;

            risk += Math.Min(negativeCount - 1, 3);

            return risk;
        }

        private List<SurveyCategory> GetWeakestCategories(List<RentalSurvey> surveys)
        {
            var categoryAvgs = new Dictionary<SurveyCategory, double>();
            foreach (var cat in Enum.GetValues(typeof(SurveyCategory)).Cast<SurveyCategory>())
            {
                var ratings = surveys
                    .Where(s => s.CategoryRatings.ContainsKey(cat))
                    .Select(s => s.CategoryRatings[cat])
                    .ToList();
                if (ratings.Count > 0)
                    categoryAvgs[cat] = ratings.Average();
            }

            return categoryAvgs
                .Where(kvp => kvp.Value < 3.0)
                .OrderBy(kvp => kvp.Value)
                .Take(3)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        private string GetImprovementSuggestion(SurveyCategory category, double avg)
        {
            switch (category)
            {
                case SurveyCategory.MovieSelection:
                    return "Expand inventory with trending titles and customer-requested movies.";
                case SurveyCategory.Pricing:
                    return "Review pricing tiers; consider loyalty discounts or bundle deals.";
                case SurveyCategory.StaffFriendliness:
                    return "Invest in customer service training and recognition programs.";
                case SurveyCategory.StoreCleanliness:
                    return "Increase cleaning frequency and conduct regular inspections.";
                case SurveyCategory.CheckoutSpeed:
                    return "Streamline checkout process; consider self-service kiosks.";
                case SurveyCategory.ReturnProcess:
                    return "Add drop-box options and simplify return procedures.";
                case SurveyCategory.DiscQuality:
                    return "Implement disc inspection program and replace damaged stock.";
                case SurveyCategory.OnlineExperience:
                    return "Improve website/app usability and add real-time availability.";
                default:
                    return "Gather more specific feedback to identify root causes.";
            }
        }
    }

    /// <summary>
    /// Customer identified as at-risk based on survey feedback.
    /// </summary>
    public class AtRiskCustomer
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipType { get; set; }
        public int LatestNpsScore { get; set; }
        public double AverageNps { get; set; }
        public int NegativeSurveyCount { get; set; }
        public string LatestComments { get; set; }
        public int RiskLevel { get; set; }
        public List<SurveyCategory> WeakestCategories { get; set; } = new List<SurveyCategory>();
    }

    /// <summary>
    /// Identified area for improvement based on survey data.
    /// </summary>
    public class ImprovementOpportunity
    {
        public SurveyCategory Category { get; set; }
        public double AverageRating { get; set; }
        public int TotalRatings { get; set; }
        public int LowRatingCount { get; set; }
        public double ImpactScore { get; set; }
        public string Priority { get; set; }
        public string Suggestion { get; set; }
    }
}
