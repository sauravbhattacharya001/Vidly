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
    public class StaffPicksTests
    {
        [TestInitialize]
        public void Setup()
        {
            StaffPicksService.Reset();
            InMemoryMovieRepository.Reset();
        }

        private StaffPicksService CreateService() =>
            new StaffPicksService(new InMemoryMovieRepository());

        private StaffPicksController CreateController() =>
            new StaffPicksController(new InMemoryMovieRepository());

        // --- Service Tests ---

        [TestMethod]
        public void GetAllPicks_ReturnsSeedData()
        {
            var picks = CreateService().GetAllPicks();
            Assert.IsTrue(picks.Count >= 3, "Should have seed picks");
        }

        [TestMethod]
        public void GetAllPicks_EachHasMovieAndPick()
        {
            var picks = CreateService().GetAllPicks();
            foreach (var p in picks)
            {
                Assert.IsNotNull(p.Movie, "Movie should not be null");
                Assert.IsNotNull(p.Pick, "Pick should not be null");
                Assert.IsFalse(string.IsNullOrEmpty(p.Pick.StaffName));
                Assert.IsFalse(string.IsNullOrEmpty(p.Pick.Theme));
            }
        }

        [TestMethod]
        public void GetPageViewModel_HasThemedLists()
        {
            var vm = CreateService().GetPageViewModel();
            Assert.IsTrue(vm.ThemedLists.Count >= 2, "Should have multiple themes");
            Assert.IsTrue(vm.TotalPicks > 0);
        }

        [TestMethod]
        public void GetPageViewModel_HasFeaturedPick()
        {
            var vm = CreateService().GetPageViewModel();
            Assert.IsNotNull(vm.FeaturedPick, "Should have a featured pick");
        }

        [TestMethod]
        public void GetPageViewModel_HasAllStaffAndThemes()
        {
            var vm = CreateService().GetPageViewModel();
            Assert.IsTrue(vm.AllStaff.Count >= 2);
            Assert.IsTrue(vm.AllThemes.Count >= 2);
        }

        [TestMethod]
        public void GetPageViewModel_FilterByStaff()
        {
            var vm = CreateService().GetPageViewModel(filterStaff: "Maria");
            foreach (var list in vm.ThemedLists)
                foreach (var pick in list.Picks)
                    Assert.AreEqual("Maria", pick.Pick.StaffName);
        }

        [TestMethod]
        public void GetPageViewModel_FilterByTheme()
        {
            var service = CreateService();
            var themes = service.GetThemes();
            var firstTheme = themes.First();
            var vm = service.GetPageViewModel(filterTheme: firstTheme);
            Assert.AreEqual(1, vm.ThemedLists.Count);
            Assert.AreEqual(firstTheme, vm.ThemedLists[0].Theme);
        }

        [TestMethod]
        public void GetPicksByStaff_ReturnsCorrectPicks()
        {
            var picks = CreateService().GetPicksByStaff("James");
            Assert.IsTrue(picks.Count > 0);
            foreach (var p in picks)
                Assert.AreEqual("James", p.Pick.StaffName);
        }

        [TestMethod]
        public void GetPicksByStaff_CaseInsensitive()
        {
            var picks = CreateService().GetPicksByStaff("james");
            Assert.IsTrue(picks.Count > 0);
        }

        [TestMethod]
        public void GetPicksByTheme_ReturnsCorrectPicks()
        {
            var picks = CreateService().GetPicksByTheme("Must-Watch Masterpieces");
            Assert.IsTrue(picks.Count > 0);
            foreach (var p in picks)
                Assert.AreEqual("Must-Watch Masterpieces", p.Pick.Theme);
        }

        [TestMethod]
        public void AddPick_IncreasesCount()
        {
            var service = CreateService();
            var before = service.GetAllPicks().Count;
            service.AddPick(1, "TestStaff", "Test Theme", "Great movie!");
            Assert.AreEqual(before + 1, service.GetAllPicks().Count);
        }

        [TestMethod]
        public void AddPick_SetsProperties()
        {
            var service = CreateService();
            var pick = service.AddPick(1, "TestStaff", "Test Theme", "A note");
            Assert.AreEqual(1, pick.MovieId);
            Assert.AreEqual("TestStaff", pick.StaffName);
            Assert.AreEqual("Test Theme", pick.Theme);
            Assert.AreEqual("A note", pick.Note);
        }

        [TestMethod]
        public void AddPick_Featured_UnfeaturesOthers()
        {
            var service = CreateService();
            service.AddPick(1, "Staff1", "Theme1", "Note1", isFeatured: true);
            var vm = service.GetPageViewModel();
            var featured = service.GetAllPicks().Where(p => p.Pick.IsFeatured).ToList();
            Assert.AreEqual(1, featured.Count, "Only one pick should be featured");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddPick_EmptyStaff_Throws()
        {
            CreateService().AddPick(1, "", "Theme", "Note");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddPick_EmptyTheme_Throws()
        {
            CreateService().AddPick(1, "Staff", "", "Note");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddPick_InvalidMovie_Throws()
        {
            CreateService().AddPick(9999, "Staff", "Theme", "Note");
        }

        [TestMethod]
        public void RemovePick_ExistingPick_ReturnsTrue()
        {
            var service = CreateService();
            var pick = service.AddPick(1, "Staff", "Theme", "Note");
            Assert.IsTrue(service.RemovePick(pick.Id));
        }

        [TestMethod]
        public void RemovePick_NonExistent_ReturnsFalse()
        {
            Assert.IsFalse(CreateService().RemovePick(9999));
        }

        [TestMethod]
        public void RemovePick_DecreasesCount()
        {
            var service = CreateService();
            var pick = service.AddPick(1, "Staff", "Theme", "Note");
            var before = service.GetAllPicks().Count;
            service.RemovePick(pick.Id);
            Assert.AreEqual(before - 1, service.GetAllPicks().Count);
        }

        [TestMethod]
        public void GetStaffNames_ReturnsDistinctOrdered()
        {
            var names = CreateService().GetStaffNames();
            Assert.IsTrue(names.Count >= 2);
            for (int i = 1; i < names.Count; i++)
                Assert.IsTrue(string.Compare(names[i - 1], names[i], StringComparison.Ordinal) <= 0);
        }

        [TestMethod]
        public void GetThemes_ReturnsDistinctOrdered()
        {
            var themes = CreateService().GetThemes();
            Assert.IsTrue(themes.Count >= 2);
            for (int i = 1; i < themes.Count; i++)
                Assert.IsTrue(string.Compare(themes[i - 1], themes[i], StringComparison.Ordinal) <= 0);
        }

        [TestMethod]
        public void ThemedLists_HaveDescriptions()
        {
            var vm = CreateService().GetPageViewModel();
            foreach (var list in vm.ThemedLists)
                Assert.IsFalse(string.IsNullOrEmpty(list.Description), $"Theme '{list.Theme}' should have description");
        }

        // --- Controller Tests ---

        [TestMethod]
        public void Index_ReturnsViewWithModel()
        {
            var result = CreateController().Index() as ViewResult;
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(StaffPicksPageViewModel));
        }

        [TestMethod]
        public void Index_WithStaffFilter_SetsViewBag()
        {
            var result = CreateController().Index(staff: "Maria") as ViewResult;
            Assert.AreEqual("Maria", result.ViewBag.FilterStaff);
        }

        [TestMethod]
        public void Staff_WithValidName_ReturnsView()
        {
            var result = CreateController().Staff("Maria") as ViewResult;
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(List<StaffPickViewModel>));
            Assert.AreEqual("Maria", result.ViewBag.StaffName);
        }

        [TestMethod]
        public void Staff_WithNull_Redirects()
        {
            var result = CreateController().Staff(null) as RedirectToRouteResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Theme_WithValidName_ReturnsView()
        {
            var result = CreateController().Theme("Hidden Gems") as ViewResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("Hidden Gems", result.ViewBag.ThemeName);
        }

        [TestMethod]
        public void Theme_WithNull_Redirects()
        {
            var result = CreateController().Theme(null) as RedirectToRouteResult;
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void Reset_ClearsAllData()
        {
            var service = CreateService();
            Assert.IsTrue(service.GetAllPicks().Count > 0);
            StaffPicksService.Reset();
            // After reset + re-seed, should have seed data again
            var service2 = CreateService();
            Assert.IsTrue(service2.GetAllPicks().Count > 0);
        }
    }
}
