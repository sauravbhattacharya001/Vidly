using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages movie trade-ins — customers can trade physical copies of movies
    /// they own (DVD, Blu-ray, 4K UHD, VHS) for rental credits based on the
    /// format and condition. Staff review pending trade-ins before credits are awarded.
    /// 
    /// GET  /TradeIn           → List all trade-ins with stats
    /// GET  /TradeIn/Submit    → Show submission form
    /// POST /TradeIn/Submit    → Submit a new trade-in
    /// GET  /TradeIn/Pending   → View pending trade-ins (staff)
    /// POST /TradeIn/Accept/5  → Accept a trade-in
    /// POST /TradeIn/Reject/5  → Reject a trade-in
    /// GET  /TradeIn/Quote     → Credit quote calculator (AJAX)
    /// </summary>
    public class TradeInController : Controller
    {
        private readonly ITradeInRepository _tradeInRepository;
        private readonly ICustomerRepository _customerRepository;

        public TradeInController()
            : this(new InMemoryTradeInRepository(), new InMemoryCustomerRepository())
        {
        }

        public TradeInController(
            ITradeInRepository tradeInRepository,
            ICustomerRepository customerRepository)
        {
            _tradeInRepository = tradeInRepository
                ?? throw new ArgumentNullException(nameof(tradeInRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // GET: /TradeIn
        public ActionResult Index()
        {
            var tradeIns = _tradeInRepository.GetAll().ToList();
            var stats = _tradeInRepository.GetStats();
            ViewBag.Stats = stats;
            return View(tradeIns);
        }

        // GET: /TradeIn/Submit
        public ActionResult Submit()
        {
            ViewBag.Customers = _customerRepository.GetAll()
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name });
            ViewBag.Formats = Enum.GetValues(typeof(TradeInFormat))
                .Cast<TradeInFormat>()
                .Select(f => new SelectListItem { Value = ((int)f).ToString(), Text = f.ToString() });
            ViewBag.Conditions = Enum.GetValues(typeof(TradeInCondition))
                .Cast<TradeInCondition>()
                .Select(c => new SelectListItem { Value = ((int)c).ToString(), Text = c.ToString() });
            return View(new TradeIn());
        }

        // POST: /TradeIn/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Submit(TradeIn tradeIn)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Customers = _customerRepository.GetAll()
                    .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name });
                ViewBag.Formats = Enum.GetValues(typeof(TradeInFormat))
                    .Cast<TradeInFormat>()
                    .Select(f => new SelectListItem { Value = ((int)f).ToString(), Text = f.ToString() });
                ViewBag.Conditions = Enum.GetValues(typeof(TradeInCondition))
                    .Cast<TradeInCondition>()
                    .Select(c => new SelectListItem { Value = ((int)c).ToString(), Text = c.ToString() });
                return View(tradeIn);
            }

            _tradeInRepository.Add(tradeIn);
            TempData["Success"] = $"Trade-in submitted for \"{tradeIn.MovieTitle}\". Estimated credit: {tradeIn.CreditsAwarded:C}. Pending staff review.";
            return RedirectToAction("Index");
        }

        // GET: /TradeIn/Pending
        public ActionResult Pending()
        {
            var pending = _tradeInRepository.GetPending().ToList();
            return View(pending);
        }

        // POST: /TradeIn/Accept/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Accept(int id)
        {
            var tradeIn = _tradeInRepository.GetById(id);
            if (tradeIn == null) return HttpNotFound();

            tradeIn.Status = TradeInStatus.Accepted;
            if (tradeIn.CreditsAwarded == 0)
                tradeIn.CreditsAwarded = InMemoryTradeInRepository.CalculateCredits(tradeIn.Format, tradeIn.Condition);
            _tradeInRepository.Update(tradeIn);

            TempData["Success"] = $"Trade-in #{id} accepted. {tradeIn.CreditsAwarded:C} credits awarded.";
            return RedirectToAction("Pending");
        }

        // POST: /TradeIn/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Reject(int id, string reason)
        {
            var tradeIn = _tradeInRepository.GetById(id);
            if (tradeIn == null) return HttpNotFound();

            tradeIn.Status = TradeInStatus.Rejected;
            tradeIn.CreditsAwarded = 0;
            tradeIn.Notes = string.IsNullOrWhiteSpace(reason) ? "Rejected by staff" : reason;
            _tradeInRepository.Update(tradeIn);

            TempData["Success"] = $"Trade-in #{id} rejected.";
            return RedirectToAction("Pending");
        }

        // GET: /TradeIn/Quote?format=1&condition=0
        public JsonResult Quote(TradeInFormat format, TradeInCondition condition)
        {
            var credits = InMemoryTradeInRepository.CalculateCredits(format, condition);
            return Json(new { credits = credits, display = credits.ToString("C") }, JsonRequestBehavior.AllowGet);
        }
    }
}
