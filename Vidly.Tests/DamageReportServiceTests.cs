using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class DamageReportServiceTests
    {
        private DamageReportService _service;

        [TestInitialize]
        public void Setup()
        {
            DamageReportService.Reset();
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            _service = new DamageReportService(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository());
        }

        [TestMethod]
        public void FileReport_ValidDamage_ReturnsReport()
        {
            var report = _service.FileReport(1, 1,
                DiscCondition.Good, DiscCondition.Poor,
                DamageType.Scratches, "Deep scratches on surface");

            Assert.IsNotNull(report);
            Assert.IsTrue(report.Id > 0);
            Assert.AreEqual(1, report.MovieId);
            Assert.AreEqual(1, report.CustomerId);
            Assert.AreEqual(DiscCondition.Good, report.ConditionBefore);
            Assert.AreEqual(DiscCondition.Poor, report.ConditionAfter);
            Assert.AreEqual(DamageType.Scratches, report.DamageType);
            Assert.AreEqual("Deep scratches on surface", report.Notes);
            Assert.IsTrue(report.DamageCharge > 0);
            Assert.IsFalse(report.ChargeCollected);
            Assert.IsFalse(report.Replaced);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void FileReport_ConditionNotWorse_Throws()
        {
            _service.FileReport(1, 1,
                DiscCondition.Poor, DiscCondition.Good,
                DamageType.Scratches);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void FileReport_SameCondition_Throws()
        {
            _service.FileReport(1, 1,
                DiscCondition.Good, DiscCondition.Good,
                DamageType.Scratches);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void FileReport_InvalidMovie_Throws()
        {
            _service.FileReport(999, 1,
                DiscCondition.Good, DiscCondition.Poor,
                DamageType.Scratches);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void FileReport_InvalidCustomer_Throws()
        {
            _service.FileReport(1, 999,
                DiscCondition.Good, DiscCondition.Poor,
                DamageType.Scratches);
        }

        [TestMethod]
        public void FileReport_AutoCalculatesCharge()
        {
            var minor = _service.FileReport(1, 1,
                DiscCondition.Good, DiscCondition.Fair,
                DamageType.Scratches);
            var major = _service.FileReport(1, 1,
                DiscCondition.Good, DiscCondition.Damaged,
                DamageType.Cracks);

            Assert.IsTrue(major.DamageCharge > minor.DamageCharge);
        }

        [TestMethod]
        public void FileReport_EnrichesNames()
        {
            var report = _service.FileReport(1, 1,
                DiscCondition.Mint, DiscCondition.Fair,
                DamageType.CaseDamage);

            Assert.IsFalse(string.IsNullOrEmpty(report.MovieName));
            Assert.IsFalse(string.IsNullOrEmpty(report.CustomerName));
        }

        [TestMethod]
        public void CollectCharge_ValidReport_ReturnsTrue()
        {
            var report = _service.FileReport(1, 1,
                DiscCondition.Mint, DiscCondition.Poor,
                DamageType.Scratches);

            Assert.IsTrue(_service.CollectCharge(report.Id));
            Assert.IsTrue(_service.GetById(report.Id).ChargeCollected);
        }

        [TestMethod]
        public void CollectCharge_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_service.CollectCharge(999));
        }

        [TestMethod]
        public void MarkReplaced_ValidReport_ReturnsTrue()
        {
            var report = _service.FileReport(1, 1,
                DiscCondition.Good, DiscCondition.Unplayable,
                DamageType.Cracks);

            Assert.IsTrue(_service.MarkReplaced(report.Id));
            Assert.IsTrue(_service.GetById(report.Id).Replaced);
        }

        [TestMethod]
        public void MarkReplaced_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_service.MarkReplaced(999));
        }

        [TestMethod]
        public void GetAllReports_ReturnsNewestFirst()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Good, DamageType.Scratches);
            _service.FileReport(2, 1, DiscCondition.Good, DiscCondition.Poor, DamageType.Cracks);

            var reports = _service.GetAllReports();
            Assert.AreEqual(2, reports.Count);
            Assert.IsTrue(reports[0].ReportedOn >= reports[1].ReportedOn);
        }

        [TestMethod]
        public void GetReportsByMovie_FiltersCorrectly()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            _service.FileReport(2, 1, DiscCondition.Good, DiscCondition.Poor, DamageType.Cracks);
            _service.FileReport(1, 2, DiscCondition.Good, DiscCondition.Damaged, DamageType.WaterDamage);

            var movie1Reports = _service.GetReportsByMovie(1);
            Assert.AreEqual(2, movie1Reports.Count);
            Assert.IsTrue(movie1Reports.All(r => r.MovieId == 1));
        }

        [TestMethod]
        public void GetReportsByCustomer_FiltersCorrectly()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            _service.FileReport(2, 2, DiscCondition.Good, DiscCondition.Poor, DamageType.Cracks);

            var cust1Reports = _service.GetReportsByCustomer(1);
            Assert.AreEqual(1, cust1Reports.Count);
            Assert.AreEqual(1, cust1Reports[0].CustomerId);
        }

        [TestMethod]
        public void GetMovieDamageSummary_NoReports_ReturnsMint()
        {
            var summary = _service.GetMovieDamageSummary(1);

            Assert.IsNotNull(summary);
            Assert.AreEqual(DiscCondition.Mint, summary.CurrentCondition);
            Assert.AreEqual(0, summary.TotalReports);
            Assert.AreEqual("Low", summary.RiskLevel);
            Assert.IsFalse(summary.NeedsReplacement);
        }

        [TestMethod]
        public void GetMovieDamageSummary_WithReports_CalculatesCorrectly()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            _service.FileReport(1, 2, DiscCondition.Fair, DiscCondition.Damaged, DamageType.Cracks);

            var summary = _service.GetMovieDamageSummary(1);

            Assert.AreEqual(2, summary.TotalReports);
            Assert.AreEqual(DiscCondition.Damaged, summary.CurrentCondition);
            Assert.IsTrue(summary.NeedsReplacement);
            Assert.AreEqual("Medium", summary.RiskLevel);
            Assert.IsTrue(summary.TotalCharges > 0);
        }

        [TestMethod]
        public void GetMovieDamageSummary_InvalidMovie_ReturnsNull()
        {
            Assert.IsNull(_service.GetMovieDamageSummary(999));
        }

        [TestMethod]
        public void GetMoviesNeedingReplacement_FiltersCorrectly()
        {
            _service.FileReport(1, 1, DiscCondition.Good, DiscCondition.Damaged, DamageType.Cracks);
            _service.FileReport(2, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);

            var replacements = _service.GetMoviesNeedingReplacement();
            Assert.AreEqual(1, replacements.Count);
            Assert.AreEqual(1, replacements[0].MovieId);
        }

        [TestMethod]
        public void GetCustomerProfile_NoDamage_ReturnsGoodTier()
        {
            var profile = _service.GetCustomerProfile(1);

            Assert.IsNotNull(profile);
            Assert.AreEqual(0, profile.TotalIncidents);
            Assert.AreEqual("Good", profile.RiskTier);
            Assert.IsFalse(profile.IsRepeatOffender);
        }

        [TestMethod]
        public void GetCustomerProfile_RepeatOffender_FlaggedTier()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            _service.FileReport(2, 1, DiscCondition.Good, DiscCondition.Poor, DamageType.CaseDamage);
            _service.FileReport(3, 1, DiscCondition.Mint, DiscCondition.Damaged, DamageType.Cracks);

            var profile = _service.GetCustomerProfile(1);

            Assert.IsTrue(profile.IsRepeatOffender);
            Assert.AreEqual("Flagged", profile.RiskTier);
            Assert.AreEqual(3, profile.TotalIncidents);
        }

        [TestMethod]
        public void GetCustomerProfile_TwoIncidents_WatchTier()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            _service.FileReport(2, 1, DiscCondition.Good, DiscCondition.Poor, DamageType.CaseDamage);

            var profile = _service.GetCustomerProfile(1);
            Assert.AreEqual("Watch", profile.RiskTier);
            Assert.IsFalse(profile.IsRepeatOffender);
        }

        [TestMethod]
        public void GetCustomerProfile_InvalidCustomer_ReturnsNull()
        {
            Assert.IsNull(_service.GetCustomerProfile(999));
        }

        [TestMethod]
        public void GetFlaggedCustomers_ReturnsOnlyFlagged()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            _service.FileReport(2, 1, DiscCondition.Good, DiscCondition.Poor, DamageType.CaseDamage);
            _service.FileReport(3, 1, DiscCondition.Mint, DiscCondition.Damaged, DamageType.Cracks);
            _service.FileReport(1, 2, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);

            var flagged = _service.GetFlaggedCustomers();
            Assert.AreEqual(1, flagged.Count);
            Assert.AreEqual(1, flagged[0].CustomerId);
        }

        [TestMethod]
        public void GetAnalytics_NoReports_ReturnsEmpty()
        {
            var analytics = _service.GetAnalytics();
            Assert.AreEqual(0, analytics.TotalReports);
            Assert.AreEqual(0, analytics.TotalCharges);
            Assert.AreEqual(DamageType.None, analytics.MostCommonDamageType);
        }

        [TestMethod]
        public void GetAnalytics_WithReports_CalculatesCorrectly()
        {
            var r1 = _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            var r2 = _service.FileReport(2, 1, DiscCondition.Good, DiscCondition.Damaged, DamageType.Scratches);
            _service.CollectCharge(r1.Id);

            var analytics = _service.GetAnalytics();

            Assert.AreEqual(2, analytics.TotalReports);
            Assert.IsTrue(analytics.TotalCharges > 0);
            Assert.IsTrue(analytics.CollectedCharges > 0);
            Assert.IsTrue(analytics.CollectionRate > 0);
            Assert.AreEqual(DamageType.Scratches, analytics.MostCommonDamageType);
            Assert.IsNotNull(analytics.MostDamagedMovie);
            Assert.IsNotNull(analytics.WorstOffender);
        }

        [TestMethod]
        public void GetAnalytics_CollectionRate_100Percent()
        {
            var r1 = _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Poor, DamageType.Scratches);
            var r2 = _service.FileReport(2, 1, DiscCondition.Mint, DiscCondition.Poor, DamageType.Cracks);
            _service.CollectCharge(r1.Id);
            _service.CollectCharge(r2.Id);

            var analytics = _service.GetAnalytics();
            Assert.AreEqual(100m, analytics.CollectionRate);
        }

        [TestMethod]
        public void FileReport_WorseDamage_HigherCharge()
        {
            var fair = _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            var poor = _service.FileReport(2, 1, DiscCondition.Mint, DiscCondition.Poor, DamageType.Scratches);
            var damaged = _service.FileReport(3, 1, DiscCondition.Mint, DiscCondition.Damaged, DamageType.Cracks);

            Assert.IsTrue(poor.DamageCharge > fair.DamageCharge);
            Assert.IsTrue(damaged.DamageCharge > poor.DamageCharge);
        }

        [TestMethod]
        public void FileReport_UnplayableCondition_HighestCharge()
        {
            var report = _service.FileReport(1, 1,
                DiscCondition.Mint, DiscCondition.Unplayable, DamageType.Cracks);
            Assert.IsTrue(report.DamageCharge >= 24.99m);
        }

        [TestMethod]
        public void CustomerProfile_AverageConditionDrop_Correct()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Good, DamageType.Scratches);
            _service.FileReport(2, 1, DiscCondition.Mint, DiscCondition.Poor, DamageType.Cracks);

            var profile = _service.GetCustomerProfile(1);
            Assert.AreEqual(2.0, profile.AverageConditionDrop, 0.01);
        }

        [TestMethod]
        public void CustomerProfile_LastIncident_IsSet()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);

            var profile = _service.GetCustomerProfile(1);
            Assert.IsNotNull(profile.LastIncident);
        }

        [TestMethod]
        public void MultipleReports_SameMovie_TracksAll()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Fair, DamageType.Scratches);
            _service.FileReport(1, 2, DiscCondition.Fair, DiscCondition.Poor, DamageType.CaseDamage);
            _service.FileReport(1, 1, DiscCondition.Poor, DiscCondition.Damaged, DamageType.Cracks);

            var reports = _service.GetReportsByMovie(1);
            Assert.AreEqual(3, reports.Count);

            var summary = _service.GetMovieDamageSummary(1);
            Assert.IsTrue(summary.NeedsReplacement);
        }

        [TestMethod]
        public void Reset_ClearsAllData()
        {
            _service.FileReport(1, 1, DiscCondition.Mint, DiscCondition.Poor, DamageType.Scratches);
            Assert.AreEqual(1, _service.GetAllReports().Count);

            DamageReportService.Reset();
            Assert.AreEqual(0, _service.GetAllReports().Count);
        }
    }
}
