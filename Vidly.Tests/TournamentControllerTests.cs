using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class TournamentControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
        }

        private TournamentController CreateController()
        {
            return new TournamentController(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository());
        }

        // ── Index ──────────────────────────────────────────

        [TestMethod]
        public void Index_ReturnsViewWithTournamentViewModel()
        {
            var controller = CreateController();

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result, "Index should return a ViewResult.");
            var vm = result.Model as TournamentViewModel;
            Assert.IsNotNull(vm, "Model should be a TournamentViewModel.");
            Assert.IsNotNull(vm.ActiveTournaments);
            Assert.IsNotNull(vm.HallOfFame);
            Assert.IsNotNull(vm.MovieRecords);
            Assert.IsTrue(vm.Movies.Count > 0, "Should have pre-seeded movies.");
            Assert.IsTrue(vm.Customers.Count > 0, "Should have pre-seeded customers.");
        }

        // ── Create ─────────────────────────────────────────

        [TestMethod]
        public void Create_ValidInput_RedirectsToBracket()
        {
            var controller = CreateController();
            var customers = new InMemoryCustomerRepository().GetCustomers();
            var firstCustomer = new System.Collections.Generic.List<Customer>(customers)[0];

            var result = controller.Create(
                "Test Tournament", firstCustomer.Id, size: 4) as RedirectToRouteResult;

            Assert.IsNotNull(result, "Create should redirect.");
            Assert.AreEqual("Bracket", result.RouteValues["action"]);
            Assert.IsNotNull(result.RouteValues["id"], "Should redirect with tournament id.");
        }

        [TestMethod]
        public void Create_WithGenreFilter_RedirectsToBracket()
        {
            var controller = CreateController();
            var customers = new System.Collections.Generic.List<Customer>(
                new InMemoryCustomerRepository().GetCustomers());

            var result = controller.Create(
                "Action Tournament", customers[0].Id, size: 4,
                genreFilter: "Action") as RedirectToRouteResult;

            Assert.IsNotNull(result, "Create with genre filter should redirect.");
            Assert.AreEqual("Bracket", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Create_EmptyName_RedirectsToIndexWithError()
        {
            var controller = CreateController();

            var result = controller.Create("", 1) as RedirectToRouteResult;

            Assert.IsNotNull(result, "Should redirect on error.");
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.IsNotNull(controller.TempData["Error"],
                "Should set error message for empty name.");
        }

        [TestMethod]
        public void Create_InvalidBracketSize_RedirectsToIndexWithError()
        {
            var controller = CreateController();

            var result = controller.Create("Bad Size", 1, size: 5) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.IsNotNull(controller.TempData["Error"],
                "Should set error for invalid bracket size.");
        }

        // ── Bracket ────────────────────────────────────────

        [TestMethod]
        public void Bracket_NonExistentTournament_RedirectsToIndex()
        {
            var controller = CreateController();

            var result = controller.Bracket(9999) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual("Tournament not found.", controller.TempData["Error"]);
        }

        [TestMethod]
        public void Bracket_ExistingTournament_ReturnsViewWithPendingMatches()
        {
            var controller = CreateController();
            var customers = new System.Collections.Generic.List<Customer>(
                new InMemoryCustomerRepository().GetCustomers());

            // Create a tournament first
            var createResult = controller.Create(
                "Bracket Test", customers[0].Id, size: 4) as RedirectToRouteResult;
            var tournamentId = (int)createResult.RouteValues["id"];

            var result = controller.Bracket(tournamentId) as ViewResult;

            Assert.IsNotNull(result, "Bracket should return a ViewResult.");
            var vm = result.Model as TournamentViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Tournament, "Should include the tournament.");
            Assert.IsNotNull(vm.PendingMatches, "Should include pending matches.");
            Assert.IsTrue(vm.PendingMatches.Count > 0,
                "A fresh 4-movie tournament should have pending matches.");
            Assert.IsNotNull(vm.CurrentMatch,
                "Should set CurrentMatch when there are pending matches.");
        }

        // ── Vote ───────────────────────────────────────────

        [TestMethod]
        public void Vote_ValidVote_RedirectsToBracketOrChampion()
        {
            var controller = CreateController();
            var customers = new System.Collections.Generic.List<Customer>(
                new InMemoryCustomerRepository().GetCustomers());

            var createResult = controller.Create(
                "Vote Test", customers[0].Id, size: 4) as RedirectToRouteResult;
            var tournamentId = (int)createResult.RouteValues["id"];

            // Get pending matches
            var bracketResult = controller.Bracket(tournamentId) as ViewResult;
            var vm = bracketResult.Model as TournamentViewModel;
            var match = vm.CurrentMatch;

            // Vote for movie1
            var voteResult = controller.Vote(
                tournamentId, match.Id, match.Movie1Id, "Better film") as RedirectToRouteResult;

            Assert.IsNotNull(voteResult, "Vote should redirect.");
            // Should go to Bracket (more matches) or Champion (if done)
            Assert.IsTrue(
                (string)voteResult.RouteValues["action"] == "Bracket" ||
                (string)voteResult.RouteValues["action"] == "Champion",
                "Should redirect to Bracket or Champion.");
        }

        [TestMethod]
        public void Vote_InvalidTournament_RedirectsToBracketWithError()
        {
            var controller = CreateController();

            var result = controller.Vote(9999, 1, 1, "reason") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Bracket", result.RouteValues["action"]);
            Assert.IsNotNull(controller.TempData["Error"]);
        }

        // ── Champion ───────────────────────────────────────

        [TestMethod]
        public void Champion_NonExistentTournament_RedirectsToIndex()
        {
            var controller = CreateController();

            var result = controller.Champion(9999) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual("Tournament not found or not completed.",
                controller.TempData["Error"]);
        }

        [TestMethod]
        public void Champion_InProgressTournament_RedirectsToIndex()
        {
            var controller = CreateController();
            var customers = new System.Collections.Generic.List<Customer>(
                new InMemoryCustomerRepository().GetCustomers());

            var createResult = controller.Create(
                "Not Done Yet", customers[0].Id, size: 4) as RedirectToRouteResult;
            var tournamentId = (int)createResult.RouteValues["id"];

            var result = controller.Champion(tournamentId) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual("Tournament not found or not completed.",
                controller.TempData["Error"]);
        }

        [TestMethod]
        public void Champion_CompletedTournament_ReturnsView()
        {
            var controller = CreateController();
            var customers = new System.Collections.Generic.List<Customer>(
                new InMemoryCustomerRepository().GetCustomers());

            // Create and play through a size-4 tournament (2 rounds, 3 matches)
            var createResult = controller.Create(
                "Full Tournament", customers[0].Id, size: 4) as RedirectToRouteResult;
            var tournamentId = (int)createResult.RouteValues["id"];

            // Vote through all matches until champion
            for (int i = 0; i < 10; i++) // safeguard: max 10 iterations
            {
                var bracketView = controller.Bracket(tournamentId) as ViewResult;
                if (bracketView == null) break; // redirected = done

                var vm = bracketView.Model as TournamentViewModel;
                if (vm.CurrentMatch == null) break;

                controller.Vote(
                    tournamentId, vm.CurrentMatch.Id,
                    vm.CurrentMatch.Movie1Id, "test vote");
            }

            var result = controller.Champion(tournamentId) as ViewResult;

            Assert.IsNotNull(result, "Champion should return ViewResult for completed tournament.");
            var championVm = result.Model as TournamentViewModel;
            Assert.IsNotNull(championVm);
            Assert.IsNotNull(championVm.Tournament);
            Assert.AreEqual(TournamentStatus.Completed, championVm.Tournament.Status);
        }

        // ── Cancel ─────────────────────────────────────────

        [TestMethod]
        public void Cancel_ExistingTournament_RedirectsToIndex()
        {
            var controller = CreateController();
            var customers = new System.Collections.Generic.List<Customer>(
                new InMemoryCustomerRepository().GetCustomers());

            var createResult = controller.Create(
                "To Cancel", customers[0].Id, size: 4) as RedirectToRouteResult;
            var tournamentId = (int)createResult.RouteValues["id"];

            var result = controller.Cancel(tournamentId) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Cancel_ThenBracket_ShowsCancelledState()
        {
            var controller = CreateController();
            var customers = new System.Collections.Generic.List<Customer>(
                new InMemoryCustomerRepository().GetCustomers());

            var createResult = controller.Create(
                "Cancel Check", customers[0].Id, size: 4) as RedirectToRouteResult;
            var tournamentId = (int)createResult.RouteValues["id"];

            controller.Cancel(tournamentId);

            // After cancellation, the tournament should not appear as active
            var indexResult = controller.Index() as ViewResult;
            var vm = indexResult.Model as TournamentViewModel;
            foreach (var t in vm.ActiveTournaments)
            {
                Assert.AreNotEqual(tournamentId, t.Id,
                    "Cancelled tournament should not appear in active list.");
            }
        }

        // ── Records ────────────────────────────────────────

        [TestMethod]
        public void Records_ReturnsViewWithRecordsAndHallOfFame()
        {
            var controller = CreateController();

            var result = controller.Records() as ViewResult;

            Assert.IsNotNull(result, "Records should return a ViewResult.");
            var vm = result.Model as TournamentViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.MovieRecords);
            Assert.IsNotNull(vm.HallOfFame);
        }

        // ── Constructor null checks ────────────────────────

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentNullException))]
        public void Constructor_NullMovieRepository_Throws()
        {
            new TournamentController(null, new InMemoryCustomerRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentNullException))]
        public void Constructor_NullCustomerRepository_Throws()
        {
            new TournamentController(new InMemoryMovieRepository(), null);
        }
    }
}
