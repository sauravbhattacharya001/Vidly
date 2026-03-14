using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class ExportControllerTests
    {
        private ExportController _controller;

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            _controller = new ExportController(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryRentalRepository());
        }

        // ── Index ─────────────────────────────────────────────────────

        [TestMethod]
        public void Index_Returns_ViewResult()
        {
            var result = _controller.Index() as ViewResult;
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void Index_Sets_MovieCount_InViewBag()
        {
            var result = _controller.Index() as ViewResult;
            Assert.IsNotNull(result);
            int count = result.ViewBag.MovieCount;
            Assert.IsTrue(count >= 3, "Should have at least 3 seeded movies");
        }

        [TestMethod]
        public void Index_Sets_CustomerCount_InViewBag()
        {
            var result = _controller.Index() as ViewResult;
            Assert.IsNotNull(result);
            int count = result.ViewBag.CustomerCount;
            Assert.IsTrue(count >= 1, "Should have at least 1 seeded customer");
        }

        [TestMethod]
        public void Index_Sets_RentalCount_InViewBag()
        {
            var result = _controller.Index() as ViewResult;
            Assert.IsNotNull(result);
            int count = result.ViewBag.RentalCount;
            Assert.IsTrue(count >= 1, "Should have at least 1 seeded rental");
        }

        // ── Movies CSV ────────────────────────────────────────────────

        [TestMethod]
        public void Movies_Csv_Returns_FileResult()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
            Assert.AreEqual("movies.csv", result.FileDownloadName);
        }

        [TestMethod]
        public void Movies_Csv_HasCorrectHeaders()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var header = csv.Split('\n')[0].Trim();
            Assert.AreEqual("Id,Name,ReleaseDate,Genre,Rating", header);
        }

        [TestMethod]
        public void Movies_Csv_ContainsSeededData()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(csv.Contains("The Godfather"), "Should contain seeded movie 'The Godfather'");
            Assert.IsTrue(csv.Contains("Toy Story"), "Should contain seeded movie 'Toy Story'");
        }

        [TestMethod]
        public void Movies_Csv_HasMultipleDataRows()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var lines = csv.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            Assert.IsTrue(lines.Length >= 4, "Header + at least 3 data rows expected");
        }

        [TestMethod]
        public void Movies_NullFormat_DefaultsToCsv()
        {
            var result = _controller.Movies(null) as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
            Assert.AreEqual("movies.csv", result.FileDownloadName);
        }

        // ── Movies JSON ───────────────────────────────────────────────

        [TestMethod]
        public void Movies_Json_Returns_FileResult()
        {
            var result = _controller.Movies("json") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
            Assert.AreEqual("movies.json", result.FileDownloadName);
        }

        [TestMethod]
        public void Movies_Json_IsValidJsonArray()
        {
            var result = _controller.Movies("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(json.StartsWith("["), "JSON should start with array bracket");
            Assert.IsTrue(json.EndsWith("]"), "JSON should end with array bracket");
        }

        [TestMethod]
        public void Movies_Json_ContainsMovieFields()
        {
            var result = _controller.Movies("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(json.Contains("\"Id\""), "JSON should contain Id field");
            Assert.IsTrue(json.Contains("\"Name\""), "JSON should contain Name field");
            Assert.IsTrue(json.Contains("\"Genre\""), "JSON should contain Genre field");
            Assert.IsTrue(json.Contains("\"Rating\""), "JSON should contain Rating field");
        }

        [TestMethod]
        public void Movies_Json_ContainsSeededMovies()
        {
            var result = _controller.Movies("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(json.Contains("The Godfather"), "JSON should contain 'The Godfather'");
            Assert.IsTrue(json.Contains("Toy Story"), "JSON should contain 'Toy Story'");
        }

        [TestMethod]
        public void Movies_Json_CaseInsensitiveFormat()
        {
            var result = _controller.Movies("JSON") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
        }

        // ── Customers CSV ─────────────────────────────────────────────

        [TestMethod]
        public void Customers_Csv_Returns_FileResult()
        {
            var result = _controller.Customers("csv") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
            Assert.AreEqual("customers.csv", result.FileDownloadName);
        }

        [TestMethod]
        public void Customers_Csv_HasCorrectHeaders()
        {
            var result = _controller.Customers("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var header = csv.Split('\n')[0].Trim();
            Assert.AreEqual("Id,Name,Email,Phone,MemberSince,MembershipType", header);
        }

        [TestMethod]
        public void Customers_Csv_ContainsSeededData()
        {
            var result = _controller.Customers("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(csv.Contains("John Smith"), "Should contain seeded customer 'John Smith'");
        }

        [TestMethod]
        public void Customers_NullFormat_DefaultsToCsv()
        {
            var result = _controller.Customers(null) as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
        }

        // ── Customers JSON ────────────────────────────────────────────

        [TestMethod]
        public void Customers_Json_Returns_FileResult()
        {
            var result = _controller.Customers("json") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
            Assert.AreEqual("customers.json", result.FileDownloadName);
        }

        [TestMethod]
        public void Customers_Json_ContainsCustomerFields()
        {
            var result = _controller.Customers("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(json.Contains("\"Name\""), "JSON should contain Name field");
            Assert.IsTrue(json.Contains("\"Email\""), "JSON should contain Email field");
            Assert.IsTrue(json.Contains("\"MembershipType\""), "JSON should contain MembershipType field");
        }

        [TestMethod]
        public void Customers_Json_ContainsSeededCustomers()
        {
            var result = _controller.Customers("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(json.Contains("john.smith@example.com"), "JSON should contain seeded customer email");
        }

        // ── Rentals CSV ───────────────────────────────────────────────

        [TestMethod]
        public void Rentals_Csv_Returns_FileResult()
        {
            var result = _controller.Rentals("csv") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
            Assert.AreEqual("rentals.csv", result.FileDownloadName);
        }

        [TestMethod]
        public void Rentals_Csv_HasCorrectHeaders()
        {
            var result = _controller.Rentals("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var header = csv.Split('\n')[0].Trim();
            Assert.AreEqual("Id,CustomerId,CustomerName,MovieId,MovieName,RentalDate,DueDate,ReturnDate,Status,DailyRate,TotalCost,LateFee", header);
        }

        [TestMethod]
        public void Rentals_Csv_ContainsSeededData()
        {
            var result = _controller.Rentals("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            // Seeded rentals include Shrek! and The Godfather
            Assert.IsTrue(csv.Contains("John Smith"), "Should contain renter name");
        }

        [TestMethod]
        public void Rentals_NullFormat_DefaultsToCsv()
        {
            var result = _controller.Rentals(null) as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
        }

        // ── Rentals JSON ──────────────────────────────────────────────

        [TestMethod]
        public void Rentals_Json_Returns_FileResult()
        {
            var result = _controller.Rentals("json") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
            Assert.AreEqual("rentals.json", result.FileDownloadName);
        }

        [TestMethod]
        public void Rentals_Json_ContainsRentalFields()
        {
            var result = _controller.Rentals("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(json.Contains("\"CustomerId\""), "JSON should contain CustomerId");
            Assert.IsTrue(json.Contains("\"MovieName\""), "JSON should contain MovieName");
            Assert.IsTrue(json.Contains("\"Status\""), "JSON should contain Status");
            Assert.IsTrue(json.Contains("\"DailyRate\""), "JSON should contain DailyRate");
        }

        [TestMethod]
        public void Rentals_Json_IsValidJsonArray()
        {
            var result = _controller.Rentals("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsTrue(json.StartsWith("["), "JSON should be an array");
            Assert.IsTrue(json.EndsWith("]"), "JSON should end with ]");
        }

        // ── CSV Injection Protection ──────────────────────────────────

        [TestMethod]
        public void CsvExport_Movies_NoFormulaInjection()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            Assert.IsNotNull(result);
            var csv = Encoding.UTF8.GetString(result.FileContents);
            AssertNoFormulaInjection(csv);
        }

        [TestMethod]
        public void CsvExport_Customers_NoFormulaInjection()
        {
            var result = _controller.Customers("csv") as FileContentResult;
            Assert.IsNotNull(result);
            var csv = Encoding.UTF8.GetString(result.FileContents);
            AssertNoFormulaInjection(csv);
        }

        [TestMethod]
        public void CsvExport_Rentals_NoFormulaInjection()
        {
            var result = _controller.Rentals("csv") as FileContentResult;
            Assert.IsNotNull(result);
            var csv = Encoding.UTF8.GetString(result.FileContents);
            AssertNoFormulaInjection(csv);
        }

        // ── Constructor Validation ────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new ExportController(null, new InMemoryCustomerRepository(), new InMemoryRentalRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new ExportController(new InMemoryMovieRepository(), null, new InMemoryRentalRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new ExportController(new InMemoryMovieRepository(), new InMemoryCustomerRepository(), null);
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static void AssertNoFormulaInjection(string csv)
        {
            var lines = csv.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var fields = SplitCsvLine(line);
                foreach (var field in fields)
                {
                    var trimmed = field.Trim();
                    if (trimmed.Length == 0) continue;
                    char first = trimmed[0];
                    if (first == '=' || first == '+' || first == '@')
                    {
                        Assert.IsTrue(
                            trimmed.StartsWith("\"'"),
                            $"CSV field '{trimmed}' is vulnerable to formula injection");
                    }
                }
            }
        }

        /// <summary>
        /// Simple CSV field splitter that respects quoted fields containing
        /// commas. Not a full RFC 4180 parser but handles the common case.
        /// </summary>
        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                        current.Append(c);
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
