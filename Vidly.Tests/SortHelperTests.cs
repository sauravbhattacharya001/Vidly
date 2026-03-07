using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Utilities;

namespace Vidly.Tests
{
    [TestClass]
    public class SortHelperTests
    {
        private class Item
        {
            public string Name { get; set; }
            public int Score { get; set; }
            public DateTime Date { get; set; }
        }

        private SortHelper<Item> CreateSorter()
        {
            return new SortHelper<Item>("name", new Dictionary<string, SortColumn<Item>>
            {
                ["name"] = new SortColumn<Item>(i => i.Name),
                ["score"] = new SortColumn<Item>(i => i.Score, descending: true, thenBy: i => i.Name),
                ["date"] = new SortColumn<Item>(i => i.Date)
            });
        }

        private List<Item> SampleItems() => new List<Item>
        {
            new Item { Name = "Charlie", Score = 80, Date = new DateTime(2025, 3, 1) },
            new Item { Name = "Alice",   Score = 95, Date = new DateTime(2025, 1, 1) },
            new Item { Name = "Bob",     Score = 80, Date = new DateTime(2025, 2, 1) },
        };

        [TestMethod]
        public void Apply_SortsByNameAscending_WhenDefaultKey()
        {
            var sorter = CreateSorter();
            var result = sorter.Apply(SampleItems(), null);

            Assert.AreEqual("Alice", result[0].Name);
            Assert.AreEqual("Bob", result[1].Name);
            Assert.AreEqual("Charlie", result[2].Name);
        }

        [TestMethod]
        public void Apply_SortsByScoreDescending_WithTieBreaker()
        {
            var sorter = CreateSorter();
            var result = sorter.Apply(SampleItems(), "score");

            // Alice (95) first, then Bob/Charlie (80) sorted by Name ascending
            Assert.AreEqual("Alice", result[0].Name);
            Assert.AreEqual("Bob", result[1].Name);
            Assert.AreEqual("Charlie", result[2].Name);
        }

        [TestMethod]
        public void Apply_SortsByDate_Ascending()
        {
            var sorter = CreateSorter();
            var result = sorter.Apply(SampleItems(), "date");

            Assert.AreEqual("Alice", result[0].Name);   // Jan
            Assert.AreEqual("Bob", result[1].Name);      // Feb
            Assert.AreEqual("Charlie", result[2].Name);  // Mar
        }

        [TestMethod]
        public void Apply_UnrecognizedKey_FallsBackToDefault()
        {
            var sorter = CreateSorter();
            var result = sorter.Apply(SampleItems(), "nonexistent");

            Assert.AreEqual("Alice", result[0].Name);
            Assert.AreEqual("Bob", result[1].Name);
        }

        [TestMethod]
        public void Apply_EmptyString_FallsBackToDefault()
        {
            var sorter = CreateSorter();
            var result = sorter.Apply(SampleItems(), "");

            Assert.AreEqual("Alice", result[0].Name);
        }

        [TestMethod]
        public void Apply_CaseInsensitiveKeys()
        {
            var sorter = CreateSorter();
            var result = sorter.Apply(SampleItems(), "SCORE");

            Assert.AreEqual("Alice", result[0].Name); // highest score
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Apply_NullSource_Throws()
        {
            var sorter = CreateSorter();
            sorter.Apply(null, "name");
        }

        [TestMethod]
        public void Apply_EmptySource_ReturnsEmptyList()
        {
            var sorter = CreateSorter();
            var result = sorter.Apply(new List<Item>(), "name");

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ResolveKey_ValidKey_ReturnsSameKey()
        {
            var sorter = CreateSorter();
            Assert.AreEqual("score", sorter.ResolveKey("score"));
        }

        [TestMethod]
        public void ResolveKey_UnknownKey_ReturnsDefault()
        {
            var sorter = CreateSorter();
            Assert.AreEqual("name", sorter.ResolveKey("unknown"));
        }

        [TestMethod]
        public void ResolveKey_Null_ReturnsDefault()
        {
            var sorter = CreateSorter();
            Assert.AreEqual("name", sorter.ResolveKey(null));
        }

        [TestMethod]
        public void AvailableKeys_ReturnsAllRegisteredKeys()
        {
            var sorter = CreateSorter();
            var keys = sorter.AvailableKeys;

            Assert.AreEqual(3, keys.Count);
            CollectionAssert.Contains(keys.ToList(), "name");
            CollectionAssert.Contains(keys.ToList(), "score");
            CollectionAssert.Contains(keys.ToList(), "date");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_EmptyDefaultKey_Throws()
        {
            new SortHelper<Item>("", new Dictionary<string, SortColumn<Item>>
            {
                ["name"] = new SortColumn<Item>(i => i.Name)
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullColumns_Throws()
        {
            new SortHelper<Item>("name", null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_DefaultKeyNotInColumns_Throws()
        {
            new SortHelper<Item>("missing", new Dictionary<string, SortColumn<Item>>
            {
                ["name"] = new SortColumn<Item>(i => i.Name)
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void SortColumn_NullKeySelector_Throws()
        {
            new SortColumn<Item>(null);
        }
    }
}
