using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Utilities;

namespace Vidly.Controllers
{
    /// <summary>
    /// Provides data export functionality for movies, customers, and rentals
    /// in CSV and JSON formats.
    /// </summary>
    public class ExportController : Controller
    {
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;

        public ExportController()
            : this(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryRentalRepository())
        {
        }

        public ExportController(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        // GET: Export
        public ActionResult Index()
        {
            var movieCount = _movieRepository.GetAll().Count;
            var customerCount = _customerRepository.GetAll().Count;
            var rentalCount = _rentalRepository.GetAll().Count;

            ViewBag.MovieCount = movieCount;
            ViewBag.CustomerCount = customerCount;
            ViewBag.RentalCount = rentalCount;

            return View();
        }

        // GET: Export/Movies?format=csv
        public ActionResult Movies(string format)
        {
            return ExportAs(
                _movieRepository.GetAll(),
                format,
                "movies",
                m => new
                {
                    m.Id,
                    m.Name,
                    ReleaseDate = m.ReleaseDate?.ToString("yyyy-MM-dd"),
                    Genre = m.Genre?.ToString(),
                    m.Rating
                },
                new[] { "Id", "Name", "ReleaseDate", "Genre", "Rating" },
                m => new[]
                {
                    m.Id.ToString(),
                    CsvEscape(m.Name),
                    m.ReleaseDate?.ToString("yyyy-MM-dd"),
                    m.Genre?.ToString(),
                    m.Rating?.ToString()
                });
        }

        // GET: Export/Customers?format=csv
        public ActionResult Customers(string format)
        {
            return ExportAs(
                _customerRepository.GetAll(),
                format,
                "customers",
                c => new
                {
                    c.Id,
                    c.Name,
                    c.Email,
                    c.Phone,
                    MemberSince = c.MemberSince?.ToString("yyyy-MM-dd"),
                    MembershipType = c.MembershipType.ToString()
                },
                new[] { "Id", "Name", "Email", "Phone", "MemberSince", "MembershipType" },
                c => new[]
                {
                    c.Id.ToString(),
                    CsvEscape(c.Name),
                    CsvEscape(c.Email),
                    CsvEscape(c.Phone),
                    c.MemberSince?.ToString("yyyy-MM-dd"),
                    c.MembershipType.ToString()
                });
        }

        // GET: Export/Rentals?format=csv
        public ActionResult Rentals(string format)
        {
            return ExportAs(
                _rentalRepository.GetAll(),
                format,
                "rentals",
                r => new
                {
                    r.Id,
                    r.CustomerId,
                    r.CustomerName,
                    r.MovieId,
                    r.MovieName,
                    RentalDate = r.RentalDate.ToString("yyyy-MM-dd"),
                    DueDate = r.DueDate.ToString("yyyy-MM-dd"),
                    ReturnDate = r.ReturnDate?.ToString("yyyy-MM-dd"),
                    Status = r.Status.ToString(),
                    r.DailyRate,
                    r.TotalCost,
                    r.LateFee
                },
                new[] { "Id", "CustomerId", "CustomerName", "MovieId", "MovieName",
                        "RentalDate", "DueDate", "ReturnDate", "Status",
                        "DailyRate", "TotalCost", "LateFee" },
                r => new[]
                {
                    r.Id.ToString(),
                    r.CustomerId.ToString(),
                    CsvEscape(r.CustomerName),
                    r.MovieId.ToString(),
                    CsvEscape(r.MovieName),
                    r.RentalDate.ToString("yyyy-MM-dd"),
                    r.DueDate.ToString("yyyy-MM-dd"),
                    r.ReturnDate?.ToString("yyyy-MM-dd"),
                    r.Status.ToString(),
                    r.DailyRate.ToString("F2"),
                    r.TotalCost.ToString("F2"),
                    r.LateFee.ToString("F2")
                });
        }

        #region Helpers

        /// <summary>
        /// Generic export helper that eliminates the repeated JSON-or-CSV
        /// branching in each action method. Each caller supplies:
        ///   - the data collection
        ///   - a JSON projection (anonymous object selector)
        ///   - CSV column headers
        ///   - a CSV row-value selector
        /// The format routing, file encoding, and content-type negotiation
        /// happen exactly once here.
        /// </summary>
        private ActionResult ExportAs<TEntity>(
            IReadOnlyList<TEntity> items,
            string format,
            string filenameBase,
            Func<TEntity, object> jsonProjection,
            string[] csvHeaders,
            Func<TEntity, string[]> csvRowValues)
        {
            if (IsJson(format))
            {
                var data = items.Select(jsonProjection);
                return JsonFile(data, $"{filenameBase}.json");
            }

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", csvHeaders));
            foreach (var item in items)
            {
                csv.AppendLine(string.Join(",", csvRowValues(item)));
            }
            return CsvFile(csv, $"{filenameBase}.csv");
        }

        private static bool IsJson(string format)
        {
            return string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
        }

        private ActionResult JsonFile(object data, string filename)
        {
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", filename);
        }

        private ActionResult CsvFile(StringBuilder csv, string filename)
        {
            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", filename);
        }

        /// <summary>
        /// Escapes a string value for safe CSV output, guarding against
        /// CSV injection (formula injection). Values starting with
        /// =, +, -, @, tab, or carriage-return are prefixed with a
        /// single-quote and quoted to neutralize formula execution
        /// in spreadsheet applications (CWE-1236).
        /// </summary>
        internal static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            bool needsQuote = value.Contains(",") || value.Contains("\"") || value.Contains("\n");
            string escaped = value.Replace("\"", "\"\"");

            if (escaped.Length > 0 && "=+-@\t\r".IndexOf(escaped[0]) >= 0)
            {
                return "\"'" + escaped + "\"";
            }

            if (needsQuote)
                return "\"" + escaped + "\"";

            return value;
        }

        #endregion
    }
}
