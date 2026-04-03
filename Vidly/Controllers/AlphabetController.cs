using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Alphabet Challenge — collect movies for every letter A-Z.
    /// A gamified view of catalog coverage by first letter.
    /// </summary>
    public class AlphabetController : Controller
    {
        private readonly AlphabetChallengeService _service;

        public AlphabetController()
            : this(new InMemoryMovieRepository())
        {
        }

        public AlphabetController(IMovieRepository movieRepository)
        {
            _service = new AlphabetChallengeService(movieRepository);
        }

        // GET: Alphabet
        public ActionResult Index()
        {
            var board = _service.GetBoard();
            var viewModel = new AlphabetChallengeViewModel { Board = board };
            return View(viewModel);
        }
    }
}
