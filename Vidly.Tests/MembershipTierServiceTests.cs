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
    public class MembershipTierServiceTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private MembershipTierService _service;

        [TestInitialize]
        public void Setup()
        {
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository(_customerRepo, new InMemoryMovieRepository());
            _service = new MembershipTierService(_customerRepo, _rentalRepo);
        }

        private Customer AddCustomer(string name, MembershipType tier = MembershipType.Basic)
        {
            var c = new Customer { Name = name, MembershipType = tier, MemberSince = DateTime.Today.AddYears(-1) };
            return _customerRepo.Add(c);
        }

        private void AddRentals(int customerId, int count, decimal dailyRate = 5m, bool allReturned = true, int lateDays = 0)
        {
            for (int i = 0; i < count; i++)
            {
                var rental = new Rental
                {
                    CustomerId = customerId,
                    MovieId = 1,
                    RentalDate = DateTime.Today.AddDays(-(count - i)),
                    DueDate = DateTime.Today.AddDays(-(count - i) + 3),
                    DailyRate = dailyRate,
                    Status = allReturned ? RentalStatus.Returned : RentalStatus.Active,
                    ReturnDate = allReturned
                        ? (DateTime?)(DateTime.Today.AddDays(-(count - i) + 3 + lateDays))
                        : null
                };
                _rentalRepo.Add(rental);
            }
        }

        // --- Benefits ---

        [TestMethod]
        public void GetBenefits_Basic_NoDiscount()
        {
            var b = _service.GetBenefits(MembershipType.Basic);
            Assert.AreEqual(0, b.DiscountPercent);
            Assert.AreEqual(2, b.MaxConcurrentRentals);
            Assert.AreEqual(0, b.GraceDays);
            Assert.IsFalse(b.FreeReservations);
            Assert.IsFalse(b.PriorityNewReleases);
        }

        [TestMethod]
        public void GetBenefits_Silver_5Percent()
        {
            var b = _service.GetBenefits(MembershipType.Silver);
            Assert.AreEqual(5, b.DiscountPercent);
            Assert.AreEqual(3, b.MaxConcurrentRentals);
            Assert.AreEqual(1, b.GraceDays);
        }

        [TestMethod]
        public void GetBenefits_Gold_FreeReservations()
        {
            var b = _service.GetBenefits(MembershipType.Gold);
            Assert.AreEqual(10, b.DiscountPercent);
            Assert.IsTrue(b.FreeReservations);
            Assert.IsFalse(b.PriorityNewReleases);
        }

        [TestMethod]
        public void GetBenefits_Platinum_AllPerks()
        {
            var b = _service.GetBenefits(MembershipType.Platinum);
            Assert.AreEqual(15, b.DiscountPercent);
            Assert.AreEqual(8, b.MaxConcurrentRentals);
            Assert.AreEqual(3, b.GraceDays);
            Assert.IsTrue(b.FreeReservations);
            Assert.IsTrue(b.PriorityNewReleases);
        }

        // --- CompareTiers ---

        [TestMethod]
        public void CompareTiers_Returns4Tiers()
        {
            var comp = _service.CompareTiers();
            Assert.AreEqual(4, comp.Tiers.Count);
            Assert.AreEqual(MembershipType.Basic, comp.Tiers[0].Tier);
            Assert.AreEqual(MembershipType.Platinum, comp.Tiers[3].Tier);
        }

        [TestMethod]
        public void CompareTiers_DiscountsIncreaseByTier()
        {
            var comp = _service.CompareTiers();
            for (int i = 1; i < comp.Tiers.Count; i++)
                Assert.IsTrue(comp.Tiers[i].DiscountPercent > comp.Tiers[i - 1].DiscountPercent);
        }

        // --- EvaluateCustomer ---

        [TestMethod]
        public void Evaluate_NoRentals_StaysBasic()
        {
            var c = AddCustomer("Alice");
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(MembershipType.Basic, eval.EvaluatedTier);
            Assert.AreEqual(0, eval.RentalsInPeriod);
        }

        [TestMethod]
        public void Evaluate_5Rentals_25Spend_QualifiesSilver()
        {
            var c = AddCustomer("Bob");
            AddRentals(c.Id, 5, 5m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(MembershipType.Silver, eval.EvaluatedTier);
        }

        [TestMethod]
        public void Evaluate_15Rentals_75Spend_QualifiesGold()
        {
            var c = AddCustomer("Carol");
            AddRentals(c.Id, 15, 5m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(MembershipType.Gold, eval.EvaluatedTier);
        }

        [TestMethod]
        public void Evaluate_30Rentals_150Spend_QualifiesPlatinum()
        {
            var c = AddCustomer("Dave");
            AddRentals(c.Id, 30, 5m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(MembershipType.Platinum, eval.EvaluatedTier);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Evaluate_InvalidCustomer_Throws()
        {
            _service.EvaluateCustomer(9999);
        }

        [TestMethod]
        public void Evaluate_HighLatePercentage_Downgrades()
        {
            var c = AddCustomer("Eve");
            // 15 rentals but all late → won't qualify for Gold (max 25% late)
            AddRentals(c.Id, 15, 5m, true, 5);
            var eval = _service.EvaluateCustomer(c.Id);
            // 100% late, so even Silver requires <= 40%
            Assert.AreEqual(MembershipType.Basic, eval.EvaluatedTier);
        }

        [TestMethod]
        public void Evaluate_ShowsProgressToNextTier()
        {
            var c = AddCustomer("Frank");
            AddRentals(c.Id, 3, 5m); // 3/5 toward Silver
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.IsTrue(eval.ProgressToNextTier > 0);
            Assert.IsTrue(eval.ProgressToNextTier < 1.0);
            Assert.IsNotNull(eval.NextTierRequirement);
        }

        [TestMethod]
        public void Evaluate_Platinum_NoNextTier()
        {
            var c = AddCustomer("Grace");
            AddRentals(c.Id, 30, 5m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(1.0, eval.ProgressToNextTier);
            Assert.IsNull(eval.NextTierRequirement);
        }

        [TestMethod]
        public void Evaluate_TierChanged_FlagSet()
        {
            var c = AddCustomer("Hank", MembershipType.Gold);
            // No rentals → evaluates as Basic
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.IsTrue(eval.TierChanged);
            Assert.IsTrue(eval.IsDowngrade);
            Assert.IsFalse(eval.IsUpgrade);
        }

        [TestMethod]
        public void Evaluate_Upgrade_FlagSet()
        {
            var c = AddCustomer("Ivy");
            AddRentals(c.Id, 5, 5m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.IsTrue(eval.TierChanged);
            Assert.IsTrue(eval.IsUpgrade);
        }

        [TestMethod]
        public void Evaluate_SameTier_NoChange()
        {
            var c = AddCustomer("Jack", MembershipType.Silver);
            AddRentals(c.Id, 5, 5m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.IsFalse(eval.TierChanged);
        }

        [TestMethod]
        public void Evaluate_OnTimeVsLateCountsCorrect()
        {
            var c = AddCustomer("Kate");
            // Add 5 on-time
            AddRentals(c.Id, 5, 5m, true, 0);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(5, eval.OnTimeReturns);
            Assert.AreEqual(0, eval.LateReturns);
        }

        [TestMethod]
        public void Evaluate_ReferenceDateFilters()
        {
            var c = AddCustomer("Leo");
            // Rentals 200 days ago — outside 90-day window
            for (int i = 0; i < 10; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = c.Id, MovieId = 1,
                    RentalDate = DateTime.Today.AddDays(-200 + i),
                    DueDate = DateTime.Today.AddDays(-197 + i),
                    ReturnDate = DateTime.Today.AddDays(-197 + i),
                    DailyRate = 5m, Status = RentalStatus.Returned
                });
            }
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(0, eval.RentalsInPeriod);
            Assert.AreEqual(MembershipType.Basic, eval.EvaluatedTier);
        }

        // --- EvaluateAllCustomers ---

        [TestMethod]
        public void EvaluateAll_ReturnsAllCustomers()
        {
            AddCustomer("A");
            AddCustomer("B");
            AddCustomer("C");
            var evals = _service.EvaluateAllCustomers();
            Assert.AreEqual(3, evals.Count);
        }

        // --- ApplyTierChanges ---

        [TestMethod]
        public void ApplyChanges_UpdatesCustomerTier()
        {
            var c = AddCustomer("Mike");
            AddRentals(c.Id, 5, 5m);
            var changes = _service.ApplyTierChanges();
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(MembershipType.Silver, changes[0].NewTier);
            Assert.AreEqual(MembershipType.Silver, _customerRepo.GetById(c.Id).MembershipType);
        }

        [TestMethod]
        public void ApplyChanges_NoChange_ReturnsEmpty()
        {
            var c = AddCustomer("Nancy", MembershipType.Basic);
            var changes = _service.ApplyTierChanges();
            Assert.AreEqual(0, changes.Count);
        }

        [TestMethod]
        public void ApplyChanges_RecordsHistory()
        {
            var c = AddCustomer("Oscar");
            AddRentals(c.Id, 15, 5m);
            _service.ApplyTierChanges();
            var history = _service.GetChangeHistory();
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(MembershipType.Gold, history[0].NewTier);
        }

        [TestMethod]
        public void ApplyChanges_Downgrade_Records()
        {
            var c = AddCustomer("Pete", MembershipType.Gold);
            // No rentals → should downgrade to Basic
            var changes = _service.ApplyTierChanges();
            Assert.AreEqual(1, changes.Count);
            Assert.IsFalse(changes[0].IsUpgrade);
            Assert.AreEqual(MembershipType.Basic, changes[0].NewTier);
        }

        // --- GetChangeHistory ---

        [TestMethod]
        public void GetCustomerHistory_FiltersByCustomer()
        {
            var c1 = AddCustomer("Q1");
            var c2 = AddCustomer("Q2");
            AddRentals(c1.Id, 5, 5m);
            AddRentals(c2.Id, 15, 5m);
            _service.ApplyTierChanges();
            Assert.AreEqual(1, _service.GetCustomerHistory(c1.Id).Count);
            Assert.AreEqual(1, _service.GetCustomerHistory(c2.Id).Count);
        }

        // --- GetTierDistribution ---

        [TestMethod]
        public void GetDistribution_CountsCorrectly()
        {
            AddCustomer("A", MembershipType.Basic);
            AddCustomer("B", MembershipType.Basic);
            AddCustomer("C", MembershipType.Silver);
            AddCustomer("D", MembershipType.Gold);
            var dist = _service.GetTierDistribution();
            Assert.AreEqual(4, dist.TotalCustomers);
            Assert.AreEqual(2, dist.Counts[MembershipType.Basic]);
            Assert.AreEqual(1, dist.Counts[MembershipType.Silver]);
            Assert.AreEqual(MembershipType.Basic, dist.MostCommonTier);
        }

        [TestMethod]
        public void GetDistribution_PercentagesAddUp()
        {
            AddCustomer("A", MembershipType.Basic);
            AddCustomer("B", MembershipType.Silver);
            var dist = _service.GetTierDistribution();
            var total = dist.Percentages.Values.Sum();
            Assert.AreEqual(1.0, total, 0.01);
        }

        // --- GetNearUpgradeCustomers ---

        [TestMethod]
        public void NearUpgrade_Finds75PercentProgress()
        {
            var c = AddCustomer("Rene");
            AddRentals(c.Id, 4, 5m); // 4/5 rentals toward Silver
            var near = _service.GetNearUpgradeCustomers(0.5);
            Assert.IsTrue(near.Count >= 1);
            Assert.IsTrue(near.Any(n => n.CustomerId == c.Id));
        }

        [TestMethod]
        public void NearUpgrade_ExcludesAlreadyQualified()
        {
            var c = AddCustomer("Sue");
            AddRentals(c.Id, 5, 5m); // Qualifies for Silver
            var near = _service.GetNearUpgradeCustomers(0.5);
            // After eval, tier changed → TierChanged is true, so should be excluded
            Assert.IsFalse(near.Any(n => n.CustomerId == c.Id));
        }

        // --- GetAtRiskCustomers ---

        [TestMethod]
        public void AtRisk_FindsDowngradeCandidates()
        {
            var c = AddCustomer("Tim", MembershipType.Gold);
            // No rentals → would downgrade
            var atRisk = _service.GetAtRiskCustomers();
            Assert.AreEqual(1, atRisk.Count);
            Assert.AreEqual(c.Id, atRisk[0].CustomerId);
        }

        // --- GetDiscountedRate ---

        [TestMethod]
        public void DiscountedRate_Basic_NoDiscount()
        {
            var c = AddCustomer("Uma");
            var rate = _service.GetDiscountedRate(c.Id, 10m);
            Assert.AreEqual(10m, rate);
        }

        [TestMethod]
        public void DiscountedRate_Silver_5Percent()
        {
            var c = AddCustomer("Vick", MembershipType.Silver);
            var rate = _service.GetDiscountedRate(c.Id, 10m);
            Assert.AreEqual(9.50m, rate);
        }

        [TestMethod]
        public void DiscountedRate_Gold_10Percent()
        {
            var c = AddCustomer("Will", MembershipType.Gold);
            var rate = _service.GetDiscountedRate(c.Id, 10m);
            Assert.AreEqual(9.00m, rate);
        }

        [TestMethod]
        public void DiscountedRate_Platinum_15Percent()
        {
            var c = AddCustomer("Xena", MembershipType.Platinum);
            var rate = _service.GetDiscountedRate(c.Id, 10m);
            Assert.AreEqual(8.50m, rate);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void DiscountedRate_InvalidCustomer_Throws()
        {
            _service.GetDiscountedRate(9999, 10m);
        }

        // --- CanRentMore ---

        [TestMethod]
        public void CanRentMore_Basic_Under2_True()
        {
            var c = AddCustomer("Yuri");
            Assert.IsTrue(_service.CanRentMore(c.Id));
        }

        [TestMethod]
        public void CanRentMore_InvalidCustomer_False()
        {
            Assert.IsFalse(_service.CanRentMore(9999));
        }

        // --- GetRemainingSlots ---

        [TestMethod]
        public void RemainingSlots_Basic_NoActive_Returns2()
        {
            var c = AddCustomer("Zoe");
            Assert.AreEqual(2, _service.GetRemainingSlots(c.Id));
        }

        [TestMethod]
        public void RemainingSlots_InvalidCustomer_Returns0()
        {
            Assert.AreEqual(0, _service.GetRemainingSlots(9999));
        }

        [TestMethod]
        public void RemainingSlots_Gold_5Slots()
        {
            var c = AddCustomer("Adam", MembershipType.Gold);
            Assert.AreEqual(5, _service.GetRemainingSlots(c.Id));
        }

        // --- MembershipReport ---

        [TestMethod]
        public void Report_GeneratesCorrectly()
        {
            var c = AddCustomer("Beth");
            AddRentals(c.Id, 5, 5m);
            var report = _service.GetMembershipReport(c.Id);
            Assert.AreEqual(c.Id, report.CustomerId);
            Assert.AreEqual("Beth", report.CustomerName);
            Assert.AreEqual(5, report.TotalRentals);
            Assert.IsNotNull(report.Benefits);
            Assert.IsNotNull(report.LatestEvaluation);
        }

        [TestMethod]
        public void Report_TextNotEmpty()
        {
            var c = AddCustomer("Carl");
            var report = _service.GetMembershipReport(c.Id);
            var text = report.GenerateTextReport();
            Assert.IsTrue(text.Contains("Carl"));
            Assert.IsTrue(text.Contains("Membership Report"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Report_InvalidCustomer_Throws()
        {
            _service.GetMembershipReport(9999);
        }

        [TestMethod]
        public void Report_IncludesHistory()
        {
            var c = AddCustomer("Dawn");
            AddRentals(c.Id, 5, 5m);
            _service.ApplyTierChanges();
            var report = _service.GetMembershipReport(c.Id);
            Assert.AreEqual(1, report.History.Count);
        }

        // --- SummaryReport ---

        [TestMethod]
        public void SummaryReport_ContainsTierCounts()
        {
            AddCustomer("E1", MembershipType.Basic);
            AddCustomer("E2", MembershipType.Silver);
            var summary = _service.GenerateSummaryReport();
            Assert.IsTrue(summary.Contains("Basic"));
            Assert.IsTrue(summary.Contains("Silver"));
            Assert.IsTrue(summary.Contains("Total Customers: 2"));
        }

        [TestMethod]
        public void SummaryReport_IncludesRecentChanges()
        {
            var c = AddCustomer("Faye");
            AddRentals(c.Id, 5, 5m);
            _service.ApplyTierChanges();
            var summary = _service.GenerateSummaryReport();
            Assert.IsTrue(summary.Contains("Recent Changes"));
            Assert.IsTrue(summary.Contains("Faye"));
        }

        // --- Constructor validation ---

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new MembershipTierService(null, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new MembershipTierService(_customerRepo, null);
        }

        [TestMethod]
        public void Constructor_NegativePeriod_Defaults90()
        {
            var svc = new MembershipTierService(_customerRepo, _rentalRepo, -5);
            var c = AddCustomer("Test");
            var eval = svc.EvaluateCustomer(c.Id);
            Assert.IsNotNull(eval);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_CustomConfigNoBasic_Throws()
        {
            var configs = new Dictionary<MembershipType, TierConfig>
            {
                [MembershipType.Silver] = new TierConfig { MinRentals = 5, MinSpend = 25m, MaxLatePercentage = 0.4 }
            };
            new MembershipTierService(_customerRepo, _rentalRepo, configs);
        }

        [TestMethod]
        public void Constructor_CustomConfig_Works()
        {
            var configs = new Dictionary<MembershipType, TierConfig>
            {
                [MembershipType.Basic] = new TierConfig { MinRentals = 0, MinSpend = 0, MaxLatePercentage = 1.0, DiscountPercent = 0, MaxConcurrentRentals = 1, GraceDays = 0 },
                [MembershipType.Silver] = new TierConfig { MinRentals = 3, MinSpend = 10m, MaxLatePercentage = 0.5, DiscountPercent = 10, MaxConcurrentRentals = 5, GraceDays = 2 }
            };
            var svc = new MembershipTierService(_customerRepo, _rentalRepo, configs);
            var c = AddCustomer("Custom");
            AddRentals(c.Id, 3, 5m);
            var eval = svc.EvaluateCustomer(c.Id);
            Assert.AreEqual(MembershipType.Silver, eval.EvaluatedTier);
        }

        // --- Edge cases ---

        [TestMethod]
        public void Evaluate_ActiveRentals_NotCountedAsLate()
        {
            var c = AddCustomer("Glen");
            AddRentals(c.Id, 5, 5m, false); // all active, not returned
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.AreEqual(0.0, eval.LatePercentage);
        }

        [TestMethod]
        public void Evaluate_SpendCorrectlyCalculated()
        {
            var c = AddCustomer("Hope");
            AddRentals(c.Id, 5, 10m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.IsTrue(eval.SpendInPeriod > 0);
        }

        [TestMethod]
        public void ApplyChanges_MultipleCustomers()
        {
            var c1 = AddCustomer("I1");
            var c2 = AddCustomer("I2");
            AddRentals(c1.Id, 5, 5m);
            AddRentals(c2.Id, 15, 5m);
            var changes = _service.ApplyTierChanges();
            Assert.AreEqual(2, changes.Count);
        }

        [TestMethod]
        public void GetChangeHistory_OrderedByDateDesc()
        {
            var c1 = AddCustomer("J1");
            var c2 = AddCustomer("J2");
            AddRentals(c1.Id, 5, 5m);
            _service.ApplyTierChanges(DateTime.Today.AddDays(-10));
            AddRentals(c2.Id, 15, 5m);
            _service.ApplyTierChanges(DateTime.Today);
            var history = _service.GetChangeHistory();
            Assert.IsTrue(history.Count >= 2);
            Assert.IsTrue(history[0].ChangeDate >= history[1].ChangeDate);
        }

        [TestMethod]
        public void Evaluate_ReasonContainsDetails()
        {
            var c = AddCustomer("Kay");
            AddRentals(c.Id, 15, 5m);
            var eval = _service.EvaluateCustomer(c.Id);
            Assert.IsTrue(eval.Reason.Contains("Qualified") || eval.Reason.Contains("threshold"));
        }

        [TestMethod]
        public void TierBenefits_HasTierName()
        {
            var b = _service.GetBenefits(MembershipType.Gold);
            Assert.AreEqual("Gold", b.TierName);
        }

        [TestMethod]
        public void Report_MembershipDays_Calculated()
        {
            var c = AddCustomer("Lana");
            var report = _service.GetMembershipReport(c.Id);
            Assert.IsTrue(report.MembershipDays > 0);
        }

        [TestMethod]
        public void Report_LifetimeLatePercentage_Zero_WhenNoReturns()
        {
            var c = AddCustomer("Max");
            var report = _service.GetMembershipReport(c.Id);
            Assert.AreEqual(0, report.LifetimeLatePercentage);
        }
    }
}
