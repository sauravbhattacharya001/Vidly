using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Clubs — form clubs, manage members, vote on movies, track watchlists.
    /// </summary>
    public class MovieClubController : Controller
    {
        /// <summary>Max length for club name (CWE-770).</summary>
        public const int MaxClubNameLength = 100;

        /// <summary>Max length for club description (CWE-770).</summary>
        public const int MaxDescriptionLength = 2000;

        /// <summary>Max length for poll title (CWE-770).</summary>
        public const int MaxPollTitleLength = 200;

        private readonly IMovieClubRepository _clubRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;

        public MovieClubController()
            : this(
                new InMemoryMovieClubRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository())
        {
        }

        public MovieClubController(
            IMovieClubRepository clubRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _clubRepository = clubRepository ?? throw new ArgumentNullException(nameof(clubRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// GET /MovieClub — list all clubs.
        /// </summary>
        public ActionResult Index()
        {
            var clubs = _clubRepository.GetAll().ToList();
            var memberCounts = new Dictionary<int, int>();
            foreach (var club in clubs)
                memberCounts[club.Id] = _clubRepository.GetMembers(club.Id).Count();

            var viewModel = new MovieClubIndexViewModel
            {
                Clubs = clubs,
                Customers = _customerRepository.GetAll(),
                MemberCounts = memberCounts
            };

            return View(viewModel);
        }

        /// <summary>
        /// GET /MovieClub/Details/1 — club detail with members, watchlist, polls.
        /// </summary>
        public ActionResult Details(int id)
        {
            var club = _clubRepository.GetById(id);
            if (club == null) return HttpNotFound();

            var members = _clubRepository.GetMembers(id).ToList();
            var customers = _customerRepository.GetAll().ToList();
            var customerNames = customers.ToDictionary(c => c.Id, c => c.Name);

            var viewModel = new MovieClubDetailViewModel
            {
                Club = club,
                Members = members,
                Watchlist = _clubRepository.GetWatchlist(id),
                Polls = _clubRepository.GetPolls(id),
                Stats = _clubRepository.GetStats(id),
                AllCustomers = customers,
                AllMovies = _movieRepository.GetAll(),
                CustomerNames = customerNames
            };

            return View(viewModel);
        }

        /// <summary>
        /// POST /MovieClub/Create — create a new club.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(string name, string description, string genre, int maxMembers, decimal groupDiscount, int founderId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Club name is required.";
                return RedirectToAction("Index");
            }

            if (name.Trim().Length > MaxClubNameLength)
            {
                TempData["Error"] = $"Club name cannot exceed {MaxClubNameLength} characters.";
                return RedirectToAction("Index");
            }

            if (description != null && description.Trim().Length > MaxDescriptionLength)
            {
                TempData["Error"] = $"Description cannot exceed {MaxDescriptionLength} characters.";
                return RedirectToAction("Index");
            }

            var founder = _customerRepository.GetById(founderId);
            if (founder == null)
            {
                TempData["Error"] = "Invalid founder.";
                return RedirectToAction("Index");
            }

            var club = new MovieClub
            {
                Name = name.Trim(),
                Description = description?.Trim(),
                Genre = genre?.Trim() ?? "General",
                MaxMembers = Math.Max(2, Math.Min(maxMembers, 100)),
                GroupDiscountPercent = Math.Max(0, Math.Min(groupDiscount, 50)),
                FounderId = founderId
            };

            _clubRepository.Add(club);

            // Auto-add founder as member
            _clubRepository.AddMember(new ClubMembership
            {
                ClubId = club.Id,
                CustomerId = founderId,
                Role = ClubRole.Founder
            });

            TempData["Success"] = $"Club \"{club.Name}\" created!";
            return RedirectToAction("Details", new { id = club.Id });
        }

        /// <summary>
        /// POST /MovieClub/Join — join a club.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Join(int clubId, int customerId)
        {
            var club = _clubRepository.GetById(clubId);
            if (club == null) return HttpNotFound();

            var existing = _clubRepository.GetMembership(clubId, customerId);
            if (existing != null)
            {
                TempData["Error"] = "Already a member of this club.";
                return RedirectToAction("Details", new { id = clubId });
            }

            var memberCount = _clubRepository.GetMembers(clubId).Count();
            if (memberCount >= club.MaxMembers)
            {
                TempData["Error"] = "Club is full.";
                return RedirectToAction("Details", new { id = clubId });
            }

            _clubRepository.AddMember(new ClubMembership
            {
                ClubId = clubId,
                CustomerId = customerId,
                Role = ClubRole.Member
            });

            TempData["Success"] = "Joined the club!";
            return RedirectToAction("Details", new { id = clubId });
        }

        /// <summary>
        /// POST /MovieClub/Leave — leave a club.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Leave(int clubId, int customerId)
        {
            _clubRepository.RemoveMember(clubId, customerId);
            TempData["Success"] = "Left the club.";
            return RedirectToAction("Details", new { id = clubId });
        }

        /// <summary>
        /// POST /MovieClub/AddToWatchlist — add a movie to club watchlist.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddToWatchlist(int clubId, int movieId, int customerId)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
            {
                TempData["Error"] = "Movie not found.";
                return RedirectToAction("Details", new { id = clubId });
            }

            _clubRepository.AddToWatchlist(new ClubWatchlistItem
            {
                ClubId = clubId,
                MovieId = movieId,
                AddedByCustomerId = customerId
            });

            TempData["Success"] = $"Added \"{movie.Name}\" to watchlist!";
            return RedirectToAction("Details", new { id = clubId });
        }

        /// <summary>
        /// POST /MovieClub/MarkWatched — mark a watchlist item as watched with rating.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult MarkWatched(int clubId, int itemId, double rating)
        {
            rating = Math.Max(1, Math.Min(rating, 5));
            _clubRepository.MarkWatched(itemId, rating);
            TempData["Success"] = "Marked as watched!";
            return RedirectToAction("Details", new { id = clubId });
        }

        /// <summary>
        /// POST /MovieClub/CreatePoll — create a movie vote poll.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreatePoll(int clubId, string title, string movieIds, int createdBy)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(movieIds))
            {
                TempData["Error"] = "Poll title and movie options are required.";
                return RedirectToAction("Details", new { id = clubId });
            }

            if (title.Trim().Length > MaxPollTitleLength)
            {
                TempData["Error"] = $"Poll title cannot exceed {MaxPollTitleLength} characters.";
                return RedirectToAction("Details", new { id = clubId });
            }

            var ids = movieIds.Split(',').Select(s => s.Trim()).Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
            if (ids.Count < 2)
            {
                TempData["Error"] = "At least 2 movie options required.";
                return RedirectToAction("Details", new { id = clubId });
            }

            var options = new List<ClubPollOption>();
            foreach (var mid in ids)
            {
                var movie = _movieRepository.GetById(mid);
                if (movie != null)
                    options.Add(new ClubPollOption { MovieId = mid, MovieTitle = movie.Name });
            }

            _clubRepository.AddPoll(new ClubPoll
            {
                ClubId = clubId,
                Title = title.Trim(),
                Options = options,
                CreatedByCustomerId = createdBy
            });

            TempData["Success"] = "Poll created!";
            return RedirectToAction("Details", new { id = clubId });
        }

        /// <summary>
        /// POST /MovieClub/Vote — vote on a poll option.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Vote(int clubId, int pollId, int optionId, int customerId)
        {
            _clubRepository.Vote(pollId, optionId, customerId);
            TempData["Success"] = "Vote cast!";
            return RedirectToAction("Details", new { id = clubId });
        }

        /// <summary>
        /// POST /MovieClub/ClosePoll — close a poll.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ClosePoll(int clubId, int pollId)
        {
            _clubRepository.ClosePoll(pollId);
            TempData["Success"] = "Poll closed.";
            return RedirectToAction("Details", new { id = clubId });
        }
    }
}
