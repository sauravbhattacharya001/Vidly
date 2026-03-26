using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Late Fee Calculator &amp; Policy Manager — configure late fee policies
    /// (flat, per-day, tiered/graduated with grace periods and caps) and
    /// let customers estimate fees before returning movies.
    ///
    /// GET  /LateFees                                  — dashboard with policies + calculator
    /// GET  /LateFees/Calculate?policyId=1&amp;days=5      — estimate (JSON)
    /// GET  /LateFees/Schedule?policyId=1&amp;maxDays=30   — full schedule table (JSON)
    /// GET  /LateFees/Edit/1                           — edit policy form
    /// POST /LateFees/Save                             — create/update policy
    /// POST /LateFees/Delete/1                         — delete policy
    /// </summary>
    public class LateFeesController : Controller
    {
        private readonly LateFeeService _service;

        public LateFeesController()
        {
            _service = new LateFeeService();
        }

        // GET: LateFees
        public ActionResult Index(int? policyId, int? days)
        {
            var policies = _service.GetAllPolicies();
            var selectedId = policyId ?? policies.FirstOrDefault()?.Id ?? 0;
            var selectedPolicy = _service.GetPolicy(selectedId);

            var vm = new LateFeeViewModel
            {
                Policies = policies,
                SelectedPolicy = selectedPolicy,
                SelectedPolicyId = selectedId,
                CalculateDays = days
            };

            if (days.HasValue && days.Value > 0 && selectedPolicy != null)
            {
                vm.Estimate = _service.CalculateEstimate(selectedId, days.Value);
            }

            if (selectedPolicy != null)
            {
                vm.Schedule = _service.BuildSchedule(selectedId);
            }

            return View(vm);
        }

        // GET: LateFees/Calculate (AJAX)
        public ActionResult Calculate(int policyId, int days)
        {
            if (days < 0) days = 0;
            if (days > 365) days = 365;

            var estimate = _service.CalculateEstimate(policyId, days);
            return Json(estimate, JsonRequestBehavior.AllowGet);
        }

        // GET: LateFees/Schedule (AJAX)
        public ActionResult Schedule(int policyId, int maxDays = 30)
        {
            if (maxDays < 1) maxDays = 1;
            if (maxDays > 90) maxDays = 90;

            var schedule = _service.BuildSchedule(policyId, maxDays);
            return Json(schedule, JsonRequestBehavior.AllowGet);
        }

        // GET: LateFees/Edit/1
        public ActionResult Edit(int? id)
        {
            var policy = id.HasValue
                ? _service.GetPolicy(id.Value) ?? new LateFeePolicy()
                : new LateFeePolicy();

            return View(policy);
        }

        // POST: LateFees/Save
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Save(LateFeePolicy policy)
        {
            if (!ModelState.IsValid)
                return View("Edit", policy);

            try
            {
                _service.SavePolicy(policy);
                TempData["Success"] = policy.Id == 0
                    ? "Policy created successfully!"
                    : "Policy updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: LateFees/Delete/1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (_service.DeletePolicy(id))
                TempData["Success"] = "Policy deleted.";
            else
                TempData["Error"] = "Policy not found.";

            return RedirectToAction("Index");
        }
    }
}
