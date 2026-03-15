using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class QuizControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
        }

        private QuizController CreateController() =>
            new QuizController(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository());

        [TestMethod]
        public void Index_ReturnsViewWithViewModel()
        {
            var result = CreateController().Index() as ViewResult;
            Assert.IsNotNull(result);
            var model = result.Model as QuizViewModel;
            Assert.IsNotNull(model);
            Assert.IsNotNull(model.Leaderboard);
            Assert.IsNotNull(model.DailyChallenge);
            Assert.IsNotNull(model.Customers);
            Assert.IsTrue(model.Customers.Count > 0);
        }

        [TestMethod]
        public void Index_LeaderboardIsEmpty_Initially()
        {
            var result = CreateController().Index() as ViewResult;
            var model = result.Model as QuizViewModel;
            Assert.AreEqual(0, model.Leaderboard.Count);
        }

        [TestMethod]
        public void Start_ValidInput_RedirectsToPlay()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers().ToList();
            var result = controller.Start(
                customers[0].Id, "Easy", "MixedBag", 5, 0) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Play", result.RouteValues["action"]);
            Assert.IsNotNull(result.RouteValues["sessionId"]);
        }

        [TestMethod]
        public void Start_InvalidDifficulty_RedirectsToIndexWithError()
        {
            var controller = CreateController();
            controller.TempData = new System.Web.Mvc.TempDataDictionary();
            var result = controller.Start(1, "Impossible", "MixedBag") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Play_InvalidSession_RedirectsToIndex()
        {
            var controller = CreateController();
            controller.TempData = new System.Web.Mvc.TempDataDictionary();
            var result = controller.Play(999, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Play_ValidSession_ReturnsViewWithQuestion()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers().ToList();

            // Start a quiz first
            var startResult = controller.Start(
                customers[0].Id, "Easy", "Genre", 5, 0) as RedirectToRouteResult;
            var sessionId = (int)startResult.RouteValues["sessionId"];

            var result = controller.Play(sessionId, customers[0].Id) as ViewResult;
            Assert.IsNotNull(result);
            var model = result.Model as QuizViewModel;
            Assert.IsNotNull(model);
            Assert.IsNotNull(model.CurrentQuestion);
            Assert.AreEqual(1, model.CurrentQuestionIndex);
            Assert.AreEqual(5, model.TotalQuestions);
        }

        [TestMethod]
        public void Answer_ValidAnswer_AdvancesToNextQuestion()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers().ToList();
            var customerId = customers[0].Id;

            var startResult = controller.Start(
                customerId, "Easy", "Genre", 5, 0) as RedirectToRouteResult;
            var sessionId = (int)startResult.RouteValues["sessionId"];

            // Get current question
            var playResult = controller.Play(sessionId, customerId) as ViewResult;
            var model = playResult.Model as QuizViewModel;
            var questionId = model.CurrentQuestion.Id;

            // Submit answer
            var ansResult = controller.Answer(
                sessionId, questionId, 0, customerId) as RedirectToRouteResult;
            Assert.IsNotNull(ansResult);
            // Should redirect to Play (next question) or Results (if last)
            Assert.IsTrue(
                ansResult.RouteValues["action"].ToString() == "Play" ||
                ansResult.RouteValues["action"].ToString() == "Results");
        }

        [TestMethod]
        public void Results_InvalidSession_RedirectsToIndex()
        {
            var controller = CreateController();
            controller.TempData = new System.Web.Mvc.TempDataDictionary();
            var result = controller.Results(999, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Leaderboard_ReturnsViewWithLeaderboard()
        {
            var result = CreateController().Leaderboard() as ViewResult;
            Assert.IsNotNull(result);
            var model = result.Model as QuizViewModel;
            Assert.IsNotNull(model);
            Assert.IsNotNull(model.Leaderboard);
        }

        [TestMethod]
        public void FullQuizFlow_CompleteAllQuestions_ShowsResults()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers().ToList();
            var customerId = customers[0].Id;

            // Start 5-question quiz
            var startResult = controller.Start(
                customerId, "Easy", "Genre", 5, 0) as RedirectToRouteResult;
            var sessionId = (int)startResult.RouteValues["sessionId"];

            // Answer all 5 questions
            for (int i = 0; i < 5; i++)
            {
                var playResult = controller.Play(sessionId, customerId);
                if (playResult is RedirectToRouteResult redirect &&
                    redirect.RouteValues["action"].ToString() == "Results")
                    break;

                var viewResult = playResult as ViewResult;
                if (viewResult == null) break;
                var model = viewResult.Model as QuizViewModel;
                var questionId = model.CurrentQuestion.Id;

                controller.Answer(sessionId, questionId, 0, customerId);
            }

            // Should be able to see results now
            var results = controller.Results(sessionId, customerId) as ViewResult;
            Assert.IsNotNull(results);
            var resultModel = results.Model as QuizViewModel;
            Assert.IsNotNull(resultModel);
            Assert.IsNotNull(resultModel.CompletedSession);
            Assert.IsNotNull(resultModel.CustomerStats);
        }

        [TestMethod]
        public void Start_DifferentDifficulties_AllWork()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers().ToList();
            var customerId = customers[0].Id;

            foreach (var diff in new[] { "Easy", "Medium", "Hard" })
            {
                var result = controller.Start(
                    customerId, diff, "MixedBag", 5, 0) as RedirectToRouteResult;
                Assert.IsNotNull(result, $"Failed for difficulty: {diff}");
                Assert.AreEqual("Play", result.RouteValues["action"]);
            }
        }

        [TestMethod]
        public void Start_DifferentCategories_AllWork()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers().ToList();
            var customerId = customers[0].Id;

            foreach (var cat in new[] { "Genre", "ReleaseYear", "Availability", "MixedBag" })
            {
                var result = controller.Start(
                    customerId, "Easy", cat, 5, 0) as RedirectToRouteResult;
                Assert.IsNotNull(result, $"Failed for category: {cat}");
                Assert.AreEqual("Play", result.RouteValues["action"]);
            }
        }

        [TestMethod]
        public void Play_CompletedSession_RedirectsToResults()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers().ToList();
            var customerId = customers[0].Id;

            // Start 1-question quiz
            var startResult = controller.Start(
                customerId, "Easy", "Genre", 1, 0) as RedirectToRouteResult;
            var sessionId = (int)startResult.RouteValues["sessionId"];

            // Get question and answer it
            var playResult = controller.Play(sessionId, customerId) as ViewResult;
            var model = playResult.Model as QuizViewModel;
            controller.Answer(sessionId, model.CurrentQuestion.Id, 0, customerId);

            // Trying to play again should redirect to results
            var result = controller.Play(sessionId, customerId) as RedirectToRouteResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("Results", result.RouteValues["action"]);
        }
    }
}
