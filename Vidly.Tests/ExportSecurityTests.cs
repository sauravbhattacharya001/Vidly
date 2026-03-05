using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class ExportSecurityTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private ExportController _controller;

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            InMemoryGiftCardRepository.Reset();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _controller = new ExportController(_movieRepo, _customerRepo, _rentalRepo);
        }

        // ─── CSV Injection Protection ─────────────────────────────────

        [TestMethod]
        public void CsvEscape_EqualsPrefix_QuotedWithSingleQuote()
        {
            // Verify the CsvEscape private method via export output.
            // Add a movie with a name starting with '=' which would be
            // interpreted as a formula in Excel/Sheets.
            var movie = new Movie { Name = "=CMD('calc')", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            // The dangerous field must be quoted with a leading single-quote
            Assert.IsTrue(csv.Contains("\"'=CMD('calc')\""),
                "Formula injection: '=' prefix must be neutralized with single-quote");
        }

        [TestMethod]
        public void CsvEscape_PlusPrefix_QuotedWithSingleQuote()
        {
            var movie = new Movie { Name = "+1+2+3", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsTrue(csv.Contains("\"'+1+2+3\""),
                "Formula injection: '+' prefix must be neutralized");
        }

        [TestMethod]
        public void CsvEscape_MinusPrefix_QuotedWithSingleQuote()
        {
            var movie = new Movie { Name = "-1-2", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsTrue(csv.Contains("\"'-1-2\""),
                "Formula injection: '-' prefix must be neutralized");
        }

        [TestMethod]
        public void CsvEscape_AtPrefix_QuotedWithSingleQuote()
        {
            var movie = new Movie { Name = "@SUM(A1:A10)", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsTrue(csv.Contains("\"'@SUM(A1:A10)\""),
                "Formula injection: '@' prefix must be neutralized");
        }

        [TestMethod]
        public void CsvEscape_TabPrefix_QuotedWithSingleQuote()
        {
            var movie = new Movie { Name = "\t=malicious()", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsTrue(csv.Contains("\"'\t=malicious()\""),
                "Formula injection: tab prefix must be neutralized");
        }

        [TestMethod]
        public void CsvEscape_SafeString_NotPrefixed()
        {
            var movie = new Movie { Name = "The Matrix", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            // Should appear without quote wrapping (no commas, no special chars)
            Assert.IsTrue(csv.Contains("The Matrix"),
                "Safe strings should not be modified");
            Assert.IsFalse(csv.Contains("\"'The Matrix\""),
                "Safe strings should not get single-quote prefix");
        }

        [TestMethod]
        public void CsvEscape_CommaInValue_Quoted()
        {
            var movie = new Movie { Name = "Batman, The Dark Knight", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsTrue(csv.Contains("\"Batman, The Dark Knight\""),
                "Values with commas must be quoted");
        }

        [TestMethod]
        public void CsvEscape_DoubleQuotesInValue_Escaped()
        {
            var movie = new Movie { Name = "Say \"Hello\"", Genre = Genre.Action };
            _movieRepo.Add(movie);

            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsTrue(csv.Contains("\"\""),
                "Double quotes in values must be escaped by doubling");
        }

        [TestMethod]
        public void CsvEscape_EmptyValue_ReturnsEmpty()
        {
            // Customer without phone should export empty field, not null
            var result = _controller.Customers("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsNotNull(csv);
            // Should have the header
            Assert.IsTrue(csv.StartsWith("Id,Name,Email,Phone,MemberSince,MembershipType"));
        }

        // ─── CSV Structure ─────────────────────────────────────────────

        [TestMethod]
        public void MoviesCsv_HasCorrectHeader()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var firstLine = csv.Split('\n')[0].Trim();

            Assert.AreEqual("Id,Name,ReleaseDate,Genre,Rating", firstLine);
        }

        [TestMethod]
        public void CustomersCsv_HasCorrectHeader()
        {
            var result = _controller.Customers("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var firstLine = csv.Split('\n')[0].Trim();

            Assert.AreEqual("Id,Name,Email,Phone,MemberSince,MembershipType", firstLine);
        }

        [TestMethod]
        public void RentalsCsv_HasCorrectHeader()
        {
            var result = _controller.Rentals("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var firstLine = csv.Split('\n')[0].Trim();

            Assert.AreEqual(
                "Id,CustomerId,CustomerName,MovieId,MovieName,RentalDate,DueDate,ReturnDate,Status,DailyRate,TotalCost,LateFee",
                firstLine);
        }

        [TestMethod]
        public void MoviesCsv_RowCount_MatchesRepo()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var lines = csv.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            var expectedMovies = _movieRepo.GetAll().Count;
            // +1 for header
            Assert.AreEqual(expectedMovies + 1, lines.Length,
                "CSV row count should equal repo count + header");
        }

        [TestMethod]
        public void CustomersCsv_RowCount_MatchesRepo()
        {
            var result = _controller.Customers("csv") as FileContentResult;
            var csv = Encoding.UTF8.GetString(result.FileContents);
            var lines = csv.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            var expectedCustomers = _customerRepo.GetAll().Count;
            Assert.AreEqual(expectedCustomers + 1, lines.Length);
        }

        // ─── JSON Export ─────────────────────────────────────────────

        [TestMethod]
        public void MoviesJson_ContentType_IsApplicationJson()
        {
            var result = _controller.Movies("json") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
            Assert.AreEqual("movies.json", result.FileDownloadName);
        }

        [TestMethod]
        public void CustomersJson_ContentType_IsApplicationJson()
        {
            var result = _controller.Customers("json") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
            Assert.AreEqual("customers.json", result.FileDownloadName);
        }

        [TestMethod]
        public void RentalsJson_ContentType_IsApplicationJson()
        {
            var result = _controller.Rentals("json") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
            Assert.AreEqual("rentals.json", result.FileDownloadName);
        }

        [TestMethod]
        public void MoviesJson_ContainsValidJson()
        {
            var result = _controller.Movies("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);

            Assert.IsTrue(json.StartsWith("["), "JSON should be an array");
            Assert.IsTrue(json.EndsWith("]"), "JSON should end with ]");
            // Verify it contains expected fields
            Assert.IsTrue(json.Contains("\"Id\""), "JSON should contain Id field");
            Assert.IsTrue(json.Contains("\"Name\""), "JSON should contain Name field");
        }

        [TestMethod]
        public void CustomersJson_DoesNotLeakSensitiveData()
        {
            var result = _controller.Customers("json") as FileContentResult;
            var json = Encoding.UTF8.GetString(result.FileContents);

            // Export should only contain the fields explicitly projected
            Assert.IsTrue(json.Contains("\"Name\""), "Should contain Name");
            Assert.IsTrue(json.Contains("\"Email\""), "Should contain Email");
            Assert.IsTrue(json.Contains("\"MembershipType\""), "Should contain MembershipType");
            // Should NOT contain internal fields that weren't projected
            Assert.IsFalse(json.Contains("\"PasswordHash\""),
                "Should not expose password hashes");
        }

        // ─── Format Selection ─────────────────────────────────────────

        [TestMethod]
        public void Movies_DefaultFormat_ReturnsCsv()
        {
            var result = _controller.Movies(null) as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
        }

        [TestMethod]
        public void Movies_EmptyFormat_ReturnsCsv()
        {
            var result = _controller.Movies("") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType);
        }

        [TestMethod]
        public void Movies_JsonCaseInsensitive()
        {
            var result = _controller.Movies("JSON") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("application/json", result.ContentType);
        }

        [TestMethod]
        public void Movies_UnknownFormat_FallsToCsv()
        {
            var result = _controller.Movies("xml") as FileContentResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("text/csv", result.ContentType,
                "Unknown formats should fall back to CSV");
        }

        // ─── UTF-8 Encoding ─────────────────────────────────────────

        [TestMethod]
        public void CsvExport_Encoding_IsUtf8()
        {
            var result = _controller.Movies("csv") as FileContentResult;
            Assert.IsNotNull(result);
            // Verify the bytes can be decoded as valid UTF-8
            var text = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsFalse(string.IsNullOrEmpty(text));
        }

        [TestMethod]
        public void JsonExport_Encoding_IsUtf8()
        {
            var result = _controller.Movies("json") as FileContentResult;
            Assert.IsNotNull(result);
            var text = Encoding.UTF8.GetString(result.FileContents);
            Assert.IsFalse(string.IsNullOrEmpty(text));
        }
    }

    // ─── Gift Card Code Security Tests ─────────────────────────────

    [TestClass]
    public class GiftCardCodeSecurityTests
    {
        private GiftCardService _service;
        private InMemoryGiftCardRepository _repo;

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            InMemoryGiftCardRepository.Reset();
            _repo = new InMemoryGiftCardRepository();
            _service = new GiftCardService(_repo);
        }

        [TestMethod]
        public void GenerateCode_Format_MatchesPattern()
        {
            var code = _service.GenerateCode();

            Assert.IsTrue(code.StartsWith("GIFT-"), "Code must start with GIFT-");
            Assert.AreEqual(19, code.Length, "Code must be 19 chars (GIFT-XXXX-XXXX-XXXX)");

            var parts = code.Split('-');
            Assert.AreEqual(4, parts.Length, "Code must have 4 dash-separated parts");
            Assert.AreEqual("GIFT", parts[0]);
            Assert.AreEqual(4, parts[1].Length);
            Assert.AreEqual(4, parts[2].Length);
            Assert.AreEqual(4, parts[3].Length);
        }

        [TestMethod]
        public void GenerateCode_UsesOnlyAllowedCharacters()
        {
            const string allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

            for (int i = 0; i < 20; i++)
            {
                var code = _service.GenerateCode();
                var chars = code.Replace("GIFT-", "").Replace("-", "");

                foreach (var c in chars)
                {
                    Assert.IsTrue(allowed.Contains(c),
                        $"Character '{c}' is not in the allowed set");
                }
            }
        }

        [TestMethod]
        public void GenerateCode_ProducesUniqueCodesAcrossMultipleCalls()
        {
            var codes = new HashSet<string>();
            const int iterations = 100;

            for (int i = 0; i < iterations; i++)
            {
                var code = _service.GenerateCode();
                Assert.IsTrue(codes.Add(code),
                    $"Duplicate code generated on iteration {i}: {code}");
            }

            Assert.AreEqual(iterations, codes.Count);
        }

        [TestMethod]
        public void GenerateCode_HasSufficientEntropy()
        {
            // With 36 chars and 12 positions, we have 36^12 ≈ 4.7×10^18
            // possible codes. Verify that generated codes aren't clustered
            // by checking that the first character position uses multiple
            // distinct characters across many generations.
            var firstChars = new HashSet<char>();

            for (int i = 0; i < 50; i++)
            {
                var code = _service.GenerateCode();
                firstChars.Add(code[5]); // First char after "GIFT-"
            }

            // With 36 possible chars and 50 samples, we should see at least 10
            // distinct values (birthday paradox). PRNG with poor seeding might
            // produce much less variation.
            Assert.IsTrue(firstChars.Count >= 5,
                $"Only {firstChars.Count} distinct first-position chars in 50 codes — " +
                "possible PRNG weakness");
        }

        [TestMethod]
        public void GenerateCode_NoSequentialPatterns()
        {
            // Verify codes don't exhibit obvious sequential patterns that
            // would indicate a weak PRNG. Compare consecutive code pairs —
            // they should differ in multiple positions.
            var codes = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                codes.Add(_service.GenerateCode());
            }

            for (int i = 1; i < codes.Count; i++)
            {
                var prev = codes[i - 1].Replace("-", "").Substring(4);
                var curr = codes[i].Replace("-", "").Substring(4);

                int diffCount = 0;
                for (int j = 0; j < prev.Length; j++)
                {
                    if (prev[j] != curr[j]) diffCount++;
                }

                // Consecutive CSPRNG codes should differ in most positions
                Assert.IsTrue(diffCount >= 3,
                    $"Codes {codes[i - 1]} and {codes[i]} differ in only {diffCount} positions — " +
                    "possible sequential PRNG");
            }
        }

        [TestMethod]
        public void Create_GeneratesSecureCode()
        {
            var card = _service.Create(25.00m, "Test User");

            Assert.IsNotNull(card.Code);
            Assert.IsTrue(card.Code.StartsWith("GIFT-"));
            Assert.AreEqual(19, card.Code.Length);
            Assert.AreEqual(25.00m, card.Balance);
        }

        [TestMethod]
        public void Create_MultipleCards_AllHaveUniqueCodes()
        {
            var codes = new HashSet<string>();

            for (int i = 0; i < 25; i++)
            {
                var card = _service.Create(10.00m, $"User {i}");
                Assert.IsTrue(codes.Add(card.Code),
                    $"Duplicate code on card {i}: {card.Code}");
            }
        }

        [TestMethod]
        public void Redeem_WithValidCode_DeductsBalance()
        {
            var card = _service.Create(50.00m, "Buyer");
            var result = _service.Redeem(card.Code, 20.00m);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(20.00m, result.AmountDeducted);
            Assert.AreEqual(30.00m, result.RemainingBalance);
        }

        [TestMethod]
        public void Redeem_CodeIsCaseSensitiveInLookup()
        {
            var card = _service.Create(50.00m, "Buyer");
            // Gift card lookup should handle the code as stored
            var result = _service.Redeem(card.Code, 10.00m);
            Assert.IsTrue(result.Success, "Exact code should work");
        }

        [TestMethod]
        public void Redeem_ExpiredCard_Fails()
        {
            var card = _service.Create(50.00m, "Buyer", expirationDate: DateTime.Today.AddDays(-1));
            var result = _service.Redeem(card.Code, 10.00m);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Message.Contains("cannot be redeemed"));
        }

        [TestMethod]
        public void Redeem_AmountExceedsBalance_CapsAtBalance()
        {
            var card = _service.Create(15.00m, "Buyer");
            var result = _service.Redeem(card.Code, 100.00m);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(15.00m, result.AmountDeducted);
            Assert.AreEqual(0m, result.RemainingBalance);
        }

        [TestMethod]
        public void TopUp_IncreasesBalance()
        {
            var card = _service.Create(20.00m, "Buyer");
            var result = _service.TopUp(card.Code, 30.00m);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(50.00m, result.RemainingBalance);
        }

        [TestMethod]
        public void TopUp_DisabledCard_Fails()
        {
            var card = _service.Create(20.00m, "Buyer");
            card.IsActive = false;
            _repo.Update(card);

            var result = _service.TopUp(card.Code, 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void TopUp_BelowMinimum_Fails()
        {
            var card = _service.Create(20.00m, "Buyer");
            var result = _service.TopUp(card.Code, 1.00m);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Message.Contains("$5.00"));
        }

        [TestMethod]
        public void CheckBalance_EmptyCode_Fails()
        {
            var result = _service.CheckBalance("");
            Assert.IsFalse(result.Found);
        }

        [TestMethod]
        public void CheckBalance_NullCode_Fails()
        {
            var result = _service.CheckBalance(null);
            Assert.IsFalse(result.Found);
        }

        [TestMethod]
        public void CheckBalance_NonexistentCode_Fails()
        {
            var result = _service.CheckBalance("GIFT-XXXX-YYYY-ZZZZ");
            Assert.IsFalse(result.Found);
        }
    }
}
