using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class RentalSurveyServiceTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private RentalSurveyService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.Reset();
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _service = new RentalSurveyService(_customerRepo, _rentalRepo, _movieRepo);
        }

        private int _nextCustId = 8000;
        private int _nextRentalId = 8000;
        private int _nextMovieId = 8000;

        private Customer AddCustomer(string name = "Test Customer")
        {
            var c = new Customer { Id = _nextCustId++, Name = name, MembershipType = MembershipType.Basic };
            _customerRepo.Add(c);
            return c;
        }

        private Movie AddMovie(string name = "Test Movie")
        {
            var m = new Movie { Id = _nextMovieId++, Name = name, Genre = Genre.Action };
            _movieRepo.Add(m);
            return m;
        }

        private Rental AddReturnedRental(int customerId, int movieId, DateTime? returnDate = null)
        {
            var rented = DateTime.Today.AddDays(-10);
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rented,
                DueDate = rented.AddDays(7),
                DailyRate = 3.99m,
                ReturnDate = returnDate ?? rented.AddDays(5),
                Status = RentalStatus.Returned
            };
            _rentalRepo.Add(r);
            return r;
        }

        private RentalSurvey SubmitSurvey(int customerId, int rentalId,
            int nps = 8, int satisfaction = 4,
            Dictionary<SurveyCategory, int> categories = null,
            string comments = null,
            RentAgainResponse again = RentAgainResponse.Yes)
        {
            return _service.Submit(customerId, rentalId, nps, satisfaction,
                categories, comments, again);
        }

        // ── Constructor ─────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullCustomerRepo_Throws()
            => new RentalSurveyService(null, _rentalRepo, _movieRepo);

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
            => new RentalSurveyService(_customerRepo, null, _movieRepo);

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws()
            => new RentalSurveyService(_customerRepo, _rentalRepo, null);

        // ── Submit ──────────────────────────────────────────────────

        [TestMethod]
        public void Submit_ValidSurvey_ReturnsWithId()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);

            var survey = SubmitSurvey(c.Id, r.Id);

            Assert.IsTrue(survey.Id > 0);
            Assert.AreEqual(c.Id, survey.CustomerId);
            Assert.AreEqual(r.Id, survey.RentalId);
            Assert.AreEqual(8, survey.NpsScore);
            Assert.AreEqual(4, survey.OverallSatisfaction);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Submit_NonexistentCustomer_Throws()
            => _service.Submit(99999, 1, 8, 4);

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Submit_NonexistentRental_Throws()
        {
            var c = AddCustomer();
            _service.Submit(c.Id, 99999, 8, 4);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Submit_RentalBelongsToOtherCustomer_Throws()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie();
            var r = AddReturnedRental(c1.Id, m.Id);

            _service.Submit(c2.Id, r.Id, 8, 4);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Submit_RentalNotReturned_Throws()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = c.Id,
                MovieId = m.Id,
                RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                DailyRate = 3.99m,
                Status = RentalStatus.Active
            };
            _rentalRepo.Add(r);

            _service.Submit(c.Id, r.Id, 8, 4);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Submit_DuplicateSurvey_Throws()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);

            SubmitSurvey(c.Id, r.Id);
            SubmitSurvey(c.Id, r.Id); // duplicate
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Submit_NpsOutOfRange_Throws()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            _service.Submit(c.Id, r.Id, 11, 4);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Submit_SatisfactionOutOfRange_Throws()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            _service.Submit(c.Id, r.Id, 8, 6);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Submit_CategoryRatingOutOfRange_Throws()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var cats = new Dictionary<SurveyCategory, int>
            {
                { SurveyCategory.Pricing, 6 }
            };
            _service.Submit(c.Id, r.Id, 8, 4, cats);
        }

        [TestMethod]
        public void Submit_WithCategoryRatings_StoresAll()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var cats = new Dictionary<SurveyCategory, int>
            {
                { SurveyCategory.Pricing, 3 },
                { SurveyCategory.StaffFriendliness, 5 },
                { SurveyCategory.DiscQuality, 2 }
            };

            var survey = _service.Submit(c.Id, r.Id, 7, 3, cats);

            Assert.AreEqual(3, survey.CategoryRatings.Count);
            Assert.AreEqual(3, survey.CategoryRatings[SurveyCategory.Pricing]);
            Assert.AreEqual(5, survey.CategoryRatings[SurveyCategory.StaffFriendliness]);
        }

        [TestMethod]
        public void Submit_WithComments_TrimsWhitespace()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);

            var survey = _service.Submit(c.Id, r.Id, 8, 4, null, "  Great service!  ");
            Assert.AreEqual("Great service!", survey.Comments);
        }

        // ── NPS Classification ──────────────────────────────────────

        [TestMethod]
        public void NpsCategory_Score9_IsPromoter()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var s = SubmitSurvey(c.Id, r.Id, nps: 9);
            Assert.AreEqual(NpsCategory.Promoter, s.NpsCategory);
        }

        [TestMethod]
        public void NpsCategory_Score10_IsPromoter()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var s = SubmitSurvey(c.Id, r.Id, nps: 10);
            Assert.AreEqual(NpsCategory.Promoter, s.NpsCategory);
        }

        [TestMethod]
        public void NpsCategory_Score7_IsPassive()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var s = SubmitSurvey(c.Id, r.Id, nps: 7);
            Assert.AreEqual(NpsCategory.Passive, s.NpsCategory);
        }

        [TestMethod]
        public void NpsCategory_Score6_IsDetractor()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var s = SubmitSurvey(c.Id, r.Id, nps: 6);
            Assert.AreEqual(NpsCategory.Detractor, s.NpsCategory);
        }

        // ── CalculateNps ────────────────────────────────────────────

        [TestMethod]
        public void CalculateNps_InsufficientData_ReturnsNull()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            SubmitSurvey(c.Id, r.Id);

            Assert.IsNull(_service.CalculateNps());
        }

        [TestMethod]
        public void CalculateNps_AllPromoters_Returns100()
        {
            var c = AddCustomer();
            for (int i = 0; i < 5; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, nps: 10);
            }

            Assert.AreEqual(100.0, _service.CalculateNps());
        }

        [TestMethod]
        public void CalculateNps_AllDetractors_ReturnsMinus100()
        {
            var c = AddCustomer();
            for (int i = 0; i < 5; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, nps: 3);
            }

            Assert.AreEqual(-100.0, _service.CalculateNps());
        }

        [TestMethod]
        public void CalculateNps_MixedScores_CorrectFormula()
        {
            var c = AddCustomer();
            // 2 promoters (9,10), 2 passives (7,8), 1 detractor (4)
            int[] scores = { 9, 10, 7, 8, 4 };
            foreach (var nps in scores)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, nps: nps);
            }

            // (2/5 - 1/5) * 100 = 20
            Assert.AreEqual(20.0, _service.CalculateNps());
        }

        // ── GetCustomerSurveys ──────────────────────────────────────

        [TestMethod]
        public void GetCustomerSurveys_ReturnsOnlyThatCustomer()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");

            for (int i = 0; i < 3; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c1.Id, m.Id);
                SubmitSurvey(c1.Id, r.Id);
            }
            var m2 = AddMovie();
            var r2 = AddReturnedRental(c2.Id, m2.Id);
            SubmitSurvey(c2.Id, r2.Id);

            Assert.AreEqual(3, _service.GetCustomerSurveys(c1.Id).Count);
            Assert.AreEqual(1, _service.GetCustomerSurveys(c2.Id).Count);
        }

        // ── GenerateReport ──────────────────────────────────────────

        [TestMethod]
        public void GenerateReport_NoSurveys_ReturnsEmpty()
        {
            var report = _service.GenerateReport();
            Assert.AreEqual(0, report.TotalResponses);
        }

        [TestMethod]
        public void GenerateReport_WithData_PopulatesAllFields()
        {
            var c = AddCustomer();
            var cats = new Dictionary<SurveyCategory, int>
            {
                { SurveyCategory.Pricing, 4 },
                { SurveyCategory.StaffFriendliness, 5 },
                { SurveyCategory.DiscQuality, 2 }
            };

            for (int i = 0; i < 6; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, nps: 9, satisfaction: 4, categories: cats,
                    comments: "Nice!");
            }

            var report = _service.GenerateReport();

            Assert.AreEqual(6, report.TotalResponses);
            Assert.IsTrue(report.AverageNps > 0);
            Assert.AreEqual(100.0, report.NpsScore); // all promoters
            Assert.AreEqual(4.0, report.AverageSatisfaction);
            Assert.AreEqual(3, report.CategoryAverages.Count);
            Assert.IsNotNull(report.StrongestCategory);
            Assert.IsNotNull(report.WeakestCategory);
            Assert.AreEqual(SurveyCategory.StaffFriendliness, report.StrongestCategory);
            Assert.AreEqual(SurveyCategory.DiscQuality, report.WeakestCategory);
            Assert.AreEqual(100.0, report.WouldRentAgainPercent);
            Assert.IsNotNull(report.OverallGrade);
        }

        [TestMethod]
        public void GenerateReport_GradeAPlus_HighScores()
        {
            var c = AddCustomer();
            for (int i = 0; i < 6; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, nps: 10, satisfaction: 5);
            }

            var report = _service.GenerateReport();
            Assert.AreEqual("A+", report.OverallGrade);
        }

        [TestMethod]
        public void GenerateReport_DateFiltering_Works()
        {
            var c = AddCustomer();
            for (int i = 0; i < 3; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id);
            }

            // Filter to future date range — should return 0
            var report = _service.GenerateReport(
                from: DateTime.Today.AddDays(10),
                to: DateTime.Today.AddDays(20));
            Assert.AreEqual(0, report.TotalResponses);
        }

        [TestMethod]
        public void GenerateReport_InsightsGenerated()
        {
            var c = AddCustomer();
            for (int i = 0; i < 6; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, nps: 10, satisfaction: 5,
                    comments: "Excellent!");
            }

            var report = _service.GenerateReport();
            Assert.IsTrue(report.KeyInsights.Count > 0);
        }

        // ── GetAtRiskCustomers ──────────────────────────────────────

        [TestMethod]
        public void GetAtRiskCustomers_DetractorIdentified()
        {
            var c = AddCustomer("Unhappy Joe");
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            SubmitSurvey(c.Id, r.Id, nps: 2, satisfaction: 1,
                again: RentAgainResponse.No, comments: "Terrible experience");

            var atRisk = _service.GetAtRiskCustomers();

            Assert.AreEqual(1, atRisk.Count);
            Assert.AreEqual("Unhappy Joe", atRisk[0].CustomerName);
            Assert.IsTrue(atRisk[0].RiskLevel > 0);
            Assert.AreEqual("Terrible experience", atRisk[0].LatestComments);
        }

        [TestMethod]
        public void GetAtRiskCustomers_PromoterNotIncluded()
        {
            var c = AddCustomer("Happy Jane");
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            SubmitSurvey(c.Id, r.Id, nps: 10, satisfaction: 5);

            Assert.AreEqual(0, _service.GetAtRiskCustomers().Count);
        }

        // ── GetImprovementOpportunities ─────────────────────────────

        [TestMethod]
        public void GetImprovementOpportunities_LowRatedCategoryFound()
        {
            var c = AddCustomer();
            var cats = new Dictionary<SurveyCategory, int>
            {
                { SurveyCategory.CheckoutSpeed, 1 },
                { SurveyCategory.StaffFriendliness, 5 }
            };

            for (int i = 0; i < 5; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, categories: cats);
            }

            var opps = _service.GetImprovementOpportunities();

            Assert.IsTrue(opps.Count > 0);
            Assert.AreEqual(SurveyCategory.CheckoutSpeed, opps[0].Category);
            Assert.AreEqual("Critical", opps[0].Priority);
            Assert.IsNotNull(opps[0].Suggestion);
        }

        [TestMethod]
        public void GetImprovementOpportunities_HighRatedNotIncluded()
        {
            var c = AddCustomer();
            var cats = new Dictionary<SurveyCategory, int>
            {
                { SurveyCategory.StaffFriendliness, 5 }
            };

            for (int i = 0; i < 5; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id, categories: cats);
            }

            Assert.AreEqual(0, _service.GetImprovementOpportunities().Count);
        }

        // ── GetById / GetAll ────────────────────────────────────────

        [TestMethod]
        public void GetById_ExistingSurvey_Returns()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var s = SubmitSurvey(c.Id, r.Id);

            Assert.IsNotNull(_service.GetById(s.Id));
        }

        [TestMethod]
        public void GetById_NonexistentSurvey_ReturnsNull()
        {
            Assert.IsNull(_service.GetById(99999));
        }

        [TestMethod]
        public void GetAll_ReturnsAllSubmitted()
        {
            var c = AddCustomer();
            for (int i = 0; i < 3; i++)
            {
                var m = AddMovie();
                var r = AddReturnedRental(c.Id, m.Id);
                SubmitSurvey(c.Id, r.Id);
            }

            Assert.AreEqual(3, _service.GetAll().Count);
        }

        // ── Invitations ─────────────────────────────────────────────

        [TestMethod]
        public void GetPendingInvitations_Empty_Initially()
        {
            Assert.AreEqual(0, _service.GetPendingInvitations().Count);
        }

        [TestMethod]
        public void Submit_MarksInvitationCompleted()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);

            // Generate invitation then submit survey
            _service.GenerateInvitations();
            SubmitSurvey(c.Id, r.Id);

            Assert.AreEqual(0, _service.GetPendingInvitations().Count);
        }

        // ── NPS Edge Cases ──────────────────────────────────────────

        [TestMethod]
        public void Submit_NpsScore0_Valid()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var s = SubmitSurvey(c.Id, r.Id, nps: 0);

            Assert.AreEqual(0, s.NpsScore);
            Assert.AreEqual(NpsCategory.Detractor, s.NpsCategory);
        }

        [TestMethod]
        public void Submit_Satisfaction1_Valid()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            var s = SubmitSurvey(c.Id, r.Id, satisfaction: 1);
            Assert.AreEqual(1, s.OverallSatisfaction);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Submit_NegativeNps_Throws()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            _service.Submit(c.Id, r.Id, -1, 4);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Submit_Satisfaction0_Throws()
        {
            var c = AddCustomer();
            var m = AddMovie();
            var r = AddReturnedRental(c.Id, m.Id);
            _service.Submit(c.Id, r.Id, 8, 0);
        }
    }
}
