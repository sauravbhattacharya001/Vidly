using System.Linq;
using System.Web.Mvc;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Connections — NYT Connections-style puzzle game.
    /// Group 16 movies into 4 hidden categories of 4.
    /// </summary>
    public class ConnectionsController : Controller
    {
        private readonly ConnectionsService _connectionsService;

        public ConnectionsController()
        {
            _connectionsService = new ConnectionsService();
        }

        // GET: Connections
        public ActionResult Index(int? puzzle)
        {
            int idx;
            ConnectionsPuzzleData data;

            if (puzzle.HasValue)
            {
                idx = puzzle.Value;
                data = _connectionsService.GetPuzzle(idx);
            }
            else
            {
                data = _connectionsService.GetRandomPuzzle(out idx);
            }

            var vm = new ConnectionsViewModel
            {
                PuzzleIndex = idx,
                Puzzles = Enumerable.Range(0, _connectionsService.PuzzleCount)
                    .Select(i =>
                    {
                        var p = _connectionsService.GetPuzzle(i);
                        return new ConnectionsPuzzle
                        {
                            Title = p.Title,
                            Groups = p.Groups.Select(g => new ConnectionsGroup
                            {
                                Category = g.Category,
                                Difficulty = g.Difficulty,
                                Items = g.Items.ToList()
                            }).ToList()
                        };
                    }).ToList()
            };

            return View(vm);
        }
    }
}
