using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class ReferralServiceTests
    {
        private ReferralService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryCustomerRepository.Reset();
            _service = new ReferralService(new InMemoryCustomerRepository());
        }

        [TestMethod]
        public void GenerateReferralCode_ValidCustomer_ReturnsCodeWithPrefix()
        {
            var code = _service.GenerateReferralCode(1);
            Assert.IsTrue(code.StartsWith("REF-1-"));
            Assert.AreEqual(12, code.Length); // REF-1-XXXXXX
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateReferralCode_InvalidCustomer_Throws()
        {
            _service.GenerateReferralCode(999);
        }

        [TestMethod]
        public void CreateReferral_Valid_ReturnsReferral()
        {
            var referral = _service.CreateReferral(1, "Test Friend", "friend@test.com");

            Assert.IsNotNull(referral);
            Assert.AreEqual(1, referral.ReferrerId);
            Assert.AreEqual("Test Friend", referral.ReferredName);
            Assert.AreEqual("friend@test.com", referral.ReferredEmail);
            Assert.AreEqual(ReferralStatus.Pending, referral.Status);
            Assert.IsTrue(referral.ReferralCode.StartsWith("REF-1-"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_EmptyName_Throws()
        {
            _service.CreateReferral(1, "", "friend@test.com");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_EmptyEmail_Throws()
        {
            _service.CreateReferral(1, "Friend", "");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_InvalidCustomer_Throws()
        {
            _service.CreateReferral(999, "Friend", "friend@test.com");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CreateReferral_DuplicateEmail_Throws()
        {
            _service.CreateReferral(1, "Friend A", "same@test.com");
            _service.CreateReferral(1, "Friend B", "same@test.com");
        }

        [TestMethod]
        public void CreateReferral_SameEmailDifferentReferrer_Succeeds()
        {
            _service.CreateReferral(1, "Friend A", "shared@test.com");
            var r2 = _service.CreateReferral(2, "Friend A", "shared@test.com");
            Assert.IsNotNull(r2);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CreateReferral_ExceedsPendingLimit_Throws()
        {
            for (int i = 0; i <= ReferralService.MaxPendingPerCustomer; i++)
            {
                _service.CreateReferral(1, $"Friend {i}", $"friend{i}@test.com");
            }
        }

        [TestMethod]
        public void ConvertReferral_Valid_UpdatesStatusAndPoints()
        {
            var referral = _service.CreateReferral(1, "Friend", "friend@test.com");
            var converted = _service.ConvertReferral(referral.ReferralCode, 2);

            Assert.AreEqual(ReferralStatus.Converted, converted.Status);
            Assert.AreEqual(2, converted.ReferredCustomerId);
            Assert.AreEqual(ReferralService.ConversionBonusPoints, converted.PointsAwarded);
            Assert.IsNotNull(converted.ConvertedDate);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ConvertReferral_InvalidCode_Throws()
        {
            _service.ConvertReferral("INVALID-CODE", 2);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ConvertReferral_AlreadyConverted_Throws()
        {
            var referral = _service.CreateReferral(1, "Friend", "friend@test.com");
            _service.ConvertReferral(referral.ReferralCode, 2);
            _service.ConvertReferral(referral.ReferralCode, 3); // should throw
        }

        [TestMethod]
        public void GetReferralsByCustomer_ReturnsOnlyThatCustomer()
        {
            _service.CreateReferral(1, "Friend A", "a@test.com");
            _service.CreateReferral(1, "Friend B", "b@test.com");
            _service.CreateReferral(2, "Friend C", "c@test.com");

            var refs = _service.GetReferralsByCustomer(1);
            Assert.AreEqual(2, refs.Count);
            Assert.IsTrue(refs.All(r => r.ReferrerId == 1));
        }

        [TestMethod]
        public void GetByCode_ExistingCode_ReturnsReferral()
        {
            var created = _service.CreateReferral(1, "Friend", "f@test.com");
            var found = _service.GetByCode(created.ReferralCode);

            Assert.IsNotNull(found);
            Assert.AreEqual(created.Id, found.Id);
        }

        [TestMethod]
        public void GetByCode_NonExistent_ReturnsNull()
        {
            Assert.IsNull(_service.GetByCode("NOPE"));
        }

        [TestMethod]
        public void GetByCode_Null_ReturnsNull()
        {
            Assert.IsNull(_service.GetByCode(null));
        }

        [TestMethod]
        public void GetCustomerSummary_WithMixedStatuses_CalculatesCorrectly()
        {
            _service.CreateReferral(1, "A", "a@test.com");
            var r2 = _service.CreateReferral(1, "B", "b@test.com");
            _service.ConvertReferral(r2.ReferralCode, 3);
            _service.CreateReferral(1, "C", "c@test.com");

            var summary = _service.GetCustomerSummary(1);

            Assert.AreEqual(1, summary.CustomerId);
            Assert.AreEqual(3, summary.TotalReferrals);
            Assert.AreEqual(1, summary.ConvertedCount);
            Assert.AreEqual(2, summary.PendingCount);
            Assert.AreEqual(ReferralService.ConversionBonusPoints, summary.TotalPointsEarned);
            Assert.IsTrue(summary.ConversionRate > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetCustomerSummary_InvalidCustomer_Throws()
        {
            _service.GetCustomerSummary(999);
        }

        [TestMethod]
        public void GetProgramStats_Empty_ReturnsZeros()
        {
            var stats = _service.GetProgramStats();

            Assert.AreEqual(0, stats.TotalReferrals);
            Assert.AreEqual(0, stats.TotalConverted);
            Assert.AreEqual(0, stats.OverallConversionRate);
        }

        [TestMethod]
        public void GetProgramStats_WithData_CalculatesLeaderboard()
        {
            var r1 = _service.CreateReferral(1, "A", "a@test.com");
            _service.ConvertReferral(r1.ReferralCode, 3);
            _service.CreateReferral(2, "B", "b@test.com");

            var stats = _service.GetProgramStats();

            Assert.AreEqual(2, stats.TotalReferrals);
            Assert.AreEqual(1, stats.TotalConverted);
            Assert.AreEqual(1, stats.TotalPending);
            Assert.IsTrue(stats.Leaderboard.Count > 0);
            Assert.AreEqual(1, stats.Leaderboard[0].Rank);
        }

        [TestMethod]
        public void GetAll_NoFilter_ReturnsAllReferrals()
        {
            _service.CreateReferral(1, "A", "a@test.com");
            _service.CreateReferral(2, "B", "b@test.com");

            var all = _service.GetAll();
            Assert.AreEqual(2, all.Count);
        }

        [TestMethod]
        public void GetAll_WithStatusFilter_ReturnsFiltered()
        {
            _service.CreateReferral(1, "A", "a@test.com");
            var r2 = _service.CreateReferral(1, "B", "b@test.com");
            _service.ConvertReferral(r2.ReferralCode, 3);

            var pending = _service.GetAll(ReferralStatus.Pending);
            Assert.AreEqual(1, pending.Count);

            var converted = _service.GetAll(ReferralStatus.Converted);
            Assert.AreEqual(1, converted.Count);
        }

        [TestMethod]
        public void ConvertReferral_CaseInsensitiveCode()
        {
            var r = _service.CreateReferral(1, "Friend", "f@test.com");
            var converted = _service.ConvertReferral(r.ReferralCode.ToLower(), 2);
            Assert.AreEqual(ReferralStatus.Converted, converted.Status);
        }

        [TestMethod]
        public void LeaderboardTiers_CorrectAssignment()
        {
            // Create and convert 3 referrals for customer 1 → Advocate
            for (int i = 0; i < 3; i++)
            {
                var r = _service.CreateReferral(1, $"F{i}", $"f{i}@test.com");
                // Need unique new customers - use existing IDs 2-4
                _service.ConvertReferral(r.ReferralCode, i + 2);
            }

            var stats = _service.GetProgramStats();
            var entry = stats.Leaderboard.First(e => e.CustomerId == 1);
            Assert.AreEqual("Advocate", entry.Tier);
            Assert.AreEqual(3, entry.ConvertedReferrals);
        }

        [TestMethod]
        public void ExpireOldReferrals_NoPendingToExpire_ReturnsZero()
        {
            var count = _service.ExpireOldReferrals();
            Assert.AreEqual(0, count);
        }

        [TestMethod]
        public void GenerateReferralCode_CodesAreUnpredictable()
        {
            // Generating multiple codes in rapid succession should produce
            // different suffixes. With System.Random seeded from the clock,
            // codes generated in the same millisecond were identical.
            // CSPRNG ensures uniqueness even under tight timing.
            var codes = Enumerable.Range(0, 20)
                .Select(_ => _service.GenerateReferralCode(1))
                .ToList();

            var suffixes = codes.Select(c => c.Substring("REF-1-".Length)).ToList();
            var uniqueSuffixes = suffixes.Distinct().Count();

            // With 32^6 = ~1 billion possible codes, 20 draws should never
            // collide. Allow at most 1 collision to be safe.
            Assert.IsTrue(uniqueSuffixes >= 19,
                $"Expected at least 19 unique suffixes from CSPRNG, got {uniqueSuffixes}. " +
                "Codes may be using a predictable seed.");
        }

        [TestMethod]
        public void GenerateReferralCode_SuffixUsesExpectedCharset()
        {
            // Verify the suffix only contains characters from the allowed set
            // (no ambiguous chars like 0, O, 1, I, L)
            var allowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var code = _service.GenerateReferralCode(1);
            var suffix = code.Substring("REF-1-".Length);

            Assert.AreEqual(6, suffix.Length);
            foreach (var c in suffix)
            {
                Assert.IsTrue(allowedChars.Contains(c),
                    $"Unexpected character '{c}' in referral code suffix.");
            }
        }

        // ── Input validation tests ──────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_InvalidEmail_NoAtSign_Throws()
        {
            _service.CreateReferral(1, "Test", "invalidemail");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_InvalidEmail_NoDot_Throws()
        {
            _service.CreateReferral(1, "Test", "user@localhost");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_InvalidEmail_MultipleAt_Throws()
        {
            _service.CreateReferral(1, "Test", "user@@domain.com");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_InvalidEmail_WhitespaceInEmail_Throws()
        {
            _service.CreateReferral(1, "Test", "user @domain.com");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_InvalidEmail_EmptyLocalPart_Throws()
        {
            _service.CreateReferral(1, "Test", "@domain.com");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_InvalidEmail_DomainStartsWithDot_Throws()
        {
            _service.CreateReferral(1, "Test", "user@.domain.com");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_NameTooLong_Throws()
        {
            var longName = new string('A', ReferralService.MaxNameLength + 1);
            _service.CreateReferral(1, longName, "friend@test.com");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateReferral_EmailTooLong_Throws()
        {
            var longEmail = new string('a', 250) + "@b.co";
            _service.CreateReferral(1, "Test", longEmail);
        }

        [TestMethod]
        public void CreateReferral_ValidEmail_Succeeds()
        {
            var referral = _service.CreateReferral(1, "Test", "valid.user+tag@sub.domain.com");
            Assert.IsNotNull(referral);
            Assert.AreEqual("valid.user+tag@sub.domain.com", referral.ReferredEmail);
        }

        [TestMethod]
        public void IsValidEmailFormat_VariousInputs()
        {
            // Valid
            Assert.IsTrue(ReferralService.IsValidEmailFormat("a@b.com"));
            Assert.IsTrue(ReferralService.IsValidEmailFormat("user+tag@domain.co.uk"));
            Assert.IsTrue(ReferralService.IsValidEmailFormat("test.email@example.org"));

            // Invalid
            Assert.IsFalse(ReferralService.IsValidEmailFormat(null));
            Assert.IsFalse(ReferralService.IsValidEmailFormat(""));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("nope"));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("@domain.com"));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("user@"));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("user@domain"));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("user@.com"));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("user@domain."));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("user@domain..com"));
            Assert.IsFalse(ReferralService.IsValidEmailFormat("us er@domain.com"));
        }
    }
}
