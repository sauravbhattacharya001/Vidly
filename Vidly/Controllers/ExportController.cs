using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using System.Reflection;
using Vidly.Models;
using Vidly.Repositories;

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
            var json = SimpleJsonSerialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", filename);
        }

        /// <summary>
        /// Lightweight JSON serializer for export data (handles anonymous types
        /// and IEnumerable). Replaces System.Web.Script.Serialization which is
        /// unavailable in the SDK-style test project.
        /// </summary>
        private static string SimpleJsonSerialize(object obj)
        {
            if (obj == null) return "null";

            if (obj is string s)
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                               .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";

            if (obj is bool b) return b ? "true" : "false";
            if (obj is int || obj is long || obj is decimal || obj is double || obj is float)
                return obj.ToString();

            if (obj is DateTime dt) return "\"" + dt.ToString("o") + "\"";
            if (obj is DateTimeOffset dto) return "\"" + dto.ToString("o") + "\"";

            if (obj is System.Collections.IEnumerable enumerable)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                    items.Add(SimpleJsonSerialize(item));
                return "[" + string.Join(",", items) + "]";
            }

            // Object (anonymous type or POCO) — serialize public properties
            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var pairs = new List<string>();
            foreach (var prop in props)
            {
                var val = prop.GetValue(obj);
                pairs.Add("\"" + prop.Name + "\":" + SimpleJsonSerialize(val));
            }
            return "{" + string.Join(",", pairs) + "}";
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
