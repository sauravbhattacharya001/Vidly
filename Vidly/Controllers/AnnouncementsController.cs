using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages the store announcements board. Staff can create, publish, pin, and archive
    /// announcements. Customers see active announcements and can acknowledge important ones.
    /// </summary>
    public class AnnouncementsController : Controller
    {
        private readonly IAnnouncementRepository _announcementRepository;

        public AnnouncementsController()
            : this(new InMemoryAnnouncementRepository()) { }

        public AnnouncementsController(IAnnouncementRepository announcementRepository)
        {
            _announcementRepository = announcementRepository
                ?? throw new ArgumentNullException(nameof(announcementRepository));
        }

        /// <summary>
        /// Public announcements board showing active announcements to customers.
        /// </summary>
        public ActionResult Index(AnnouncementCategory? category, string q)
        {
            IReadOnlyList<Announcement> announcements;

            if (!string.IsNullOrWhiteSpace(q))
                announcements = _announcementRepository.Search(q)
                    .Where(a => a.Status == AnnouncementStatus.Active).ToList().AsReadOnly();
            else if (category.HasValue)
                announcements = _announcementRepository.GetByCategory(category.Value)
                    .Where(a => a.Status == AnnouncementStatus.Active).ToList().AsReadOnly();
            else
                announcements = _announcementRepository.GetActive();

            // Increment view counts
            foreach (var a in announcements)
                a.ViewCount++;

            var vm = new AnnouncementBoardViewModel
            {
                Announcements = announcements,
                PinnedAnnouncements = _announcementRepository.GetPinned(),
                Analytics = _announcementRepository.GetAnalytics(),
                CategoryFilter = category,
                SearchQuery = q,
                View = "board"
            };

            return View(vm);
        }

        /// <summary>
        /// Staff management view showing all announcements with status controls.
        /// </summary>
        public ActionResult Manage(AnnouncementStatus? status)
        {
            var all = _announcementRepository.GetAll();
            if (status.HasValue)
                all = all.Where(a => a.Status == status.Value).ToList().AsReadOnly();

            var vm = new AnnouncementBoardViewModel
            {
                Announcements = all,
                PinnedAnnouncements = _announcementRepository.GetPinned(),
                Analytics = _announcementRepository.GetAnalytics(),
                StatusFilter = status,
                View = "manage"
            };

            return View(vm);
        }

        /// <summary>
        /// Shows the create announcement form.
        /// </summary>
        public ActionResult Create()
        {
            return View(new AnnouncementCreateViewModel());
        }

        /// <summary>
        /// Creates a new announcement (draft by default).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(AnnouncementCreateViewModel model)
        {
            if (model == null) return new HttpStatusCodeResult(400);

            if (string.IsNullOrWhiteSpace(model.Title))
                ModelState.AddModelError("Title", "Title is required.");

            if (string.IsNullOrWhiteSpace(model.Body))
                ModelState.AddModelError("Body", "Body is required.");

            if (model.Title != null && model.Title.Length > 200)
                ModelState.AddModelError("Title", "Title must be 200 characters or less.");

            if (!ModelState.IsValid)
                return View(model);

            var announcement = new Announcement
            {
                Title = model.Title.Trim(),
                Body = model.Body.Trim(),
                Category = model.Category,
                Priority = model.Priority,
                Status = AnnouncementStatus.Draft,
                AuthorStaffId = "staff-current",
                RequiresAcknowledgment = model.RequiresAcknowledgment,
                RelatedMovieId = model.RelatedMovieId,
                Tags = string.IsNullOrWhiteSpace(model.Tags)
                    ? new List<string>()
                    : model.Tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList()
            };

            if (model.HasExpiration && model.ExpirationDays > 0)
                announcement.ExpiresAt = DateTime.Now.AddDays(model.ExpirationDays);

            _announcementRepository.Add(announcement);
            TempData["Message"] = $"Announcement '{announcement.Title}' created as draft.";
            return RedirectToAction("Manage");
        }

        /// <summary>
        /// Shows announcement details.
        /// </summary>
        public ActionResult Details(int id)
        {
            var announcement = _announcementRepository.GetById(id);
            if (announcement == null) return HttpNotFound();

            announcement.ViewCount++;
            var pinned = _announcementRepository.GetPinned();

            var vm = new AnnouncementDetailViewModel
            {
                Announcement = announcement,
                IsPinned = pinned.Any(p => p.Id == id)
            };

            return View(vm);
        }

        /// <summary>
        /// Publishes a draft announcement.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Publish(int id)
        {
            var result = _announcementRepository.Publish(id);
            if (result == null) return HttpNotFound();

            TempData["Message"] = $"'{result.Title}' has been published!";
            return RedirectToAction("Manage");
        }

        /// <summary>
        /// Archives an announcement.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Archive(int id)
        {
            var result = _announcementRepository.Archive(id);
            if (result == null) return HttpNotFound();

            TempData["Message"] = $"'{result.Title}' has been archived.";
            return RedirectToAction("Manage");
        }

        /// <summary>
        /// Pins or unpins an announcement.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult TogglePin(int id)
        {
            var announcement = _announcementRepository.GetById(id);
            if (announcement == null) return HttpNotFound();

            _announcementRepository.TogglePin(id, "staff-current");
            TempData["Message"] = $"Pin toggled for '{announcement.Title}'.";
            return RedirectToAction("Manage");
        }

        /// <summary>
        /// Customer acknowledges an announcement.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Acknowledge(int id, int customerId)
        {
            var announcement = _announcementRepository.GetById(id);
            if (announcement == null) return HttpNotFound();

            if (!announcement.RequiresAcknowledgment)
            {
                TempData["Error"] = "This announcement does not require acknowledgment.";
                return RedirectToAction("Details", new { id });
            }

            _announcementRepository.Acknowledge(id, customerId);
            TempData["Message"] = "Announcement acknowledged. Thank you!";
            return RedirectToAction("Details", new { id });
        }

        /// <summary>
        /// Deletes an announcement (staff only).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var announcement = _announcementRepository.GetById(id);
            if (announcement == null) return HttpNotFound();

            _announcementRepository.Remove(id);
            TempData["Message"] = $"Announcement '{announcement.Title}' has been deleted.";
            return RedirectToAction("Manage");
        }

        /// <summary>
        /// Returns analytics dashboard for announcements.
        /// </summary>
        public ActionResult Analytics()
        {
            var analytics = _announcementRepository.GetAnalytics();
            return View(analytics);
        }
    }
}
