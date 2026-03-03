using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory gift card repository with seed data.
    /// </summary>
    public class InMemoryGiftCardRepository : IGiftCardRepository
    {
        private static readonly object _lock = new object();
        private static readonly List<GiftCard> _giftCards = new List<GiftCard>();
        private static int _nextId = 1;
        private static int _nextTxId = 1;
        private static bool _seeded;

        public InMemoryGiftCardRepository()
        {
            lock (_lock)
            {
                if (!_seeded)
                {
                    Seed();
                    _seeded = true;
                }
            }
        }

        private static void Seed()
        {
            var now = DateTime.Today;
            var cards = new[]
            {
                new GiftCard
                {
                    Id = _nextId++,
                    Code = "GIFT-AB12-CD34-EF56",
                    OriginalValue = 50.00m,
                    Balance = 35.50m,
                    PurchaserName = "Alice Johnson",
                    RecipientName = "Bob Smith",
                    Message = "Happy Birthday!",
                    IsActive = true,
                    ExpirationDate = now.AddDays(365),
                    CreatedDate = now.AddDays(-30)
                },
                new GiftCard
                {
                    Id = _nextId++,
                    Code = "GIFT-GH78-IJ90-KL12",
                    OriginalValue = 25.00m,
                    Balance = 25.00m,
                    PurchaserName = "Carol Davis",
                    RecipientName = null,
                    Message = null,
                    IsActive = true,
                    ExpirationDate = null,
                    CreatedDate = now.AddDays(-7)
                },
                new GiftCard
                {
                    Id = _nextId++,
                    Code = "GIFT-MN34-OP56-QR78",
                    OriginalValue = 100.00m,
                    Balance = 0m,
                    PurchaserName = "Dave Wilson",
                    RecipientName = "Eve Brown",
                    Message = "Enjoy your movies!",
                    IsActive = true,
                    ExpirationDate = now.AddDays(180),
                    CreatedDate = now.AddDays(-90)
                },
                new GiftCard
                {
                    Id = _nextId++,
                    Code = "GIFT-ST90-UV12-WX34",
                    OriginalValue = 75.00m,
                    Balance = 75.00m,
                    PurchaserName = "Frank Lee",
                    RecipientName = "Grace Chen",
                    Message = "Merry Christmas!",
                    IsActive = false,
                    ExpirationDate = now.AddDays(-10),
                    CreatedDate = now.AddDays(-120)
                }
            };

            foreach (var card in cards)
            {
                card.Transactions.Add(new GiftCardTransaction
                {
                    Id = _nextTxId++,
                    GiftCardId = card.Id,
                    Type = GiftCardTransactionType.InitialLoad,
                    Amount = card.OriginalValue,
                    BalanceAfter = card.OriginalValue,
                    Description = "Gift card purchased",
                    Date = card.CreatedDate
                });
            }

            // Add some redemption history for the first card
            cards[0].Transactions.Add(new GiftCardTransaction
            {
                Id = _nextTxId++,
                GiftCardId = cards[0].Id,
                Type = GiftCardTransactionType.Redemption,
                Amount = 14.50m,
                BalanceAfter = 35.50m,
                Description = "Rental checkout",
                Date = now.AddDays(-15)
            });

            // Add full redemption for the third card
            cards[2].Transactions.Add(new GiftCardTransaction
            {
                Id = _nextTxId++,
                GiftCardId = cards[2].Id,
                Type = GiftCardTransactionType.Redemption,
                Amount = 100.00m,
                BalanceAfter = 0m,
                Description = "Rental checkout",
                Date = now.AddDays(-30)
            });

            _giftCards.AddRange(cards);
        }

        public IReadOnlyList<GiftCard> GetAll()
        {
            lock (_lock)
            {
                return _giftCards.ToList().AsReadOnly();
            }
        }

        public GiftCard GetById(int id)
        {
            lock (_lock)
            {
                return _giftCards.FirstOrDefault(g => g.Id == id);
            }
        }

        public GiftCard GetByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            lock (_lock)
            {
                return _giftCards.FirstOrDefault(g =>
                    string.Equals(g.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            }
        }

        public void Add(GiftCard giftCard)
        {
            if (giftCard == null) throw new ArgumentNullException(nameof(giftCard));
            lock (_lock)
            {
                if (_giftCards.Any(g =>
                    string.Equals(g.Code, giftCard.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"A gift card with code '{giftCard.Code}' already exists.");
                }

                giftCard.Id = _nextId++;
                giftCard.CreatedDate = DateTime.Now;

                // Add initial load transaction
                giftCard.Transactions.Add(new GiftCardTransaction
                {
                    Id = _nextTxId++,
                    GiftCardId = giftCard.Id,
                    Type = GiftCardTransactionType.InitialLoad,
                    Amount = giftCard.OriginalValue,
                    BalanceAfter = giftCard.Balance,
                    Description = "Gift card purchased",
                    Date = DateTime.Now
                });

                _giftCards.Add(giftCard);
            }
        }

        public void Update(GiftCard giftCard)
        {
            if (giftCard == null) throw new ArgumentNullException(nameof(giftCard));
            lock (_lock)
            {
                var existing = _giftCards.FirstOrDefault(g => g.Id == giftCard.Id);
                if (existing == null)
                    throw new KeyNotFoundException($"Gift card {giftCard.Id} not found.");

                existing.PurchaserName = giftCard.PurchaserName;
                existing.RecipientName = giftCard.RecipientName;
                existing.Message = giftCard.Message;
                existing.IsActive = giftCard.IsActive;
                existing.ExpirationDate = giftCard.ExpirationDate;
                existing.Balance = giftCard.Balance;
            }
        }

        public void AddTransaction(int giftCardId, GiftCardTransaction transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            lock (_lock)
            {
                var card = _giftCards.FirstOrDefault(g => g.Id == giftCardId);
                if (card == null)
                    throw new KeyNotFoundException($"Gift card {giftCardId} not found.");

                transaction.Id = _nextTxId++;
                transaction.GiftCardId = giftCardId;
                transaction.Date = DateTime.Now;
                card.Transactions.Add(transaction);
            }
        }
    }
}
