using System;
using System.Security.Cryptography;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Service for gift card operations: creation, balance checks, redemption, and top-ups.
    /// </summary>
    public class GiftCardService
    {
        private readonly IGiftCardRepository _giftCardRepository;
        private readonly IClock _clock;
        /// <summary>
        /// Maximum retry attempts for code generation to prevent unbounded
        /// recursion if the code space becomes saturated.
        /// </summary>
        private const int MaxCodeGenerationAttempts = 10;

        public GiftCardService() : this(new InMemoryGiftCardRepository()) { }

        public GiftCardService(IGiftCardRepository giftCardRepository,
            IClock clock = null)
        {
            _giftCardRepository = giftCardRepository
                ?? throw new ArgumentNullException(nameof(giftCardRepository));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Generate a unique gift card code in format GIFT-XXXX-XXXX-XXXX.
        /// Uses cryptographically secure random number generation to prevent
        /// code prediction attacks. System.Random is deterministic and not
        /// thread-safe — an attacker observing generated codes could predict
        /// future ones and redeem other customers' gift cards.
        /// </summary>
        public string GenerateCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            const int codeCharCount = 12; // 3 groups of 4

            for (int attempt = 0; attempt < MaxCodeGenerationAttempts; attempt++)
            {
                var sb = new StringBuilder("GIFT-", 19); // "GIFT-" + 12 chars + 2 dashes

                // Generate all random bytes at once (one syscall) instead of
                // per-character to minimize CSPRNG overhead.
                var randomBytes = new byte[codeCharCount];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(randomBytes);
                }

                int byteIndex = 0;
                for (int group = 0; group < 3; group++)
                {
                    if (group > 0) sb.Append('-');
                    for (int i = 0; i < 4; i++)
                    {
                        // Modulo bias: 256 % 36 = 4, so indices 0-3 are ~0.4%
                        // more likely than 4-35. For a 12-char code from a 36-char
                        // alphabet this is negligible (total bias < 0.05 bits of
                        // entropy loss from the ~62-bit ideal).
                        sb.Append(chars[randomBytes[byteIndex++] % chars.Length]);
                    }
                }

                var code = sb.ToString();

                // Check uniqueness with bounded retry instead of unbounded recursion
                if (_giftCardRepository.GetByCode(code) == null)
                    return code;
            }

            throw new InvalidOperationException(
                $"Failed to generate a unique gift card code after {MaxCodeGenerationAttempts} attempts.");
        }

        /// <summary>
        /// Create a new gift card.
        /// </summary>
        public GiftCard Create(decimal value, string purchaserName,
            string recipientName = null, string message = null, DateTime? expirationDate = null)
        {
            if (value < 5.00m || value > 500.00m)
                throw new ArgumentOutOfRangeException(nameof(value),
                    "Gift card value must be between $5.00 and $500.00.");

            var card = new GiftCard
            {
                Code = GenerateCode(),
                OriginalValue = value,
                Balance = value,
                PurchaserName = purchaserName,
                RecipientName = recipientName,
                Message = message,
                IsActive = true,
                ExpirationDate = expirationDate,
                CreatedDate = _clock.Now
            };

            _giftCardRepository.Add(card);
            return card;
        }

        /// <summary>
        /// Check the balance of a gift card by code.
        /// </summary>
        public GiftCardBalanceResult CheckBalance(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return GiftCardBalanceResult.Fail("Please enter a gift card code.");

            var card = _giftCardRepository.GetByCode(code.Trim());
            if (card == null)
                return GiftCardBalanceResult.Fail("Gift card not found.");

            return new GiftCardBalanceResult
            {
                Found = true,
                Code = card.Code,
                Balance = card.Balance,
                OriginalValue = card.OriginalValue,
                IsRedeemable = card.IsRedeemable,
                Status = card.StatusDisplay,
                ExpirationDate = card.ExpirationDate
            };
        }

        /// <summary>
        /// Redeem an amount from a gift card. Returns the actual amount deducted.
        /// </summary>
        public GiftCardRedemptionResult Redeem(string code, decimal amount, string description = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return GiftCardRedemptionResult.Fail("Please enter a gift card code.");

            if (amount <= 0)
                return GiftCardRedemptionResult.Fail("Redemption amount must be positive.");

            var card = _giftCardRepository.GetByCode(code.Trim());
            if (card == null)
                return GiftCardRedemptionResult.Fail("Gift card not found.");

            if (!card.IsRedeemable)
                return GiftCardRedemptionResult.Fail(
                    $"This gift card cannot be redeemed (status: {card.StatusDisplay}).");

            // Deduct up to the available balance
            var deducted = Math.Min(amount, card.Balance);
            card.Balance -= deducted;

            _giftCardRepository.Update(card);
            _giftCardRepository.AddTransaction(card.Id, new GiftCardTransaction
            {
                Type = GiftCardTransactionType.Redemption,
                Amount = deducted,
                BalanceAfter = card.Balance,
                Description = description ?? "Rental checkout"
            });

            return new GiftCardRedemptionResult
            {
                Success = true,
                AmountDeducted = deducted,
                RemainingBalance = card.Balance,
                Message = $"${deducted:F2} redeemed. Remaining balance: ${card.Balance:F2}"
            };
        }

        /// <summary>
        /// Add funds to an existing gift card.
        /// </summary>
        public GiftCardRedemptionResult TopUp(string code, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(code))
                return GiftCardRedemptionResult.Fail("Please enter a gift card code.");

            if (amount < 5.00m || amount > 500.00m)
                return GiftCardRedemptionResult.Fail("Top-up amount must be between $5.00 and $500.00.");

            var card = _giftCardRepository.GetByCode(code.Trim());
            if (card == null)
                return GiftCardRedemptionResult.Fail("Gift card not found.");

            if (!card.IsActive)
                return GiftCardRedemptionResult.Fail("This gift card has been disabled.");

            card.Balance += amount;
            _giftCardRepository.Update(card);
            _giftCardRepository.AddTransaction(card.Id, new GiftCardTransaction
            {
                Type = GiftCardTransactionType.TopUp,
                Amount = amount,
                BalanceAfter = card.Balance,
                Description = $"Top-up of ${amount:F2}"
            });

            return new GiftCardRedemptionResult
            {
                Success = true,
                AmountDeducted = amount,
                RemainingBalance = card.Balance,
                Message = $"${amount:F2} added. New balance: ${card.Balance:F2}"
            };
        }
    }

    /// <summary>
    /// Result of a gift card balance check.
    /// </summary>
    public class GiftCardBalanceResult
    {
        public bool Found { get; set; }
        public string Code { get; set; }
        public decimal Balance { get; set; }
        public decimal OriginalValue { get; set; }
        public bool IsRedeemable { get; set; }
        public string Status { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string ErrorMessage { get; set; }

        public static GiftCardBalanceResult Fail(string message) =>
            new GiftCardBalanceResult { Found = false, ErrorMessage = message };
    }

    /// <summary>
    /// Result of a gift card redemption or top-up.
    /// </summary>
    public class GiftCardRedemptionResult
    {
        public bool Success { get; set; }
        public decimal AmountDeducted { get; set; }
        public decimal RemainingBalance { get; set; }
        public string Message { get; set; }

        public static GiftCardRedemptionResult Fail(string message) =>
            new GiftCardRedemptionResult { Success = false, Message = message };
    }
}
