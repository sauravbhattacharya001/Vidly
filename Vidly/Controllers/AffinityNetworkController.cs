using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Affinity Network — autonomous co-rental pattern discovery with
    /// Jaccard similarity, cluster detection, and proactive bundling insights.
    /// </summary>
    public class AffinityNetworkController : Controller
    {
        private readonly AffinityNetworkService _service;

        public AffinityNetworkController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public AffinityNetworkController(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _service = new AffinityNetworkService(movieRepo, rentalRepo, customerRepo);
        }

        // GET: AffinityNetwork
        public ActionResult Index()
        {
            var summary = _service.BuildNetwork(30);
            return View(summary);
        }

        // GET: AffinityNetwork/Neighbors/5
        public ActionResult Neighbors(int id)
        {
            try
            {
                var neighbors = _service.GetNeighbors(id, 10);
                return Json(new
                {
                    success = true,
                    movieId = id,
                    neighbors = neighbors.Select(n => new
                    {
                        n.MovieIdA, n.MovieNameA,
                        n.MovieIdB, n.MovieNameB,
                        n.SharedCustomers, n.AffinityScore, n.Strength
                    })
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message },
                    JsonRequestBehavior.AllowGet);
            }
        }

        // GET: AffinityNetwork/Data
        public ActionResult Data()
        {
            var summary = _service.BuildNetwork(50);
            return Json(new
            {
                success = true,
                summary.TotalMovies,
                summary.TotalLinks,
                summary.TotalClusters,
                summary.AverageAffinity,
                affinities = summary.TopAffinities.Select(a => new
                {
                    a.MovieIdA, a.MovieNameA,
                    a.MovieIdB, a.MovieNameB,
                    a.SharedCustomers, a.AffinityScore, a.Strength
                }),
                clusters = summary.Clusters.Select(c => new
                {
                    c.ClusterId, c.Label, c.Cohesion,
                    members = c.Members.Select(m => new
                    {
                        m.MovieId, m.MovieName, m.Genre, m.TotalRentals, m.Connections
                    })
                }),
                insights = summary.Insights.Select(i => new
                {
                    i.Icon, i.Title, i.Description, i.ActionType
                })
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
