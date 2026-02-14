using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;

namespace Vidly.Tests
{
    [TestClass]
    public class MovieModelTests
    {
        /// <summary>
        /// Validates that the Movie model enforces the [Required] attribute on Name.
        /// </summary>
        [TestMethod]
        public void Movie_NameIsRequired()
        {
            var movie = new Movie { Id = 1, Name = null };
            var results = ValidateModel(movie);

            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains("Name")),
                "Expected a validation error on 'Name' when it is null.");
        }

        /// <summary>
        /// Ensures that a movie with a valid name passes validation.
        /// </summary>
        [TestMethod]
        public void Movie_ValidName_PassesValidation()
        {
            var movie = new Movie { Id = 1, Name = "Inception" };
            var results = ValidateModel(movie);

            Assert.IsFalse(
                results.Any(r => r.MemberNames.Contains("Name")),
                "A valid name should not produce validation errors.");
        }

        /// <summary>
        /// Ensures that Name exceeding 255 characters fails validation.
        /// </summary>
        [TestMethod]
        public void Movie_NameExceeds255Chars_FailsValidation()
        {
            var movie = new Movie { Id = 1, Name = new string('X', 256) };
            var results = ValidateModel(movie);

            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains("Name")),
                "Name exceeding 255 characters should fail validation.");
        }

        /// <summary>
        /// Verifies that Name at exactly 255 characters passes validation.
        /// </summary>
        [TestMethod]
        public void Movie_NameAt255Chars_PassesValidation()
        {
            var movie = new Movie { Id = 1, Name = new string('A', 255) };
            var results = ValidateModel(movie);

            Assert.IsFalse(
                results.Any(r => r.MemberNames.Contains("Name")),
                "Name at exactly 255 characters should pass validation.");
        }

        /// <summary>
        /// Verifies that ReleaseDate is optional (nullable).
        /// </summary>
        [TestMethod]
        public void Movie_ReleaseDateIsOptional()
        {
            var movie = new Movie { Id = 1, Name = "Test", ReleaseDate = null };
            var results = ValidateModel(movie);

            Assert.AreEqual(0, results.Count,
                "A movie without a release date should pass validation.");
        }

        /// <summary>
        /// Verifies that a movie with a release date passes validation.
        /// </summary>
        [TestMethod]
        public void Movie_WithReleaseDate_PassesValidation()
        {
            var movie = new Movie
            {
                Id = 1,
                Name = "Inception",
                ReleaseDate = new DateTime(2010, 7, 16)
            };
            var results = ValidateModel(movie);

            Assert.AreEqual(0, results.Count,
                "A fully valid movie should produce no validation errors.");
        }

        /// <summary>
        /// Ensures default values are sensible (Id = 0, Name = null, ReleaseDate = null, Genre = null, Rating = null).
        /// </summary>
        [TestMethod]
        public void Movie_Defaults_AreNull()
        {
            var movie = new Movie();

            Assert.AreEqual(0, movie.Id);
            Assert.IsNull(movie.Name);
            Assert.IsNull(movie.ReleaseDate);
            Assert.IsNull(movie.Genre);
            Assert.IsNull(movie.Rating);
        }

        /// <summary>
        /// Verifies that Genre is optional.
        /// </summary>
        [TestMethod]
        public void Movie_GenreIsOptional()
        {
            var movie = new Movie { Id = 1, Name = "Test", Genre = null };
            var results = ValidateModel(movie);

            Assert.IsFalse(
                results.Any(r => r.MemberNames.Contains("Genre")),
                "Genre should be optional.");
        }

        /// <summary>
        /// Verifies that a movie with a valid genre passes validation.
        /// </summary>
        [TestMethod]
        public void Movie_WithGenre_PassesValidation()
        {
            var movie = new Movie { Id = 1, Name = "Test", Genre = Genre.Action };
            var results = ValidateModel(movie);

            Assert.AreEqual(0, results.Count,
                "Movie with valid genre should pass validation.");
        }

        /// <summary>
        /// Verifies that Rating is optional.
        /// </summary>
        [TestMethod]
        public void Movie_RatingIsOptional()
        {
            var movie = new Movie { Id = 1, Name = "Test", Rating = null };
            var results = ValidateModel(movie);

            Assert.IsFalse(
                results.Any(r => r.MemberNames.Contains("Rating")),
                "Rating should be optional.");
        }

        /// <summary>
        /// Verifies that valid rating passes validation.
        /// </summary>
        [TestMethod]
        public void Movie_ValidRating_PassesValidation()
        {
            var movie = new Movie { Id = 1, Name = "Test", Rating = 3 };
            var results = ValidateModel(movie);

            Assert.AreEqual(0, results.Count,
                "Movie with valid rating (3) should pass validation.");
        }

        /// <summary>
        /// Verifies that rating below 1 fails validation.
        /// </summary>
        [TestMethod]
        public void Movie_RatingBelowRange_FailsValidation()
        {
            var movie = new Movie { Id = 1, Name = "Test", Rating = 0 };
            var results = ValidateModel(movie);

            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains("Rating")),
                "Rating of 0 should fail validation (min is 1).");
        }

        /// <summary>
        /// Verifies that rating above 5 fails validation.
        /// </summary>
        [TestMethod]
        public void Movie_RatingAboveRange_FailsValidation()
        {
            var movie = new Movie { Id = 1, Name = "Test", Rating = 6 };
            var results = ValidateModel(movie);

            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains("Rating")),
                "Rating of 6 should fail validation (max is 5).");
        }

        /// <summary>
        /// Verifies boundary: rating of 1 passes.
        /// </summary>
        [TestMethod]
        public void Movie_RatingAtMin_PassesValidation()
        {
            var movie = new Movie { Id = 1, Name = "Test", Rating = 1 };
            var results = ValidateModel(movie);

            Assert.AreEqual(0, results.Count,
                "Rating of 1 should pass validation.");
        }

        /// <summary>
        /// Verifies boundary: rating of 5 passes.
        /// </summary>
        [TestMethod]
        public void Movie_RatingAtMax_PassesValidation()
        {
            var movie = new Movie { Id = 1, Name = "Test", Rating = 5 };
            var results = ValidateModel(movie);

            Assert.AreEqual(0, results.Count,
                "Rating of 5 should pass validation.");
        }

        /// <summary>
        /// Verifies fully populated movie with all fields passes validation.
        /// </summary>
        [TestMethod]
        public void Movie_FullyPopulated_PassesValidation()
        {
            var movie = new Movie
            {
                Id = 1,
                Name = "Inception",
                ReleaseDate = new DateTime(2010, 7, 16),
                Genre = Genre.SciFi,
                Rating = 5
            };
            var results = ValidateModel(movie);

            Assert.AreEqual(0, results.Count,
                "A fully populated movie should pass validation.");
        }

        private static IList<ValidationResult> ValidateModel(object model)
        {
            var results = new List<ValidationResult>();
            var context = new ValidationContext(model, null, null);
            Validator.TryValidateObject(model, context, results, validateAllProperties: true);
            return results;
        }
    }
}
