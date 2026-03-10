using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vidly.Tests
{
    public class StaffSchedulingServiceTests
    {
        private readonly List<StaffMember> _testStaff;
        private readonly StaffSchedulingService _service;

        public StaffSchedulingServiceTests()
        {
            _testStaff = new List<StaffMember>
            {
                new StaffMember { Id = 1, Name = "Alice", Role = StaffRole.Manager, HireDate = new DateTime(2020, 1, 1), IsActive = true },
                new StaffMember { Id = 2, Name = "Bob", Role = StaffRole.SeniorClerk, HireDate = new DateTime(2021, 6, 1), IsActive = true },
                new StaffMember { Id = 3, Name = "Carol", Role = StaffRole.Clerk, HireDate = new DateTime(2023, 3, 15), IsActive = true },
                new StaffMember { Id = 4, Name = "Dave", Role = StaffRole.ShiftLead, HireDate = new DateTime(2022, 9, 1), IsActive = true },
                new StaffMember { Id = 5, Name = "Eve", Role = StaffRole.Clerk, HireDate = new DateTime(2024, 1, 10), IsActive = false }
            };
            _service = new StaffSchedulingService(_testStaff);
        }

        private DateTime Today => new DateTime(2026, 3, 9); // Monday

        [TestMethod]
        public void Constructor_NullStaff_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new StaffSchedulingService(null));
        }

        [TestMethod]
        public void Constructor_EmptyStaff_Works()
        {
            var svc = new StaffSchedulingService(new List<StaffMember>());
            Assert.AreEqual(0, svc.TotalShifts);
        }

        [TestMethod]
        public void ScheduleShift_ValidInput_CreatesShift()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17), ShiftType.FullDay);
            Assert.IsNotNull(shift);
            Assert.AreEqual(1, shift.StaffId);
            Assert.AreEqual("Alice", shift.StaffName);
            Assert.AreEqual(ShiftType.FullDay, shift.Type);
            Assert.AreEqual(8.0, shift.DurationHours);
            Assert.IsFalse(shift.IsConfirmed);
        }

        [TestMethod]
        public void ScheduleShift_StartAfterEnd_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                _service.ScheduleShift(1, Today.AddHours(17), Today.AddHours(9)));
        }

        [TestMethod]
        public void ScheduleShift_Over12Hours_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(19)));
        }

        [TestMethod]
        public void ScheduleShift_TooShort_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(9).AddMinutes(15)));
        }

        [TestMethod]
        public void ScheduleShift_UnknownStaff_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                _service.ScheduleShift(99, Today.AddHours(9), Today.AddHours(17)));
        }

        [TestMethod]
        public void ScheduleShift_InactiveStaff_Throws()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                _service.ScheduleShift(5, Today.AddHours(9), Today.AddHours(17)));
        }

        [TestMethod]
        public void ScheduleShift_WithNotes_Stored()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17), notes: "Training day");
            Assert.AreEqual("Training day", shift.Notes);
        }

        [TestMethod]
        public void ScheduleShift_OverlappingShift_Throws()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            Assert.ThrowsException<InvalidOperationException>(() =>
                _service.ScheduleShift(1, Today.AddHours(12), Today.AddHours(17)));
        }

        [TestMethod]
        public void ScheduleShift_AdjacentShifts_OK()
        {
            _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(9));
            _service.MinRestHoursBetweenShifts = 0;
            var s2 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            Assert.IsNotNull(s2);
        }

        [TestMethod]
        public void ScheduleShift_DifferentStaff_NoConflict()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var s2 = _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            Assert.IsNotNull(s2);
        }

        [TestMethod]
        public void GetConflictingShifts_FindsOverlap()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var conflicts = _service.GetConflictingShifts(1, Today.AddHours(12), Today.AddHours(17));
            Assert.Single(conflicts);
        }

        [TestMethod]
        public void GetConflictingShifts_NoOverlap_Empty()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var conflicts = _service.GetConflictingShifts(1, Today.AddHours(14), Today.AddHours(17));
            Assert.IsFalse(conflicts.Any());
        }

        [TestMethod]
        public void ScheduleShift_InsufficientRest_Throws()
        {
            _service.ScheduleShift(1, Today.AddHours(14), Today.AddHours(22));
            Assert.ThrowsException<InvalidOperationException>(() =>
                _service.ScheduleShift(1, Today.AddDays(1).AddHours(4), Today.AddDays(1).AddHours(12)));
        }

        [TestMethod]
        public void ScheduleShift_SufficientRest_OK()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(14));
            var s2 = _service.ScheduleShift(1, Today.AddHours(22), Today.AddDays(1).AddHours(2));
            Assert.IsNotNull(s2);
        }

        [TestMethod]
        public void CheckRestViolation_ReturnsNull_WhenOK()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var result = _service.CheckRestViolation(1, Today.AddHours(22), Today.AddDays(1).AddHours(4));
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetShift_ExistingId_Found()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.IsNotNull(_service.GetShift(shift.Id));
        }

        [TestMethod]
        public void GetShift_InvalidId_Null()
        {
            Assert.IsNull(_service.GetShift(999));
        }

        [TestMethod]
        public void CancelShift_Removes()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.IsTrue(_service.CancelShift(shift.Id));
            Assert.IsNull(_service.GetShift(shift.Id));
        }

        [TestMethod]
        public void CancelShift_InvalidId_False()
        {
            Assert.IsFalse(_service.CancelShift(999));
        }

        [TestMethod]
        public void ConfirmShift_SetsFlag()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.IsTrue(_service.ConfirmShift(shift.Id));
            Assert.IsTrue(_service.GetShift(shift.Id).IsConfirmed);
        }

        [TestMethod]
        public void ConfirmShift_InvalidId_False()
        {
            Assert.IsFalse(_service.ConfirmShift(999));
        }

        [TestMethod]
        public void GetShiftsForDate_FiltersCorrectly()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(13), Today.AddHours(21));
            _service.ScheduleShift(3, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(17));

            var todayShifts = _service.GetShiftsForDate(Today);
            Assert.AreEqual(2, todayShifts.Count);
        }

        [TestMethod]
        public void GetShiftsForStaff_DateRange_Works()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(13));
            _service.ScheduleShift(1, Today.AddDays(5).AddHours(9), Today.AddDays(5).AddHours(13));

            var inRange = _service.GetShiftsForStaff(1, Today, Today.AddDays(2));
            Assert.AreEqual(2, inRange.Count);
        }

        [TestMethod]
        public void SetAvailability_CreatesRecord()
        {
            var avail = _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            Assert.IsNotNull(avail);
            Assert.AreEqual(1, avail.StaffId);
            Assert.AreEqual(8.0, avail.AvailableHours);
        }

        [TestMethod]
        public void SetAvailability_InvalidRange_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(17, 0, 0), new TimeSpan(9, 0, 0)));
        }

        [TestMethod]
        public void SetAvailability_ReplacesExisting()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(13, 0, 0));
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(8, 0, 0), new TimeSpan(16, 0, 0));
            var avail = _service.GetAvailability(1);
            Assert.Single(avail);
            Assert.AreEqual(new TimeSpan(8, 0, 0), avail[0].AvailableFrom);
        }

        [TestMethod]
        public void SetAvailability_UnknownStaff_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                _service.SetAvailability(99, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0)));
        }

        [TestMethod]
        public void GetAvailableStaffForDay_ReturnsMatching()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            _service.SetAvailability(2, DayOfWeek.Monday, new TimeSpan(13, 0, 0), new TimeSpan(21, 0, 0));
            _service.SetAvailability(3, DayOfWeek.Tuesday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

            var monday = _service.GetAvailableStaffForDay(DayOfWeek.Monday);
            Assert.AreEqual(2, monday.Count);
        }

        [TestMethod]
        public void IsStaffAvailable_WithinWindow_True()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            Assert.IsTrue(_service.IsStaffAvailable(1, Today.AddHours(10), Today.AddHours(15)));
        }

        [TestMethod]
        public void IsStaffAvailable_OutsideWindow_False()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(14, 0, 0));
            Assert.IsFalse(_service.IsStaffAvailable(1, Today.AddHours(10), Today.AddHours(17)));
        }

        [TestMethod]
        public void IsStaffAvailable_NoAvailabilitySet_True()
        {
            Assert.IsTrue(_service.IsStaffAvailable(1, Today.AddHours(9), Today.AddHours(17)));
        }

        [TestMethod]
        public void GetCoverageReport_EmptyDay()
        {
            var report = _service.GetCoverageReport(Today);
            Assert.AreEqual(0, report.TotalShifts);
            Assert.IsFalse(report.HasManagerOnDuty);
        }

        [TestMethod]
        public void GetCoverageReport_FullCoverage()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(13));
            _service.ScheduleShift(3, Today.AddHours(13), Today.AddHours(17));
            _service.ScheduleShift(4, Today.AddHours(13), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(17), Today.AddHours(21));
            _service.ScheduleShift(3, Today.AddHours(17), Today.AddHours(21));

            var report = _service.GetCoverageReport(Today);
            Assert.AreEqual(6, report.TotalShifts);
            Assert.IsTrue(report.HasManagerOnDuty);
            Assert.IsTrue(report.MeetsMinimumCoverage);
        }

        [TestMethod]
        public void GetCoverageReport_IdentifiesGaps()
        {
            _service.ScheduleShift(3, Today.AddHours(9), Today.AddHours(13));
            var report = _service.GetCoverageReport(Today);
            Assert.IsTrue(report.CoverageGaps.Any(g => g.Contains("Morning")));
            Assert.IsTrue(report.CoverageGaps.Any(g => g.Contains("manager")));
        }

        [TestMethod]
        public void GetCoverageReport_CountsConfirmed()
        {
            var s1 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            _service.ConfirmShift(s1.Id);

            var report = _service.GetCoverageReport(Today);
            Assert.AreEqual(1, report.ConfirmedShifts);
            Assert.AreEqual(1, report.UnconfirmedShifts);
        }

        [TestMethod]
        public void GetWeeklyCoverage_Returns7Days()
        {
            var coverage = _service.GetWeeklyCoverage(Today);
            Assert.AreEqual(7, coverage.Count);
        }

        [TestMethod]
        public void GetUnderstaffedDays_FiltersCorrectly()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(3, Today.AddHours(13), Today.AddHours(21));
            _service.ScheduleShift(4, Today.AddHours(17), Today.AddHours(21));

            var understaffed = _service.GetUnderstaffedDays(Today, Today.AddDays(1));
            Assert.IsTrue(understaffed.Any(d => d.Date == Today.AddDays(1).Date));
        }

        [TestMethod]
        public void GetWeeklySummary_CalculatesHours()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(17));
            _service.ScheduleShift(1, Today.AddDays(2).AddHours(9), Today.AddDays(2).AddHours(13));

            var summary = _service.GetWeeklySummary(1, Today);
            Assert.AreEqual(3, summary.ShiftCount);
            Assert.AreEqual(20.0, summary.TotalHours);
            Assert.IsFalse(summary.ExceedsMaxHours);
        }

        [TestMethod]
        public void GetWeeklySummary_DetectsOvertime()
        {
            _service.MaxWeeklyHours = 20;
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(18));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(6), Today.AddDays(1).AddHours(18));

            var summary = _service.GetWeeklySummary(1, Today);
            Assert.IsTrue(summary.ExceedsMaxHours);
        }

        [TestMethod]
        public void GetWeeklySummary_UnknownStaff_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => _service.GetWeeklySummary(99, Today));
        }

        [TestMethod]
        public void GetAllWeeklySummaries_ReturnsActiveOnly()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var summaries = _service.GetAllWeeklySummaries(Today);
            Assert.AreEqual(4, summaries.Count);
        }

        [TestMethod]
        public void GetOvertimeRisk_IdentifiesAtRisk()
        {
            _service.MaxWeeklyHours = 10;
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(18));

            var atRisk = _service.GetOvertimeRisk(Today);
            Assert.Single(atRisk);
            Assert.AreEqual(1, atRisk[0].StaffId);
        }

        [TestMethod]
        public void RequestSwap_CreatesRequest()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Drop, reason: "Doctor appointment");

            Assert.IsNotNull(request);
            Assert.AreEqual(SwapRequestStatus.Pending, request.Status);
            Assert.AreEqual("Doctor appointment", request.Reason);
        }

        [TestMethod]
        public void RequestSwap_NotOwnShift_Throws()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.ThrowsException<InvalidOperationException>(() =>
                _service.RequestSwap(2, shift.Id, SwapRequestType.Drop));
        }

        [TestMethod]
        public void RequestSwap_InvalidShift_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                _service.RequestSwap(1, 999, SwapRequestType.Drop));
        }

        [TestMethod]
        public void RequestSwap_WithTarget_StoresTarget()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Swap, targetStaffId: 2);
            Assert.AreEqual(2, request.TargetStaffId);
            Assert.AreEqual("Bob", request.TargetStaffName);
        }

        [TestMethod]
        public void ApproveSwap_ReassignsShift()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Cover, targetStaffId: 2);
            Assert.IsTrue(_service.ApproveSwap(request.Id, coveringStaffId: 2));

            var updated = _service.GetShift(shift.Id);
            Assert.IsTrue(updated.IsCovered);
            Assert.AreEqual(2, updated.CoveredByStaffId);
            Assert.AreEqual(0, _service.GetPendingSwapRequests().Count);
        }

        [TestMethod]
        public void ApproveSwap_ConflictingCoverer_False()
        {
            var s1 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, s1.Id, SwapRequestType.Cover);
            Assert.IsFalse(_service.ApproveSwap(request.Id, coveringStaffId: 2));
        }

        [TestMethod]
        public void DenySwap_UpdatesStatus()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Drop);
            Assert.IsTrue(_service.DenySwap(request.Id));
            Assert.AreEqual(0, _service.GetPendingSwapRequests().Count);
        }

        [TestMethod]
        public void DenySwap_AlreadyResolved_False()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Drop);
            _service.DenySwap(request.Id);
            Assert.IsFalse(_service.DenySwap(request.Id));
        }

        [TestMethod]
        public void GetSwapHistory_FiltersByStaff()
        {
            var s1 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var s2 = _service.ScheduleShift(2, Today.AddHours(13), Today.AddHours(17));
            _service.RequestSwap(1, s1.Id, SwapRequestType.Drop);
            _service.RequestSwap(2, s2.Id, SwapRequestType.Drop);

            Assert.Single(_service.GetSwapHistory(1));
            Assert.Single(_service.GetSwapHistory(2));
        }

        [TestMethod]
        public void SuggestStaffForShift_ExcludesConflicts()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var suggestions = _service.SuggestStaffForShift(Today.AddHours(9), Today.AddHours(17), Today);

            Assert.IsFalse(s => s.Staff.Id == 1.Contains(suggestions));
            Assert.IsFalse(s => s.Staff.Id == 5.Contains(suggestions));
        }

        [TestMethod]
        public void SuggestStaffForShift_PrefersAvailable()
        {
            _service.SetAvailability(2, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            var suggestions = _service.SuggestStaffForShift(Today.AddHours(9), Today.AddHours(17), Today);

            Assert.IsTrue(suggestions.Count > 0);
            var bob = suggestions.FirstOrDefault(s => s.Staff.Id == 2);
            Assert.IsTrue(bob.IsAvailable);
        }

        [TestMethod]
        public void SuggestStaffForShift_ExcludesOvertimeRisk()
        {
            _service.MaxWeeklyHours = 10;
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(2, Today.AddHours(6), Today.AddHours(12));

            var suggestions = _service.SuggestStaffForShift(
                Today.AddDays(1).AddHours(6), Today.AddDays(1).AddHours(12), Today);
            Assert.IsFalse(s => s.Staff.Id == 2.Contains(suggestions));
        }

        [TestMethod]
        public void GetSchedulingFairness_EqualDistribution_Zero()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));

            var fairness = _service.GetSchedulingFairness(Today);
            Assert.AreEqual(0.0, fairness, 2);
        }

        [TestMethod]
        public void GetSchedulingFairness_UnequalDistribution_NonZero()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(13));

            var fairness = _service.GetSchedulingFairness(Today);
            Assert.IsTrue(fairness > 0);
        }

        [TestMethod]
        public void GetSchedulingFairness_SingleStaff_Zero()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.AreEqual(0.0, _service.GetSchedulingFairness(Today));
        }

        [TestMethod]
        public void TotalShifts_Tracks()
        {
            Assert.AreEqual(0, _service.TotalShifts);
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.AreEqual(1, _service.TotalShifts);
        }

        [TestMethod]
        public void TotalAvailabilityRecords_Tracks()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            Assert.AreEqual(1, _service.TotalAvailabilityRecords);
        }

        [TestMethod]
        public void TotalSwapRequests_Tracks()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.RequestSwap(1, shift.Id, SwapRequestType.Drop);
            Assert.AreEqual(1, _service.TotalSwapRequests);
        }

        [TestMethod]
        public void Shift_OverlapsWith_Works()
        {
            var shift = new Shift { StartTime = Today.AddHours(9), EndTime = Today.AddHours(17) };
            Assert.IsTrue(shift.OverlapsWith(Today.AddHours(12), Today.AddHours(20)));
            Assert.IsTrue(shift.OverlapsWith(Today.AddHours(5), Today.AddHours(10)));
            Assert.IsFalse(shift.OverlapsWith(Today.AddHours(17), Today.AddHours(22)));
            Assert.IsFalse(shift.OverlapsWith(Today.AddHours(5), Today.AddHours(9)));
        }

        [TestMethod]
        public void Shift_ShiftDate_IsStartDate()
        {
            var shift = new Shift { StartTime = Today.AddHours(22), EndTime = Today.AddDays(1).AddHours(2) };
            Assert.AreEqual(Today.Date, shift.ShiftDate);
        }
    }
}
