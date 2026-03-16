using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Vidly.Filters;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Utilities;

namespace Vidly.Controllers
{
    /// <summary>
    /// Provides data export functionality for movies, customers, and rentals
    /// in CSV and JSON formats.
    /// Rate-limited to prevent automated mass data exfiltration — export
    /// endpoints return full datasets in a single response, so even a
    /// modest request rate can extract the entire database.
    /// </summary>
    [RateLimit(MaxRequests = 5, WindowSeconds = 300,
        Message = "Export rate limit reached. Please wait before exporting again.")]
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
            var movies = _movieRepository.GetAll();

            if (IsJson(format))
            {
                var data = movies.Select(m => new
                {
                    m.Id,
                    m.Name,
                    ReleaseDate = m.ReleaseDate?.ToString("yyyy-MM-dd"),
                    Genre = m.Genre?.ToString(),
                    m.Rating
                });
                return JsonFile(data, "movies.json");
            }

            var csv = new StringBuilder();
            csv.AppendLine("Id,Name,ReleaseDate,Genre,Rating");
            foreach (var m in movies)
            {
                csv.AppendLine($"{m.Id},{CsvEscape(m.Name)},{m.ReleaseDate?.ToString("yyyy-MM-dd")},{m.Genre},{m.Rating}");
            }
            return CsvFile(csv, "movies.csv");
        }

        // GET: Export/Customers?format=csv
        public ActionResult Customers(string format)
        {
            var customers = _customerRepository.GetAll();

            if (IsJson(format))
            {
                var data = customers.Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Email,
                    c.Phone,
                    MemberSince = c.MemberSince?.ToString("yyyy-MM-dd"),
                    MembershipType = c.MembershipType.ToString()
                });
                return JsonFile(data, "customers.json");
            }

            var csv = new StringBuilder();
            csv.AppendLine("Id,Name,Email,Phone,MemberSince,MembershipType");
            foreach (var c in customers)
            {
                csv.AppendLine($"{c.Id},{CsvEscape(c.Name)},{CsvEscape(c.Email)},{CsvEscape(c.Phone)},{c.MemberSince?.ToString("yyyy-MM-dd")},{c.MembershipType}");
            }
            return CsvFile(csv, "customers.csv");
        }

        // GET: Export/Rentals?format=csv
        public ActionResult Rentals(string format)
        {
            var rentals = _rentalRepository.GetAll();

            if (IsJson(format))
            {
                var data = rentals.Select(r => new
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
                });
                return JsonFile(data, "rentals.json");
            }

            var csv = new StringBuilder();
            csv.AppendLine("Id,CustomerId,CustomerName,MovieId,MovieName,RentalDate,DueDate,ReturnDate,Status,DailyRate,TotalCost,LateFee");
            foreach (var r in rentals)
            {
                csv.AppendLine($"{r.Id},{r.CustomerId},{CsvEscape(r.CustomerName)},{r.MovieId},{CsvEscape(r.MovieName)},{r.RentalDate:yyyy-MM-dd},{r.DueDate:yyyy-MM-dd},{r.ReturnDate?.ToString("yyyy-MM-dd")},{r.Status},{r.DailyRate:F2},{r.TotalCost:F2},{r.LateFee:F2}");
            }
            return CsvFile(csv, "rentals.csv");
        }

        #region Helpers

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

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // Guard against CSV injection (formula injection). Values starting
            // with =, +, -, @, tab, or carriage-return can trigger formula
            // execution in spreadsheet applications such as Excel, Google
            // Sheets, and LibreOffice Calc. Prefix with a single-quote to
            // neutralize them and always quote the field.
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
