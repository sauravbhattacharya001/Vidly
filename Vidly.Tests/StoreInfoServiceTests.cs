using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class StoreInfoServiceTests
    {
        private StoreInfoService _service;

        [TestInitialize]
        public void Setup()
        {
            _service = new StoreInfoService();
        }

        [TestMethod]
        public void GetAllStores_ReturnsNonEmptyList()
        {
            var stores = _service.GetAllStores();
            Assert.IsNotNull(stores);
            Assert.IsTrue(stores.Count > 0, "Should return at least one store.");
        }

        [TestMethod]
        public void GetAllStores_EachStoreHasRequiredFields()
        {
            var stores = _service.GetAllStores();
            foreach (var store in stores)
            {
                Assert.IsFalse(string.IsNullOrEmpty(store.Name), "Store name should not be empty.");
                Assert.IsFalse(string.IsNullOrEmpty(store.Address), "Store address should not be empty.");
                Assert.IsFalse(string.IsNullOrEmpty(store.Phone), "Store phone should not be empty.");
                Assert.IsNotNull(store.Hours, "Store hours should not be null.");
                Assert.IsTrue(store.Hours.Count == 7, "Store should have hours for all 7 days.");
            }
        }

        [TestMethod]
        public void GetStoreById_ValidId_ReturnsStore()
        {
            var store = _service.GetStoreById(1);
            Assert.IsNotNull(store);
            Assert.AreEqual(1, store.Id);
        }

        [TestMethod]
        public void GetStoreById_InvalidId_ReturnsNull()
        {
            var store = _service.GetStoreById(999);
            Assert.IsNull(store);
        }

        [TestMethod]
        public void Store_FullAddress_IsFormatted()
        {
            var store = _service.GetStoreById(1);
            Assert.IsNotNull(store.FullAddress);
            Assert.IsTrue(store.FullAddress.Contains(","), "Full address should be comma-separated.");
        }

        [TestMethod]
        public void Store_SpecialDays_ContainsHolidays()
        {
            var store = _service.GetStoreById(1);
            Assert.IsNotNull(store.SpecialDays);
            Assert.IsTrue(store.SpecialDays.Count > 0, "Should have holiday schedule.");
            Assert.IsTrue(store.SpecialDays.Any(s => s.Label == "Christmas Day"),
                "Should include Christmas Day.");
        }

        [TestMethod]
        public void StoreHours_FormattedTimes_AreNotEmpty()
        {
            var store = _service.GetStoreById(1);
            foreach (var hours in store.Hours.Where(h => !h.IsClosed))
            {
                Assert.IsFalse(string.IsNullOrEmpty(hours.FormattedOpen));
                Assert.IsFalse(string.IsNullOrEmpty(hours.FormattedClose));
            }
        }
    }
}
