using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class RouletteControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
        }

        [TestMethod]
        public void Index_ReturnsViewWithWheelMovies()
        {
            var controller = new RouletteController();

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as RouletteViewModel;
            Assert.IsNotNull(vm);
            Assert.IsFalse(vm.HasSpun);
            Assert.IsTrue(vm.WheelMovies.Count > 0);
        }

        [TestMethod]
        public void Spin_NoFilter_ReturnsPickedMovie()
        {
            var controller = new RouletteController();

            var result = controller.Spin(null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as RouletteViewModel;
            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.HasSpun);
            Assert.IsNotNull(vm.Result);
            Assert.IsNotNull(vm.Result.PickedMovie);
        }

        [TestMethod]
        public void Spin_WithGenreFilter_ReturnsMatchingMovie()
        {
            var controller = new RouletteController();

            var result = controller.Spin(Genre.Animation, null) as ViewResult;

            var vm = result.Model as RouletteViewModel;
            Assert.IsNotNull(vm.Result.PickedMovie);
            Assert.AreEqual(Genre.Animation, vm.Result.PickedMovie.Genre);
            Assert.AreEqual(Genre.Animation, vm.SelectedGenre);
        }

        [TestMethod]
        public void Spin_WithMinRating_ReturnsHighRatedMovie()
        {
            var controller = new RouletteController();

            var result = controller.Spin(null, 5) as ViewResult;

            var vm = result.Model as RouletteViewModel;
            Assert.IsNotNull(vm.Result.PickedMovie);
            Assert.IsTrue(vm.Result.PickedMovie.Rating >= 5);
        }

        [TestMethod]
        public void Spin_NoMatchingMovies_ReturnsNullPick()
        {
            InMemoryMovieRepository.ResetEmpty();
            var controller = new RouletteController();

            var result = controller.Spin(null, null) as ViewResult;

            var vm = result.Model as RouletteViewModel;
            Assert.IsTrue(vm.HasSpun);
            Assert.IsNull(vm.Result.PickedMovie);
            Assert.AreEqual(0, vm.Result.TotalCandidates);
        }
    }
}
