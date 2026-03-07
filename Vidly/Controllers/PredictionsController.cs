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
    /// Late return prediction dashboard — identifies active rentals at risk
    /// of being returned late and suggests proactive actions for staff.
    /// </summary>
    public class PredictionsController : Controller
    {
        private readonly LateReturnPredictorService _predictorService;

        public PredictionsController()
            : this(new LateReturnPredictorService(
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository()))
        {
        }

        public PredictionsController(LateReturnPredictorService predictorService)
        {
            _predictorService = predictorService
                ?? throw new ArgumentNullException(nameof(predictorService));
        }

        // GET: Predictions
        public ActionResult Index(string level = null)
        {
            var predictions = _predictorService.PredictAll();

            if (!string.IsNullOrEmpty(level) && Enum.TryParse<RiskLevel>(level, true, out var filter))
            {
                predictions = predictions.Where(p => p.Level == filter).ToList();
            }

            var viewModel = new LateReturnViewModel
            {
                Summary = _predictorService.GetSummary(),
                Predictions = predictions,
                FilterLevel = level
            };

            return View(viewModel);
        }

        // GET: Predictions/Details/5
        public ActionResult Details(int id)
        {
            try
            {
                var prediction = _predictorService.PredictForRental(id);
                return View(prediction);
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }
            catch (InvalidOperationException)
            {
                return RedirectToAction("Index");
            }
        }
    }
}
