using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class DrinkingGameServiceTests
    {
        private DrinkingGameService _service;

        [TestInitialize]
        public void SetUp()
        {
            _service = new DrinkingGameService();
        }

        [TestMethod]
        public void Generate_WithActionMovie_ReturnsGameWithRules()
        {
            var movie = new Movie { Id = 1, Name = "Die Hard", Genre = "Action" };

            var game = _service.Generate(movie, Difficulty.Standard);

            Assert.IsNotNull(game);
            Assert.AreEqual("Die Hard", game.MovieName);
            Assert.AreEqual("Action", game.Genre);
            Assert.AreEqual(Difficulty.Standard, game.Difficulty);
            Assert.AreEqual(7, game.Rules.Count);
        }

        [TestMethod]
        public void Generate_CasualDifficulty_Returns4Rules()
        {
            var movie = new Movie { Id = 2, Name = "Toy Story", Genre = "Comedy" };

            var game = _service.Generate(movie, Difficulty.Casual);

            Assert.AreEqual(4, game.Rules.Count);
        }

        [TestMethod]
        public void Generate_ExpertDifficulty_Returns10Rules()
        {
            var movie = new Movie { Id = 3, Name = "Alien", Genre = "Horror" };

            var game = _service.Generate(movie, Difficulty.Expert);

            Assert.AreEqual(10, game.Rules.Count);
        }

        [TestMethod]
        public void Generate_UnknownGenre_FallsBackToDrama()
        {
            var movie = new Movie { Id = 4, Name = "Mystery Film", Genre = "Mystery" };

            var game = _service.Generate(movie, Difficulty.Standard);

            Assert.IsNotNull(game);
            Assert.AreEqual(7, game.Rules.Count);
        }

        [TestMethod]
        public void Generate_EstimatedSips_IsPositive()
        {
            var movie = new Movie { Id = 5, Name = "Star Wars", Genre = "Sci-Fi" };

            var game = _service.Generate(movie, Difficulty.Standard);

            Assert.IsTrue(game.EstimatedSips > 0, "EstimatedSips should be positive.");
        }

        [TestMethod]
        public void Generate_RulesHaveUniqueIds()
        {
            var movie = new Movie { Id = 6, Name = "The Matrix", Genre = "Sci-Fi" };

            var game = _service.Generate(movie, Difficulty.Expert);
            var ids = game.Rules.Select(r => r.Id).Distinct().Count();

            Assert.AreEqual(game.Rules.Count, ids, "All rule IDs should be unique.");
        }

        [TestMethod]
        public void Generate_HasDisclaimer()
        {
            var movie = new Movie { Id = 7, Name = "Superbad", Genre = "Comedy" };

            var game = _service.Generate(movie, Difficulty.Casual);

            Assert.IsFalse(string.IsNullOrWhiteSpace(game.Disclaimer));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Generate_NullMovie_ThrowsArgumentNullException()
        {
            _service.Generate(null, Difficulty.Standard);
        }
    }
}
