using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class MarathonPlannerServiceTests
    {
        private InMemoryMovieRepository _repo;
        private MarathonPlannerService _service;

        [TestInitialize]
        public void Setup()
        {
            _repo = new InMemoryMovieRepository();
            _service = new MarathonPlannerService(_repo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRepo_Throws()
        {
            new MarathonPlannerService(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void BuildPlan_NullRequest_Throws()
        {
            _service.BuildPlan(null);
        }

        [TestMethod]
        public void BuildPlan_EmptyMovieIds_ReturnsEmptyPlan()
        {
            var request = new MarathonRequest { MovieIds = new List<int>() };
            var plan = _service.BuildPlan(request);
            Assert.AreEqual(0, plan.MovieCount);
            Assert.AreEqual(TimeSpan.Zero, plan.TotalWatchTime);
        }

        [TestMethod]
        public void BuildPlan_InvalidMovieIds_ReturnsEmptyPlan()
        {
            var request = new MarathonRequest { MovieIds = new List<int> { 999, 998 } };
            var plan = _service.BuildPlan(request);
            Assert.AreEqual(0, plan.MovieCount);
        }

        [TestMethod]
        public void BuildPlan_SingleMovie_CorrectTiming()
        {
            var start = new DateTime(2026, 1, 1, 18, 0, 0);
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1 },
                StartTime = start,
                AvgRuntimeMinutes = 90,
                BreakMinutes = 15
            };

            var plan = _service.BuildPlan(request);

            Assert.AreEqual(1, plan.MovieCount);
            Assert.AreEqual(start, plan.OverallStart);
            Assert.AreEqual(start.AddMinutes(90), plan.OverallEnd);
            Assert.AreEqual(TimeSpan.FromMinutes(90), plan.TotalWatchTime);
            Assert.AreEqual(TimeSpan.Zero, plan.TotalBreakTime); // no break after last movie
            Assert.IsFalse(plan.Entries[0].HasBreakAfter);
        }

        [TestMethod]
        public void BuildPlan_TwoMovies_BreaksIncluded()
        {
            var start = new DateTime(2026, 1, 1, 18, 0, 0);
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2 },
                StartTime = start,
                AvgRuntimeMinutes = 120,
                BreakMinutes = 15
            };

            var plan = _service.BuildPlan(request);

            Assert.AreEqual(2, plan.MovieCount);
            Assert.AreEqual(TimeSpan.FromMinutes(240), plan.TotalWatchTime);
            Assert.AreEqual(TimeSpan.FromMinutes(15), plan.TotalBreakTime);
            Assert.IsTrue(plan.Entries[0].HasBreakAfter);
            Assert.IsFalse(plan.Entries[1].HasBreakAfter);

            // Second movie starts after first + break
            Assert.AreEqual(start.AddMinutes(135), plan.Entries[1].StartTime);
        }

        [TestMethod]
        public void BuildPlan_ZeroBreak_NoBreaks()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 },
                AvgRuntimeMinutes = 60,
                BreakMinutes = 0
            };

            var plan = _service.BuildPlan(request);

            Assert.AreEqual(TimeSpan.Zero, plan.TotalBreakTime);
            Assert.IsTrue(plan.Entries.All(e => !e.HasBreakAfter));
        }

        [TestMethod]
        public void BuildPlan_ChronologicalOrder_SortsByReleaseDate()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 }, // Shrek 2001, Godfather 1972, Toy Story 1995
                Order = MarathonOrder.Chronological
            };

            var plan = _service.BuildPlan(request);

            Assert.AreEqual("The Godfather", plan.Entries[0].Movie.Name);
            Assert.AreEqual("Toy Story", plan.Entries[1].Movie.Name);
            Assert.AreEqual("Shrek!", plan.Entries[2].Movie.Name);
        }

        [TestMethod]
        public void BuildPlan_ReverseChronological_NewestFirst()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 },
                Order = MarathonOrder.ReverseChronological
            };

            var plan = _service.BuildPlan(request);

            Assert.AreEqual("Shrek!", plan.Entries[0].Movie.Name);
        }

        [TestMethod]
        public void BuildPlan_RatingDescending_HighestFirst()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 },
                Order = MarathonOrder.RatingDescending
            };

            var plan = _service.BuildPlan(request);

            // Godfather=5, Toy Story=5, Shrek=4
            Assert.AreEqual(5, plan.Entries[0].Movie.Rating);
            Assert.AreEqual(4, plan.Entries.Last().Movie.Rating);
        }

        [TestMethod]
        public void BuildPlan_GenreGrouped_GroupsByGenre()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 }, // Animation, Drama, Animation
                Order = MarathonOrder.GenreGrouped
            };

            var plan = _service.BuildPlan(request);

            // Animation movies should be adjacent
            var genres = plan.Entries.Select(e => e.Movie.Genre).ToList();
            Assert.AreEqual(genres[0], genres[1]); // both Animation grouped together
        }

        [TestMethod]
        public void BuildPlan_GenreBreakdown_Correct()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 }
            };

            var plan = _service.BuildPlan(request);

            Assert.AreEqual(2, plan.GenreBreakdown["Animation"]);
            Assert.AreEqual(1, plan.GenreBreakdown["Drama"]);
        }

        [TestMethod]
        public void BuildPlan_AverageRating_Calculated()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 } // 4, 5, 5
            };

            var plan = _service.BuildPlan(request);

            Assert.IsNotNull(plan.AverageRating);
            Assert.AreEqual(4.7, plan.AverageRating.Value, 0.1);
        }

        [TestMethod]
        public void BuildPlan_SpansMidnight_Detected()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 },
                StartTime = new DateTime(2026, 1, 1, 22, 0, 0),
                AvgRuntimeMinutes = 120,
                BreakMinutes = 15
            };

            var plan = _service.BuildPlan(request);
            Assert.IsTrue(plan.SpansMidnight);
        }

        [TestMethod]
        public void BuildPlan_EarlyStart_DoesNotSpanMidnight()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1 },
                StartTime = new DateTime(2026, 1, 1, 14, 0, 0),
                AvgRuntimeMinutes = 90
            };

            var plan = _service.BuildPlan(request);
            Assert.IsFalse(plan.SpansMidnight);
        }

        [TestMethod]
        public void BuildPlan_EstimatedCost_UsesDefaultRate()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2 }
            };

            var plan = _service.BuildPlan(request);
            // Default $3.99 per movie
            Assert.AreEqual(7.98m, plan.EstimatedCost);
        }

        [TestMethod]
        public void BuildPlan_Positions_Sequential()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 }
            };

            var plan = _service.BuildPlan(request);

            for (int i = 0; i < plan.Entries.Count; i++)
            {
                Assert.AreEqual(i + 1, plan.Entries[i].Position);
            }
        }

        [TestMethod]
        public void SuggestMovies_ReturnsTopRated()
        {
            var suggestions = _service.SuggestMovies(2);
            Assert.AreEqual(2, suggestions.Count);
            Assert.IsTrue(suggestions[0].Rating >= suggestions[1].Rating);
        }

        [TestMethod]
        public void SuggestMovies_FilterByGenre()
        {
            var suggestions = _service.SuggestMovies(10, Genre.Animation);
            Assert.IsTrue(suggestions.All(m => m.Genre == Genre.Animation));
        }

        [TestMethod]
        public void SuggestMovies_CountExceedsAvailable_ReturnsAll()
        {
            var suggestions = _service.SuggestMovies(100, Genre.Drama);
            Assert.AreEqual(1, suggestions.Count); // only Godfather
        }

        // --- MarathonRequest model tests ---

        [TestMethod]
        public void MarathonRequest_Defaults_Correct()
        {
            var req = new MarathonRequest();
            Assert.AreEqual(120, req.AvgRuntimeMinutes);
            Assert.AreEqual(15, req.BreakMinutes);
            Assert.AreEqual(MarathonOrder.Chronological, req.Order);
            Assert.IsNotNull(req.MovieIds);
            Assert.AreEqual(0, req.MovieIds.Count);
        }

        // --- MarathonPlan computed property tests ---

        [TestMethod]
        public void MarathonPlan_TotalDuration_IsWatchPlusBreak()
        {
            var plan = new MarathonPlan
            {
                TotalWatchTime = TimeSpan.FromMinutes(240),
                TotalBreakTime = TimeSpan.FromMinutes(30)
            };

            Assert.AreEqual(TimeSpan.FromMinutes(270), plan.TotalDuration);
        }

        [TestMethod]
        public void BuildPlan_RandomOrder_IncludesAllMovies()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 },
                Order = MarathonOrder.Random
            };

            var plan = _service.BuildPlan(request);
            Assert.AreEqual(3, plan.MovieCount);
            var ids = plan.Entries.Select(e => e.Movie.Id).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(new List<int> { 1, 2, 3 }, ids);
        }

        [TestMethod]
        public void BuildPlan_ThreeMovies_TwoBreaks()
        {
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 },
                BreakMinutes = 10,
                AvgRuntimeMinutes = 60
            };

            var plan = _service.BuildPlan(request);
            Assert.AreEqual(TimeSpan.FromMinutes(20), plan.TotalBreakTime);
            Assert.AreEqual(2, plan.Entries.Count(e => e.HasBreakAfter));
        }

        [TestMethod]
        public void BuildPlan_EntryTimesAreContiguous()
        {
            var start = new DateTime(2026, 6, 15, 19, 0, 0);
            var request = new MarathonRequest
            {
                MovieIds = new List<int> { 1, 2, 3 },
                StartTime = start,
                AvgRuntimeMinutes = 100,
                BreakMinutes = 20
            };

            var plan = _service.BuildPlan(request);

            Assert.AreEqual(start, plan.Entries[0].StartTime);
            for (int i = 1; i < plan.Entries.Count; i++)
            {
                var prev = plan.Entries[i - 1];
                var expected = prev.HasBreakAfter ? prev.BreakEndTime.Value : prev.EndTime;
                Assert.AreEqual(expected, plan.Entries[i].StartTime,
                    $"Entry {i} start doesn't match previous end");
            }
        }
    }
}
