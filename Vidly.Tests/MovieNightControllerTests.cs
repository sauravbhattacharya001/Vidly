using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class MovieNightControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryRentalRepository.Reset();
            InMemoryCustomerRepository.Reset();
        }

        [TestMethod]
        public void Index_Get_ReturnsViewWithThemesAndCustomers()
        {
            var controller = new MovieNightController();

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Themes.Count > 0, "Should list available themes.");
            Assert.IsTrue(vm.Customers.Count > 0, "Should list customers.");
            Assert.IsNull(vm.Plan, "No plan on initial load.");
        }

        [TestMethod]
        public void Plan_Post_SurpriseMe_ReturnsPlanWithMovies()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.SurpriseMe,
                MovieCount = 3,
                EstimatedRuntimeMinutes = 120,
                BreakMinutes = 15
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Plan);
            Assert.IsTrue(vm.Plan.MovieCount > 0, "Should generate movies.");
            Assert.IsTrue(vm.Plan.Slots.Count > 0, "Should have schedule slots.");
            Assert.IsNotNull(vm.Plan.Title);
            Assert.IsTrue(vm.Plan.TotalMinutes > 0);
        }

        [TestMethod]
        public void Plan_Post_GenreFocus_ReturnsPlanWithTitle()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.GenreFocus,
                Genre = Genre.Action,
                MovieCount = 2
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Plan);
            Assert.IsTrue(vm.Plan.Title.Contains("Action") || vm.Plan.MovieCount > 0);
        }

        [TestMethod]
        public void Plan_Post_CriticsChoice_ReturnsHighRatedMovies()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.CriticsChoice,
                MovieCount = 4
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.HasPlan);
            Assert.AreEqual("Critics' Choice Marathon", vm.Plan.Title);
        }

        [TestMethod]
        public void Plan_Post_WithCustomer_ReturnsPlan()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.HiddenGems,
                MovieCount = 3,
                CustomerId = 1
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Plan);
        }

        [TestMethod]
        public void Plan_Post_NullRequest_RedirectsToIndex()
        {
            var controller = new MovieNightController();

            var result = controller.Plan(null);

            Assert.IsInstanceOfType(result, typeof(RedirectToRouteResult));
        }

        [TestMethod]
        public void Plan_Post_IncludesSnackSuggestions()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.GenreMix,
                MovieCount = 4
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            if (vm.HasPlan)
            {
                Assert.IsNotNull(vm.Plan.SnackSuggestions);
                Assert.IsTrue(vm.Plan.SnackSuggestions.Count > 0, "Should suggest snacks.");
            }
        }

        [TestMethod]
        public void Plan_Post_SlotsHaveTimeSlots()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.FanFavorites,
                MovieCount = 3,
                EstimatedRuntimeMinutes = 90,
                BreakMinutes = 10
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            if (vm.HasPlan)
            {
                foreach (var slot in vm.Plan.Slots)
                {
                    Assert.IsTrue(slot.EndTime > slot.StartTime, "End should be after start.");
                    Assert.AreEqual(90, slot.RuntimeMinutes);
                    Assert.IsNotNull(slot.Movie);
                    Assert.IsNotNull(slot.SlotNote);
                }
            }
        }

        [TestMethod]
        public void Plan_Post_NewReleases_ReturnsCorrectTitle()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.NewReleases,
                MovieCount = 2
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            if (vm.HasPlan)
            {
                Assert.AreEqual("New Releases Premiere Night", vm.Plan.Title);
            }
        }

        [TestMethod]
        public void Plan_Post_PreservesRequestInViewModel()
        {
            var controller = new MovieNightController();
            var request = new MovieNightRequest
            {
                Theme = MovieNightTheme.DecadeFocus,
                Decade = 1990,
                MovieCount = 3,
                EstimatedRuntimeMinutes = 100,
                BreakMinutes = 20
            };

            var result = controller.Plan(request) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as MovieNightViewModel;
            Assert.IsNotNull(vm);
            Assert.AreEqual(MovieNightTheme.DecadeFocus, vm.Request.Theme);
            Assert.AreEqual(3, vm.Request.MovieCount);
        }
    }
}
