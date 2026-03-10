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
    public class GiftCardServiceTests
    {
        #region Test Repository

        private class StubGiftCardRepository : IGiftCardRepository
        {
            private readonly Dictionary<int, GiftCard> _cards = new Dictionary<int, GiftCard>();
            private readonly Dictionary<int, List<GiftCardTransaction>> _transactions =
                new Dictionary<int, List<GiftCardTransaction>>();
            private int _nextId = 1;

            public IReadOnlyList<GiftCard> GetAll() => _cards.Values.ToList();
            public GiftCard GetById(int id) =>
                _cards.TryGetValue(id, out var c) ? c : null;
            public GiftCard GetByCode(string code) =>
                code == null ? null :
                _cards.Values.FirstOrDefault(c =>
                    string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            public void Add(GiftCard card)
            {
                card.Id = _nextId++;
                _cards[card.Id] = card;
            }
            public void Update(GiftCard card)
            {
                _cards[card.Id] = card;
            }
            public void AddTransaction(int giftCardId, GiftCardTransaction tx)
            {
                if (!_transactions.ContainsKey(giftCardId))
                    _transactions[giftCardId] = new List<GiftCardTransaction>();
                _transactions[giftCardId].Add(tx);
            }
            public List<GiftCardTransaction> GetTransactions(int giftCardId) =>
                _transactions.TryGetValue(giftCardId, out var txs) ? txs : new List<GiftCardTransaction>();
        }

        #endregion

        private StubGiftCardRepository _repo;
        private GiftCardService _service;

        [TestInitialize]
        public void Setup()
        {
            _repo = new StubGiftCardRepository();
            _service = new GiftCardService(_repo);
        }

        // ── Constructor ──────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRepository_Throws()
        {
            new GiftCardService(null);
        }

        // ── GenerateCode ─────────────────────────────────────────

        [TestMethod]
        public void GenerateCode_ReturnsCorrectFormat()
        {
            var code = _service.GenerateCode();
            Assert.IsTrue(code.StartsWith("GIFT-"));
            Assert.AreEqual(19, code.Length); // GIFT-XXXX-XXXX-XXXX
            var parts = code.Split('-');
            Assert.AreEqual(4, parts.Length);
            Assert.AreEqual("GIFT", parts[0]);
            Assert.AreEqual(4, parts[1].Length);
            Assert.AreEqual(4, parts[2].Length);
            Assert.AreEqual(4, parts[3].Length);
        }

        [TestMethod]
        public void GenerateCode_ReturnsUniqueCodes()
        {
            var codes = new HashSet<string>();
            for (int i = 0; i < 50; i++)
                codes.Add(_service.GenerateCode());
            Assert.AreEqual(50, codes.Count, "All 50 codes should be unique");
        }

        [TestMethod]
        public void GenerateCode_ContainsOnlyAlphanumericAndDashes()
        {
            for (int i = 0; i < 20; i++)
            {
                var code = _service.GenerateCode();
                foreach (var ch in code)
                    Assert.IsTrue(char.IsLetterOrDigit(ch) || ch == '-',
                        $"Unexpected character '{ch}' in code '{code}'");
            }
        }

        // ── Create ───────────────────────────────────────────────

        [TestMethod]
        public void Create_ValidValue_ReturnsGiftCard()
        {
            var card = _service.Create(25.00m, "Alice");
            Assert.IsNotNull(card);
            Assert.AreEqual(25.00m, card.OriginalValue);
            Assert.AreEqual(25.00m, card.Balance);
            Assert.AreEqual("Alice", card.PurchaserName);
            Assert.IsTrue(card.IsActive);
            Assert.IsTrue(card.Code.StartsWith("GIFT-"));
        }

        [TestMethod]
        public void Create_WithRecipientAndMessage_SetsFields()
        {
            var card = _service.Create(50.00m, "Alice", "Bob", "Happy birthday!");
            Assert.AreEqual("Bob", card.RecipientName);
            Assert.AreEqual("Happy birthday!", card.Message);
        }

        [TestMethod]
        public void Create_WithExpiration_SetsDate()
        {
            var exp = DateTime.Today.AddMonths(6);
            var card = _service.Create(100.00m, "Alice", expirationDate: exp);
            Assert.AreEqual(exp, card.ExpirationDate);
        }

        [TestMethod]
        public void Create_MinValue_Succeeds()
        {
            var card = _service.Create(5.00m, "Alice");
            Assert.AreEqual(5.00m, card.OriginalValue);
        }

        [TestMethod]
        public void Create_MaxValue_Succeeds()
        {
            var card = _service.Create(500.00m, "Alice");
            Assert.AreEqual(500.00m, card.OriginalValue);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Create_BelowMinValue_Throws()
        {
            _service.Create(4.99m, "Alice");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Create_AboveMaxValue_Throws()
        {
            _service.Create(500.01m, "Alice");
        }

        // ── CheckBalance ─────────────────────────────────────────

        [TestMethod]
        public void CheckBalance_ValidCode_ReturnsBalance()
        {
            var card = _service.Create(50.00m, "Alice");
            var result = _service.CheckBalance(card.Code);
            Assert.IsTrue(result.Found);
            Assert.AreEqual(50.00m, result.Balance);
            Assert.AreEqual(50.00m, result.OriginalValue);
            Assert.IsTrue(result.IsRedeemable);
        }

        [TestMethod]
        public void CheckBalance_NullCode_ReturnsNotFound()
        {
            var result = _service.CheckBalance(null);
            Assert.IsFalse(result.Found);
            Assert.IsTrue(result.ErrorMessage.Contains("enter"));
        }

        [TestMethod]
        public void CheckBalance_EmptyCode_ReturnsNotFound()
        {
            var result = _service.CheckBalance("");
            Assert.IsFalse(result.Found);
        }

        [TestMethod]
        public void CheckBalance_InvalidCode_ReturnsNotFound()
        {
            var result = _service.CheckBalance("INVALID-CODE");
            Assert.IsFalse(result.Found);
            Assert.IsTrue(result.ErrorMessage.Contains("not found"));
        }

        // ── Redeem ───────────────────────────────────────────────

        [TestMethod]
        public void Redeem_ValidAmount_DeductsBalance()
        {
            var card = _service.Create(50.00m, "Alice");
            var result = _service.Redeem(card.Code, 20.00m);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(20.00m, result.AmountDeducted);
            Assert.AreEqual(30.00m, result.RemainingBalance);
        }

        [TestMethod]
        public void Redeem_AmountExceedsBalance_DeductsOnlyBalance()
        {
            var card = _service.Create(10.00m, "Alice");
            var result = _service.Redeem(card.Code, 25.00m);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(10.00m, result.AmountDeducted);
            Assert.AreEqual(0m, result.RemainingBalance);
        }

        [TestMethod]
        public void Redeem_EntireBalance_EmptiesCard()
        {
            var card = _service.Create(50.00m, "Alice");
            var result = _service.Redeem(card.Code, 50.00m);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0m, result.RemainingBalance);
        }

        [TestMethod]
        public void Redeem_NullCode_Fails()
        {
            var result = _service.Redeem(null, 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Redeem_InvalidCode_Fails()
        {
            var result = _service.Redeem("FAKE-CODE", 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Redeem_ZeroAmount_Fails()
        {
            var card = _service.Create(50.00m, "Alice");
            var result = _service.Redeem(card.Code, 0m);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Message.Contains("positive"));
        }

        [TestMethod]
        public void Redeem_NegativeAmount_Fails()
        {
            var card = _service.Create(50.00m, "Alice");
            var result = _service.Redeem(card.Code, -5.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Redeem_DisabledCard_Fails()
        {
            var card = _service.Create(50.00m, "Alice");
            card.IsActive = false;
            _repo.Update(card);
            var result = _service.Redeem(card.Code, 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Redeem_ExpiredCard_Fails()
        {
            var card = _service.Create(50.00m, "Alice",
                expirationDate: DateTime.Today.AddDays(-1));
            var result = _service.Redeem(card.Code, 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void Redeem_MultipleRedemptions_TracksBalance()
        {
            var card = _service.Create(100.00m, "Alice");
            _service.Redeem(card.Code, 30.00m);
            _service.Redeem(card.Code, 25.00m);
            var result = _service.Redeem(card.Code, 10.00m);
            Assert.AreEqual(35.00m, result.RemainingBalance);
        }

        [TestMethod]
        public void Redeem_RecordsTransaction()
        {
            var card = _service.Create(50.00m, "Alice");
            _service.Redeem(card.Code, 20.00m, "Test rental");
            var txs = _repo.GetTransactions(card.Id);
            Assert.AreEqual(1, txs.Count);
            Assert.AreEqual(GiftCardTransactionType.Redemption, txs[0].Type);
            Assert.AreEqual(20.00m, txs[0].Amount);
        }

        // ── TopUp ────────────────────────────────────────────────

        [TestMethod]
        public void TopUp_ValidAmount_AddsToBalance()
        {
            var card = _service.Create(25.00m, "Alice");
            var result = _service.TopUp(card.Code, 50.00m);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(75.00m, result.RemainingBalance);
        }

        [TestMethod]
        public void TopUp_NullCode_Fails()
        {
            var result = _service.TopUp(null, 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void TopUp_InvalidCode_Fails()
        {
            var result = _service.TopUp("FAKE", 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void TopUp_BelowMinimum_Fails()
        {
            var card = _service.Create(25.00m, "Alice");
            var result = _service.TopUp(card.Code, 4.99m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void TopUp_AboveMaximum_Fails()
        {
            var card = _service.Create(25.00m, "Alice");
            var result = _service.TopUp(card.Code, 500.01m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void TopUp_DisabledCard_Fails()
        {
            var card = _service.Create(25.00m, "Alice");
            card.IsActive = false;
            _repo.Update(card);
            var result = _service.TopUp(card.Code, 10.00m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void TopUp_RecordsTransaction()
        {
            var card = _service.Create(25.00m, "Alice");
            _service.TopUp(card.Code, 50.00m);
            var txs = _repo.GetTransactions(card.Id);
            Assert.AreEqual(1, txs.Count);
            Assert.AreEqual(GiftCardTransactionType.TopUp, txs[0].Type);
            Assert.AreEqual(50.00m, txs[0].Amount);
        }

        // ── GiftCard model properties ────────────────────────────

        [TestMethod]
        public void GiftCard_IsRedeemable_ActiveWithBalance()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 10.00m,
                ExpirationDate = DateTime.Today.AddDays(30)
            };
            Assert.IsTrue(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_IsRedeemable_ZeroBalance_False()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 0m,
                ExpirationDate = DateTime.Today.AddDays(30)
            };
            Assert.IsFalse(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_IsRedeemable_Inactive_False()
        {
            var card = new GiftCard { IsActive = false, Balance = 50.00m };
            Assert.IsFalse(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_IsRedeemable_Expired_False()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 50.00m,
                ExpirationDate = DateTime.Today.AddDays(-1)
            };
            Assert.IsFalse(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Active()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 50.00m,
                ExpirationDate = DateTime.Today.AddDays(30)
            };
            Assert.AreEqual("Active", card.StatusDisplay);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Disabled()
        {
            var card = new GiftCard { IsActive = false, Balance = 50.00m };
            Assert.AreEqual("Disabled", card.StatusDisplay);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Expired()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 50.00m,
                ExpirationDate = DateTime.Today.AddDays(-1)
            };
            Assert.AreEqual("Expired", card.StatusDisplay);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Empty()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 0m,
                ExpirationDate = DateTime.Today.AddDays(30)
            };
            Assert.AreEqual("Empty", card.StatusDisplay);
        }
    }
}
