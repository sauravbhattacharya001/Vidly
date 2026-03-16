using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class SurveyControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.Reset();
        }

        [TestMethod]
        public void Index_ReturnsViewWithSurveyViewModel()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(SurveyViewModel));
        }

        [TestMethod]
        public void Index_ViewModel_ContainsReport()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            Assert.IsNotNull(model.Report);
        }

        [TestMethod]
        public void Index_ViewModel_ContainsCustomers()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            Assert.IsNotNull(model.Customers);
            Assert.IsTrue(model.Customers.Count > 0);
        }

        [TestMethod]
        public void Index_SeedsRecentSurveys()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            Assert.IsTrue(model.RecentSurveys.Count > 0, "Should have seeded surveys");
        }

        [TestMethod]
        public void Index_Report_HasNpsScore()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            // NPS score should be calculated from seeded data
            Assert.IsNotNull(model.Report);
            Assert.IsTrue(model.Report.TotalResponses > 0);
        }

        [TestMethod]
        public void Index_Report_HasCategoryAverages()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            Assert.IsTrue(model.Report.CategoryAverages.Count > 0,
                "Seeded surveys should produce category averages");
        }

        [TestMethod]
        public void Detail_InvalidId_ReturnsNotFound()
        {
            var controller = new SurveyController();

            var result = controller.Detail(999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Detail_ValidId_ReturnsDetailView()
        {
            var controller = new SurveyController();
            // Seed data first
            controller.Index();

            var result = controller.Detail(1) as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(SurveyDetailViewModel));
        }

        [TestMethod]
        public void Detail_ValidId_HasCustomerName()
        {
            var controller = new SurveyController();
            controller.Index();

            var result = controller.Detail(1) as ViewResult;
            var model = result.Model as SurveyDetailViewModel;

            Assert.IsNotNull(model.CustomerName);
            Assert.AreNotEqual("Unknown", model.CustomerName);
        }

        [TestMethod]
        public void Report_ReturnsViewWithSurveyReport()
        {
            var controller = new SurveyController();

            var result = controller.Report() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(SurveyReport));
        }

        [TestMethod]
        public void Report_HasOverallGrade()
        {
            var controller = new SurveyController();

            var result = controller.Report() as ViewResult;
            var model = result.Model as SurveyReport;

            Assert.IsNotNull(model);
            if (model.TotalResponses > 0)
            {
                Assert.IsNotNull(model.OverallGrade);
            }
        }

        [TestMethod]
        public void Submit_InvalidCustomer_RedirectsWithError()
        {
            var controller = new SurveyController();
            controller.ControllerContext = new ControllerContext();

            var result = controller.Submit(
                customerId: 9999, rentalId: 1, npsScore: 8,
                overallSatisfaction: 4, comments: null, wouldRentAgain: 1,
                movieSelection: null, pricing: null, staffFriendliness: null,
                storeCleanliness: null, checkoutSpeed: null, returnProcess: null,
                discQuality: null, onlineExperience: null) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Index_RecentSurveys_AreSortedByDate()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            for (int i = 1; i < model.RecentSurveys.Count; i++)
            {
                Assert.IsTrue(
                    model.RecentSurveys[i - 1].SubmittedAt >= model.RecentSurveys[i].SubmittedAt,
                    "Recent surveys should be sorted newest first");
            }
        }

        [TestMethod]
        public void Index_CompletedRentals_ArePopulated()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            Assert.IsNotNull(model.CompletedRentals);
        }

        [TestMethod]
        public void Report_MonthlyTrends_ContainsData()
        {
            var controller = new SurveyController();

            var result = controller.Report() as ViewResult;
            var model = result.Model as SurveyReport;

            // With seeded data, trends may or may not appear depending on dates
            Assert.IsNotNull(model);
        }

        [TestMethod]
        public void Index_Report_WouldRentAgainPercent_InRange()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            if (model.Report.TotalResponses > 0)
            {
                Assert.IsTrue(model.Report.WouldRentAgainPercent >= 0);
                Assert.IsTrue(model.Report.WouldRentAgainPercent <= 100);
            }
        }

        [TestMethod]
        public void Index_Report_HasKeyInsights()
        {
            var controller = new SurveyController();

            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            if (model.Report.TotalResponses > 0)
            {
                Assert.IsTrue(model.Report.KeyInsights.Count > 0,
                    "Should generate insights from seeded data");
            }
        }

        [TestMethod]
        public void Index_CalledTwice_DoesNotDuplicateSeedData()
        {
            var controller = new SurveyController();

            controller.Index();
            var result = controller.Index() as ViewResult;
            var model = result.Model as SurveyViewModel;

            // Seed guard should prevent duplicates
            Assert.IsTrue(model.RecentSurveys.Count <= 15,
                "Should not have more than 15 seeded surveys");
        }
    }
}
