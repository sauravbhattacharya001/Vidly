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
    }
}
