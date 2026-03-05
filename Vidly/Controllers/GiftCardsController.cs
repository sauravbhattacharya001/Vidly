using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages gift cards: list, create, check balance, redeem, top-up, toggle.
    /// </summary>
    public class GiftCardsController : Controller
    {
        private readonly IGiftCardRepository _giftCardRepository;
        private readonly GiftCardService _giftCardService;

        public GiftCardsController()
            : this(new InMemoryGiftCardRepository()) { }

        public GiftCardsController(IGiftCardRepository giftCardRepository)
        {
            _giftCardRepository = giftCardRepository
                ?? throw new ArgumentNullException(nameof(giftCardRepository));
            _giftCardService = new GiftCardService(_giftCardRepository);
        }

        // GET: GiftCards
        public ActionResult Index(string status)
        {
            var allCards = _giftCardRepository.GetAll();

            var cards = !string.IsNullOrWhiteSpace(status)
                ? allCards.Where(c =>
                    string.Equals(c.StatusDisplay, status, StringComparison.OrdinalIgnoreCase))
                    .ToList().AsReadOnly()
                : allCards;

            var viewModel = new GiftCardIndexViewModel
            {
                GiftCards = cards,
                StatusFilter = status,
                TotalCount = allCards.Count,
                ActiveCount = allCards.Count(c => c.StatusDisplay == "Active"),
                EmptyCount = allCards.Count(c => c.StatusDisplay == "Empty"),
                ExpiredCount = allCards.Count(c => c.StatusDisplay == "Expired" || c.StatusDisplay == "Disabled"),
                TotalOutstandingBalance = allCards.Where(c => c.IsRedeemable).Sum(c => c.Balance)
            };

            return View(viewModel);
        }

        // GET: GiftCards/Create
        public ActionResult Create()
        {
            return View(new GiftCardCreateViewModel());
        }

        // POST: GiftCards/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(GiftCardCreateViewModel model)
        {
            if (model == null)
                return new HttpStatusCodeResult(400);

            if (model.Value < 5.00m || model.Value > 500.00m)
                ModelState.AddModelError("Value", "Value must be between $5.00 and $500.00.");

            if (string.IsNullOrWhiteSpace(model.PurchaserName))
                ModelState.AddModelError("PurchaserName", "Purchaser name is required.");

            if (!ModelState.IsValid)
                return View(model);

            DateTime? expiration = model.HasExpiration
                ? DateTime.Today.AddDays(model.ExpirationDays)
                : (DateTime?)null;

            var card = _giftCardService.Create(
                model.Value,
                model.PurchaserName.Trim(),
                model.RecipientName?.Trim(),
                model.Message?.Trim(),
                expiration);

            TempData["Message"] = $"Gift card created! Code: {card.Code}";
            return RedirectToAction("Index");
        }

        // GET: GiftCards/Details/5
        public ActionResult Details(int id)
        {
            var card = _giftCardRepository.GetById(id);
            if (card == null)
                return HttpNotFound();
            return View(card);
        }

        // GET: GiftCards/Balance
        public ActionResult Balance()
        {
            return View(new GiftCardBalanceViewModel());
        }

        // POST: GiftCards/Balance
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Balance(GiftCardBalanceViewModel model)
        {
            if (model == null)
                return new HttpStatusCodeResult(400);

            model.Result = _giftCardService.CheckBalance(model.Code);
            return View(model);
        }

        // POST: GiftCards/Toggle/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Toggle(int id)
        {
            var card = _giftCardRepository.GetById(id);
            if (card == null)
                return HttpNotFound();

            card.IsActive = !card.IsActive;
            _giftCardRepository.Update(card);
            TempData["Message"] = $"Gift card '{card.Code}' {(card.IsActive ? "enabled" : "disabled")}.";

            return RedirectToAction("Index");
        }

        // POST: GiftCards/TopUp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult TopUp(string code, decimal amount)
        {
            var result = _giftCardService.TopUp(code, amount);
            if (result.Success)
                TempData["Message"] = result.Message;
            else
                TempData["Error"] = result.Message;

            return RedirectToAction("Index");
        }
    }
}
