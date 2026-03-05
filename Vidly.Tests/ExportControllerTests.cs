using System.Text;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
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

        [TestMethod]
        public void Movies_Csv_Returns_FileResult()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
            Assert.AreEqual("movies.csv", result.FileDownloadName);
        }

        [TestMethod]
        public void Movies_Json_Returns_FileResult()
        {
            var result = _controller.Movies("json") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
        }

        [TestMethod]
        public void Customers_Csv_Returns_FileResult()
        {
            var result = _controller.Customers("csv") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
        }

        [TestMethod]
        public void Rentals_Csv_Returns_FileResult()
        {
            var result = _controller.Rentals("csv") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
        }

        [TestMethod]
        public void CsvExport_DoesNot_ContainFormulaInjection()
        {
            // The in-memory repos have seeded data. Verify the CSV output
            // doesn't contain unquoted formula-injection characters at field
            // starts. We check that no CSV field starts with =, +, -, or @
            // without being prefixed by a single-quote guard.
            var result = _controller.Movies("csv") as FileContentResult;
            Assert.IsNotNull(result);
            var csv = Encoding.UTF8.GetString(result.FileContents);

            // Skip header line
            var lines = csv.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                // Each comma-separated field that starts with a formula char
                // should be quoted with a leading single-quote
                var fields = line.Split(',');
                foreach (var field in fields)
                {
                    var trimmed = field.Trim();
                    if (trimmed.Length == 0) continue;
                    char first = trimmed[0];
                    if (first == '=' || first == '+' || first == '@')
                    {
                        // Must be quoted with single-quote prefix: "'"
                        Assert.IsTrue(
                            trimmed.StartsWith("\"'"),
                            $"CSV field '{trimmed}' is vulnerable to formula injection");
                    }
                }
            }
        }

        [TestMethod]
        public void Index_Returns_ViewWithCounts()
        {
            var result = _controller.Index() as ViewResult;
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.ViewBag.MovieCount);
            Assert.IsNotNull(result.ViewBag.CustomerCount);
            Assert.IsNotNull(result.ViewBag.RentalCount);
        }
    }
}
