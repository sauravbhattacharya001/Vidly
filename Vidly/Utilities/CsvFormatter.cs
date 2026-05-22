using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vidly.Utilities
{
    /// <summary>
    /// CSV writing helpers extracted from <c>ExportController</c>.
    ///
    /// <para>
    /// Centralizes two concerns that were previously inlined and
    /// duplicated across export actions:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Field escaping</b> per RFC 4180 section 2 (quote when the
    ///     value contains <c>,</c>, <c>"</c>, <c>\n</c>, or <c>\r</c>;
    ///     double up embedded quotes).
    ///   </item>
    ///   <item>
    ///     <b>CSV-injection neutralization</b> (CWE-1236): values whose
    ///     first character is one of <c>=</c>, <c>+</c>, <c>-</c>,
    ///     <c>@</c>, <c>\t</c>, or <c>\r</c> are prefixed with a single
    ///     quote so spreadsheet applications do not evaluate them as
    ///     formulas.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Mirrors the extraction pattern already used by
    /// <see cref="JsonSerializer"/>: the controller keeps a thin shim
    /// for backwards compatibility and tests, but the real behavior
    /// lives here so other call sites (services, jobs, future API
    /// endpoints) can reuse it without taking a Controllers reference.
    /// </para>
    /// </summary>
    public static class CsvFormatter
    {
        // Characters that force RFC 4180 quoting when they appear
        // anywhere inside a field.
        private static readonly char[] QuoteTriggers = { ',', '"', '\n', '\r' };

        // Characters that, when they appear as the first character of a
        // field, can be interpreted as the start of a spreadsheet
        // formula and must be neutralized with a leading single quote.
        // Tab and CR are included because some parsers strip them
        // before formula evaluation.
        private const string FormulaTriggers = "=+-@\t\r";

        /// <summary>
        /// Escapes a single field for safe CSV output.
        /// </summary>
        /// <param name="value">
        /// Raw field value. <c>null</c> and empty are returned as the
        /// empty string so an entire empty column still round-trips.
        /// </param>
        /// <returns>The escaped value, ready to be joined with commas.</returns>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // RFC 4180 sec. 2.6: fields containing a comma, double-quote,
            // LF, or CR must be enclosed in double-quotes. Including CR
            // in this check defends against CRLF row injection (CWE-93)
            // where a lone '\r' in attacker-controlled data would
            // otherwise ship unquoted and be parsed as a record
            // separator by Excel / many CSV libraries.
            bool needsQuote = value.IndexOfAny(QuoteTriggers) >= 0;
            string escaped = value.Replace("\"", "\"\"");

            // Formula-injection neutralization (CWE-1236) wins over the
            // plain-quote path: we always quote and prepend a single
            // quote so the cell is rendered as text in Excel / Sheets.
            if (escaped.Length > 0 && FormulaTriggers.IndexOf(escaped[0]) >= 0)
            {
                return "\"'" + escaped + "\"";
            }

            if (needsQuote)
                return "\"" + escaped + "\"";

            return value;
        }

        /// <summary>
        /// Builds a complete CSV document with the supplied headers and
        /// pre-escaped row values. Row builders should normally pass
        /// each value through <see cref="Escape"/> for any column that
        /// can hold user-supplied text.
        /// </summary>
        /// <param name="headers">Column headers, output verbatim.</param>
        /// <param name="rows">
        /// Sequence of row arrays. Each array must have one entry per
        /// header. Null entries are emitted as the empty string.
        /// </param>
        public static string BuildDocument(
            IReadOnlyList<string> headers,
            IEnumerable<string[]> rows)
        {
            if (headers == null) throw new ArgumentNullException(nameof(headers));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers));
            foreach (var row in rows)
            {
                if (row == null)
                {
                    sb.AppendLine(string.Empty);
                    continue;
                }

                // Normalize nulls inside a row to empty string so the
                // delimiter alignment never collapses.
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] == null) row[i] = string.Empty;
                }
                sb.AppendLine(string.Join(",", row));
            }
            return sb.ToString();
        }
    }
}
