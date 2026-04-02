using System.Web.Mvc;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Crossword — interactive crossword puzzles with movie-themed clues.
    /// </summary>
    public class CrosswordController : Controller
    {
        /// <summary>
        /// GET /Crossword — main crossword puzzle page.
        /// </summary>
        public ActionResult Index()
        {
            return View();
        }
    }
}
