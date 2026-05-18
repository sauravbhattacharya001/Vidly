using System.Web;
using Newtonsoft.Json;

namespace Vidly.Utilities
{
    /// <summary>
    /// Safely serializes objects to JSON for direct embedding inside an
    /// HTML <c>&lt;script&gt;</c> block.
    ///
    /// <para>
    /// <b>Why this exists.</b> Using <c>@@Html.Raw(JsonConvert.SerializeObject(x))</c>
    /// from inside a script tag is unsafe whenever <c>x</c> can contain
    /// strings sourced from end users. <see cref="JsonConvert"/>'s default
    /// settings do <i>not</i> escape <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>,
    /// <c>'</c>, or the U+2028 / U+2029 line-separator characters.
    /// </para>
    ///
    /// <para>
    /// If any serialized string contains the literal substring
    /// <c>&lt;/script&gt;</c> (or its case variants), the browser closes
    /// the surrounding script element early and parses whatever follows
    /// as HTML — turning a JSON payload into a stored / reflected XSS
    /// sink (CWE-79, OWASP A03:2021 Injection). The U+2028 / U+2029
    /// characters are valid in JSON but terminate JavaScript string
    /// literals, which is a separate but related correctness bug.
    /// </para>
    ///
    /// <para>
    /// This helper sets <see cref="StringEscapeHandling.EscapeHtml"/>
    /// which causes Newtonsoft.Json to encode all of the above
    /// (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c>, <c>"</c>,
    /// U+2028, U+2029) as <c>\uXXXX</c> escapes. The output remains
    /// valid JSON and is safe to drop directly into <c>&lt;script&gt;</c>.
    /// </para>
    ///
    /// <para>
    /// Use this helper anywhere a Razor view previously called
    /// <c>@@Html.Raw(JsonConvert.SerializeObject(model))</c> inside a
    /// <c>&lt;script&gt;</c> block. Returning <see cref="IHtmlString"/>
    /// lets call sites write <c>@@JsonForScript.Serialize(model)</c>
    /// without an additional <c>Html.Raw</c> wrapper.
    /// </para>
    /// </summary>
    public static class JsonForScript
    {
        private static readonly JsonSerializerSettings ScriptSafeSettings = new JsonSerializerSettings
        {
            // Escapes <, >, &, ', ", U+2028, U+2029 as \uXXXX. Safe for
            // direct embedding inside <script>...</script>.
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        };

        /// <summary>
        /// Serializes <paramref name="value"/> to a JSON string that is
        /// safe to embed directly inside an HTML <c>&lt;script&gt;</c>
        /// element. Returns an <see cref="IHtmlString"/> so Razor will
        /// not double-encode the output.
        /// </summary>
        public static IHtmlString Serialize(object value)
        {
            string json = JsonConvert.SerializeObject(value, ScriptSafeSettings);
            return new HtmlString(json);
        }
    }
}
