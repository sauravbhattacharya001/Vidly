using System;
using System.Text;
using System.Web.Security;

namespace Vidly.Utilities
{
    /// <summary>
    /// Wraps <see cref="MachineKey.Protect"/> / <see cref="MachineKey.Unprotect"/>
    /// to round-trip server-side state through an untrusted client (hidden form
    /// fields, query strings, cookies) without letting the client tamper with it.
    ///
    /// <para>
    /// <b>Why this exists.</b> Several controllers (EmojiStory, Negotiator, ...) used
    /// to serialize a game / negotiation session to JSON, drop it in a hidden
    /// <c>&lt;input&gt;</c>, and deserialize it back on the next request. A player
    /// could edit the hidden field in DevTools to inflate <c>Score</c>,
    /// <c>BestStreak</c>, or change the negotiation state. This is
    /// <see href="https://cwe.mitre.org/data/definitions/565.html">CWE-565
    /// (Reliance on Cookies without Validation)</see> and
    /// <see href="https://cwe.mitre.org/data/definitions/345.html">CWE-345
    /// (Insufficient Verification of Data Authenticity)</see>.
    /// </para>
    ///
    /// <para>
    /// <b>What this provides.</b> <see cref="Protect"/> wraps the payload in an
    /// authenticated AES + HMAC envelope keyed by the application's machine key.
    /// Any client-side modification will fail HMAC verification and
    /// <see cref="TryUnprotect"/> will return <c>false</c> — the caller then
    /// starts a fresh session instead of trusting the tampered payload.
    /// </para>
    ///
    /// <para>
    /// <b>Purpose strings.</b> Each call site passes a distinct purpose string
    /// so a token minted for one feature cannot be replayed against another
    /// (e.g. an EmojiStory session blob can't be submitted to the Negotiator
    /// endpoint and vice versa). This matches the
    /// <see cref="MachineKey.Protect(byte[], string[])"/> contract.
    /// </para>
    /// </summary>
    public static class SignedPayload
    {
        /// <summary>
        /// Encrypts and signs <paramref name="plaintext"/> for round-tripping
        /// through an untrusted client. The returned string is hex-encoded
        /// and safe to embed in HTML form values, query strings, or cookies.
        /// </summary>
        /// <param name="plaintext">
        /// The serialized state to protect. Typically a JSON document. Must
        /// not be <c>null</c>; empty strings are allowed and round-trip to
        /// an empty string.
        /// </param>
        /// <param name="purpose">
        /// A short, stable identifier of the call site (e.g.
        /// <c>"EmojiStory.Session.v1"</c>). Tokens with different purposes
        /// cannot be substituted for one another.
        /// </param>
        public static string Protect(string plaintext, string purpose)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (string.IsNullOrEmpty(purpose)) throw new ArgumentException("purpose required", nameof(purpose));

            byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] sealed_ = MachineKey.Protect(bytes, purpose);
            return ToHex(sealed_);
        }

        /// <summary>
        /// Verifies the HMAC and decrypts <paramref name="token"/>. Returns
        /// <c>true</c> on success and writes the recovered plaintext to
        /// <paramref name="plaintext"/>. Returns <c>false</c> for any
        /// failure (null/empty token, malformed hex, wrong purpose, bad
        /// signature, machine-key rollover) and leaves <paramref name="plaintext"/>
        /// set to <c>null</c>.
        ///
        /// <para>
        /// Callers should treat a <c>false</c> return as "untrusted client
        /// state, start fresh" — never as a fatal error visible to the user,
        /// which would let an attacker probe the signing key by observing
        /// error pages.
        /// </para>
        /// </summary>
        public static bool TryUnprotect(string token, string purpose, out string plaintext)
        {
            plaintext = null;
            if (string.IsNullOrEmpty(token)) return false;
            if (string.IsNullOrEmpty(purpose)) return false;

            try
            {
                byte[] sealed_ = FromHex(token);
                if (sealed_ == null) return false;
                byte[] bytes = MachineKey.Unprotect(sealed_, purpose);
                if (bytes == null) return false;
                plaintext = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch
            {
                // System.Security.Cryptography.CryptographicException (bad HMAC,
                // wrong purpose, key rollover) and FormatException (bad hex) all
                // collapse to "untrusted, start fresh". Swallow intentionally —
                // do not surface error details to the client.
                return false;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] FromHex(string hex)
        {
            if (hex.Length % 2 != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = HexDigit(hex[i * 2]);
                int lo = HexDigit(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                bytes[i] = (byte)((hi << 4) | lo);
            }
            return bytes;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
