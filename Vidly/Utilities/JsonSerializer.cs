using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Vidly.Utilities
{
    /// <summary>
    /// Lightweight JSON serializer for export data.
    ///
    /// Handles anonymous types, POCOs, and IEnumerable without requiring
    /// external dependencies like Newtonsoft.Json or System.Text.Json.
    /// Originally inlined in ExportController; extracted so that other
    /// controllers, services, and API endpoints can reuse it.
    /// </summary>
    public static class JsonSerializer
    {
        /// <summary>
        /// Serializes an object graph to a JSON string.
        /// </summary>
        /// <param name="obj">
        /// The object to serialize. Supports null, strings, booleans,
        /// numeric types (int, long, decimal, double, float), DateTime,
        /// DateTimeOffset, IEnumerable (arrays/lists), and objects with
        /// public instance properties (anonymous types, POCOs).
        /// </param>
        /// <returns>A JSON string representation of the object.</returns>
        public static string Serialize(object obj)
        {
            if (obj == null) return "null";

            if (obj is string s)
                return "\"" + EscapeString(s) + "\"";

            if (obj is bool b) return b ? "true" : "false";

            if (obj is int i) return i.ToString(CultureInfo.InvariantCulture);
            if (obj is long l) return l.ToString(CultureInfo.InvariantCulture);
            if (obj is decimal dec) return dec.ToString(CultureInfo.InvariantCulture);
            if (obj is double dbl) return dbl.ToString(CultureInfo.InvariantCulture);
            if (obj is float flt) return flt.ToString(CultureInfo.InvariantCulture);

            if (obj is DateTime dt) return "\"" + dt.ToString("o") + "\"";
            if (obj is DateTimeOffset dto) return "\"" + dto.ToString("o") + "\"";

            if (obj is System.Collections.IEnumerable enumerable)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                    items.Add(Serialize(item));
                return "[" + string.Join(",", items) + "]";
            }

            // Object (anonymous type or POCO) — serialize public properties
            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var pairs = new List<string>();
            foreach (var prop in props)
            {
                var val = prop.GetValue(obj);
                pairs.Add("\"" + prop.Name + "\":" + Serialize(val));
            }
            return "{" + string.Join(",", pairs) + "}";
        }

        /// <summary>
        /// Escapes special characters in a string for JSON embedding.
        /// Per RFC 8259, all control characters (U+0000–U+001F) must be
        /// escaped.  We also escape backspace, form-feed, and forward
        /// slash (the latter for safe embedding inside HTML script tags).
        /// </summary>
        internal static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";

            var sb = new StringBuilder(value.Length + 6);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '/':  sb.Append("\\/");   break; // XSS prevention in HTML contexts
                    default:
                        // Escape remaining control characters (U+0000–U+001F)
                        if (c < ' ')
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
