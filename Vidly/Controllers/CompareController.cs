using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class CompareController : Controller
    {
        private readonly MovieComparisonService _comparisonService;

        public CompareController()
        {
            _comparisonService = new MovieComparisonService();
        }

        public CompareController(MovieComparisonService comparisonService)
        {
            _comparisonService = comparisonService
                ?? throw new ArgumentNullException(nameof(comparisonService));
        }

        // GET: Compare
        public ActionResult Index(string ids)
        {
            var viewModel = new CompareViewModel
            {
                AvailableMovies = _comparisonService.GetAvailableMovies()
            };

            // If IDs are passed via query string, auto-compare
            if (!string.IsNullOrWhiteSpace(ids))
            {
                var movieIds = ids.Split(',')
                    .Select(s => { int id; return int.TryParse(s.Trim(), out id) ? id : -1; })
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                if (movieIds.Count >= 2 && movieIds.Count <= 4)
                {
                    viewModel.SelectedIds = movieIds;
                    try
                    {
                        viewModel.Result = _comparisonService.Compare(movieIds);
                    }
                    catch (ArgumentException ex)
                    {
                        viewModel.ErrorMessage = ex.Message;
                    }
                }
                else if (movieIds.Count > 0)
                {
                    viewModel.ErrorMessage = "Please select between 2 and 4 movies to compare.";
                }
            }

            return View(viewModel);
        }

        // POST: Compare
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(List<int> selectedIds)
        {
            var viewModel = new CompareViewModel
            {
                AvailableMovies = _comparisonService.GetAvailableMovies(),
                SelectedIds = selectedIds ?? new List<int>()
            };

            if (selectedIds == null || selectedIds.Count < 2)
            {
                viewModel.ErrorMessage = "Please select at least 2 movies to compare.";
                return View(viewModel);
            }

            if (selectedIds.Count > 4)
            {
                viewModel.ErrorMessage = "You can compare at most 4 movies at once.";
                return View(viewModel);
            }

            try
            {
                viewModel.Result = _comparisonService.Compare(selectedIds);
            }
            catch (ArgumentException ex)
            {
                viewModel.ErrorMessage = ex.Message;
            }

            return View(viewModel);
        }
    }
}
