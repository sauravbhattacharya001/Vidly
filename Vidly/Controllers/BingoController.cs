using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class BingoController : Controller
    {
        private readonly BingoService _bingoService;

        public BingoController()
        {
            _bingoService = new BingoService();
        }

        // GET: Bingo
        public ActionResult Index()
        {
            var viewModel = new BingoViewModel
            {
                AvailableThemes = _bingoService.GetThemes()
            };
            return View(viewModel);
        }

        // POST: Bingo/Generate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Generate(BingoRequest request)
        {
            if (request == null)
                request = new BingoRequest();

            // If genre specified, auto-map to theme
            if (request.Genre.HasValue && string.IsNullOrWhiteSpace(request.Theme))
                request.Theme = _bingoService.GenreToTheme(request.Genre);

            var card = _bingoService.Generate(request);

            var viewModel = new BingoViewModel
            {
                Card = card,
                Request = request,
                AvailableThemes = _bingoService.GetThemes()
            };

            return View("Index", viewModel);
        }

        // GET: Bingo/Print/{id} — printer-friendly card
        public ActionResult Print(string id)
        {
            // Generate a fresh card for the print view
            var card = _bingoService.Generate(new BingoRequest());
            var viewModel = new BingoViewModel
            {
                Card = card,
                AvailableThemes = _bingoService.GetThemes()
            };
            return View(viewModel);
        }
    }
}
