using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class GiftCardsControllerTests
    {
        private IGiftCardRepository _repo;
        private GiftCardsController _controller;
        private GiftCardService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryGiftCardRepository.Reset();
            _repo = new InMemoryGiftCardRepository();
            _controller = new GiftCardsController(_repo);
            _service = new GiftCardService(_repo);
        }

        // ── Index ────────────────────────────────────────────────────

        [TestMethod]
        public void Index_NoFilter_ReturnsViewWithAllCards()
        {
            _service.Create(25.00m, "Alice");
            _service.Create(50.00m, "Bob");

            var result = _controller.Index(null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as GiftCardIndexViewModel;
            Assert.IsNotNull(vm);
            Assert.AreEqual(2, vm.TotalCount);
        }

        [TestMethod]
        public void Index_StatusFilter_FiltersCards()
        {
            var card = _service.Create(25.00m, "Alice");
            card.IsActive = false;
            _repo.Update(card);
            _service.Create(50.00m, "Bob");

            var result = _controller.Index("Active") as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as GiftCardIndexViewModel;
            Assert.IsNotNull(vm);
            // Total count should still reflect all cards
            Assert.AreEqual(2, vm.TotalCount);
            // But the filtered list should have only 1
            Assert.AreEqual(1, vm.GiftCards.Count);
        }

        // ── Create GET ───────────────────────────────────────────────

        [TestMethod]
        public void Create_Get_ReturnsView()
        {
            var result = _controller.Create() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(GiftCardCreateViewModel));
        }

        // ── Create POST ──────────────────────────────────────────────

        [TestMethod]
        public void Create_ValidModel_RedirectsToIndex()
        {
            var model = new GiftCardCreateViewModel
            {
                Value = 25.00m,
                PurchaserName = "Alice",
                HasExpiration = false
            };

            var result = _controller.Create(model) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Create_ValueTooLow_ReturnsViewWithError()
        {
            var model = new GiftCardCreateViewModel
            {
                Value = 1.00m,
                PurchaserName = "Alice"
            };

            var result = _controller.Create(model) as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsFalse(_controller.ModelState.IsValid);
        }

        [TestMethod]
        public void Create_ValueTooHigh_ReturnsViewWithError()
        {
            var model = new GiftCardCreateViewModel
            {
                Value = 999.00m,
                PurchaserName = "Alice"
            };

            var result = _controller.Create(model) as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsFalse(_controller.ModelState.IsValid);
        }

        [TestMethod]
        public void Create_NullModel_ReturnsBadRequest()
        {
            var result = _controller.Create((GiftCardCreateViewModel)null)
                as HttpStatusCodeResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
        }

        [TestMethod]
        public void Create_MissingPurchaserName_ReturnsViewWithError()
        {
            var model = new GiftCardCreateViewModel
            {
                Value = 25.00m,
                PurchaserName = ""
            };

            var result = _controller.Create(model) as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsFalse(_controller.ModelState.IsValid);
        }

        // ── Details ──────────────────────────────────────────────────

        [TestMethod]
        public void Details_ExistingCard_ReturnsView()
        {
            var card = _service.Create(25.00m, "Alice");

            var result = _controller.Details(card.Id) as ViewResult;

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void Details_NonExistentCard_ReturnsNotFound()
        {
            var result = _controller.Details(9999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        // ── Balance GET ──────────────────────────────────────────────

        [TestMethod]
        public void Balance_Get_ReturnsView()
        {
            var result = _controller.Balance() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(GiftCardBalanceViewModel));
        }

        // ── Toggle ───────────────────────────────────────────────────

        [TestMethod]
        public void Toggle_ExistingCard_TogglesActiveAndRedirects()
        {
            var card = _service.Create(25.00m, "Alice");
            Assert.IsTrue(card.IsActive);

            var result = _controller.Toggle(card.Id) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            var updated = _repo.GetById(card.Id);
            Assert.IsFalse(updated.IsActive);
        }

        [TestMethod]
        public void Toggle_NonExistentCard_ReturnsNotFound()
        {
            var result = _controller.Toggle(9999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Toggle_DisabledCard_EnablesIt()
        {
            var card = _service.Create(25.00m, "Alice");
            card.IsActive = false;
            _repo.Update(card);

            _controller.Toggle(card.Id);

            var updated = _repo.GetById(card.Id);
            Assert.IsTrue(updated.IsActive);
        }

        // ── TopUp ────────────────────────────────────────────────────

        [TestMethod]
        public void TopUp_ValidAmount_RedirectsWithSuccessMessage()
        {
            var card = _service.Create(25.00m, "Alice");

            var result = _controller.TopUp(card.Code, 10.00m) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            var updated = _repo.GetById(card.Id);
            Assert.AreEqual(35.00m, updated.Balance);
        }

        [TestMethod]
        public void TopUp_InvalidCode_RedirectsWithErrorMessage()
        {
            var result = _controller.TopUp("INVALID-CODE", 10.00m) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }
    }
}
