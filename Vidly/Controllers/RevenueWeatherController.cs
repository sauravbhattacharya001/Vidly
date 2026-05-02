using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Revenue Weather Map — autonomous revenue pattern analysis presented
    /// as weather phenomena (storms, droughts, fronts, forecasts).
    /// </summary>
    public class RevenueWeatherController : Controller
    {
        private readonly RevenueWeatherService _service;

        public RevenueWeatherController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new SystemClock())
        {
        }

        public RevenueWeatherController(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            IClock clock,
            WeatherEngineConfig config = null)
        {
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new RevenueWeatherService(rentalRepository, movieRepository, clock, config);
        }

        /// <summary>
        /// GET /RevenueWeather — Full weather report.
        /// </summary>
        [HttpGet]
        public ActionResult Index()
        {
            var report = _service.Analyze();
            return Json(new
            {
                report.GeneratedAt,
                report.AnalysisWindowDays,
                report.OverallCondition,
                report.StoreTemperature,
                report.OverallSummary,
                report.HealthScore,
                ActivePhenomena = report.ActivePhenomena.Select(p => new
                {
                    p.Type, p.Description, p.Intensity, p.StartDate,
                    p.EndDate, p.AffectedArea, p.ContributingFactors
                }),
                RecentPhenomena = report.RecentPhenomena.Select(p => new
                {
                    p.Type, p.Description, p.Intensity, p.StartDate,
                    p.EndDate, p.AffectedArea
                }),
                Microclimates = report.Microclimates.Select(m => new
                {
                    Genre = m.Genre.ToString(),
                    m.Condition, m.Temperature, m.Humidity,
                    m.WindSpeed, m.WindDirection, m.Pressure,
                    m.Forecast, m.Advisories
                }),
                Forecasts = report.Forecasts.Select(f => new
                {
                    f.Period, f.ExpectedCondition, f.ConfidencePercent,
                    f.ExpectedRevenue, f.TemperatureRangeLow,
                    f.TemperatureRangeHigh, f.Watches, f.Advisories
                }),
                report.AutonomousInsights,
                report.StormWarnings
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /RevenueWeather/Phenomena — Active and recent phenomena only.
        /// </summary>
        [HttpGet]
        public ActionResult Phenomena()
        {
            var report = _service.Analyze();
            return Json(new
            {
                report.OverallCondition,
                ActivePhenomena = report.ActivePhenomena.Select(p => new
                {
                    p.Type, p.Description, p.Intensity,
                    p.StartDate, p.EndDate, p.AffectedArea,
                    p.ContributingFactors
                }),
                RecentPhenomena = report.RecentPhenomena.Select(p => new
                {
                    p.Type, p.Description, p.Intensity,
                    p.StartDate, p.EndDate, p.AffectedArea
                })
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /RevenueWeather/Forecast — Revenue forecasts only.
        /// </summary>
        [HttpGet]
        public ActionResult Forecast()
        {
            var report = _service.Analyze();
            return Json(new
            {
                Forecasts = report.Forecasts.Select(f => new
                {
                    f.Period, f.ExpectedCondition, f.ConfidencePercent,
                    f.ExpectedRevenue, f.TemperatureRangeLow,
                    f.TemperatureRangeHigh, f.Watches, f.Advisories
                }),
                report.StormWarnings
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /RevenueWeather/Microclimates — Genre microclimates only.
        /// </summary>
        [HttpGet]
        public ActionResult Microclimates()
        {
            var report = _service.Analyze();
            return Json(new
            {
                Microclimates = report.Microclimates.Select(m => new
                {
                    Genre = m.Genre.ToString(),
                    m.Condition, m.Temperature, m.Humidity,
                    m.WindSpeed, m.WindDirection, m.Pressure,
                    m.Forecast, m.Advisories
                })
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
