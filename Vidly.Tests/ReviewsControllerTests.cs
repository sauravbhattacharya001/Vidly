using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class ReviewsControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryReviewRepository.ResetEmpty();
            InMemoryCustomerRepository.Reset();
            InMemoryMovieRepository.Reset();
        }

        [TestMethod]
        public void Index_NoParams_ReturnsViewWithViewModel()
        {
            var controller = new ReviewsController();

            var result = controller.Index(null, null, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as ReviewIndexViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Reviews);
            Assert.IsNotNull(vm.Summary);
        }

        [TestMethod]
        public void Index_WithSearchAndMinStars_FiltersReviews()
        {
            var controller = new ReviewsController();

            var result = controller.Index("test", 3, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as ReviewIndexViewModel;
            Assert.IsNotNull(vm);
            Assert.AreEqual("test", vm.SearchQuery);
            Assert.AreEqual(3, vm.MinStars);
        }

        [TestMethod]
        public void Index_WithMessage_SetsStatusMessage()
        {
            var controller = new ReviewsController();

            var result = controller.Index(null, null, "Success!", false) as ViewResult;
            var vm = result?.Model as ReviewIndexViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual("Success!", vm.StatusMessage);
            Assert.IsFalse(vm.IsError);
        }

        [TestMethod]
        public void Movie_InvalidId_ReturnsHttpNotFound()
        {
            var controller = new ReviewsController();

            var result = controller.Movie(9999, null, null);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Movie_ValidId_ReturnsViewWithReviews()
        {
            var controller = new ReviewsController();

            // Movie ID 1 should exist in seed data
            var result = controller.Movie(1, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as ReviewIndexViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.SelectedMovie);
            Assert.AreEqual(1, vm.SelectedMovie.Id);
        }

        [TestMethod]
        public void Create_InvalidStars_RedirectsWithError()
        {
            var controller = new ReviewsController();

            var result = controller.Create(1, 1, 0, "text") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void Create_StarsAboveFive_RedirectsWithError()
        {
            var controller = new ReviewsController();

            var result = controller.Create(1, 1, 6, "text") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void Create_ValidReview_RedirectsWithSuccess()
        {
            var controller = new ReviewsController();

            var result = controller.Create(1, 1, 4, "Great movie!") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.IsNull(result.RouteValues.ContainsKey("error") ? result.RouteValues["error"] : null);
        }

        [TestMethod]
        public void Create_DuplicateReview_RedirectsWithError()
        {
            var controller = new ReviewsController();

            // First review should succeed
            controller.Create(1, 1, 4, "First");
            // Duplicate should fail
            var result = controller.Create(1, 1, 5, "Second") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void IsLocalUrl_ValidRelativePath_ReturnsTrue()
        {
            Assert.IsTrue(ReviewsController.IsLocalUrl("/Reviews"));
            Assert.IsTrue(ReviewsController.IsLocalUrl("/Reviews/Movie/1"));
        }

        [TestMethod]
        public void IsLocalUrl_AbsoluteUrl_ReturnsFalse()
        {
            Assert.IsFalse(ReviewsController.IsLocalUrl("http://evil.com"));
            Assert.IsFalse(ReviewsController.IsLocalUrl("https://evil.com/path"));
        }

        [TestMethod]
        public void IsLocalUrl_ProtocolRelative_ReturnsFalse()
        {
            Assert.IsFalse(ReviewsController.IsLocalUrl("//evil.com"));
        }

        [TestMethod]
        public void IsLocalUrl_BackslashBypass_ReturnsFalse()
        {
            Assert.IsFalse(ReviewsController.IsLocalUrl("/\\evil.com"));
        }

        [TestMethod]
        public void IsLocalUrl_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(ReviewsController.IsLocalUrl(null));
            Assert.IsFalse(ReviewsController.IsLocalUrl(""));
            Assert.IsFalse(ReviewsController.IsLocalUrl("  "));
        }

        [TestMethod]
        public void Constructor_NullReviewRepo_Throws()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new ReviewsController(null, new InMemoryCustomerRepository(), new InMemoryMovieRepository()));
        }
    }
}
