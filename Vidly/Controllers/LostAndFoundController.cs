using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages the store's lost-and-found system — staff can log found items,
    /// customers can submit claims, and staff can verify/approve claims or
    /// dispose of unclaimed items past their retention period.
    /// </summary>
    public class LostAndFoundController : Controller
    {
        // ── Input length limits (CWE-770) ───────────────────────────
        // Without caps, an attacker can submit multi-megabyte strings in
        // any text field, exhausting server memory and bloating storage.
        // These limits match what a human would reasonably type.

        /// <summary>Max length for item description.</summary>
        public const int MaxDescriptionLength = 500;

        /// <summary>Max length for location, color, brand, storage bin fields.</summary>
        public const int MaxShortFieldLength = 100;

        /// <summary>Max length for notes / customer claim description.</summary>
        public const int MaxNotesLength = 2000;

        /// <summary>Max length for staff ID.</summary>
        public const int MaxStaffIdLength = 50;

        /// <summary>Max length for claim rejection reason.</summary>
        public const int MaxReasonLength = 1000;

        private readonly ILostAndFoundRepository _repository;
        private readonly ICustomerRepository _customerRepository;

        public LostAndFoundController()
            : this(new InMemoryLostAndFoundRepository(), new InMemoryCustomerRepository())
        {
        }

        public LostAndFoundController(
            ILostAndFoundRepository repository,
            ICustomerRepository customerRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // GET: LostAndFound
        public ActionResult Index(LostItemStatus? status, LostItemCategory? category, string q, string message, bool? error)
        {
            var items = _repository.GetAll();

            if (!string.IsNullOrWhiteSpace(q))
                items = _repository.Search(q);
            else if (status.HasValue)
                items = _repository.GetByStatus(status.Value);
            else if (category.HasValue)
                items = _repository.GetByCategory(category.Value);

            var viewModel = new LostAndFoundViewModel
            {
                Items = items.OrderByDescending(i => i.FoundAt),
                Customers = _customerRepository.GetAll(),
                Report = _repository.GetReport(),
                FilterStatus = status,
                FilterCategory = category,
                SearchQuery = q,
                StatusMessage = message,
                IsError = error ?? false,
            };

            return View(viewModel);
        }

        // POST: LostAndFound/LogItem
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogItem(string description, LostItemCategory category, string locationFound,
            string color, string brand, string notes, string storageBin, string staffId)
        {
            if (string.IsNullOrWhiteSpace(description))
                return RedirectToAction("Index", new { message = "Description is required.", error = true });

            // Enforce input length limits to prevent memory exhaustion (CWE-770).
            // A malicious or misbehaving client could POST megabytes of text in
            // any of these fields; the in-memory repository stores them forever.
            var lengthError = ValidateFieldLengths(
                (description, MaxDescriptionLength, "Description"),
                (locationFound, MaxShortFieldLength, "Location"),
                (color, MaxShortFieldLength, "Color"),
                (brand, MaxShortFieldLength, "Brand"),
                (notes, MaxNotesLength, "Notes"),
                (storageBin, MaxShortFieldLength, "Storage bin"),
                (staffId, MaxStaffIdLength, "Staff ID"));
            if (lengthError != null)
                return RedirectToAction("Index", new { message = lengthError, error = true });

            var item = new LostItem
            {
                Description = description.Trim(),
                Category = category,
                LocationFound = locationFound?.Trim(),
                Color = color?.Trim(),
                Brand = brand?.Trim(),
                Notes = notes?.Trim(),
                StorageBin = storageBin?.Trim(),
                FoundByStaffId = string.IsNullOrWhiteSpace(staffId) ? "STAFF" : staffId.Trim(),
                FoundAt = DateTime.Now,
                Status = LostItemStatus.Found,
            };

            _repository.Add(item);

            return RedirectToAction("Index", new
            {
                message = $"Item logged: \"{item.Description}\" stored in {item.StorageBin ?? "unassigned bin"}.",
            });
        }

        // POST: LostAndFound/SubmitClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SubmitClaim(int itemId, int customerId, string customerDescription)
        {
            var item = _repository.GetById(itemId);
            if (item == null)
                return RedirectToAction("Index", new { message = "Item not found.", error = true });

            if (item.Status != LostItemStatus.Found && item.Status != LostItemStatus.ClaimPending)
                return RedirectToAction("Index", new { message = "This item is no longer available for claims.", error = true });

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                return RedirectToAction("Index", new { message = "Customer not found.", error = true });

            if (string.IsNullOrWhiteSpace(customerDescription))
                return RedirectToAction("Index", new { message = "Please describe the item to verify ownership.", error = true });

            if (customerDescription.Trim().Length > MaxNotesLength)
                return RedirectToAction("Index", new
                {
                    message = $"Description cannot exceed {MaxNotesLength} characters.",
                    error = true
                });

            var claim = new LostItemClaim
            {
                ItemId = itemId,
                CustomerId = customerId,
                CustomerDescription = customerDescription.Trim(),
                ClaimDate = DateTime.Now,
            };

            _repository.AddClaim(claim);

            item.Status = LostItemStatus.ClaimPending;
            _repository.Update(item);

            return RedirectToAction("Index", new
            {
                message = $"Claim submitted by {customer.Name} for \"{item.Description}\". Awaiting staff verification.",
            });
        }

        // POST: LostAndFound/VerifyClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VerifyClaim(int claimId, string staffId)
        {
            var claim = _repository.GetClaimById(claimId);
            if (claim == null)
                return RedirectToAction("Index", new { message = "Claim not found.", error = true });

            var item = _repository.GetById(claim.ItemId);
            if (item == null)
                return RedirectToAction("Index", new { message = "Item not found.", error = true });

            if (staffId != null && staffId.Trim().Length > MaxStaffIdLength)
                return RedirectToAction("Index", new
                {
                    message = $"Staff ID cannot exceed {MaxStaffIdLength} characters.",
                    error = true
                });

            claim.Verified = true;
            claim.VerifiedByStaffId = string.IsNullOrWhiteSpace(staffId) ? "STAFF" : staffId.Trim();
            claim.VerifiedAt = DateTime.Now;
            _repository.UpdateClaim(claim);

            item.Status = LostItemStatus.Claimed;
            item.ClaimedAt = DateTime.Now;
            item.ClaimedByCustomerId = claim.CustomerId;
            _repository.Update(item);

            // Reject other pending claims for this item
            var otherClaims = _repository.GetClaimsForItem(item.Id)
                .Where(c => c.Id != claimId && !c.Verified && !c.Rejected);
            foreach (var other in otherClaims)
            {
                other.Rejected = true;
                other.RejectionReason = "Another claim was verified for this item.";
                _repository.UpdateClaim(other);
            }

            return RedirectToAction("Index", new
            {
                message = $"Claim verified! \"{item.Description}\" released to customer #{claim.CustomerId}.",
            });
        }

        // POST: LostAndFound/RejectClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RejectClaim(int claimId, string reason)
        {
            var claim = _repository.GetClaimById(claimId);
            if (claim == null)
                return RedirectToAction("Index", new { message = "Claim not found.", error = true });

            if (reason != null && reason.Trim().Length > MaxReasonLength)
                return RedirectToAction("Index", new
                {
                    message = $"Rejection reason cannot exceed {MaxReasonLength} characters.",
                    error = true
                });

            claim.Rejected = true;
            claim.RejectionReason = reason?.Trim() ?? "Description did not match item.";
            _repository.UpdateClaim(claim);

            // If no other pending claims, revert item to Found
            var item = _repository.GetById(claim.ItemId);
            if (item != null)
            {
                var pendingClaims = _repository.GetClaimsForItem(item.Id)
                    .Any(c => !c.Verified && !c.Rejected);
                if (!pendingClaims)
                {
                    item.Status = LostItemStatus.Found;
                    _repository.Update(item);
                }
            }

            return RedirectToAction("Index", new { message = "Claim rejected.", });
        }

        // POST: LostAndFound/Dispose
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Dispose(int id, bool donate = false)
        {
            var item = _repository.GetById(id);
            if (item == null)
                return RedirectToAction("Index", new { message = "Item not found.", error = true });

            item.Status = donate ? LostItemStatus.Donated : LostItemStatus.Disposed;
            item.DisposalDate = DateTime.Now;
            _repository.Update(item);

            var action = donate ? "donated" : "disposed of";
            return RedirectToAction("Index", new
            {
                message = $"\"{item.Description}\" has been {action}.",
            });
        }

        // GET: LostAndFound/Report
        public ActionResult Report()
        {
            var report = _repository.GetReport();
            return Json(report, JsonRequestBehavior.AllowGet);
        }

        // ── Input Validation ─────────────────────────────────────────

        /// <summary>
        /// Checks each (value, maxLength, label) tuple and returns the first
        /// error message, or null if all pass. Null/empty values are allowed
        /// (they're optional fields); only non-null values over the limit
        /// are rejected.
        /// </summary>
        private static string ValidateFieldLengths(
            params (string value, int maxLength, string label)[] fields)
        {
            foreach (var (value, maxLength, label) in fields)
            {
                if (value != null && value.Trim().Length > maxLength)
                    return $"{label} cannot exceed {maxLength} characters.";
            }
            return null;
        }

        private class RentalIdComparer : System.Collections.Generic.IEqualityComparer<Rental>
        {
            public bool Equals(Rental x, Rental y) => x?.Id == y?.Id;
            public int GetHashCode(Rental obj) => obj?.Id.GetHashCode() ?? 0;
        }
    }
}
