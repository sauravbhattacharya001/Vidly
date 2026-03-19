using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class TournamentController : Controller
    {
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly MovieTournamentService _tournamentService;

        public TournamentController()
            : this(new InMemoryMovieRepository(), new InMemoryCustomerRepository())
        {
        }

        public TournamentController(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _tournamentService = new MovieTournamentService(
                _movieRepository.GetMovies());
        }

        // GET: Tournament
        public ActionResult Index()
        {
            var vm = new TournamentViewModel
            {
                ActiveTournaments = _tournamentService.ListTournaments(TournamentStatus.InProgress),
                HallOfFame = _tournamentService.GetHallOfFame(),
                MovieRecords = _tournamentService.GetMovieRecords(),
                Customers = _customerRepository.GetCustomers().ToList(),
                Movies = _movieRepository.GetMovies().ToList()
            };
            return View(vm);
        }

        // POST: Tournament/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(string name, int customerId, int size = 8,
            string genreFilter = null)
        {
            try
            {
                Genre? genre = null;
                if (!string.IsNullOrEmpty(genreFilter) && genreFilter != "Any")
                    genre = (Genre)Enum.Parse(typeof(Genre), genreFilter);

                var customer = _customerRepository.GetCustomer(customerId);
                var customerName = customer?.Name ?? "Unknown";

                var tournament = _tournamentService.CreateTournament(
                    name, customerId, customerName, size, genre);

                return RedirectToAction("Bracket", new { id = tournament.Id });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception)
            {
                TempData["Error"] = "An unexpected error occurred. Please try again.";
                return RedirectToAction("Index");
            }
        }

        // GET: Tournament/Bracket/1
        public ActionResult Bracket(int id)
        {
            var tournament = _tournamentService.GetTournament(id);
            if (tournament == null)
            {
                TempData["Error"] = "Tournament not found.";
                return RedirectToAction("Index");
            }

            var vm = new TournamentViewModel
            {
                Tournament = tournament,
                PendingMatches = _tournamentService.GetPendingMatches(id)
            };

            if (vm.PendingMatches.Count > 0)
                vm.CurrentMatch = vm.PendingMatches[0];

            return View(vm);
        }

        // POST: Tournament/Vote
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Vote(int tournamentId, int matchId, int winnerId, string reason)
        {
            try
            {
                _tournamentService.Vote(tournamentId, matchId, winnerId, reason);
                var tournament = _tournamentService.GetTournament(tournamentId);

                if (tournament.Status == TournamentStatus.Completed)
                    return RedirectToAction("Champion", new { id = tournamentId });

                return RedirectToAction("Bracket", new { id = tournamentId });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception)
            {
                TempData["Error"] = "An unexpected error occurred. Please try again.";
                return RedirectToAction("Bracket", new { id = tournamentId });
            }
        }

        // GET: Tournament/Champion/1
        public ActionResult Champion(int id)
        {
            var tournament = _tournamentService.GetTournament(id);
            if (tournament == null || tournament.Status != TournamentStatus.Completed)
            {
                TempData["Error"] = "Tournament not found or not completed.";
                return RedirectToAction("Index");
            }

            var vm = new TournamentViewModel
            {
                Tournament = tournament,
                HallOfFame = _tournamentService.GetHallOfFame(),
                MovieRecords = _tournamentService.GetMovieRecords()
            };
            return View(vm);
        }

        // POST: Tournament/Cancel
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cancel(int id)
        {
            _tournamentService.CancelTournament(id);
            return RedirectToAction("Index");
        }

        // GET: Tournament/Records
        public ActionResult Records()
        {
            var vm = new TournamentViewModel
            {
                MovieRecords = _tournamentService.GetMovieRecords(),
                HallOfFame = _tournamentService.GetHallOfFame()
            };
            return View(vm);
        }
    }
}
