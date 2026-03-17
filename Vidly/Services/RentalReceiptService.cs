using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Generates detailed, formatted rental receipts for checkout and return
    /// transactions. Supports single and batch receipts, itemized line items
    /// (base rental, membership discounts, late fees, damage charges, insurance,
    /// taxes), and multiple output formats (text, CSV).
    /// </summary>
    public class RentalReceiptService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        /// <summary>Store name displayed on receipts.</summary>
        public const string StoreName = "Vidly Video Rentals";

        /// <summary>Store address line.</summary>
        public const string StoreAddress = "123 Main Street, Anytown, USA";

        /// <summary>Store phone.</summary>
        public const string StorePhone = "(555) 555-0199";

        /// <summary>Sales tax rate (percentage, e.g. 8.5 = 8.5%).</summary>
        public const decimal TaxRate = 8.5m;

        /// <summary>Minimum rental subtotal before tax applies.</summary>
        public const decimal TaxExemptionThreshold = 0m;

        /// <summary>Receipt line width for text formatting.</summary>
        public const int ReceiptWidth = 48;

        public RentalReceiptService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // ── Single Receipt ──────────────────────────────────────────

        /// <summary>
        /// Generate a receipt for a single rental transaction.
        /// </summary>
        /// <param name="rentalId">Rental ID.</param>
        /// <param name="options">Optional formatting/calculation options.</param>
        /// <returns>A detailed <see cref="Receipt"/>.</returns>
        /// <exception cref="ArgumentException">If rental not found.</exception>
        public Receipt GenerateReceipt(int rentalId, ReceiptOptions options = null)
        {
            options = options ?? ReceiptOptions.Default;
            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                throw new ArgumentException($"Rental {rentalId} not found.", nameof(rentalId));

            var customer = _customerRepository.GetById(rental.CustomerId);
            var movie = _movieRepository.GetById(rental.MovieId);

            return BuildReceipt(rental, customer, movie, options);
        }

        /// <summary>
        /// Generate receipts for multiple rentals (e.g. batch checkout).
        /// </summary>
        public BatchReceipt GenerateBatchReceipt(IEnumerable<int> rentalIds, ReceiptOptions options = null)
        {
            options = options ?? ReceiptOptions.Default;
            if (rentalIds == null)
                throw new ArgumentNullException(nameof(rentalIds));

            var ids = rentalIds.ToList();
            if (ids.Count == 0)
                throw new ArgumentException("At least one rental ID is required.", nameof(rentalIds));

            var receipts = new List<Receipt>();
            foreach (var id in ids)
            {
                receipts.Add(GenerateReceipt(id, options));
            }

            return new BatchReceipt
            {
                Receipts = receipts,
                GeneratedAt = _clock.Now,
                CustomerName = receipts.First().CustomerName,
                CustomerId = receipts.First().CustomerId,
                TotalItems = receipts.Count,
                Subtotal = receipts.Sum(r => r.Subtotal),
                TotalDiscount = receipts.Sum(r => r.MembershipDiscount),
                TotalTax = receipts.Sum(r => r.Tax),
                GrandTotal = receipts.Sum(r => r.Total),
            };
        }

        // ── Receipt Building ────────────────────────────────────────

        private Receipt BuildReceipt(Rental rental, Customer customer, Movie movie, ReceiptOptions options)
        {
            var receipt = new Receipt
            {
                ReceiptNumber = GenerateReceiptNumber(rental.Id),
                RentalId = rental.Id,
                CustomerId = rental.CustomerId,
                CustomerName = customer?.Name ?? rental.CustomerName ?? "Unknown",
                CustomerEmail = customer?.Email,
                MembershipTier = customer?.MembershipType ?? MembershipType.Basic,
                MovieId = rental.MovieId,
                MovieTitle = movie?.Name ?? rental.MovieName ?? "Unknown",
                Genre = movie?.Genre,
                RentalDate = rental.RentalDate,
                DueDate = rental.DueDate,
                ReturnDate = rental.ReturnDate,
                Status = rental.Status,
                GeneratedAt = _clock.Now,
                LineItems = new List<ReceiptLineItem>(),
            };

            // 1. Base rental charge
            var rentalDays = CalculateRentalDays(rental);
            var baseCharge = rentalDays * rental.DailyRate;
            receipt.LineItems.Add(new ReceiptLineItem
            {
                Description = $"{receipt.MovieTitle} ({rentalDays} day{(rentalDays != 1 ? "s" : "")} @ {rental.DailyRate:C}/day)",
                Amount = baseCharge,
                Category = LineItemCategory.Rental,
            });

            // 2. Membership discount
            var discount = CalculateMembershipDiscount(baseCharge, receipt.MembershipTier);
            if (discount > 0)
            {
                receipt.LineItems.Add(new ReceiptLineItem
                {
                    Description = $"{receipt.MembershipTier} member discount ({GetDiscountPercent(receipt.MembershipTier)}%)",
                    Amount = -discount,
                    Category = LineItemCategory.Discount,
                });
            }

            // 3. Late fee (if returned late or currently overdue)
            var lateFee = rental.LateFee;
            if (lateFee > 0)
            {
                receipt.LineItems.Add(new ReceiptLineItem
                {
                    Description = $"Late fee ({rental.DaysOverdue} day{(rental.DaysOverdue != 1 ? "s" : "")} overdue)",
                    Amount = lateFee,
                    Category = LineItemCategory.LateFee,
                });
            }

            // 4. Subtotal, tax, total
            var subtotal = receipt.LineItems.Sum(li => li.Amount);
            var tax = 0m;
            if (options.IncludeTax && subtotal > TaxExemptionThreshold)
            {
                tax = Math.Round(subtotal * (TaxRate / 100m), 2);
                receipt.LineItems.Add(new ReceiptLineItem
                {
                    Description = $"Sales tax ({TaxRate}%)",
                    Amount = tax,
                    Category = LineItemCategory.Tax,
                });
            }

            receipt.DailyRate = rental.DailyRate;
            receipt.RentalDays = rentalDays;
            receipt.BaseCharge = baseCharge;
            receipt.MembershipDiscount = discount;
            receipt.LateFee = lateFee;
            receipt.Subtotal = subtotal;
            receipt.Tax = tax;
            receipt.Total = subtotal + tax;

            return receipt;
        }

        // ── Formatting ──────────────────────────────────────────────

        /// <summary>
        /// Format a receipt as a human-readable text block (thermal printer style).
        /// </summary>
        public string FormatAsText(Receipt receipt)
        {
            var sb = new StringBuilder();
            var w = ReceiptWidth;

            sb.AppendLine(CenterText(StoreName, w));
            sb.AppendLine(CenterText(StoreAddress, w));
            sb.AppendLine(CenterText(StorePhone, w));
            sb.AppendLine(new string('=', w));
            sb.AppendLine($"Receipt #: {receipt.ReceiptNumber}");
            sb.AppendLine($"Date:      {receipt.GeneratedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine(new string('-', w));
            sb.AppendLine($"Customer:  {receipt.CustomerName}");
            sb.AppendLine($"Member:    {receipt.MembershipTier}");
            if (!string.IsNullOrEmpty(receipt.CustomerEmail))
                sb.AppendLine($"Email:     {receipt.CustomerEmail}");
            sb.AppendLine(new string('-', w));
            sb.AppendLine($"Movie:     {receipt.MovieTitle}");
            if (receipt.Genre.HasValue)
                sb.AppendLine($"Genre:     {receipt.Genre}");
            sb.AppendLine($"Rented:    {receipt.RentalDate:yyyy-MM-dd}");
            sb.AppendLine($"Due:       {receipt.DueDate:yyyy-MM-dd}");
            if (receipt.ReturnDate.HasValue)
                sb.AppendLine($"Returned:  {receipt.ReturnDate:yyyy-MM-dd}");
            sb.AppendLine($"Status:    {receipt.Status}");
            sb.AppendLine(new string('=', w));

            // Line items
            foreach (var item in receipt.LineItems)
            {
                var desc = item.Description;
                var amt = item.Amount.ToString("C");
                if (desc.Length + amt.Length + 2 > w)
                    desc = desc.Substring(0, w - amt.Length - 3) + "~";
                var padding = w - desc.Length - amt.Length;
                sb.AppendLine(desc + new string(' ', Math.Max(1, padding)) + amt);
            }

            sb.AppendLine(new string('-', w));
            sb.AppendLine(FormatLine("TOTAL", receipt.Total.ToString("C"), w));
            sb.AppendLine(new string('=', w));
            sb.AppendLine();
            sb.AppendLine(CenterText("Thank you for your rental!", w));
            sb.AppendLine(CenterText("Please return by " + receipt.DueDate.ToString("MMM dd, yyyy"), w));

            return sb.ToString();
        }

        /// <summary>
        /// Format a receipt as CSV (one line item per row).
        /// </summary>
        public string FormatAsCsv(Receipt receipt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ReceiptNumber,RentalId,Customer,Movie,Category,Description,Amount");
            foreach (var item in receipt.LineItems)
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(receipt.ReceiptNumber),
                    receipt.RentalId,
                    CsvEscape(receipt.CustomerName),
                    CsvEscape(receipt.MovieTitle),
                    item.Category,
                    CsvEscape(item.Description),
                    item.Amount.ToString("F2")));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Format a batch receipt summary as text.
        /// </summary>
        public string FormatBatchAsText(BatchReceipt batch)
        {
            var sb = new StringBuilder();
            var w = ReceiptWidth;

            sb.AppendLine(CenterText(StoreName, w));
            sb.AppendLine(CenterText("BATCH RECEIPT", w));
            sb.AppendLine(new string('=', w));
            sb.AppendLine($"Customer:  {batch.CustomerName}");
            sb.AppendLine($"Date:      {batch.GeneratedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Items:     {batch.TotalItems}");
            sb.AppendLine(new string('-', w));

            foreach (var receipt in batch.Receipts)
            {
                sb.AppendLine(FormatLine(receipt.MovieTitle, receipt.Total.ToString("C"), w));
            }

            sb.AppendLine(new string('-', w));
            if (batch.TotalDiscount > 0)
                sb.AppendLine(FormatLine("Discounts", $"-{batch.TotalDiscount:C}", w));
            if (batch.TotalTax > 0)
                sb.AppendLine(FormatLine("Tax", batch.TotalTax.ToString("C"), w));
            sb.AppendLine(new string('=', w));
            sb.AppendLine(FormatLine("GRAND TOTAL", batch.GrandTotal.ToString("C"), w));
            sb.AppendLine(new string('=', w));

            return sb.ToString();
        }

        // ── Customer History ────────────────────────────────────────

        /// <summary>
        /// Generate a spending summary for a customer over a date range.
        /// </summary>
        public SpendingSummary GetSpendingSummary(int customerId, DateTime? from = null, DateTime? to = null)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.", nameof(customerId));

            var rentals = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == customerId)
                .ToList();

            if (from.HasValue)
                rentals = rentals.Where(r => r.RentalDate >= from.Value).ToList();
            if (to.HasValue)
                rentals = rentals.Where(r => r.RentalDate <= to.Value).ToList();

            var totalBase = 0m;
            var totalLateFees = 0m;
            var totalDiscount = 0m;
            var genreCounts = new Dictionary<string, int>();

            foreach (var rental in rentals)
            {
                var days = CalculateRentalDays(rental);
                var baseChg = days * rental.DailyRate;
                totalBase += baseChg;
                totalLateFees += rental.LateFee;
                totalDiscount += CalculateMembershipDiscount(baseChg, customer.MembershipType);

                var movie = _movieRepository.GetById(rental.MovieId);
                var genre = movie?.Genre?.ToString() ?? "Unknown";
                if (!genreCounts.ContainsKey(genre))
                    genreCounts[genre] = 0;
                genreCounts[genre]++;
            }

            var subtotal = totalBase - totalDiscount + totalLateFees;
            var tax = Math.Round(subtotal * (TaxRate / 100m), 2);

            return new SpendingSummary
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MembershipTier = customer.MembershipType,
                FromDate = from,
                ToDate = to,
                TotalRentals = rentals.Count,
                TotalBaseCharges = totalBase,
                TotalMembershipSavings = totalDiscount,
                TotalLateFees = totalLateFees,
                EstimatedTax = tax,
                EstimatedTotal = subtotal + tax,
                GenreBreakdown = genreCounts,
                AveragePerRental = rentals.Count > 0 ? Math.Round(subtotal / rentals.Count, 2) : 0m,
                OnTimeRate = rentals.Count > 0
                    ? Math.Round(100.0m * rentals.Count(r => r.DaysOverdue == 0) / rentals.Count, 1)
                    : 0m,
            };
        }

        // ── Helpers ─────────────────────────────────────────────────

        internal static int CalculateRentalDays(Rental rental)
        {
            var endDate = rental.ReturnDate ?? _clock.Today;
            return Math.Max(1, (int)Math.Ceiling((endDate - rental.RentalDate).TotalDays));
        }

        internal static decimal CalculateMembershipDiscount(decimal baseCharge, MembershipType tier)
        {
            var pct = GetDiscountPercent(tier);
            return pct > 0 ? Math.Round(baseCharge * pct / 100m, 2) : 0m;
        }

        internal static decimal GetDiscountPercent(MembershipType tier)
        {
            switch (tier)
            {
                case MembershipType.Silver: return 5m;
                case MembershipType.Gold: return 10m;
                case MembershipType.Platinum: return 15m;
                default: return 0m;
            }
        }

        internal static string GenerateReceiptNumber(int rentalId)
        {
            return $"VDL-{_clock.Now:yyyyMMdd}-{rentalId:D6}";
        }

        private static string CenterText(string text, int width)
        {
            if (text.Length >= width) return text;
            var pad = (width - text.Length) / 2;
            return new string(' ', pad) + text;
        }

        private static string FormatLine(string left, string right, int width)
        {
            var padding = width - left.Length - right.Length;
            return left + new string(' ', Math.Max(1, padding)) + right;
        }

        private static string CsvEscape(string value)
        {
            if (value == null) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  Models
    // ═════════════════════════════════════════════════════════════════

    /// <summary>Options for receipt generation.</summary>
    public class ReceiptOptions
    {
        /// <summary>Whether to include sales tax on the receipt.</summary>
        public bool IncludeTax { get; set; } = true;

        /// <summary>Default options (tax included).</summary>
        public static readonly ReceiptOptions Default = new ReceiptOptions();

        /// <summary>No-tax options.</summary>
        public static readonly ReceiptOptions NoTax = new ReceiptOptions { IncludeTax = false };
    }

    /// <summary>A single rental receipt with itemized charges.</summary>
    public class Receipt
    {
        public string ReceiptNumber { get; set; }
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string CustomerEmail { get; set; }
        public MembershipType MembershipTier { get; set; }
        public int MovieId { get; set; }
        public string MovieTitle { get; set; }
        public Genre? Genre { get; set; }
        public DateTime RentalDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public RentalStatus Status { get; set; }
        public DateTime GeneratedAt { get; set; }

        public decimal DailyRate { get; set; }
        public int RentalDays { get; set; }
        public decimal BaseCharge { get; set; }
        public decimal MembershipDiscount { get; set; }
        public decimal LateFee { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }

        public List<ReceiptLineItem> LineItems { get; set; } = new List<ReceiptLineItem>();
    }

    /// <summary>A single line item on a receipt.</summary>
    public class ReceiptLineItem
    {
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public LineItemCategory Category { get; set; }
    }

    /// <summary>Line item classification.</summary>
    public enum LineItemCategory
    {
        Rental,
        Discount,
        LateFee,
        Tax,
    }

    /// <summary>Batch receipt for multiple rentals.</summary>
    public class BatchReceipt
    {
        public List<Receipt> Receipts { get; set; } = new List<Receipt>();
        public DateTime GeneratedAt { get; set; }
        public string CustomerName { get; set; }
        public int CustomerId { get; set; }
        public int TotalItems { get; set; }
        public decimal Subtotal { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal TotalTax { get; set; }
        public decimal GrandTotal { get; set; }
    }

    /// <summary>Customer spending summary over a date range.</summary>
    public class SpendingSummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int TotalRentals { get; set; }
        public decimal TotalBaseCharges { get; set; }
        public decimal TotalMembershipSavings { get; set; }
        public decimal TotalLateFees { get; set; }
        public decimal EstimatedTax { get; set; }
        public decimal EstimatedTotal { get; set; }
        public Dictionary<string, int> GenreBreakdown { get; set; } = new Dictionary<string, int>();
        public decimal AveragePerRental { get; set; }
        public decimal OnTimeRate { get; set; }
    }
}
