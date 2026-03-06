using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class ParentalControlServiceTests
    {
        #region Helpers

        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private ParentalControlService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _service = new ParentalControlService(_movieRepo, _customerRepo);
        }

        private Movie AddMovie(string name, Genre? genre = null, int? id = null)
        {
            var movie = new Movie { Name = name, Genre = genre };
            if (id.HasValue) movie.Id = id.Value;
            return _movieRepo.Add(movie);
        }

        private Customer AddCustomer(string name, int? id = null)
        {
            var c = new Customer { Name = name };
            if (id.HasValue) c.Id = id.Value;
            return _customerRepo.Add(c);
        }

        #endregion

        // ── Constructor ──────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new ParentalControlService(null, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new ParentalControlService(_movieRepo, null);
        }

        // ── Content Rating Enums ─────────────────────────────────

        [TestMethod]
        public void MovieContentProfile_RatingLabel_G()
        {
            var p = new MovieContentProfile { Rating = ContentRating.G };
            Assert.AreEqual("G", p.RatingLabel);
        }

        [TestMethod]
        public void MovieContentProfile_RatingLabel_PG13()
        {
            var p = new MovieContentProfile { Rating = ContentRating.PG13 };
            Assert.AreEqual("PG-13", p.RatingLabel);
        }

        [TestMethod]
        public void MovieContentProfile_RatingLabel_R()
        {
            var p = new MovieContentProfile { Rating = ContentRating.R };
            Assert.AreEqual("R", p.RatingLabel);
        }

        [TestMethod]
        public void MovieContentProfile_RatingLabel_NC17()
        {
            var p = new MovieContentProfile { Rating = ContentRating.NC17 };
            Assert.AreEqual("NC-17", p.RatingLabel);
        }

        [TestMethod]
        public void MovieContentProfile_RatingLabel_Unrated()
        {
            var p = new MovieContentProfile { Rating = ContentRating.Unrated };
            Assert.AreEqual("NR", p.RatingLabel);
        }

        [TestMethod]
        public void MovieContentProfile_AdvisoryLabels_Multiple()
        {
            var p = new MovieContentProfile
            {
                Advisories = ContentAdvisory.Violence | ContentAdvisory.Language
            };
            var labels = p.AdvisoryLabels;
            Assert.AreEqual(2, labels.Count);
            CollectionAssert.Contains(labels.ToList(), "Violence");
            CollectionAssert.Contains(labels.ToList(), "Strong Language");
        }

        [TestMethod]
        public void MovieContentProfile_AdvisoryLabels_None_Empty()
        {
            var p = new MovieContentProfile { Advisories = ContentAdvisory.None };
            Assert.AreEqual(0, p.AdvisoryLabels.Count);
        }

        [TestMethod]
        public void MovieContentProfile_AdvisoryLabels_AllFlags()
        {
            var all = ContentAdvisory.Violence | ContentAdvisory.Language
                      | ContentAdvisory.NudityOrSexualContent | ContentAdvisory.DrugUse
                      | ContentAdvisory.ScaryScenes | ContentAdvisory.ThematicElements
                      | ContentAdvisory.SmokingOrAlcohol | ContentAdvisory.GamblingDepictions;
            var p = new MovieContentProfile { Advisories = all };
            Assert.AreEqual(8, p.AdvisoryLabels.Count);
        }

        // ── ParentalControlProfile ───────────────────────────────

        [TestMethod]
        public void ParentalControlProfile_HasPin_ValidPin()
        {
            var p = new ParentalControlProfile { Pin = "1234" };
            Assert.IsTrue(p.HasPin);
        }

        [TestMethod]
        public void ParentalControlProfile_HasPin_NullPin()
        {
            var p = new ParentalControlProfile { Pin = null };
            Assert.IsFalse(p.HasPin);
        }

        [TestMethod]
        public void ParentalControlProfile_HasPin_ShortPin()
        {
            var p = new ParentalControlProfile { Pin = "12" };
            Assert.IsFalse(p.HasPin);
        }

        [TestMethod]
        public void ParentalControlProfile_HasPin_NonDigitPin()
        {
            var p = new ParentalControlProfile { Pin = "abcd" };
            Assert.IsFalse(p.HasPin);
        }

        // ── SetMovieRating ───────────────────────────────────────

        [TestMethod]
        public void SetMovieRating_ValidMovie_ReturnsProfile()
        {
            var movie = AddMovie("Toy Story", Genre.Animation);
            var profile = _service.SetMovieRating(movie.Id, ContentRating.G);
            Assert.AreEqual(ContentRating.G, profile.Rating);
            Assert.AreEqual(movie.Id, profile.MovieId);
        }

        [TestMethod]
        public void SetMovieRating_WithAdvisories_SetsFlags()
        {
            var movie = AddMovie("Die Hard", Genre.Action);
            var profile = _service.SetMovieRating(movie.Id, ContentRating.R,
                ContentAdvisory.Violence | ContentAdvisory.Language, "Classic action film");
            Assert.IsTrue(profile.Advisories.HasFlag(ContentAdvisory.Violence));
            Assert.IsTrue(profile.Advisories.HasFlag(ContentAdvisory.Language));
            Assert.AreEqual("Classic action film", profile.CustomNote);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SetMovieRating_InvalidMovie_Throws()
        {
            _service.SetMovieRating(999, ContentRating.PG);
        }

        [TestMethod]
        public void SetMovieRating_UpdateExisting_Overwrites()
        {
            var movie = AddMovie("Film", Genre.Drama);
            _service.SetMovieRating(movie.Id, ContentRating.PG);
            _service.SetMovieRating(movie.Id, ContentRating.R);
            var profile = _service.GetMovieProfile(movie.Id);
            Assert.AreEqual(ContentRating.R, profile.Rating);
        }

        // ── GetMovieProfile ──────────────────────────────────────

        [TestMethod]
        public void GetMovieProfile_NotRated_ReturnsNull()
        {
            Assert.IsNull(_service.GetMovieProfile(123));
        }

        [TestMethod]
        public void GetMovieProfile_AfterRating_ReturnsProfile()
        {
            var movie = AddMovie("Test Film");
            _service.SetMovieRating(movie.Id, ContentRating.PG13);
            var profile = _service.GetMovieProfile(movie.Id);
            Assert.IsNotNull(profile);
            Assert.AreEqual(ContentRating.PG13, profile.Rating);
        }

        // ── SuggestRating ────────────────────────────────────────

        [TestMethod]
        public void SuggestRating_Animation_ReturnsG()
        {
            Assert.AreEqual(ContentRating.G, _service.SuggestRating(Genre.Animation));
        }

        [TestMethod]
        public void SuggestRating_Horror_ReturnsR()
        {
            Assert.AreEqual(ContentRating.R, _service.SuggestRating(Genre.Horror));
        }

        [TestMethod]
        public void SuggestRating_Comedy_ReturnsPG()
        {
            Assert.AreEqual(ContentRating.PG, _service.SuggestRating(Genre.Comedy));
        }

        [TestMethod]
        public void SuggestRating_Drama_ReturnsPG13()
        {
            Assert.AreEqual(ContentRating.PG13, _service.SuggestRating(Genre.Drama));
        }

        // ── SuggestAdvisories ────────────────────────────────────

        [TestMethod]
        public void SuggestAdvisories_Horror_ViolenceAndScary()
        {
            var adv = _service.SuggestAdvisories(Genre.Horror);
            Assert.IsTrue(adv.HasFlag(ContentAdvisory.Violence));
            Assert.IsTrue(adv.HasFlag(ContentAdvisory.ScaryScenes));
        }

        [TestMethod]
        public void SuggestAdvisories_Animation_None()
        {
            Assert.AreEqual(ContentAdvisory.None, _service.SuggestAdvisories(Genre.Animation));
        }

        // ── AutoRateUnratedMovies ────────────────────────────────

        [TestMethod]
        public void AutoRateUnratedMovies_RatesAll()
        {
            AddMovie("Toy Story", Genre.Animation);
            AddMovie("Die Hard", Genre.Action);
            AddMovie("The Ring", Genre.Horror);

            var count = _service.AutoRateUnratedMovies();
            Assert.AreEqual(3, count);
        }

        [TestMethod]
        public void AutoRateUnratedMovies_SkipsAlreadyRated()
        {
            var movie = AddMovie("Toy Story", Genre.Animation);
            _service.SetMovieRating(movie.Id, ContentRating.G);
            AddMovie("Die Hard", Genre.Action);

            var count = _service.AutoRateUnratedMovies();
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void AutoRateUnratedMovies_SkipsNoGenre()
        {
            AddMovie("Unknown Film"); // no genre
            Assert.AreEqual(0, _service.AutoRateUnratedMovies());
        }

        // ── EnableControls ───────────────────────────────────────

        [TestMethod]
        public void EnableControls_ValidCustomer_ReturnsProfile()
        {
            var customer = AddCustomer("Alice");
            var profile = _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsTrue(profile.IsEnabled);
            Assert.AreEqual(ContentRating.PG, profile.MaxAllowedRating);
            Assert.IsTrue(profile.HasPin);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void EnableControls_InvalidCustomer_Throws()
        {
            _service.EnableControls(999, ContentRating.PG);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void EnableControls_BadPin_Throws()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "abc");
        }

        [TestMethod]
        public void EnableControls_NullPin_NoPinSet()
        {
            var customer = AddCustomer("Alice");
            var profile = _service.EnableControls(customer.Id, ContentRating.PG);
            Assert.IsFalse(profile.HasPin);
        }

        // ── DisableControls ──────────────────────────────────────

        [TestMethod]
        public void DisableControls_WithCorrectPin_Disables()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsTrue(_service.DisableControls(customer.Id, "1234"));
            var profile = _service.GetControlProfile(customer.Id);
            Assert.IsFalse(profile.IsEnabled);
        }

        [TestMethod]
        public void DisableControls_WrongPin_Fails()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsFalse(_service.DisableControls(customer.Id, "9999"));
        }

        [TestMethod]
        public void DisableControls_NoProfile_ReturnsFalse()
        {
            Assert.IsFalse(_service.DisableControls(999));
        }

        [TestMethod]
        public void DisableControls_NoPin_Succeeds()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG);
            Assert.IsTrue(_service.DisableControls(customer.Id));
        }

        // ── UpdatePin ────────────────────────────────────────────

        [TestMethod]
        public void UpdatePin_CorrectOldPin_Updates()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsTrue(_service.UpdatePin(customer.Id, "1234", "5678"));
            var profile = _service.GetControlProfile(customer.Id);
            Assert.AreEqual("5678", profile.Pin);
        }

        [TestMethod]
        public void UpdatePin_WrongOldPin_Fails()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsFalse(_service.UpdatePin(customer.Id, "0000", "5678"));
        }

        [TestMethod]
        public void UpdatePin_InvalidNewPin_Fails()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsFalse(_service.UpdatePin(customer.Id, "1234", "ab"));
        }

        [TestMethod]
        public void UpdatePin_NoProfile_Fails()
        {
            Assert.IsFalse(_service.UpdatePin(999, null, "1234"));
        }

        // ── CheckAccess ──────────────────────────────────────────

        [TestMethod]
        public void CheckAccess_NoControls_Allowed()
        {
            var movie = AddMovie("Test", Genre.Horror);
            var customer = AddCustomer("Bob");
            _service.SetMovieRating(movie.Id, ContentRating.R);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsTrue(result.IsAllowed);
        }

        [TestMethod]
        public void CheckAccess_ControlsDisabled_Allowed()
        {
            var movie = AddMovie("Test", Genre.Horror);
            var customer = AddCustomer("Bob");
            _service.SetMovieRating(movie.Id, ContentRating.R);
            _service.EnableControls(customer.Id, ContentRating.PG);
            _service.DisableControls(customer.Id);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsTrue(result.IsAllowed);
        }

        [TestMethod]
        public void CheckAccess_RatingExceeded_Blocked()
        {
            var movie = AddMovie("Horror Film", Genre.Horror);
            var customer = AddCustomer("Alice");
            _service.SetMovieRating(movie.Id, ContentRating.R);
            _service.EnableControls(customer.Id, ContentRating.PG);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsFalse(result.IsAllowed);
            Assert.IsTrue(result.BlockReason.Contains("R"));
        }

        [TestMethod]
        public void CheckAccess_RatingWithinLimit_Allowed()
        {
            var movie = AddMovie("Family Film", Genre.Animation);
            var customer = AddCustomer("Alice");
            _service.SetMovieRating(movie.Id, ContentRating.G);
            _service.EnableControls(customer.Id, ContentRating.PG);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsTrue(result.IsAllowed);
        }

        [TestMethod]
        public void CheckAccess_BlockedAdvisory_Blocked()
        {
            var movie = AddMovie("Drug Film");
            var customer = AddCustomer("Alice");
            _service.SetMovieRating(movie.Id, ContentRating.PG,
                ContentAdvisory.DrugUse);
            _service.EnableControls(customer.Id, ContentRating.PG13,
                blockAdvisories: ContentAdvisory.DrugUse);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsFalse(result.IsAllowed);
            Assert.IsTrue(result.BlockReason.Contains("Drug Use"));
        }

        [TestMethod]
        public void CheckAccess_WarnAdvisory_AllowedWithWarnings()
        {
            var movie = AddMovie("Violent Film");
            var customer = AddCustomer("Alice");
            _service.SetMovieRating(movie.Id, ContentRating.PG13,
                ContentAdvisory.Violence);
            _service.EnableControls(customer.Id, ContentRating.R,
                warnAdvisories: ContentAdvisory.Violence);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsTrue(result.IsAllowed);
            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(1, result.Warnings.Count);
        }

        [TestMethod]
        public void CheckAccess_UnratedMovie_BlockedForStrictProfile()
        {
            var movie = AddMovie("Unknown Film");
            var customer = AddCustomer("Alice");
            // No rating set, strict controls (max PG)
            _service.EnableControls(customer.Id, ContentRating.PG);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsFalse(result.IsAllowed);
            Assert.IsTrue(result.BlockReason.Contains("not been rated"));
        }

        [TestMethod]
        public void CheckAccess_UnratedMovie_AllowedForLenientProfile()
        {
            var movie = AddMovie("Unknown Film");
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.R);
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsTrue(result.IsAllowed);
        }

        [TestMethod]
        public void CheckAccess_CanOverrideWithPin_TrueWhenPinSet()
        {
            var movie = AddMovie("R Film");
            var customer = AddCustomer("Alice");
            _service.SetMovieRating(movie.Id, ContentRating.R);
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            var result = _service.CheckAccess(customer.Id, movie.Id);
            Assert.IsFalse(result.IsAllowed);
            Assert.IsTrue(result.CanOverrideWithPin);
        }

        // ── TryOverrideWithPin ───────────────────────────────────

        [TestMethod]
        public void TryOverrideWithPin_CorrectPin_ReturnsTrue()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsTrue(_service.TryOverrideWithPin(customer.Id, "1234"));
        }

        [TestMethod]
        public void TryOverrideWithPin_WrongPin_ReturnsFalse()
        {
            var customer = AddCustomer("Alice");
            _service.EnableControls(customer.Id, ContentRating.PG, "1234");
            Assert.IsFalse(_service.TryOverrideWithPin(customer.Id, "0000"));
        }

        [TestMethod]
        public void TryOverrideWithPin_NoProfile_ReturnsFalse()
        {
            Assert.IsFalse(_service.TryOverrideWithPin(999, "1234"));
        }

        // ── GetAllowedMovies ─────────────────────────────────────

        [TestMethod]
        public void GetAllowedMovies_FiltersBlockedMovies()
        {
            var m1 = AddMovie("G Film", Genre.Animation);
            var m2 = AddMovie("R Film", Genre.Horror);
            var customer = AddCustomer("Alice");
            _service.SetMovieRating(m1.Id, ContentRating.G);
            _service.SetMovieRating(m2.Id, ContentRating.R);
            _service.EnableControls(customer.Id, ContentRating.PG);
            var allowed = _service.GetAllowedMovies(customer.Id);
            Assert.AreEqual(1, allowed.Count);
            Assert.AreEqual("G Film", allowed[0].Name);
        }

        [TestMethod]
        public void GetAllowedMovies_NoControls_ReturnsAll()
        {
            AddMovie("Film A");
            AddMovie("Film B");
            var customer = AddCustomer("Bob");
            var allowed = _service.GetAllowedMovies(customer.Id);
            Assert.AreEqual(2, allowed.Count);
        }

        // ── GetFamilyFriendlyMovies ──────────────────────────────

        [TestMethod]
        public void GetFamilyFriendlyMovies_OnlyGAndPG()
        {
            var m1 = AddMovie("Kids Film", Genre.Animation);
            var m2 = AddMovie("Teen Film", Genre.Drama);
            var m3 = AddMovie("Adult Film", Genre.Horror);
            _service.SetMovieRating(m1.Id, ContentRating.G);
            _service.SetMovieRating(m2.Id, ContentRating.PG13);
            _service.SetMovieRating(m3.Id, ContentRating.R);
            var family = _service.GetFamilyFriendlyMovies();
            Assert.AreEqual(1, family.Count);
            Assert.AreEqual("Kids Film", family[0].Name);
        }

        [TestMethod]
        public void GetFamilyFriendlyMovies_ExcludesUnrated()
        {
            AddMovie("Unrated Film");
            var family = _service.GetFamilyFriendlyMovies();
            Assert.AreEqual(0, family.Count);
        }

        // ── GetMoviesByRating ────────────────────────────────────

        [TestMethod]
        public void GetMoviesByRating_FiltersByRating()
        {
            var m1 = AddMovie("G Film", Genre.Animation);
            var m2 = AddMovie("R Film", Genre.Horror);
            _service.SetMovieRating(m1.Id, ContentRating.G);
            _service.SetMovieRating(m2.Id, ContentRating.R);
            var gMovies = _service.GetMoviesByRating(ContentRating.G);
            Assert.AreEqual(1, gMovies.Count);
            Assert.AreEqual("G Film", gMovies[0].Name);
        }

        // ── GetMoviesByAdvisory ──────────────────────────────────

        [TestMethod]
        public void GetMoviesByAdvisory_FiltersByFlag()
        {
            var m1 = AddMovie("Violent Film");
            var m2 = AddMovie("Clean Film");
            _service.SetMovieRating(m1.Id, ContentRating.R, ContentAdvisory.Violence);
            _service.SetMovieRating(m2.Id, ContentRating.G);
            var violent = _service.GetMoviesByAdvisory(ContentAdvisory.Violence);
            Assert.AreEqual(1, violent.Count);
            Assert.AreEqual("Violent Film", violent[0].Name);
        }

        // ── GetRatingDistribution ────────────────────────────────

        [TestMethod]
        public void GetRatingDistribution_CountsCorrectly()
        {
            var m1 = AddMovie("G Film");
            var m2 = AddMovie("R Film");
            AddMovie("Unrated Film");
            _service.SetMovieRating(m1.Id, ContentRating.G);
            _service.SetMovieRating(m2.Id, ContentRating.R);

            var dist = _service.GetRatingDistribution();
            Assert.AreEqual(1, dist[ContentRating.G]);
            Assert.AreEqual(1, dist[ContentRating.R]);
            Assert.IsTrue(dist[ContentRating.Unrated] >= 1);
        }

        // ── GetAdvisoryDistribution ──────────────────────────────

        [TestMethod]
        public void GetAdvisoryDistribution_CountsFlags()
        {
            var m1 = AddMovie("A");
            var m2 = AddMovie("B");
            _service.SetMovieRating(m1.Id, ContentRating.R,
                ContentAdvisory.Violence | ContentAdvisory.Language);
            _service.SetMovieRating(m2.Id, ContentRating.R,
                ContentAdvisory.Violence);
            var dist = _service.GetAdvisoryDistribution();
            Assert.AreEqual(2, dist[ContentAdvisory.Violence]);
            Assert.AreEqual(1, dist[ContentAdvisory.Language]);
            Assert.AreEqual(0, dist[ContentAdvisory.DrugUse]);
        }

        // ── GetFamilyFriendlyPercent ─────────────────────────────

        [TestMethod]
        public void GetFamilyFriendlyPercent_CalculatesCorrectly()
        {
            var m1 = AddMovie("G Film");
            var m2 = AddMovie("R Film");
            _service.SetMovieRating(m1.Id, ContentRating.G);
            _service.SetMovieRating(m2.Id, ContentRating.R);
            var pct = _service.GetFamilyFriendlyPercent();
            Assert.AreEqual(50.0, pct);
        }

        [TestMethod]
        public void GetFamilyFriendlyPercent_EmptyCatalog_ReturnsZero()
        {
            Assert.AreEqual(0.0, _service.GetFamilyFriendlyPercent());
        }

        // ── ContentAccessResult static helpers ───────────────────

        [TestMethod]
        public void ContentAccessResult_Allowed_Properties()
        {
            var r = ContentAccessResult.Allowed();
            Assert.IsTrue(r.IsAllowed);
            Assert.IsFalse(r.HasWarnings);
            Assert.IsNull(r.BlockReason);
        }

        [TestMethod]
        public void ContentAccessResult_AllowedWithWarnings_Properties()
        {
            var r = ContentAccessResult.AllowedWithWarnings(
                new List<string> { "violence" });
            Assert.IsTrue(r.IsAllowed);
            Assert.IsTrue(r.HasWarnings);
            Assert.AreEqual(1, r.Warnings.Count);
        }

        [TestMethod]
        public void ContentAccessResult_Blocked_Properties()
        {
            var r = ContentAccessResult.Blocked("Too violent", true);
            Assert.IsFalse(r.IsAllowed);
            Assert.AreEqual("Too violent", r.BlockReason);
            Assert.IsTrue(r.CanOverrideWithPin);
        }
    }
}
