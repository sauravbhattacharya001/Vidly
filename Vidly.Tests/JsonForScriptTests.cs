using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Utilities;

namespace Vidly.Tests
{
    /// <summary>
    /// Verifies that <see cref="JsonForScript"/> produces JSON that is
    /// safe to embed directly inside an HTML &lt;script&gt; block.
    ///
    /// The vulnerability being guarded against (CWE-79) is:
    /// when an unsafe serializer emits the literal substring
    /// <c>&lt;/script&gt;</c> inside a JSON string, the browser closes
    /// the surrounding script element early and parses anything that
    /// follows as HTML — escalating a stored data field into an XSS
    /// sink. The U+2028 and U+2029 characters are valid in JSON but
    /// terminate JavaScript string literals, breaking the page.
    ///
    /// All assertions check the rendered JSON characters, not just the
    /// in-memory string, because the bug only manifests after the
    /// raw JSON is dropped into the response body.
    /// </summary>
    [TestClass]
    public class JsonForScriptTests
    {
        [TestMethod]
        public void Serialize_String_EscapesScriptCloseTag()
        {
            // The classic XSS payload: a string containing </script>.
            var payload = new { name = "evil </script><img src=x onerror=alert(1)>" };

            var rendered = JsonForScript.Serialize(payload).ToHtmlString();

            // The literal sequence "</script>" must NOT appear, because
            // browsers will terminate the surrounding script tag on it,
            // regardless of JavaScript string context.
            StringAssert.DoesNotMatch(
                rendered,
                new System.Text.RegularExpressions.Regex(@"</script", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                "JsonForScript output must not contain a literal </script tag; got: " + rendered);

            // It also must not contain a raw '<' that could start an HTML
            // tag inside the embedded JSON.
            Assert.IsFalse(rendered.Contains("<"),
                "JsonForScript output must not contain a raw '<'; got: " + rendered);
        }

        [TestMethod]
        public void Serialize_String_EscapesAmpersandAndQuotes()
        {
            var payload = new { greeting = "Tom & Jerry's \"adventure\"" };

            var rendered = JsonForScript.Serialize(payload).ToHtmlString();

            // & must be escaped to \u0026 to avoid HTML-entity ambiguity
            // when the payload is reflected into mixed HTML/JS contexts.
            Assert.IsFalse(rendered.Contains("&"),
                "JsonForScript output must not contain a raw '&'; got: " + rendered);

            // The single quote (apostrophe) must be escaped (\u0027) so
            // the payload is safe inside single-quoted HTML attributes.
            Assert.IsFalse(rendered.Contains("'"),
                "JsonForScript output must not contain a raw '''; got: " + rendered);
        }

        [TestMethod]
        public void Serialize_String_EscapesLineSeparators()
        {
            // U+2028 (LINE SEPARATOR) and U+2029 (PARAGRAPH SEPARATOR)
            // are valid JSON but terminate JavaScript string literals.
            var payload = new { text = "before\u2028middle\u2029after" };

            var rendered = JsonForScript.Serialize(payload).ToHtmlString();

            Assert.IsFalse(rendered.Contains("\u2028"),
                "JsonForScript output must escape U+2028; got: " + rendered);
            Assert.IsFalse(rendered.Contains("\u2029"),
                "JsonForScript output must escape U+2029; got: " + rendered);
        }

        [TestMethod]
        public void Serialize_PreservesRoundTripValue()
        {
            // After escaping, the JSON must still parse back to the
            // exact same logical value (escaping must not corrupt data).
            var payload = new { name = "evil </script>", n = 42 };

            var rendered = JsonForScript.Serialize(payload).ToHtmlString();
            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, object>>(rendered);

            Assert.AreEqual("evil </script>", parsed["name"]);
            Assert.AreEqual(42L, System.Convert.ToInt64(parsed["n"]));
        }

        [TestMethod]
        public void Serialize_Null_ReturnsJsonNull()
        {
            var rendered = JsonForScript.Serialize(null).ToHtmlString();
            Assert.AreEqual("null", rendered);
        }
    }
}
