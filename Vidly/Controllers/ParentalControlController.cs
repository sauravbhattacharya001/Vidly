using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Parental Controls — family profiles with age-rating restrictions,
    /// PIN protection, time windows, genre blocking, and activity logging.
    /// </summary>
    public class ParentalControlController : Controller
    {
        private readonly ParentalControlService _service;

        public ParentalControlController()
        {
            _service = new ParentalControlService();
        }

        // GET: ParentalControl
        public ActionResult Index()
        {
            var vm = new ParentalControlViewModel
            {
                Profiles = _service.GetAllProfiles(),
                ActiveProfile = _service.GetActiveProfile(),
                RecentLogs = _service.GetRecentLogs(15),
                BlockedAttemptsByRating = _service.GetBlockedAttemptsByRating(),
                TotalBlockedThisWeek = _service.GetBlockedCountThisWeek()
            };
            return View(vm);
        }

        // GET: ParentalControl/Create
        public ActionResult Create()
        {
            var vm = new ProfileFormViewModel
            {
                Profile = new FamilyProfile { MaxRating = ContentRating.PG },
                IsEdit = false,
                AvailableGenres = Enum.GetNames(typeof(Genre)).ToList()
            };
            return View("ProfileForm", vm);
        }

        // POST: ParentalControl/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(FamilyProfile profile)
        {
            if (!ModelState.IsValid)
            {
                var vm = new ProfileFormViewModel
                {
                    Profile = profile,
                    IsEdit = false,
                    AvailableGenres = Enum.GetNames(typeof(Genre)).ToList()
                };
                return View("ProfileForm", vm);
            }

            _service.CreateProfile(profile);
            TempData["Success"] = $"Profile '{profile.Name}' created successfully.";
            return RedirectToAction("Index");
        }

        // GET: ParentalControl/Edit/5
        public ActionResult Edit(int id)
        {
            var profile = _service.GetProfile(id);
            if (profile == null) return HttpNotFound();

            var vm = new ProfileFormViewModel
            {
                Profile = profile,
                IsEdit = true,
                AvailableGenres = Enum.GetNames(typeof(Genre)).ToList()
            };
            return View("ProfileForm", vm);
        }

        // POST: ParentalControl/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(FamilyProfile profile)
        {
            if (!ModelState.IsValid)
            {
                var vm = new ProfileFormViewModel
                {
                    Profile = profile,
                    IsEdit = true,
                    AvailableGenres = Enum.GetNames(typeof(Genre)).ToList()
                };
                return View("ProfileForm", vm);
            }

            if (!_service.UpdateProfile(profile))
            {
                TempData["Error"] = "Profile not found.";
                return RedirectToAction("Index");
            }

            TempData["Success"] = $"Profile '{profile.Name}' updated.";
            return RedirectToAction("Index");
        }

        // POST: ParentalControl/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (!_service.DeleteProfile(id))
            {
                TempData["Error"] = "Cannot delete this profile (parent profiles cannot be removed).";
                return RedirectToAction("Index");
            }

            TempData["Success"] = "Profile deleted.";
            return RedirectToAction("Index");
        }

        // POST: ParentalControl/Switch
        [HttpPost]
        public ActionResult Switch(int profileId, string pin)
        {
            if (_service.SwitchProfile(profileId, pin))
            {
                var profile = _service.GetProfile(profileId);
                TempData["Success"] = $"Switched to {profile?.Name ?? "profile"}.";
            }
            else
            {
                TempData["Error"] = "Incorrect PIN or profile not found.";
            }
            return RedirectToAction("Index");
        }

        // GET: ParentalControl/Logs/5
        public ActionResult Logs(int id)
        {
            var profile = _service.GetProfile(id);
            if (profile == null) return HttpNotFound();

            ViewBag.Profile = profile;
            var logs = _service.GetLogsByProfile(id);
            return View(logs);
        }

        // GET: ParentalControl/Check?rating=4&genre=Horror
        [HttpGet]
        public JsonResult Check(int rating, string genre)
        {
            var contentRating = (ContentRating)rating;
            var reason = _service.CheckRentalPermission(contentRating, genre);
            return Json(new
            {
                allowed = reason == null,
                reason = reason,
                activeProfile = _service.GetActiveProfile()?.Name
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
