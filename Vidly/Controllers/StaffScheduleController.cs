using System;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages weekly staff scheduling, shift creation, and shift swap requests.
    /// Managers can view the week at a glance, add/remove shifts, and approve swaps.
    /// </summary>
    public class StaffScheduleController : Controller
    {
        private readonly IStaffScheduleRepository _repo;

        public StaffScheduleController()
            : this(new InMemoryStaffScheduleRepository()) { }

        public StaffScheduleController(IStaffScheduleRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        /// <summary>
        /// GET: StaffSchedule — Weekly schedule grid with all shifts and staff summaries.
        /// Optional ?week=yyyy-MM-dd to view a specific week.
        /// </summary>
        public ActionResult Index(string week)
        {
            DateTime weekStart;
            if (!string.IsNullOrEmpty(week) &&
                DateTime.TryParseExact(week, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                weekStart = StartOfWeek(parsed);
            }
            else
            {
                weekStart = StartOfWeek(DateTime.Today);
            }

            var shifts = _repo.GetShiftsForWeek(weekStart);
            var summaries = _repo.GetWeeklySummaries(weekStart);

            var vm = new ScheduleWeekViewModel
            {
                WeekStart = weekStart,
                WeekEnd = weekStart.AddDays(6),
                Staff = _repo.GetAllStaff().ToList(),
                Summaries = summaries,
                PendingSwaps = _repo.GetPendingSwaps().ToList(),
                TotalShifts = shifts.Count,
                TotalHours = shifts.Sum(s => s.Hours)
            };

            // Group shifts by date
            for (int i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                vm.ShiftsByDate[date] = shifts.Where(s => s.Date.Date == date.Date).ToList();
            }

            return View(vm);
        }

        /// <summary>
        /// GET: StaffSchedule/Create — Show form to add a new shift.
        /// </summary>
        public ActionResult Create(string date)
        {
            var vm = new ShiftCreateViewModel
            {
                AvailableStaff = _repo.GetAllStaff().ToList()
            };

            if (!string.IsNullOrEmpty(date) &&
                DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                vm.Date = parsed;
            }

            return View(vm);
        }

        /// <summary>
        /// POST: StaffSchedule/Create — Creates a new shift assignment.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(ShiftCreateViewModel model)
        {
            if (model == null) return new HttpStatusCodeResult(400);

            var staff = _repo.GetStaffById(model.StaffId);
            if (staff == null)
                ModelState.AddModelError("StaffId", "Please select a valid staff member.");

            if (!TimeSpan.TryParse(model.StartTime, out var start))
                ModelState.AddModelError("StartTime", "Invalid start time.");

            if (!TimeSpan.TryParse(model.EndTime, out var end))
                ModelState.AddModelError("EndTime", "Invalid end time.");

            if (start >= end)
                ModelState.AddModelError("EndTime", "End time must be after start time.");

            if (!ModelState.IsValid)
            {
                model.AvailableStaff = _repo.GetAllStaff().ToList();
                return View(model);
            }

            // Check if staff member prefers this day off
            if (staff.DaysOff.Contains(model.Date.DayOfWeek))
            {
                TempData["Warning"] = $"{staff.Name} prefers {model.Date.DayOfWeek} off — shift created anyway.";
            }

            var shift = new Shift
            {
                StaffId = staff.Id,
                StaffName = staff.Name,
                StaffRole = staff.Role,
                Date = model.Date.Date,
                StartTime = start,
                EndTime = end,
                Type = model.Type,
                Notes = model.Notes?.Trim()
            };

            _repo.AddShift(shift);
            TempData["Message"] = $"Shift added for {staff.Name} on {model.Date:ddd MMM d}.";
            return RedirectToAction("Index", new { week = StartOfWeek(model.Date).ToString("yyyy-MM-dd") });
        }

        /// <summary>
        /// POST: StaffSchedule/DeleteShift — Removes a shift.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteShift(int id, string returnWeek)
        {
            var shift = _repo.GetShiftById(id);
            if (shift == null) return HttpNotFound();

            _repo.RemoveShift(id);
            TempData["Message"] = $"Shift for {shift.StaffName} on {shift.Date:ddd MMM d} removed.";

            return RedirectToAction("Index", new { week = returnWeek });
        }

        /// <summary>
        /// GET: StaffSchedule/Staff/5 — View a single staff member's schedule.
        /// </summary>
        public ActionResult Staff(int id)
        {
            var staff = _repo.GetStaffById(id);
            if (staff == null) return HttpNotFound();

            var shifts = _repo.GetShiftsForStaff(id);
            ViewBag.Staff = staff;
            ViewBag.TotalHours = shifts.Sum(s => s.Hours);
            return View(shifts);
        }

        /// <summary>
        /// GET: StaffSchedule/RequestSwap/5 — Form to request swapping shift #5.
        /// </summary>
        public ActionResult RequestSwap(int id)
        {
            var shift = _repo.GetShiftById(id);
            if (shift == null) return HttpNotFound();

            // Get other shifts in the same week that belong to different staff
            var weekStart = StartOfWeek(shift.Date);
            var weekShifts = _repo.GetShiftsForWeek(weekStart)
                .Where(s => s.StaffId != shift.StaffId)
                .ToList();

            var vm = new SwapRequestViewModel
            {
                ShiftId = id,
                CurrentShift = shift,
                AvailableShifts = weekShifts
            };

            return View(vm);
        }

        /// <summary>
        /// POST: StaffSchedule/RequestSwap — Submits a swap request.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RequestSwap(SwapRequestViewModel model)
        {
            if (model == null) return new HttpStatusCodeResult(400);

            var shiftA = _repo.GetShiftById(model.ShiftId);
            var shiftB = _repo.GetShiftById(model.TargetShiftId);

            if (shiftA == null || shiftB == null) return HttpNotFound();

            var swap = new ShiftSwapRequest
            {
                RequestingShiftId = shiftA.Id,
                TargetShiftId = shiftB.Id,
                RequestingStaffName = shiftA.StaffName,
                TargetStaffName = shiftB.StaffName,
                Reason = model.Reason?.Trim()
            };

            _repo.RequestSwap(swap);
            TempData["Message"] = $"Swap request submitted: {shiftA.StaffName}'s shift ↔ {shiftB.StaffName}'s shift.";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// POST: StaffSchedule/ApproveSwap — Manager approves a shift swap.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ApproveSwap(int id, string note)
        {
            _repo.ApproveSwap(id, note);
            TempData["Message"] = "Shift swap approved and applied!";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// POST: StaffSchedule/DenySwap — Manager denies a shift swap.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DenySwap(int id, string note)
        {
            _repo.DenySwap(id, note);
            TempData["Message"] = "Shift swap denied.";
            return RedirectToAction("Index");
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.AddDays(-diff).Date;
        }
    }
}
