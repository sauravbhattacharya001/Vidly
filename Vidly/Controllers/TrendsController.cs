using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class TrendsController : Controller
    {
        private readonly RentalTrendsService _trendsService;

        public TrendsController()
            : this(new InMemoryRentalRepository(), new InMemoryMovieRepository()) { }

        public TrendsController(IRentalRepository rentalRepository, IMovieRepository movieRepository)
        {
            if (rentalRepository == null) throw new ArgumentNullException(nameof(rentalRepository));
            if (movieRepository == null) throw new ArgumentNullException(nameof(movieRepository));
            _trendsService = new RentalTrendsService(rentalRepository, movieRepository);
        }

        internal TrendsController(RentalTrendsService trendsService)
        {
            _trendsService = trendsService ?? throw new ArgumentNullException(nameof(trendsService));
        }

        public ActionResult Index(int? days, int? top)
        {
            var w = days ?? RentalTrendsService.DefaultWindowDays;
            var t = top ?? RentalTrendsService.DefaultTopCount;
            return View(new TrendsViewModel { Report = _trendsService.GetTrendsReport(w, t), WindowDays = w, TopCount = t });
        }

        public ActionResult Movie(int id, int? days)
        {
            var w = days ?? RentalTrendsService.DefaultWindowDays;
            var trend = _trendsService.GetMovieTrend(id, w);
            if (trend == null) return HttpNotFound();
            ViewBag.WindowDays = w;
            return View(trend);
        }

        public ActionResult Trending(int? days, int? count)
        {
            var w = days ?? RentalTrendsService.DefaultWindowDays;
            var c = count ?? RentalTrendsService.DefaultTopCount;
            ViewBag.WindowDays = w;
            return View(_trendsService.GetTrending(w, c));
        }
    }
}
