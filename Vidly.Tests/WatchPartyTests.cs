using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class WatchPartyControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
        }

        [TestMethod]
        public void Index_ReturnsViewWithThemes()
        {
            var controller = new WatchPartyController();
            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as WatchPartyViewModel;
            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Themes.Count > 0);
        }

        [TestMethod]
        public void Plan_ValidTheme_ReturnsPlan()
        {
            var controller = new WatchPartyController();
            var result = controller.Plan("movie-marathon", null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as WatchPartyViewModel;
            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.HasPlan);
            Assert.IsNotNull(vm.Plan.ShareCode);
        }

        [TestMethod]
        public void Plan_NullTheme_RedirectsToIndex()
        {
            var controller = new WatchPartyController();
            var result = controller.Plan(null, null) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Plan_UnknownTheme_RedirectsWithError()
        {
            var controller = new WatchPartyController();
            var result = controller.Plan("nonexistent", null) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Plan_WithGuests_SetsGuestCount()
        {
            var controller = new WatchPartyController();
            var result = controller.Plan("date-night", 2) as ViewResult;

            var vm = result.Model as WatchPartyViewModel;
            Assert.AreEqual(2, vm.Plan.GuestCount);
        }

        [TestMethod]
        public void Share_InvalidCode_RedirectsToIndex()
        {
            var controller = new WatchPartyController();
            var result = controller.Share("INVALID") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Share_NullCode_RedirectsToIndex()
        {
            var controller = new WatchPartyController();
            var result = controller.Share(null) as RedirectToRouteResult;

            Assert.IsNotNull(result);
        }
    }

    [TestClass]
    public class WatchPartyServiceTests
    {
        private WatchPartyService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            _service = new WatchPartyService(new InMemoryMovieRepository());
        }

        [TestMethod]
        public void GetThemes_ReturnsAllThemes()
        {
            var themes = _service.GetThemes();
            Assert.AreEqual(6, themes.Count);
        }

        [TestMethod]
        public void GetThemes_EachHasRequiredFields()
        {
            foreach (var theme in _service.GetThemes())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Name));
                Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Emoji));
                Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Description));
                Assert.IsTrue(theme.MovieCount > 0);
                Assert.IsTrue(theme.Snacks.Count > 0);
            }
        }

        [TestMethod]
        public void GeneratePlan_ValidTheme_ReturnsPlan()
        {
            var plan = _service.GeneratePlan("movie-marathon");

            Assert.IsNotNull(plan);
            Assert.IsNotNull(plan.Theme);
            Assert.IsTrue(plan.Movies.Count > 0);
            Assert.IsNotNull(plan.ShareCode);
            Assert.AreEqual(6, plan.ShareCode.Length);
        }

        [TestMethod]
        public void GeneratePlan_InvalidTheme_ReturnsNull()
        {
            var plan = _service.GeneratePlan("nonexistent");
            Assert.IsNull(plan);
        }

        [TestMethod]
        public void GeneratePlan_NullTheme_ReturnsNull()
        {
            var plan = _service.GeneratePlan(null);
            Assert.IsNull(plan);
        }

        [TestMethod]
        public void GeneratePlan_SetsGuestCount()
        {
            var plan = _service.GeneratePlan("date-night", 2);
            Assert.AreEqual(2, plan.GuestCount);
        }

        [TestMethod]
        public void GeneratePlan_HasSnackSuggestions()
        {
            var plan = _service.GeneratePlan("horror-night");
            Assert.IsTrue(plan.SnackSuggestions.Count > 0);
        }

        [TestMethod]
        public void GeneratePlan_HasEstimatedRuntime()
        {
            var plan = _service.GeneratePlan("binge-watch");
            Assert.IsTrue(plan.EstimatedRuntimeMinutes > 0);
        }

        [TestMethod]
        public void GeneratePlan_CaseInsensitive()
        {
            var plan = _service.GeneratePlan("MOVIE-MARATHON");
            Assert.IsNotNull(plan);
        }

        [TestMethod]
        public void FormattedRuntime_CorrectFormat()
        {
            var plan = _service.GeneratePlan("date-night");
            Assert.IsNotNull(plan.FormattedRuntime);
            Assert.IsTrue(plan.FormattedRuntime.Contains("h") || plan.FormattedRuntime.Contains("m"));
        }

        [TestMethod]
        public void ToShareText_ContainsKey()
        {
            var plan = _service.GeneratePlan("movie-marathon");
            var text = plan.ToShareText();

            Assert.IsTrue(text.Contains("Watch Party"));
            Assert.IsTrue(text.Contains(plan.ShareCode));
        }

        [TestMethod]
        public void GetSavedParties_ReturnsCreatedParties()
        {
            _service.GeneratePlan("date-night");
            _service.GeneratePlan("horror-night");

            var saved = _service.GetSavedParties();
            Assert.IsTrue(saved.Count >= 2);
        }

        [TestMethod]
        public void GetPartyCount_MatchesSavedCount()
        {
            var before = _service.GetPartyCount();
            _service.GeneratePlan("throwback");
            Assert.AreEqual(before + 1, _service.GetPartyCount());
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentNullException))]
        public void Constructor_NullRepo_Throws()
        {
            new WatchPartyService(null);
        }
    }
}
