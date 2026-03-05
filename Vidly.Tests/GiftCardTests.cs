using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;
using System.Web.Mvc;
using System.Collections.Generic;

namespace Vidly.Tests
{
    [TestClass]
    public class GiftCardTests
    {
        // ─── Model Tests ─────────────────────────────────────────────

        [TestMethod]
        public void GiftCard_IsRedeemable_ActiveWithBalance_ReturnsTrue()
        {
            var card = new GiftCard { IsActive = true, Balance = 10m, ExpirationDate = null };
            Assert.IsTrue(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_IsRedeemable_ZeroBalance_ReturnsFalse()
        {
            var card = new GiftCard { IsActive = true, Balance = 0m };
            Assert.IsFalse(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_IsRedeemable_Inactive_ReturnsFalse()
        {
            var card = new GiftCard { IsActive = false, Balance = 50m };
            Assert.IsFalse(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_IsRedeemable_Expired_ReturnsFalse()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 50m,
                ExpirationDate = DateTime.Today.AddDays(-1)
            };
            Assert.IsFalse(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_IsRedeemable_FutureExpiration_ReturnsTrue()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 25m,
                ExpirationDate = DateTime.Today.AddDays(30)
            };
            Assert.IsTrue(card.IsRedeemable);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Active()
        {
            var card = new GiftCard { IsActive = true, Balance = 10m };
            Assert.AreEqual("Active", card.StatusDisplay);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Disabled()
        {
            var card = new GiftCard { IsActive = false, Balance = 10m };
            Assert.AreEqual("Disabled", card.StatusDisplay);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Expired()
        {
            var card = new GiftCard
            {
                IsActive = true,
                Balance = 10m,
                ExpirationDate = DateTime.Today.AddDays(-5)
            };
            Assert.AreEqual("Expired", card.StatusDisplay);
        }

        [TestMethod]
        public void GiftCard_StatusDisplay_Empty()
        {
            var card = new GiftCard { IsActive = true, Balance = 0m };
            Assert.AreEqual("Empty", card.StatusDisplay);
        }

        // ─── Service Tests ───────────────────────────────────────────

        private GiftCardService CreateService(out TestGiftCardRepository repo)
        {
            repo = new TestGiftCardRepository();
            return new GiftCardService(repo);
        }

        [TestMethod]
        public void GiftCardService_Create_ValidCard_ReturnsCard()
        {
            var service = CreateService(out var repo);
            var card = service.Create(50m, "Alice");

            Assert.IsNotNull(card);
            Assert.AreEqual(50m, card.Balance);
            Assert.AreEqual(50m, card.OriginalValue);
            Assert.IsTrue(card.Code.StartsWith("GIFT-"));
            Assert.AreEqual(19, card.Code.Length);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GiftCardService_Create_TooLow_Throws()
        {
            var service = CreateService(out _);
            service.Create(2m, "Alice");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GiftCardService_Create_TooHigh_Throws()
        {
            var service = CreateService(out _);
            service.Create(999m, "Alice");
        }

        [TestMethod]
        public void GiftCardService_Create_WithRecipientAndMessage()
        {
            var service = CreateService(out _);
            var card = service.Create(25m, "Alice", "Bob", "Happy Birthday!");

            Assert.AreEqual("Alice", card.PurchaserName);
            Assert.AreEqual("Bob", card.RecipientName);
            Assert.AreEqual("Happy Birthday!", card.Message);
        }

        [TestMethod]
        public void GiftCardService_CheckBalance_ValidCode()
        {
            var service = CreateService(out _);
            var card = service.Create(100m, "Alice");

            var result = service.CheckBalance(card.Code);
            Assert.IsTrue(result.Found);
            Assert.AreEqual(100m, result.Balance);
            Assert.AreEqual("Active", result.Status);
        }

        [TestMethod]
        public void GiftCardService_CheckBalance_InvalidCode()
        {
            var service = CreateService(out _);
            var result = service.CheckBalance("INVALID-CODE");
            Assert.IsFalse(result.Found);
            Assert.AreEqual("Gift card not found.", result.ErrorMessage);
        }

        [TestMethod]
        public void GiftCardService_CheckBalance_EmptyCode()
        {
            var service = CreateService(out _);
            var result = service.CheckBalance("");
            Assert.IsFalse(result.Found);
        }

        [TestMethod]
        public void GiftCardService_Redeem_FullAmount()
        {
            var service = CreateService(out _);
            var card = service.Create(50m, "Alice");

            var result = service.Redeem(card.Code, 30m);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(30m, result.AmountDeducted);
            Assert.AreEqual(20m, result.RemainingBalance);
        }

        [TestMethod]
        public void GiftCardService_Redeem_MoreThanBalance_CapsAtBalance()
        {
            var service = CreateService(out _);
            var card = service.Create(25m, "Alice");

            var result = service.Redeem(card.Code, 100m);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(25m, result.AmountDeducted);
            Assert.AreEqual(0m, result.RemainingBalance);
        }

        [TestMethod]
        public void GiftCardService_Redeem_InvalidCode()
        {
            var service = CreateService(out _);
            var result = service.Redeem("BAD-CODE", 10m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void GiftCardService_Redeem_ZeroAmount()
        {
            var service = CreateService(out _);
            var card = service.Create(50m, "Alice");
            var result = service.Redeem(card.Code, 0m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void GiftCardService_Redeem_DisabledCard()
        {
            var service = CreateService(out var repo);
            var card = service.Create(50m, "Alice");
            card.IsActive = false;
            repo.Update(card);

            var result = service.Redeem(card.Code, 10m);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Message.Contains("cannot be redeemed"));
        }

        [TestMethod]
        public void GiftCardService_TopUp_ValidAmount()
        {
            var service = CreateService(out _);
            var card = service.Create(25m, "Alice");

            var result = service.TopUp(card.Code, 50m);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(75m, result.RemainingBalance);
        }

        [TestMethod]
        public void GiftCardService_TopUp_TooLow()
        {
            var service = CreateService(out _);
            var card = service.Create(25m, "Alice");
            var result = service.TopUp(card.Code, 2m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void GiftCardService_TopUp_DisabledCard()
        {
            var service = CreateService(out var repo);
            var card = service.Create(25m, "Alice");
            card.IsActive = false;
            repo.Update(card);

            var result = service.TopUp(card.Code, 25m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void GiftCardService_TopUp_InvalidCode()
        {
            var service = CreateService(out _);
            var result = service.TopUp("NOPE", 25m);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void GiftCardService_GenerateCode_CorrectFormat()
        {
            var service = CreateService(out _);
            var code = service.GenerateCode();

            Assert.IsTrue(code.StartsWith("GIFT-"));
            Assert.AreEqual(19, code.Length); // GIFT-XXXX-XXXX-XXXX
            Assert.AreEqual(2, code.Skip(5).Count(c => c == '-')); // 2 dashes between 3 groups
        }

        [TestMethod]
        public void GiftCardService_MultipleRedemptions_TrackBalance()
        {
            var service = CreateService(out _);
            var card = service.Create(100m, "Alice");

            service.Redeem(card.Code, 30m);
            service.Redeem(card.Code, 25m);
            var result = service.Redeem(card.Code, 10m);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(35m, result.RemainingBalance);
        }

        // ─── Repository Tests ────────────────────────────────────────

        [TestMethod]
        public void TestRepo_Add_AssignsId()
        {
            var repo = new TestGiftCardRepository();
            var card = new GiftCard
            {
                Code = "TEST-1111-2222-3333",
                OriginalValue = 50m,
                Balance = 50m,
                IsActive = true
            };
            repo.Add(card);
            Assert.IsTrue(card.Id > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestRepo_Add_DuplicateCode_Throws()
        {
            var repo = new TestGiftCardRepository();
            repo.Add(new GiftCard { Code = "DUPE-1111-2222-3333", OriginalValue = 25m, Balance = 25m });
            repo.Add(new GiftCard { Code = "DUPE-1111-2222-3333", OriginalValue = 50m, Balance = 50m });
        }

        [TestMethod]
        public void TestRepo_GetByCode_CaseInsensitive()
        {
            var repo = new TestGiftCardRepository();
            repo.Add(new GiftCard { Code = "FIND-AAAA-BBBB-CCCC", OriginalValue = 10m, Balance = 10m });
            var found = repo.GetByCode("find-aaaa-bbbb-cccc");
            Assert.IsNotNull(found);
        }

        [TestMethod]
        public void TestRepo_GetByCode_Null_ReturnsNull()
        {
            var repo = new TestGiftCardRepository();
            Assert.IsNull(repo.GetByCode(null));
        }

        // ─── Controller Tests ────────────────────────────────────────

        [TestMethod]
        public void GiftCardsController_Index_ReturnsView()
        {
            var repo = new TestGiftCardRepository();
            repo.Add(new GiftCard { Code = "CTRL-1111-2222-3333", OriginalValue = 50m, Balance = 50m, IsActive = true });
            var controller = new GiftCardsController(repo);

            var result = controller.Index(null) as ViewResult;
            Assert.IsNotNull(result);
            var model = result.Model as GiftCardIndexViewModel;
            Assert.IsNotNull(model);
            Assert.AreEqual(1, model.TotalCount);
        }

        [TestMethod]
        public void GiftCardsController_Index_StatusFilter()
        {
            var repo = new TestGiftCardRepository();
            repo.Add(new GiftCard { Code = "ACT-1111-2222-3333", OriginalValue = 50m, Balance = 50m, IsActive = true });
            repo.Add(new GiftCard { Code = "EMP-1111-2222-3333", OriginalValue = 25m, Balance = 0m, IsActive = true });
            var controller = new GiftCardsController(repo);

            var result = controller.Index("Empty") as ViewResult;
            var model = result.Model as GiftCardIndexViewModel;
            Assert.AreEqual(1, model.GiftCards.Count);
        }

        [TestMethod]
        public void GiftCardsController_Details_NotFound()
        {
            var controller = new GiftCardsController(new TestGiftCardRepository());
            var result = controller.Details(999);
            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void GiftCardsController_Details_Found()
        {
            var repo = new TestGiftCardRepository();
            repo.Add(new GiftCard { Code = "DET-1111-2222-3333", OriginalValue = 50m, Balance = 50m, IsActive = true });
            var card = repo.GetAll().First();
            var controller = new GiftCardsController(repo);

            var result = controller.Details(card.Id) as ViewResult;
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(GiftCard));
        }

        [TestMethod]
        public void GiftCardsController_Balance_Get_ReturnsView()
        {
            var controller = new GiftCardsController(new TestGiftCardRepository());
            var result = controller.Balance() as ViewResult;
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void GiftCardsController_Toggle_FlipsActive()
        {
            var repo = new TestGiftCardRepository();
            repo.Add(new GiftCard { Code = "TOG-1111-2222-3333", OriginalValue = 50m, Balance = 50m, IsActive = true });
            var card = repo.GetAll().First();
            var controller = new GiftCardsController(repo);

            controller.Toggle(card.Id);
            Assert.IsFalse(repo.GetById(card.Id).IsActive);

            controller.Toggle(card.Id);
            Assert.IsTrue(repo.GetById(card.Id).IsActive);
        }

        [TestMethod]
        public void GiftCardsController_Toggle_NotFound()
        {
            var controller = new GiftCardsController(new TestGiftCardRepository());
            var result = controller.Toggle(999);
            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        // ─── Test Repository (isolated, no static state) ────────────

        private class TestGiftCardRepository : IGiftCardRepository
        {
            private readonly List<GiftCard> _cards = new List<GiftCard>();
            private int _nextId = 1;
            private int _nextTxId = 1;

            public IReadOnlyList<GiftCard> GetAll() => _cards.AsReadOnly();

            public GiftCard GetById(int id) => _cards.FirstOrDefault(c => c.Id == id);

            public GiftCard GetByCode(string code)
            {
                if (string.IsNullOrWhiteSpace(code)) return null;
                return _cards.FirstOrDefault(c =>
                    string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            public void Add(GiftCard giftCard)
            {
                if (_cards.Any(c => string.Equals(c.Code, giftCard.Code, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Duplicate code '{giftCard.Code}'.");
                giftCard.Id = _nextId++;
                giftCard.CreatedDate = DateTime.Now;
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
                _cards.Add(giftCard);
            }

            public void Update(GiftCard giftCard)
            {
                var existing = _cards.FirstOrDefault(c => c.Id == giftCard.Id);
                if (existing == null) throw new KeyNotFoundException();
                existing.Balance = giftCard.Balance;
                existing.IsActive = giftCard.IsActive;
                existing.RecipientName = giftCard.RecipientName;
                existing.PurchaserName = giftCard.PurchaserName;
                existing.Message = giftCard.Message;
                existing.ExpirationDate = giftCard.ExpirationDate;
            }

            public void AddTransaction(int giftCardId, GiftCardTransaction transaction)
            {
                var card = _cards.FirstOrDefault(c => c.Id == giftCardId);
                if (card == null) throw new KeyNotFoundException();
                transaction.Id = _nextTxId++;
                transaction.GiftCardId = giftCardId;
                transaction.Date = DateTime.Now;
                card.Transactions.Add(transaction);
            }
        }
    }
}
