using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Utilities;

namespace Vidly.Tests
{
    /// <summary>
    /// Direct unit tests for <see cref="CsvFormatter"/>.
    ///
    /// <para>
    /// <see cref="ExportSecurityTests"/> covers the same escaping rules
    /// end-to-end through <c>ExportController</c>, but exercising
    /// <see cref="CsvFormatter"/> in isolation pins down the contract
    /// for the new shared helper (other call sites - services, jobs,
    /// future API endpoints - will depend on it directly).
    /// </para>
    /// </summary>
    [TestClass]
    public class CsvFormatterTests
    {
        // ─── Escape: pass-through ──────────────────────────────────

        [TestMethod]
        public void Escape_Null_ReturnsEmpty()
        {
            Assert.AreEqual("", CsvFormatter.Escape(null));
        }

        [TestMethod]
        public void Escape_Empty_ReturnsEmpty()
        {
            Assert.AreEqual("", CsvFormatter.Escape(""));
        }

        [TestMethod]
        public void Escape_PlainText_Unchanged()
        {
            Assert.AreEqual("The Matrix", CsvFormatter.Escape("The Matrix"));
        }

        // ─── Escape: RFC 4180 quoting ──────────────────────────────

        [TestMethod]
        public void Escape_ContainsComma_Quoted()
        {
            Assert.AreEqual("\"a,b\"", CsvFormatter.Escape("a,b"));
        }

        [TestMethod]
        public void Escape_ContainsDoubleQuote_Doubled()
        {
            // Embedded " must be doubled AND the field quoted.
            Assert.AreEqual("\"a\"\"b\"", CsvFormatter.Escape("a\"b"));
        }

        [TestMethod]
        public void Escape_ContainsLf_Quoted()
        {
            Assert.AreEqual("\"a\nb\"", CsvFormatter.Escape("a\nb"));
        }

        [TestMethod]
        public void Escape_ContainsCr_Quoted()
        {
            // CWE-93 regression: lone CR used to slip past needsQuote.
            Assert.AreEqual("\"a\rb\"", CsvFormatter.Escape("a\rb"));
        }

        [TestMethod]
        public void Escape_ContainsCrLf_Quoted()
        {
            Assert.AreEqual("\"a\r\nb\"", CsvFormatter.Escape("a\r\nb"));
        }

        // ─── Escape: CWE-1236 formula injection ────────────────────

        [TestMethod]
        public void Escape_EqualsPrefix_QuotedWithLeadingSingleQuote()
        {
            Assert.AreEqual("\"'=SUM(A1)\"", CsvFormatter.Escape("=SUM(A1)"));
        }

        [TestMethod]
        public void Escape_PlusPrefix_QuotedWithLeadingSingleQuote()
        {
            Assert.AreEqual("\"'+1+2\"", CsvFormatter.Escape("+1+2"));
        }

        [TestMethod]
        public void Escape_MinusPrefix_QuotedWithLeadingSingleQuote()
        {
            Assert.AreEqual("\"'-1\"", CsvFormatter.Escape("-1"));
        }

        [TestMethod]
        public void Escape_AtPrefix_QuotedWithLeadingSingleQuote()
        {
            Assert.AreEqual("\"'@cmd\"", CsvFormatter.Escape("@cmd"));
        }

        [TestMethod]
        public void Escape_TabPrefix_QuotedWithLeadingSingleQuote()
        {
            Assert.AreEqual("\"'\t=evil\"", CsvFormatter.Escape("\t=evil"));
        }

        [TestMethod]
        public void Escape_CrPrefix_QuotedWithLeadingSingleQuote()
        {
            // Leading CR triggers both the formula path and embedded-CR
            // path; formula path wins (it produces a quoted output that
            // also satisfies the embedded-CR requirement).
            Assert.AreEqual("\"'\rfoo\"", CsvFormatter.Escape("\rfoo"));
        }

        [TestMethod]
        public void Escape_FormulaPrefixWithEmbeddedQuote_BothEscaped()
        {
            // "=A1+\"x\""  should become  "'=A1+""x"""
            Assert.AreEqual("\"'=A1+\"\"x\"\"\"", CsvFormatter.Escape("=A1+\"x\""));
        }

        // ─── BuildDocument ─────────────────────────────────────────

        [TestMethod]
        public void BuildDocument_HeadersOnly_EmitsHeaderRow()
        {
            var csv = CsvFormatter.BuildDocument(
                new[] { "Id", "Name" },
                new List<string[]>());

            Assert.IsTrue(csv.StartsWith("Id,Name"));
        }

        [TestMethod]
        public void BuildDocument_TwoRows_AreJoinedWithCommas()
        {
            var csv = CsvFormatter.BuildDocument(
                new[] { "Id", "Name" },
                new[]
                {
                    new[] { "1", "Alice" },
                    new[] { "2", "Bob"   }
                });

            StringAssert.Contains(csv, "1,Alice");
            StringAssert.Contains(csv, "2,Bob");
        }

        [TestMethod]
        public void BuildDocument_NullValueInRow_BecomesEmpty()
        {
            // Pre-escaped null entries must not crash and must not
            // collapse the column count.
            var csv = CsvFormatter.BuildDocument(
                new[] { "A", "B", "C" },
                new[] { new string[] { "x", null, "z" } });

            StringAssert.Contains(csv, "x,,z");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void BuildDocument_NullHeaders_Throws()
        {
            CsvFormatter.BuildDocument(null, new List<string[]>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void BuildDocument_NullRows_Throws()
        {
            CsvFormatter.BuildDocument(new[] { "A" }, null);
        }
    }
}
