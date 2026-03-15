using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class RentalReceiptServiceTests
    {
        private RentalReceiptService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            _service = new RentalReceiptService(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository());
        }

        private Customer CreateCustomer(int id, string name, MembershipType tier = MembershipType.Basic)
        {
            var repo = new InMemoryCustomerRepository();
            var customer = new Customer
            {
                Id = id,
                Name = name,
                Email = name.ToLower().Replace(" ", ".") + "@test.com",
                Phone = "(555) 555-0100",
                MembershipType = tier,
                MemberSince = new DateTime(2023, 1, 1)
            };
            repo.Add(customer);
            return customer;
        }

        private Movie CreateMovie(int id, string name, Genre genre = Genre.Action, DateTime? releaseDate = null)
        {
            var repo = new InMemoryMovieRepository();
            var movie = new Movie
            {
                Id = id,
                Name = name,
                Genre = genre,
                ReleaseDate = releaseDate ?? new DateTime(2024, 6, 1)
            };
            repo.Add(movie);
            return movie;
        }

        private Rental CreateRental(int id, int customerId, int movieId, DateTime rentalDate,
            int durationDays = 7, decimal dailyRate = 3.99m, decimal lateFee = 0m,
            RentalStatus status = RentalStatus.Active, DateTime? returnDate = null)
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(durationDays),
                DailyRate = dailyRate,
                LateFee = lateFee,
                Status = status,
                ReturnDate = returnDate,
                CustomerName = "Test Customer",
                MovieName = "Test Movie"
            };
            repo.Add(rental);
            return rental;
        }

        // ── Constructor ─────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RentalReceiptService(null, new InMemoryMovieRepository(), new InMemoryCustomerRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RentalReceiptService(new InMemoryRentalRepository(), null, new InMemoryCustomerRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RentalReceiptService(new InMemoryRentalRepository(), new InMemoryMovieRepository(), null);
        }

        // ── GenerateReceipt ─────────────────────────────────────────

        [TestMethod]
        public void GenerateReceipt_BasicRental_ReturnsCorrectReceipt()
        {
            var customer = CreateCustomer(1, "Alice Smith");
            var movie = CreateMovie(1, "Die Hard", Genre.Action);
            var rental = CreateRental(1, 1, 1, new DateTime(2025, 3, 1), dailyRate: 3.99m);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(1, receipt.RentalId);
            Assert.AreEqual("Alice Smith", receipt.CustomerName);
            Assert.AreEqual("Die Hard", receipt.MovieTitle);
            Assert.AreEqual(3.99m, receipt.DailyRate);
            Assert.IsTrue(receipt.RentalDays >= 1);
            Assert.IsTrue(receipt.BaseCharge > 0);
            Assert.IsTrue(receipt.Total > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateReceipt_NonexistentRental_Throws()
        {
            _service.GenerateReceipt(999);
        }

        [TestMethod]
        public void GenerateReceipt_HasReceiptNumber()
        {
            CreateCustomer(1, "Bob Jones");
            CreateMovie(1, "Terminator");
            CreateRental(1, 1, 1, new DateTime(2025, 3, 1));

            var receipt = _service.GenerateReceipt(1);

            Assert.IsNotNull(receipt.ReceiptNumber);
            Assert.IsTrue(receipt.ReceiptNumber.StartsWith("VDL-"));
            Assert.IsTrue(receipt.ReceiptNumber.Contains("000001"));
        }

        [TestMethod]
        public void GenerateReceipt_BasicMember_NoDiscount()
        {
            CreateCustomer(1, "Basic User", MembershipType.Basic);
            CreateMovie(1, "Movie A");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), dailyRate: 5.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(0m, receipt.MembershipDiscount);
            Assert.IsFalse(receipt.LineItems.Any(li => li.Category == LineItemCategory.Discount));
        }

        [TestMethod]
        public void GenerateReceipt_SilverMember_5PercentDiscount()
        {
            CreateCustomer(1, "Silver User", MembershipType.Silver);
            CreateMovie(1, "Movie B");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2), dailyRate: 10.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            Assert.IsTrue(receipt.MembershipDiscount > 0);
            var discountItem = receipt.LineItems.First(li => li.Category == LineItemCategory.Discount);
            Assert.IsTrue(discountItem.Description.Contains("Silver"));
            Assert.IsTrue(discountItem.Amount < 0);
        }

        [TestMethod]
        public void GenerateReceipt_GoldMember_10PercentDiscount()
        {
            CreateCustomer(1, "Gold User", MembershipType.Gold);
            CreateMovie(1, "Movie C");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-5), dailyRate: 4.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            var baseCharge = receipt.BaseCharge;
            var expectedDiscount = Math.Round(baseCharge * 0.10m, 2);
            Assert.AreEqual(expectedDiscount, receipt.MembershipDiscount);
        }

        [TestMethod]
        public void GenerateReceipt_PlatinumMember_15PercentDiscount()
        {
            CreateCustomer(1, "Platinum User", MembershipType.Platinum);
            CreateMovie(1, "Movie D");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-4), dailyRate: 6.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            var baseCharge = receipt.BaseCharge;
            var expectedDiscount = Math.Round(baseCharge * 0.15m, 2);
            Assert.AreEqual(expectedDiscount, receipt.MembershipDiscount);
        }

        [TestMethod]
        public void GenerateReceipt_WithLateFee_ShowsLateFeeLineItem()
        {
            CreateCustomer(1, "Late Larry");
            CreateMovie(1, "Overdue Film");
            CreateRental(1, 1, 1, new DateTime(2025, 1, 1), durationDays: 7,
                dailyRate: 3.99m, lateFee: 4.50m,
                returnDate: new DateTime(2025, 1, 12),
                status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(4.50m, receipt.LateFee);
            var lateFeeItem = receipt.LineItems.FirstOrDefault(li => li.Category == LineItemCategory.LateFee);
            Assert.IsNotNull(lateFeeItem);
            Assert.AreEqual(4.50m, lateFeeItem.Amount);
        }

        [TestMethod]
        public void GenerateReceipt_NoLateFee_NoLateFeeLineItem()
        {
            CreateCustomer(1, "Timely Tim");
            CreateMovie(1, "On-Time Movie");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), dailyRate: 3.99m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(0m, receipt.LateFee);
            Assert.IsFalse(receipt.LineItems.Any(li => li.Category == LineItemCategory.LateFee));
        }

        [TestMethod]
        public void GenerateReceipt_WithTax_IncludesTaxLineItem()
        {
            CreateCustomer(1, "Tax Tanya");
            CreateMovie(1, "Taxable Movie");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), dailyRate: 10.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1, ReceiptOptions.Default);

            Assert.IsTrue(receipt.Tax > 0);
            var taxItem = receipt.LineItems.FirstOrDefault(li => li.Category == LineItemCategory.Tax);
            Assert.IsNotNull(taxItem);
            Assert.IsTrue(taxItem.Description.Contains("8.5%"));
        }

        [TestMethod]
        public void GenerateReceipt_NoTaxOption_ExcludesTax()
        {
            CreateCustomer(1, "No Tax Ned");
            CreateMovie(1, "Tax-Free Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2), dailyRate: 5.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1, ReceiptOptions.NoTax);

            Assert.AreEqual(0m, receipt.Tax);
            Assert.IsFalse(receipt.LineItems.Any(li => li.Category == LineItemCategory.Tax));
        }

        [TestMethod]
        public void GenerateReceipt_TotalEqualsSubtotalPlusTax()
        {
            CreateCustomer(1, "Math Mike");
            CreateMovie(1, "Numbers Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-5), dailyRate: 4.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(receipt.Subtotal + receipt.Tax, receipt.Total);
        }

        [TestMethod]
        public void GenerateReceipt_SubtotalEqualsLineItemsSum()
        {
            CreateCustomer(1, "Sum Sam", MembershipType.Gold);
            CreateMovie(1, "Sum Movie");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), dailyRate: 8.00m, lateFee: 3.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1, ReceiptOptions.NoTax);

            var nonTaxSum = receipt.LineItems.Where(li => li.Category != LineItemCategory.Tax).Sum(li => li.Amount);
            Assert.AreEqual(nonTaxSum, receipt.Subtotal);
        }

        [TestMethod]
        public void GenerateReceipt_IncludesGenre()
        {
            CreateCustomer(1, "Genre Gary");
            CreateMovie(1, "Sci-Fi Epic", Genre.ScienceFiction);
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2));

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(Genre.ScienceFiction, receipt.Genre);
        }

        [TestMethod]
        public void GenerateReceipt_IncludesCustomerEmail()
        {
            CreateCustomer(1, "Email Emma");
            CreateMovie(1, "Email Movie");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-1));

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual("email.emma@test.com", receipt.CustomerEmail);
        }

        [TestMethod]
        public void GenerateReceipt_ActiveRental_StatusActive()
        {
            CreateCustomer(1, "Active Andy");
            CreateMovie(1, "Active Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2), status: RentalStatus.Active);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(RentalStatus.Active, receipt.Status);
        }

        // ── BatchReceipt ────────────────────────────────────────────

        [TestMethod]
        public void GenerateBatchReceipt_MultipleRentals_AggregatesCorrectly()
        {
            CreateCustomer(1, "Batch Betty");
            CreateMovie(1, "Movie 1");
            CreateMovie(2, "Movie 2");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), dailyRate: 5.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);
            CreateRental(2, 1, 2, DateTime.Today.AddDays(-2), dailyRate: 4.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var batch = _service.GenerateBatchReceipt(new[] { 1, 2 });

            Assert.AreEqual(2, batch.TotalItems);
            Assert.AreEqual(2, batch.Receipts.Count);
            Assert.AreEqual("Batch Betty", batch.CustomerName);
            Assert.IsTrue(batch.GrandTotal > 0);
        }

        [TestMethod]
        public void GenerateBatchReceipt_GrandTotalEqualsSumOfReceipts()
        {
            CreateCustomer(1, "Sum Sue");
            CreateMovie(1, "M1");
            CreateMovie(2, "M2");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-4), dailyRate: 3.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);
            CreateRental(2, 1, 2, DateTime.Today.AddDays(-2), dailyRate: 6.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var batch = _service.GenerateBatchReceipt(new[] { 1, 2 });

            var sumOfTotals = batch.Receipts.Sum(r => r.Total);
            Assert.AreEqual(sumOfTotals, batch.GrandTotal);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerateBatchReceipt_NullIds_Throws()
        {
            _service.GenerateBatchReceipt(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateBatchReceipt_EmptyIds_Throws()
        {
            _service.GenerateBatchReceipt(new List<int>());
        }

        // ── FormatAsText ────────────────────────────────────────────

        [TestMethod]
        public void FormatAsText_ContainsStoreName()
        {
            CreateCustomer(1, "Text Tom");
            CreateMovie(1, "Text Movie");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2), dailyRate: 5.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);
            var text = _service.FormatAsText(receipt);

            Assert.IsTrue(text.Contains(RentalReceiptService.StoreName));
            Assert.IsTrue(text.Contains(RentalReceiptService.StoreAddress));
            Assert.IsTrue(text.Contains(RentalReceiptService.StorePhone));
        }

        [TestMethod]
        public void FormatAsText_ContainsCustomerInfo()
        {
            CreateCustomer(1, "Info Irene");
            CreateMovie(1, "Info Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2));

            var receipt = _service.GenerateReceipt(1);
            var text = _service.FormatAsText(receipt);

            Assert.IsTrue(text.Contains("Info Irene"));
            Assert.IsTrue(text.Contains("info.irene@test.com"));
        }

        [TestMethod]
        public void FormatAsText_ContainsMovieInfo()
        {
            CreateCustomer(1, "Mov Max");
            CreateMovie(1, "The Matrix", Genre.ScienceFiction);
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3));

            var receipt = _service.GenerateReceipt(1);
            var text = _service.FormatAsText(receipt);

            Assert.IsTrue(text.Contains("The Matrix"));
            Assert.IsTrue(text.Contains("ScienceFiction"));
        }

        [TestMethod]
        public void FormatAsText_ContainsTotalLine()
        {
            CreateCustomer(1, "Total Tony");
            CreateMovie(1, "Total Movie");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), dailyRate: 5.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);
            var text = _service.FormatAsText(receipt);

            Assert.IsTrue(text.Contains("TOTAL"));
            Assert.IsTrue(text.Contains("Thank you"));
        }

        [TestMethod]
        public void FormatAsText_ContainsReceiptNumber()
        {
            CreateCustomer(1, "Num Nancy");
            CreateMovie(1, "Number Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2));

            var receipt = _service.GenerateReceipt(1);
            var text = _service.FormatAsText(receipt);

            Assert.IsTrue(text.Contains(receipt.ReceiptNumber));
        }

        // ── FormatAsCsv ─────────────────────────────────────────────

        [TestMethod]
        public void FormatAsCsv_HasHeader()
        {
            CreateCustomer(1, "CSV Carol");
            CreateMovie(1, "CSV Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2), dailyRate: 5.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);
            var csv = _service.FormatAsCsv(receipt);

            var lines = csv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.IsTrue(lines[0].StartsWith("ReceiptNumber,RentalId,Customer,Movie"));
        }

        [TestMethod]
        public void FormatAsCsv_HasCorrectRowCount()
        {
            CreateCustomer(1, "Row Rick", MembershipType.Gold);
            CreateMovie(1, "Row Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), dailyRate: 5.00m, lateFee: 2.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);
            var csv = _service.FormatAsCsv(receipt);
            var lines = csv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Header + line items (rental + discount + late fee + tax)
            Assert.AreEqual(1 + receipt.LineItems.Count, lines.Length);
        }

        [TestMethod]
        public void FormatAsCsv_EscapesCommasInMovieTitle()
        {
            CreateCustomer(1, "Escape Ed");
            CreateMovie(1, "Die Hard, With a Vengeance");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2));

            var receipt = _service.GenerateReceipt(1);
            var csv = _service.FormatAsCsv(receipt);

            Assert.IsTrue(csv.Contains("\"Die Hard, With a Vengeance\""));
        }

        // ── FormatBatchAsText ───────────────────────────────────────

        [TestMethod]
        public void FormatBatchAsText_ContainsBatchHeader()
        {
            CreateCustomer(1, "Batch Brian");
            CreateMovie(1, "BM1");
            CreateMovie(2, "BM2");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2), dailyRate: 3.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);
            CreateRental(2, 1, 2, DateTime.Today.AddDays(-1), dailyRate: 4.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var batch = _service.GenerateBatchReceipt(new[] { 1, 2 });
            var text = _service.FormatBatchAsText(batch);

            Assert.IsTrue(text.Contains("BATCH RECEIPT"));
            Assert.IsTrue(text.Contains("GRAND TOTAL"));
            Assert.IsTrue(text.Contains("Batch Brian"));
        }

        // ── SpendingSummary ─────────────────────────────────────────

        [TestMethod]
        public void GetSpendingSummary_NoRentals_ReturnsZeros()
        {
            CreateCustomer(1, "Empty Eve");

            var summary = _service.GetSpendingSummary(1);

            Assert.AreEqual(0, summary.TotalRentals);
            Assert.AreEqual(0m, summary.TotalBaseCharges);
            Assert.AreEqual(0m, summary.AveragePerRental);
        }

        [TestMethod]
        public void GetSpendingSummary_WithRentals_CalculatesCorrectly()
        {
            CreateCustomer(1, "Summary Sam", MembershipType.Gold);
            CreateMovie(1, "Film A", Genre.Action);
            CreateMovie(2, "Film B", Genre.Comedy);
            CreateRental(1, 1, 1, new DateTime(2025, 3, 1), dailyRate: 5.00m,
                returnDate: new DateTime(2025, 3, 5), status: RentalStatus.Returned);
            CreateRental(2, 1, 2, new DateTime(2025, 3, 10), dailyRate: 4.00m,
                returnDate: new DateTime(2025, 3, 13), status: RentalStatus.Returned);

            var summary = _service.GetSpendingSummary(1);

            Assert.AreEqual(2, summary.TotalRentals);
            Assert.AreEqual("Summary Sam", summary.CustomerName);
            Assert.AreEqual(MembershipType.Gold, summary.MembershipTier);
            Assert.IsTrue(summary.TotalBaseCharges > 0);
            Assert.IsTrue(summary.TotalMembershipSavings > 0); // Gold = 10% discount
            Assert.IsTrue(summary.EstimatedTotal > 0);
        }

        [TestMethod]
        public void GetSpendingSummary_DateFilter_OnlyIncludesRange()
        {
            CreateCustomer(1, "Filter Fiona");
            CreateMovie(1, "Old Film");
            CreateMovie(2, "New Film");
            CreateRental(1, 1, 1, new DateTime(2025, 1, 15), dailyRate: 3.00m,
                returnDate: new DateTime(2025, 1, 18), status: RentalStatus.Returned);
            CreateRental(2, 1, 2, new DateTime(2025, 3, 15), dailyRate: 4.00m,
                returnDate: new DateTime(2025, 3, 18), status: RentalStatus.Returned);

            var summary = _service.GetSpendingSummary(1,
                from: new DateTime(2025, 3, 1), to: new DateTime(2025, 3, 31));

            Assert.AreEqual(1, summary.TotalRentals);
        }

        [TestMethod]
        public void GetSpendingSummary_GenreBreakdown()
        {
            CreateCustomer(1, "Genre Gail");
            CreateMovie(1, "Action 1", Genre.Action);
            CreateMovie(2, "Comedy 1", Genre.Comedy);
            CreateMovie(3, "Action 2", Genre.Action);
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-5), dailyRate: 3.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);
            CreateRental(2, 1, 2, DateTime.Today.AddDays(-4), dailyRate: 3.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);
            CreateRental(3, 1, 3, DateTime.Today.AddDays(-3), dailyRate: 3.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var summary = _service.GetSpendingSummary(1);

            Assert.AreEqual(2, summary.GenreBreakdown["Action"]);
            Assert.AreEqual(1, summary.GenreBreakdown["Comedy"]);
        }

        [TestMethod]
        public void GetSpendingSummary_OnTimeRate_AllOnTime()
        {
            CreateCustomer(1, "OnTime Olga");
            CreateMovie(1, "Film 1");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-3), durationDays: 7, dailyRate: 3.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var summary = _service.GetSpendingSummary(1);

            Assert.AreEqual(100.0m, summary.OnTimeRate);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetSpendingSummary_NonexistentCustomer_Throws()
        {
            _service.GetSpendingSummary(999);
        }

        [TestMethod]
        public void GetSpendingSummary_AveragePerRental()
        {
            CreateCustomer(1, "Average Andy");
            CreateMovie(1, "Film A");
            CreateMovie(2, "Film B");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-5), dailyRate: 4.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);
            CreateRental(2, 1, 2, DateTime.Today.AddDays(-3), dailyRate: 6.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var summary = _service.GetSpendingSummary(1);

            Assert.IsTrue(summary.AveragePerRental > 0);
            Assert.AreEqual(2, summary.TotalRentals);
        }

        // ── CalculateRentalDays ─────────────────────────────────────

        [TestMethod]
        public void CalculateRentalDays_ReturnedRental_UsesReturnDate()
        {
            var rental = new Rental
            {
                RentalDate = new DateTime(2025, 3, 1),
                ReturnDate = new DateTime(2025, 3, 4)
            };
            Assert.AreEqual(3, RentalReceiptService.CalculateRentalDays(rental));
        }

        [TestMethod]
        public void CalculateRentalDays_MinimumOneDay()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today,
                ReturnDate = DateTime.Today
            };
            Assert.AreEqual(1, RentalReceiptService.CalculateRentalDays(rental));
        }

        // ── CalculateMembershipDiscount ─────────────────────────────

        [TestMethod]
        public void CalculateMembershipDiscount_Basic_Zero()
        {
            Assert.AreEqual(0m, RentalReceiptService.CalculateMembershipDiscount(100m, MembershipType.Basic));
        }

        [TestMethod]
        public void CalculateMembershipDiscount_Silver_5Percent()
        {
            Assert.AreEqual(5.00m, RentalReceiptService.CalculateMembershipDiscount(100m, MembershipType.Silver));
        }

        [TestMethod]
        public void CalculateMembershipDiscount_Gold_10Percent()
        {
            Assert.AreEqual(10.00m, RentalReceiptService.CalculateMembershipDiscount(100m, MembershipType.Gold));
        }

        [TestMethod]
        public void CalculateMembershipDiscount_Platinum_15Percent()
        {
            Assert.AreEqual(15.00m, RentalReceiptService.CalculateMembershipDiscount(100m, MembershipType.Platinum));
        }

        [TestMethod]
        public void CalculateMembershipDiscount_RoundsToTwoDecimals()
        {
            var discount = RentalReceiptService.CalculateMembershipDiscount(33.33m, MembershipType.Silver);
            Assert.AreEqual(1.67m, discount); // 33.33 * 0.05 = 1.6665 -> 1.67
        }

        // ── GetDiscountPercent ──────────────────────────────────────

        [TestMethod]
        public void GetDiscountPercent_AllTiers()
        {
            Assert.AreEqual(0m, RentalReceiptService.GetDiscountPercent(MembershipType.Basic));
            Assert.AreEqual(5m, RentalReceiptService.GetDiscountPercent(MembershipType.Silver));
            Assert.AreEqual(10m, RentalReceiptService.GetDiscountPercent(MembershipType.Gold));
            Assert.AreEqual(15m, RentalReceiptService.GetDiscountPercent(MembershipType.Platinum));
        }

        // ── CsvEscape (via FormatAsCsv) ─────────────────────────────

        [TestMethod]
        public void FormatAsCsv_EscapesQuotesInName()
        {
            CreateCustomer(1, "O'Brien \"OB\"");
            CreateMovie(1, "Test Film");
            CreateRental(1, 1, 1, DateTime.Today.AddDays(-2));

            var receipt = _service.GenerateReceipt(1);
            var csv = _service.FormatAsCsv(receipt);

            Assert.IsTrue(csv.Contains("\"O'Brien \"\"OB\"\"\""));
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [TestMethod]
        public void GenerateReceipt_SameDayRental_OneDay()
        {
            CreateCustomer(1, "Same Day Dan");
            CreateMovie(1, "Quick Watch");
            CreateRental(1, 1, 1, DateTime.Today, dailyRate: 5.00m,
                returnDate: DateTime.Today, status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(1, receipt.RentalDays);
            Assert.AreEqual(5.00m, receipt.BaseCharge);
        }

        [TestMethod]
        public void GenerateReceipt_HighLateFee_IncludesInTotal()
        {
            CreateCustomer(1, "Late Lucy");
            CreateMovie(1, "Way Overdue");
            CreateRental(1, 1, 1, new DateTime(2025, 1, 1), dailyRate: 3.00m, lateFee: 25.00m,
                returnDate: new DateTime(2025, 2, 1), status: RentalStatus.Returned);

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual(25.00m, receipt.LateFee);
            Assert.IsTrue(receipt.Total > receipt.BaseCharge);
        }

        [TestMethod]
        public void GenerateReceipt_NullMovieInRepo_UsesRentalName()
        {
            CreateCustomer(1, "Missing Movie Mike");
            // Don't create movie — force fallback to rental.MovieName
            var repo = new InMemoryRentalRepository();
            repo.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 999,
                RentalDate = DateTime.Today.AddDays(-2),
                DueDate = DateTime.Today.AddDays(5),
                DailyRate = 3.99m, Status = RentalStatus.Active,
                MovieName = "Fallback Title"
            });

            var receipt = _service.GenerateReceipt(1);

            Assert.AreEqual("Fallback Title", receipt.MovieTitle);
        }
    }
}
