using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages damage assessment for returned rentals. Staff can log damage
    /// reports, assess fees, resolve or waive charges, and view damage history
    /// per customer or movie.
    /// </summary>
    public class DamageController : Controller
    {
        private readonly IDamageRepository _repository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;

        public DamageController()
            : this(new InMemoryDamageRepository(), new InMemoryCustomerRepository(), new InMemoryMovieRepository())
        {
        }

        public DamageController(
            IDamageRepository repository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // GET: Damage
        public ActionResult Index(DamageStatus? status, DamageSeverity? severity, string message, bool? error)
        {
            var reports = _repository.GetAll();

            if (status.HasValue)
                reports = _repository.GetByStatus(status.Value);
            if (severity.HasValue)
                reports = _repository.GetBySeverity(severity.Value);

            var viewModel = new DamageViewModel
            {
                Reports = reports.OrderByDescending(r => r.ReportedAt),
                Summary = _repository.GetSummary(),
                Customers = _customerRepository.GetAll(),
                Movies = _movieRepository.GetAll(),
                FilterStatus = status,
                FilterSeverity = severity,
                StatusMessage = message,
                IsError = error ?? false,
            };

            return View(viewModel);
        }

        // POST: Damage/Report
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Report(int customerId, int movieId, DamageType damageType,
            DamageSeverity severity, string description, decimal assessedFee)
        {
            if (string.IsNullOrWhiteSpace(description))
                return RedirectToAction("Index", new { message = "Description is required.", error = true });

            var customer = _customerRepository.GetAll().FirstOrDefault(c => c.Id == customerId);
            var movie = _movieRepository.GetAll().FirstOrDefault(m => m.Id == movieId);

            if (customer == null || movie == null)
                return RedirectToAction("Index", new { message = "Invalid customer or movie.", error = true });

            var report = new DamageReport
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MovieId = movieId,
                MovieTitle = movie.Name,
                DamageType = damageType,
                Severity = severity,
                Status = assessedFee > 0 ? DamageStatus.Assessed : DamageStatus.Open,
                Description = description,
                AssessedFee = assessedFee,
            };

            _repository.Add(report);

            return RedirectToAction("Index", new { message = $"Damage report #{report.Id} filed for \"{movie.Name}\"." });
        }

        // POST: Damage/Resolve
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Resolve(int id, DamageStatus resolution, string staffNotes)
        {
            var report = _repository.GetById(id);
            if (report == null)
                return RedirectToAction("Index", new { message = "Report not found.", error = true });

            if (resolution != DamageStatus.Paid && resolution != DamageStatus.Waived)
                return RedirectToAction("Index", new { message = "Invalid resolution. Must be Paid or Waived.", error = true });

            var update = new DamageReport
            {
                Id = id,
                Status = resolution,
                AssessedFee = report.AssessedFee,
                StaffNotes = staffNotes,
                Severity = report.Severity,
            };

            _repository.Update(update);

            var label = resolution == DamageStatus.Waived ? "waived" : "marked as paid";
            return RedirectToAction("Index", new { message = $"Report #{id} {label}." });
        }
    }
}
