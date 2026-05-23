using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Utilities;

namespace Vidly.Tests
{
    /// <summary>
    /// Verifies the <see cref="SignedPayload"/> protect/unprotect round-trip
    /// and, critically, that tampered tokens are rejected. These tests
    /// guard the fix for the EmojiStory hidden-field tampering bug
    /// (CWE-565 / CWE-345): if HMAC verification ever silently degrades to
    /// "accept anything", a player could once again forge their score.
    /// </summary>
    [TestClass]
    public class SignedPayloadTests
    {
        private const string Purpose = "Vidly.Tests.SignedPayload";
        private const string OtherPurpose = "Vidly.Tests.SignedPayload.Other";

        [TestMethod]
        public void Protect_Unprotect_RoundTripsArbitraryPayload()
        {
            const string payload = "{\"Score\":42,\"BestStreak\":7,\"History\":[]}";

            var token = SignedPayload.Protect(payload, Purpose);
            Assert.IsFalse(string.IsNullOrEmpty(token), "Protect should produce a non-empty token.");
            Assert.AreNotEqual(payload, token, "Token must not equal the plaintext.");

            Assert.IsTrue(SignedPayload.TryUnprotect(token, Purpose, out var recovered));
            Assert.AreEqual(payload, recovered);
        }

        [TestMethod]
        public void Protect_EmptyString_RoundTrips()
        {
            var token = SignedPayload.Protect(string.Empty, Purpose);
            Assert.IsTrue(SignedPayload.TryUnprotect(token, Purpose, out var recovered));
            Assert.AreEqual(string.Empty, recovered);
        }

        [TestMethod]
        public void Protect_ProducesDifferentTokensForSamePayload()
        {
            // MachineKey.Protect uses a fresh IV per call, so two protect
            // calls with the same input must produce distinct ciphertexts.
            // Otherwise a client could correlate identical sessions.
            var a = SignedPayload.Protect("hello", Purpose);
            var b = SignedPayload.Protect("hello", Purpose);
            Assert.AreNotEqual(a, b, "Protect output must be non-deterministic.");
        }

        [TestMethod]
        public void TryUnprotect_TamperedToken_ReturnsFalse()
        {
            var token = SignedPayload.Protect("{\"Score\":0}", Purpose);

            // Flip the last hex nibble - this corrupts either ciphertext or
            // HMAC. Either way verification must fail.
            var tampered = token.Substring(0, token.Length - 1)
                + (token[token.Length - 1] == 'a' ? 'b' : 'a');

            Assert.IsFalse(SignedPayload.TryUnprotect(tampered, Purpose, out var recovered),
                "Tampered token must be rejected.");
            Assert.IsNull(recovered);
        }

        [TestMethod]
        public void TryUnprotect_WrongPurpose_ReturnsFalse()
        {
            // A token minted for one feature must not be replayable against
            // another, even if the attacker knows the other purpose string.
            var token = SignedPayload.Protect("{\"Score\":0}", Purpose);

            Assert.IsFalse(SignedPayload.TryUnprotect(token, OtherPurpose, out var recovered));
            Assert.IsNull(recovered);
        }

        [TestMethod]
        public void TryUnprotect_Null_ReturnsFalse()
        {
            Assert.IsFalse(SignedPayload.TryUnprotect(null, Purpose, out var recovered));
            Assert.IsNull(recovered);
        }

        [TestMethod]
        public void TryUnprotect_Empty_ReturnsFalse()
        {
            Assert.IsFalse(SignedPayload.TryUnprotect(string.Empty, Purpose, out var recovered));
            Assert.IsNull(recovered);
        }

        [TestMethod]
        public void TryUnprotect_MalformedHex_ReturnsFalse()
        {
            // Odd length and non-hex characters must both be rejected
            // without throwing.
            Assert.IsFalse(SignedPayload.TryUnprotect("abc", Purpose, out _));
            Assert.IsFalse(SignedPayload.TryUnprotect("zzzz", Purpose, out _));
        }

        [TestMethod]
        public void TryUnprotect_RandomBytes_ReturnsFalse()
        {
            // 64 bytes of fake "ciphertext" - MachineKey.Unprotect should
            // throw CryptographicException internally; SignedPayload must
            // swallow that and return false.
            var bogus = new string('0', 128);
            Assert.IsFalse(SignedPayload.TryUnprotect(bogus, Purpose, out var recovered));
            Assert.IsNull(recovered);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Protect_NullPlaintext_Throws()
        {
            SignedPayload.Protect(null, Purpose);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Protect_EmptyPurpose_Throws()
        {
            SignedPayload.Protect("x", "");
        }
    }
}
