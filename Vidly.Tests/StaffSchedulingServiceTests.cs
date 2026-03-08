using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Services;
using Xunit;

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

        [Fact]
        public void Constructor_NullStaff_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new StaffSchedulingService(null));
        }

        [Fact]
        public void Constructor_EmptyStaff_Works()
        {
            var svc = new StaffSchedulingService(new List<StaffMember>());
            Assert.Equal(0, svc.TotalShifts);
        }

        [Fact]
        public void ScheduleShift_ValidInput_CreatesShift()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17), ShiftType.FullDay);
            Assert.NotNull(shift);
            Assert.Equal(1, shift.StaffId);
            Assert.Equal("Alice", shift.StaffName);
            Assert.Equal(ShiftType.FullDay, shift.Type);
            Assert.Equal(8.0, shift.DurationHours);
            Assert.False(shift.IsConfirmed);
        }

        [Fact]
        public void ScheduleShift_StartAfterEnd_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.ScheduleShift(1, Today.AddHours(17), Today.AddHours(9)));
        }

        [Fact]
        public void ScheduleShift_Over12Hours_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(19)));
        }

        [Fact]
        public void ScheduleShift_TooShort_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(9).AddMinutes(15)));
        }

        [Fact]
        public void ScheduleShift_UnknownStaff_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.ScheduleShift(99, Today.AddHours(9), Today.AddHours(17)));
        }

        [Fact]
        public void ScheduleShift_InactiveStaff_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _service.ScheduleShift(5, Today.AddHours(9), Today.AddHours(17)));
        }

        [Fact]
        public void ScheduleShift_WithNotes_Stored()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17), notes: "Training day");
            Assert.Equal("Training day", shift.Notes);
        }

        [Fact]
        public void ScheduleShift_OverlappingShift_Throws()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            Assert.Throws<InvalidOperationException>(() =>
                _service.ScheduleShift(1, Today.AddHours(12), Today.AddHours(17)));
        }

        [Fact]
        public void ScheduleShift_AdjacentShifts_OK()
        {
            _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(9));
            _service.MinRestHoursBetweenShifts = 0;
            var s2 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            Assert.NotNull(s2);
        }

        [Fact]
        public void ScheduleShift_DifferentStaff_NoConflict()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var s2 = _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            Assert.NotNull(s2);
        }

        [Fact]
        public void GetConflictingShifts_FindsOverlap()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var conflicts = _service.GetConflictingShifts(1, Today.AddHours(12), Today.AddHours(17));
            Assert.Single(conflicts);
        }

        [Fact]
        public void GetConflictingShifts_NoOverlap_Empty()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var conflicts = _service.GetConflictingShifts(1, Today.AddHours(14), Today.AddHours(17));
            Assert.Empty(conflicts);
        }

        [Fact]
        public void ScheduleShift_InsufficientRest_Throws()
        {
            _service.ScheduleShift(1, Today.AddHours(14), Today.AddHours(22));
            Assert.Throws<InvalidOperationException>(() =>
                _service.ScheduleShift(1, Today.AddDays(1).AddHours(4), Today.AddDays(1).AddHours(12)));
        }

        [Fact]
        public void ScheduleShift_SufficientRest_OK()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(14));
            var s2 = _service.ScheduleShift(1, Today.AddHours(22), Today.AddDays(1).AddHours(2));
            Assert.NotNull(s2);
        }

        [Fact]
        public void CheckRestViolation_ReturnsNull_WhenOK()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var result = _service.CheckRestViolation(1, Today.AddHours(22), Today.AddDays(1).AddHours(4));
            Assert.Null(result);
        }

        [Fact]
        public void GetShift_ExistingId_Found()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.NotNull(_service.GetShift(shift.Id));
        }

        [Fact]
        public void GetShift_InvalidId_Null()
        {
            Assert.Null(_service.GetShift(999));
        }

        [Fact]
        public void CancelShift_Removes()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.True(_service.CancelShift(shift.Id));
            Assert.Null(_service.GetShift(shift.Id));
        }

        [Fact]
        public void CancelShift_InvalidId_False()
        {
            Assert.False(_service.CancelShift(999));
        }

        [Fact]
        public void ConfirmShift_SetsFlag()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.True(_service.ConfirmShift(shift.Id));
            Assert.True(_service.GetShift(shift.Id).IsConfirmed);
        }

        [Fact]
        public void ConfirmShift_InvalidId_False()
        {
            Assert.False(_service.ConfirmShift(999));
        }

        [Fact]
        public void GetShiftsForDate_FiltersCorrectly()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(13), Today.AddHours(21));
            _service.ScheduleShift(3, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(17));

            var todayShifts = _service.GetShiftsForDate(Today);
            Assert.Equal(2, todayShifts.Count);
        }

        [Fact]
        public void GetShiftsForStaff_DateRange_Works()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(13));
            _service.ScheduleShift(1, Today.AddDays(5).AddHours(9), Today.AddDays(5).AddHours(13));

            var inRange = _service.GetShiftsForStaff(1, Today, Today.AddDays(2));
            Assert.Equal(2, inRange.Count);
        }

        [Fact]
        public void SetAvailability_CreatesRecord()
        {
            var avail = _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            Assert.NotNull(avail);
            Assert.Equal(1, avail.StaffId);
            Assert.Equal(8.0, avail.AvailableHours);
        }

        [Fact]
        public void SetAvailability_InvalidRange_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(17, 0, 0), new TimeSpan(9, 0, 0)));
        }

        [Fact]
        public void SetAvailability_ReplacesExisting()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(13, 0, 0));
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(8, 0, 0), new TimeSpan(16, 0, 0));
            var avail = _service.GetAvailability(1);
            Assert.Single(avail);
            Assert.Equal(new TimeSpan(8, 0, 0), avail[0].AvailableFrom);
        }

        [Fact]
        public void SetAvailability_UnknownStaff_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.SetAvailability(99, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0)));
        }

        [Fact]
        public void GetAvailableStaffForDay_ReturnsMatching()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            _service.SetAvailability(2, DayOfWeek.Monday, new TimeSpan(13, 0, 0), new TimeSpan(21, 0, 0));
            _service.SetAvailability(3, DayOfWeek.Tuesday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

            var monday = _service.GetAvailableStaffForDay(DayOfWeek.Monday);
            Assert.Equal(2, monday.Count);
        }

        [Fact]
        public void IsStaffAvailable_WithinWindow_True()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            Assert.True(_service.IsStaffAvailable(1, Today.AddHours(10), Today.AddHours(15)));
        }

        [Fact]
        public void IsStaffAvailable_OutsideWindow_False()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(14, 0, 0));
            Assert.False(_service.IsStaffAvailable(1, Today.AddHours(10), Today.AddHours(17)));
        }

        [Fact]
        public void IsStaffAvailable_NoAvailabilitySet_True()
        {
            Assert.True(_service.IsStaffAvailable(1, Today.AddHours(9), Today.AddHours(17)));
        }

        [Fact]
        public void GetCoverageReport_EmptyDay()
        {
            var report = _service.GetCoverageReport(Today);
            Assert.Equal(0, report.TotalShifts);
            Assert.False(report.HasManagerOnDuty);
        }

        [Fact]
        public void GetCoverageReport_FullCoverage()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(13));
            _service.ScheduleShift(3, Today.AddHours(13), Today.AddHours(17));
            _service.ScheduleShift(4, Today.AddHours(13), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(17), Today.AddHours(21));
            _service.ScheduleShift(3, Today.AddHours(17), Today.AddHours(21));

            var report = _service.GetCoverageReport(Today);
            Assert.Equal(6, report.TotalShifts);
            Assert.True(report.HasManagerOnDuty);
            Assert.True(report.MeetsMinimumCoverage);
        }

        [Fact]
        public void GetCoverageReport_IdentifiesGaps()
        {
            _service.ScheduleShift(3, Today.AddHours(9), Today.AddHours(13));
            var report = _service.GetCoverageReport(Today);
            Assert.True(report.CoverageGaps.Any(g => g.Contains("Morning")));
            Assert.True(report.CoverageGaps.Any(g => g.Contains("manager")));
        }

        [Fact]
        public void GetCoverageReport_CountsConfirmed()
        {
            var s1 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            _service.ConfirmShift(s1.Id);

            var report = _service.GetCoverageReport(Today);
            Assert.Equal(1, report.ConfirmedShifts);
            Assert.Equal(1, report.UnconfirmedShifts);
        }

        [Fact]
        public void GetWeeklyCoverage_Returns7Days()
        {
            var coverage = _service.GetWeeklyCoverage(Today);
            Assert.Equal(7, coverage.Count);
        }

        [Fact]
        public void GetUnderstaffedDays_FiltersCorrectly()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(3, Today.AddHours(13), Today.AddHours(21));
            _service.ScheduleShift(4, Today.AddHours(17), Today.AddHours(21));

            var understaffed = _service.GetUnderstaffedDays(Today, Today.AddDays(1));
            Assert.True(understaffed.Any(d => d.Date == Today.AddDays(1).Date));
        }

        [Fact]
        public void GetWeeklySummary_CalculatesHours()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(17));
            _service.ScheduleShift(1, Today.AddDays(2).AddHours(9), Today.AddDays(2).AddHours(13));

            var summary = _service.GetWeeklySummary(1, Today);
            Assert.Equal(3, summary.ShiftCount);
            Assert.Equal(20.0, summary.TotalHours);
            Assert.False(summary.ExceedsMaxHours);
        }

        [Fact]
        public void GetWeeklySummary_DetectsOvertime()
        {
            _service.MaxWeeklyHours = 20;
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(18));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(6), Today.AddDays(1).AddHours(18));

            var summary = _service.GetWeeklySummary(1, Today);
            Assert.True(summary.ExceedsMaxHours);
        }

        [Fact]
        public void GetWeeklySummary_UnknownStaff_Throws()
        {
            Assert.Throws<ArgumentException>(() => _service.GetWeeklySummary(99, Today));
        }

        [Fact]
        public void GetAllWeeklySummaries_ReturnsActiveOnly()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var summaries = _service.GetAllWeeklySummaries(Today);
            Assert.Equal(4, summaries.Count);
        }

        [Fact]
        public void GetOvertimeRisk_IdentifiesAtRisk()
        {
            _service.MaxWeeklyHours = 10;
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(6), Today.AddHours(18));

            var atRisk = _service.GetOvertimeRisk(Today);
            Assert.Single(atRisk);
            Assert.Equal(1, atRisk[0].StaffId);
        }

        [Fact]
        public void RequestSwap_CreatesRequest()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Drop, reason: "Doctor appointment");

            Assert.NotNull(request);
            Assert.Equal(SwapRequestStatus.Pending, request.Status);
            Assert.Equal("Doctor appointment", request.Reason);
        }

        [Fact]
        public void RequestSwap_NotOwnShift_Throws()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.Throws<InvalidOperationException>(() =>
                _service.RequestSwap(2, shift.Id, SwapRequestType.Drop));
        }

        [Fact]
        public void RequestSwap_InvalidShift_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _service.RequestSwap(1, 999, SwapRequestType.Drop));
        }

        [Fact]
        public void RequestSwap_WithTarget_StoresTarget()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Swap, targetStaffId: 2);
            Assert.Equal(2, request.TargetStaffId);
            Assert.Equal("Bob", request.TargetStaffName);
        }

        [Fact]
        public void ApproveSwap_ReassignsShift()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Cover, targetStaffId: 2);
            Assert.True(_service.ApproveSwap(request.Id, coveringStaffId: 2));

            var updated = _service.GetShift(shift.Id);
            Assert.True(updated.IsCovered);
            Assert.Equal(2, updated.CoveredByStaffId);
            Assert.Empty(_service.GetPendingSwapRequests());
        }

        [Fact]
        public void ApproveSwap_ConflictingCoverer_False()
        {
            var s1 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, s1.Id, SwapRequestType.Cover);
            Assert.False(_service.ApproveSwap(request.Id, coveringStaffId: 2));
        }

        [Fact]
        public void DenySwap_UpdatesStatus()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Drop);
            Assert.True(_service.DenySwap(request.Id));
            Assert.Empty(_service.GetPendingSwapRequests());
        }

        [Fact]
        public void DenySwap_AlreadyResolved_False()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var request = _service.RequestSwap(1, shift.Id, SwapRequestType.Drop);
            _service.DenySwap(request.Id);
            Assert.False(_service.DenySwap(request.Id));
        }

        [Fact]
        public void GetSwapHistory_FiltersByStaff()
        {
            var s1 = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(13));
            var s2 = _service.ScheduleShift(2, Today.AddHours(13), Today.AddHours(17));
            _service.RequestSwap(1, s1.Id, SwapRequestType.Drop);
            _service.RequestSwap(2, s2.Id, SwapRequestType.Drop);

            Assert.Single(_service.GetSwapHistory(1));
            Assert.Single(_service.GetSwapHistory(2));
        }

        [Fact]
        public void SuggestStaffForShift_ExcludesConflicts()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            var suggestions = _service.SuggestStaffForShift(Today.AddHours(9), Today.AddHours(17), Today);

            Assert.DoesNotContain(suggestions, s => s.Staff.Id == 1);
            Assert.DoesNotContain(suggestions, s => s.Staff.Id == 5);
        }

        [Fact]
        public void SuggestStaffForShift_PrefersAvailable()
        {
            _service.SetAvailability(2, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            var suggestions = _service.SuggestStaffForShift(Today.AddHours(9), Today.AddHours(17), Today);

            Assert.True(suggestions.Count > 0);
            var bob = suggestions.FirstOrDefault(s => s.Staff.Id == 2);
            Assert.True(bob.IsAvailable);
        }

        [Fact]
        public void SuggestStaffForShift_ExcludesOvertimeRisk()
        {
            _service.MaxWeeklyHours = 10;
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(2, Today.AddHours(6), Today.AddHours(12));

            var suggestions = _service.SuggestStaffForShift(
                Today.AddDays(1).AddHours(6), Today.AddDays(1).AddHours(12), Today);
            Assert.DoesNotContain(suggestions, s => s.Staff.Id == 2);
        }

        [Fact]
        public void GetSchedulingFairness_EqualDistribution_Zero()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(17));

            var fairness = _service.GetSchedulingFairness(Today);
            Assert.Equal(0.0, fairness, 2);
        }

        [Fact]
        public void GetSchedulingFairness_UnequalDistribution_NonZero()
        {
            _service.MinRestHoursBetweenShifts = 0;
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.ScheduleShift(1, Today.AddDays(1).AddHours(9), Today.AddDays(1).AddHours(17));
            _service.ScheduleShift(2, Today.AddHours(9), Today.AddHours(13));

            var fairness = _service.GetSchedulingFairness(Today);
            Assert.True(fairness > 0);
        }

        [Fact]
        public void GetSchedulingFairness_SingleStaff_Zero()
        {
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.Equal(0.0, _service.GetSchedulingFairness(Today));
        }

        [Fact]
        public void TotalShifts_Tracks()
        {
            Assert.Equal(0, _service.TotalShifts);
            _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            Assert.Equal(1, _service.TotalShifts);
        }

        [Fact]
        public void TotalAvailabilityRecords_Tracks()
        {
            _service.SetAvailability(1, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
            Assert.Equal(1, _service.TotalAvailabilityRecords);
        }

        [Fact]
        public void TotalSwapRequests_Tracks()
        {
            var shift = _service.ScheduleShift(1, Today.AddHours(9), Today.AddHours(17));
            _service.RequestSwap(1, shift.Id, SwapRequestType.Drop);
            Assert.Equal(1, _service.TotalSwapRequests);
        }

        [Fact]
        public void Shift_OverlapsWith_Works()
        {
            var shift = new Shift { StartTime = Today.AddHours(9), EndTime = Today.AddHours(17) };
            Assert.True(shift.OverlapsWith(Today.AddHours(12), Today.AddHours(20)));
            Assert.True(shift.OverlapsWith(Today.AddHours(5), Today.AddHours(10)));
            Assert.False(shift.OverlapsWith(Today.AddHours(17), Today.AddHours(22)));
            Assert.False(shift.OverlapsWith(Today.AddHours(5), Today.AddHours(9)));
        }

        [Fact]
        public void Shift_ShiftDate_IsStartDate()
        {
            var shift = new Shift { StartTime = Today.AddHours(22), EndTime = Today.AddDays(1).AddHours(2) };
            Assert.Equal(Today.Date, shift.ShiftDate);
        }
    }
}
