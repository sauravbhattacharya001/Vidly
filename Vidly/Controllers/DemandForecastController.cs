using System;
using System.Web.Mvc;
using Newtonsoft.Json;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class DemandForecastController : Controller
    {
        private readonly DemandForecastService _service;

        public DemandForecastController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository())
        {
        }

        public DemandForecastController(IMovieRepository movieRepo, IRentalRepository rentalRepo)
        {
            _service = new DemandForecastService(
                movieRepo ?? throw new ArgumentNullException(nameof(movieRepo)),
                rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo)));
        }

        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public ActionResult Api()
        {
            var forecast = _service.GenerateForecast();
            return Content(
                JsonConvert.SerializeObject(forecast, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                "application/json");
        }
    }
}
