using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Utilities;

namespace Vidly.Tests
{
    [TestClass]
    public class JsonSerializerTests
    {
        // ── Primitives ─────────────────────────────────────────────

        [TestMethod]
        public void Serialize_Null_ReturnsNull()
        {
            Assert.AreEqual("null", JsonSerializer.Serialize(null));
        }

        [TestMethod]
        public void Serialize_Boolean_ReturnsLowerCase()
        {
            Assert.AreEqual("true", JsonSerializer.Serialize(true));
            Assert.AreEqual("false", JsonSerializer.Serialize(false));
        }

        [TestMethod]
        public void Serialize_Integer()
        {
            Assert.AreEqual("42", JsonSerializer.Serialize(42));
            Assert.AreEqual("-7", JsonSerializer.Serialize(-7));
        }

        [TestMethod]
        public void Serialize_Long()
        {
            Assert.AreEqual("9999999999", JsonSerializer.Serialize(9999999999L));
        }

        [TestMethod]
        public void Serialize_Decimal()
        {
            Assert.AreEqual("3.99", JsonSerializer.Serialize(3.99m));
        }

        [TestMethod]
        public void Serialize_Double()
        {
            var result = JsonSerializer.Serialize(3.14);
            Assert.IsTrue(result.StartsWith("3.14"));
        }

        [TestMethod]
        public void Serialize_Float()
        {
            var result = JsonSerializer.Serialize(1.5f);
            Assert.AreEqual("1.5", result);
        }

        // ── Strings ────────────────────────────────────────────────

        [TestMethod]
        public void Serialize_PlainString()
        {
            Assert.AreEqual("\"hello\"", JsonSerializer.Serialize("hello"));
        }

        [TestMethod]
        public void Serialize_StringWithQuotes()
        {
            Assert.AreEqual("\"say \\\"hi\\\"\"", JsonSerializer.Serialize("say \"hi\""));
        }

        [TestMethod]
        public void Serialize_StringWithBackslash()
        {
            Assert.AreEqual("\"a\\\\b\"", JsonSerializer.Serialize("a\\b"));
        }

        [TestMethod]
        public void Serialize_StringWithNewlines()
        {
            Assert.AreEqual("\"line1\\nline2\"", JsonSerializer.Serialize("line1\nline2"));
        }

        [TestMethod]
        public void Serialize_StringWithTab()
        {
            Assert.AreEqual("\"a\\tb\"", JsonSerializer.Serialize("a\tb"));
        }

        [TestMethod]
        public void Serialize_EmptyString()
        {
            Assert.AreEqual("\"\"", JsonSerializer.Serialize(""));
        }

        // ── DateTime ───────────────────────────────────────────────

        [TestMethod]
        public void Serialize_DateTime_UsesIsoFormat()
        {
            var dt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
            var result = JsonSerializer.Serialize(dt);
            Assert.IsTrue(result.StartsWith("\"2026-03-01"));
            Assert.IsTrue(result.EndsWith("\""));
        }

        [TestMethod]
        public void Serialize_DateTimeOffset_UsesIsoFormat()
        {
            var dto = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.FromHours(-8));
            var result = JsonSerializer.Serialize(dto);
            Assert.IsTrue(result.Contains("2026-03-01"));
        }

        // ── Collections ────────────────────────────────────────────

        [TestMethod]
        public void Serialize_EmptyList()
        {
            Assert.AreEqual("[]", JsonSerializer.Serialize(new List<int>()));
        }

        [TestMethod]
        public void Serialize_IntArray()
        {
            Assert.AreEqual("[1,2,3]", JsonSerializer.Serialize(new[] { 1, 2, 3 }));
        }

        [TestMethod]
        public void Serialize_StringList()
        {
            var result = JsonSerializer.Serialize(new List<string> { "a", "b" });
            Assert.AreEqual("[\"a\",\"b\"]", result);
        }

        [TestMethod]
        public void Serialize_NestedList()
        {
            var result = JsonSerializer.Serialize(new List<object> { 1, "two", null });
            Assert.AreEqual("[1,\"two\",null]", result);
        }

        // ── Objects ────────────────────────────────────────────────

        [TestMethod]
        public void Serialize_AnonymousType()
        {
            var obj = new { Name = "Alice", Age = 30 };
            var result = JsonSerializer.Serialize(obj);
            Assert.IsTrue(result.Contains("\"Name\":\"Alice\""));
            Assert.IsTrue(result.Contains("\"Age\":30"));
        }

        [TestMethod]
        public void Serialize_NestedObject()
        {
            var obj = new { User = new { Name = "Bob" }, Score = 100 };
            var result = JsonSerializer.Serialize(obj);
            Assert.IsTrue(result.Contains("\"User\":{\"Name\":\"Bob\"}"));
            Assert.IsTrue(result.Contains("\"Score\":100"));
        }

        [TestMethod]
        public void Serialize_ObjectWithNullProperty()
        {
            var obj = new { Name = "Test", Value = (string)null };
            var result = JsonSerializer.Serialize(obj);
            Assert.IsTrue(result.Contains("\"Value\":null"));
        }

        [TestMethod]
        public void Serialize_ListOfAnonymousTypes()
        {
            var list = new[]
            {
                new { Id = 1, Name = "A" },
                new { Id = 2, Name = "B" }
            };
            var result = JsonSerializer.Serialize(list);
            Assert.IsTrue(result.StartsWith("[{"));
            Assert.IsTrue(result.EndsWith("}]"));
            Assert.IsTrue(result.Contains("\"Id\":1"));
            Assert.IsTrue(result.Contains("\"Name\":\"B\""));
        }

        // ── EscapeString ───────────────────────────────────────────

        [TestMethod]
        public void EscapeString_NullReturnsEmpty()
        {
            Assert.AreEqual("", JsonSerializer.EscapeString(null));
        }

        [TestMethod]
        public void EscapeString_EmptyReturnsEmpty()
        {
            Assert.AreEqual("", JsonSerializer.EscapeString(""));
        }

        [TestMethod]
        public void EscapeString_NoSpecialCharsUnchanged()
        {
            Assert.AreEqual("hello world", JsonSerializer.EscapeString("hello world"));
        }

        [TestMethod]
        public void EscapeString_AllSpecialChars()
        {
            var input = "a\\b\"c\nd\re\tf";
            var expected = "a\\\\b\\\"c\\nd\\re\\tf";
            Assert.AreEqual(expected, JsonSerializer.EscapeString(input));
        }

        [TestMethod]
        public void EscapeString_LineSeparator()
        {
            Assert.AreEqual("a\\u2028b", JsonSerializer.EscapeString("a\u2028b"));
        }

        [TestMethod]
        public void EscapeString_ParagraphSeparator()
        {
            Assert.AreEqual("a\\u2029b", JsonSerializer.EscapeString("a\u2029b"));
        }

        // ── Char ───────────────────────────────────────────────────

        [TestMethod]
        public void Serialize_Char()
        {
            Assert.AreEqual("\"A\"", JsonSerializer.Serialize('A'));
        }

        [TestMethod]
        public void Serialize_Char_SpecialCharsEscaped()
        {
            Assert.AreEqual("\"\\n\"", JsonSerializer.Serialize('\n'));
        }

        // ── Property name escaping ─────────────────────────────────

        [TestMethod]
        public void Serialize_PropertyNamesAreEscaped()
        {
            // Property names from anonymous types are safe, but EscapeString
            // is now applied to all names including reflection-generated ones.
            var obj = new { Name = "test" };
            var result = JsonSerializer.Serialize(obj);
            Assert.IsTrue(result.Contains("\"Name\":\"test\""));
        }
    }
}
