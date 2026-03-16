using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MoodMatcherTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
        }

        private MoodMatcherService CreateService() =>
            new MoodMatcherService(new InMemoryMovieRepository());

        private MoodController CreateController() =>
            new MoodController(new InMemoryMovieRepository());

        // --- Service Tests ---

        [TestMethod]
        public void GetAllMoods_Returns10Moods()
        {
            var service = CreateService();
            var moods = service.GetAllMoods();
            Assert.AreEqual(10, moods.Count);
        }

        [TestMethod]
        public void GetMoodProfile_ValidMood_ReturnsProfile()
        {
            var service = CreateService();
            var profile = service.GetMoodProfile(Mood.Happy);
            Assert.IsNotNull(profile);
            Assert.AreEqual("Happy & Upbeat", profile.DisplayName);
            Assert.AreEqual("😄", profile.Emoji);
            Assert.IsTrue(profile.PreferredGenres.Contains(Genre.Comedy));
        }

        [TestMethod]
        public void GetMoodProfile_InvalidMood_ReturnsNull()
        {
            var service = CreateService();
            var profile = service.GetMoodProfile((Mood)999);
            Assert.IsNull(profile);
        }

        [TestMethod]
        public void GetRecommendations_Happy_ReturnsAnimationAndComedy()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Happy);

            Assert.IsNotNull(result.SelectedMood);
            Assert.AreEqual(Mood.Happy, result.SelectedMood.Mood);
            Assert.IsTrue(result.Recommendations.Count > 0);
            // Shrek (Animation) and Toy Story (Animation) should match
            Assert.IsTrue(result.Recommendations.Any(r => r.Movie.Name == "Shrek!"));
            Assert.IsTrue(result.Recommendations.Any(r => r.Movie.Name == "Toy Story"));
        }

        [TestMethod]
        public void GetRecommendations_Sad_ReturnsDrama()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Sad);

            Assert.IsTrue(result.Recommendations.Any(r => r.Movie.Genre == Genre.Drama));
            Assert.IsTrue(result.Recommendations.Any(r => r.Movie.Name == "The Godfather"));
        }

        [TestMethod]
        public void GetRecommendations_OrderedByRelevanceScore()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Happy);

            for (int i = 1; i < result.Recommendations.Count; i++)
            {
                Assert.IsTrue(
                    result.Recommendations[i - 1].RelevanceScore >= result.Recommendations[i].RelevanceScore,
                    "Recommendations should be ordered by descending relevance score.");
            }
        }

        [TestMethod]
        public void GetRecommendations_MaxResults_LimitsOutput()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Happy, maxResults: 1);
            Assert.IsTrue(result.Recommendations.Count <= 1);
        }

        [TestMethod]
        public void GetRecommendations_MaxResultsClamped_Below1()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Happy, maxResults: -5);
            Assert.IsTrue(result.Recommendations.Count <= 1);
        }

        [TestMethod]
        public void GetRecommendations_InvalidMood_ReturnsEmptyResult()
        {
            var service = CreateService();
            var result = service.GetRecommendations((Mood)999);
            Assert.AreEqual(0, result.Recommendations.Count);
        }

        [TestMethod]
        public void GetRecommendations_IncludesMatchReason()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Happy);

            foreach (var rec in result.Recommendations)
            {
                Assert.IsFalse(string.IsNullOrEmpty(rec.MatchReason),
                    $"Recommendation for {rec.Movie.Name} should have a match reason.");
            }
        }

        [TestMethod]
        public void GetRecommendations_TotalMoviesScanned_Positive()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Happy);
            Assert.IsTrue(result.TotalMoviesScanned > 0);
        }

        [TestMethod]
        public void AllMoodProfiles_HaveRequiredFields()
        {
            var service = CreateService();
            foreach (var profile in service.GetAllMoods())
            {
                Assert.IsFalse(string.IsNullOrEmpty(profile.DisplayName), $"{profile.Mood} missing DisplayName");
                Assert.IsFalse(string.IsNullOrEmpty(profile.Emoji), $"{profile.Mood} missing Emoji");
                Assert.IsFalse(string.IsNullOrEmpty(profile.Description), $"{profile.Mood} missing Description");
                Assert.IsFalse(string.IsNullOrEmpty(profile.ColorHex), $"{profile.Mood} missing ColorHex");
                Assert.IsTrue(profile.PreferredGenres.Count > 0, $"{profile.Mood} has no preferred genres");
            }
        }

        [TestMethod]
        public void Curious_MinRating_PenalizesLowRated()
        {
            var service = CreateService();
            var result = service.GetRecommendations(Mood.Curious);

            // Curious prefers MinRating 4; low-rated genre matches should score lower
            var highRated = result.Recommendations.Where(r => r.Movie.Rating >= 4).ToList();
            var lowRated = result.Recommendations.Where(r => r.Movie.Rating < 4).ToList();

            if (highRated.Any() && lowRated.Any())
            {
                Assert.IsTrue(highRated.First().RelevanceScore > lowRated.First().RelevanceScore);
            }
        }

        // --- Controller Tests ---

        [TestMethod]
        public void Index_ReturnsViewWithMoodProfiles()
        {
            var controller = CreateController();
            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as IReadOnlyList<MoodProfile>;
            Assert.IsNotNull(model);
            Assert.AreEqual(10, model.Count);
        }

        [TestMethod]
        public void Match_ValidMood_ReturnsViewWithResult()
        {
            var controller = CreateController();
            var result = controller.Match((int)Mood.Happy) as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as MoodMatchResult;
            Assert.IsNotNull(model);
            Assert.AreEqual(Mood.Happy, model.SelectedMood.Mood);
        }

        [TestMethod]
        public void Match_InvalidMood_ReturnsHttpNotFound()
        {
            var controller = CreateController();
            var result = controller.Match(999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRepo_Throws()
        {
            new MoodController(null);
        }
    }
}
