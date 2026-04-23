using System;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class NegotiatorController : Controller
    {
        private readonly NegotiationService _service;
        private readonly ICustomerRepository _customers;
        private readonly IMovieRepository _movies;

        public NegotiatorController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public NegotiatorController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _movies = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customers = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _service = new NegotiationService(movieRepository, rentalRepository, customerRepository);
        }

        // GET: Negotiator
        public ActionResult Index(int? customerId, int? movieId)
        {
            var vm = new NegotiationViewModel
            {
                Customers = _customers.GetAll().OrderBy(c => c.Name).ToList(),
                AvailableMovies = _movies.GetAll().OrderBy(m => m.Name).ToList(),
                SelectedCustomerId = customerId,
                SelectedMovieId = movieId
            };

            // Restore session from TempData if exists
            if (TempData["NegotiationSession"] is string json)
            {
                var session = JsonConvert.DeserializeObject<NegotiationSession>(json);
                if (session != null && session.CustomerId == (customerId ?? 0))
                {
                    vm.Session = session;
                    // Keep session alive across requests
                    TempData["NegotiationSession"] = json;
                }
            }

            return View(vm);
        }

        // POST: Negotiator/Start
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Start(int customerId, int movieId)
        {
            try
            {
                var session = _service.StartNegotiation(customerId, movieId);
                TempData["NegotiationSession"] = JsonConvert.SerializeObject(session);
                return RedirectToAction("Index", new { customerId, movieId });
            }
            catch (KeyNotFoundException ex)
            {
                TempData["NegotiationError"] = ex.Message;
                return RedirectToAction("Index", new { customerId, movieId });
            }
        }

        // POST: Negotiator/Counter
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Counter(int customerId, int movieId, string argument)
        {
            if (TempData["NegotiationSession"] is string json)
            {
                var session = JsonConvert.DeserializeObject<NegotiationSession>(json);
                if (session != null)
                {
                    session = _service.Negotiate(session, argument ?? "");
                    TempData["NegotiationSession"] = JsonConvert.SerializeObject(session);
                }
            }

            return RedirectToAction("Index", new { customerId, movieId });
        }

        // POST: Negotiator/Accept
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Accept(int customerId, int movieId)
        {
            if (TempData["NegotiationSession"] is string json)
            {
                var session = JsonConvert.DeserializeObject<NegotiationSession>(json);
                if (session != null)
                {
                    session.Status = NegotiationStatus.Accepted;
                    TempData["NegotiationSession"] = JsonConvert.SerializeObject(session);
                }
            }

            return RedirectToAction("Index", new { customerId, movieId });
        }

        // POST: Negotiator/Reject
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Reject(int customerId, int movieId)
        {
            if (TempData["NegotiationSession"] is string json)
            {
                var session = JsonConvert.DeserializeObject<NegotiationSession>(json);
                if (session != null)
                {
                    session.Status = NegotiationStatus.Rejected;
                    TempData["NegotiationSession"] = JsonConvert.SerializeObject(session);
                }
            }

            return RedirectToAction("Index", new { customerId, movieId });
        }
    }
}
