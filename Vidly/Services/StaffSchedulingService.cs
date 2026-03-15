using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages staff scheduling: shift CRUD, availability tracking,
    /// conflict detection, coverage analysis, swap requests, and
    /// weekly hour enforcement.
    /// </summary>
    public class StaffSchedulingService
    {
        private readonly List<Shift> _shifts = new List<Shift>();
        private readonly List<StaffAvailability> _availability = new List<StaffAvailability>();
        private readonly List<ShiftSwapRequest> _swapRequests = new List<ShiftSwapRequest>();
        private readonly List<StaffMember> _staff;
        private readonly IClock _clock;

        private int _nextShiftId = 1;
        private int _nextAvailId = 1;
        private int _nextSwapId = 1;

        public double MaxWeeklyHours { get; set; } = 40.0;
        public int MinimumStaffPerPeriod { get; set; } = 2;
        public double MinRestHoursBetweenShifts { get; set; } = 8.0;

        public StaffSchedulingService(IEnumerable<StaffMember> staff,
            IClock clock = null)
        {
            _staff = staff?.ToList()
                ?? throw new ArgumentNullException(nameof(staff));
            _clock = clock ?? new SystemClock();
        }

        // ── Shift CRUD ──────────────────────────────────────

        public Shift ScheduleShift(int staffId, DateTime start, DateTime end,
            ShiftType type = ShiftType.Morning, string notes = null)
        {
            if (start >= end)
                throw new ArgumentException("Shift start must be before end.");
            if ((end - start).TotalHours > 12)
                throw new ArgumentException("Shift cannot exceed 12 hours.");
            if ((end - start).TotalMinutes < 30)
                throw new ArgumentException("Shift must be at least 30 minutes.");

            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null)
                throw new ArgumentException("Staff member not found.", nameof(staffId));
            if (!member.IsActive)
                throw new InvalidOperationException("Cannot schedule inactive staff.");

            var conflicts = GetConflictingShifts(staffId, start, end);
            if (conflicts.Any())
                throw new InvalidOperationException(
                    $"Shift conflicts with existing shift(s): " +
                    string.Join(", ", conflicts.Select(c => $"#{c.Id} ({c.StartTime:HH:mm}-{c.EndTime:HH:mm})")));

            var restViolation = CheckRestViolation(staffId, start, end);
            if (restViolation != null)
                throw new InvalidOperationException(restViolation);

            var shift = new Shift
            {
                Id = _nextShiftId++,
                StaffId = staffId,
                StaffName = member.Name,
                StartTime = start,
                EndTime = end,
                Type = type,
                Notes = notes
            };
            _shifts.Add(shift);
            return shift;
        }

        public Shift GetShift(int shiftId)
        {
            return _shifts.FirstOrDefault(s => s.Id == shiftId);
        }

        public IReadOnlyList<Shift> GetShiftsForDate(DateTime date)
        {
            return _shifts
                .Where(s => s.ShiftDate == date.Date)
                .OrderBy(s => s.StartTime)
                .ToList();
        }

        public IReadOnlyList<Shift> GetShiftsForStaff(int staffId,
            DateTime? from = null, DateTime? to = null)
        {
            var query = _shifts.Where(s => s.StaffId == staffId);
            if (from.HasValue) query = query.Where(s => s.EndTime > from.Value);
            if (to.HasValue) query = query.Where(s => s.StartTime < to.Value);
            return query.OrderBy(s => s.StartTime).ToList();
        }

        public bool CancelShift(int shiftId)
        {
            var shift = _shifts.FirstOrDefault(s => s.Id == shiftId);
            if (shift == null) return false;
            _shifts.Remove(shift);
            return true;
        }

        public bool ConfirmShift(int shiftId)
        {
            var shift = _shifts.FirstOrDefault(s => s.Id == shiftId);
            if (shift == null) return false;
            shift.IsConfirmed = true;
            return true;
        }

        // ── Conflict Detection ──────────────────────────────

        public IReadOnlyList<Shift> GetConflictingShifts(int staffId,
            DateTime start, DateTime end)
        {
            return _shifts
                .Where(s => s.StaffId == staffId && s.OverlapsWith(start, end))
                .ToList();
        }

        public string CheckRestViolation(int staffId, DateTime start, DateTime end)
        {
            var staffShifts = _shifts
                .Where(s => s.StaffId == staffId)
                .OrderBy(s => s.StartTime)
                .ToList();

            foreach (var existing in staffShifts)
            {
                if (existing.EndTime <= start)
                {
                    var gap = (start - existing.EndTime).TotalHours;
                    if (gap < MinRestHoursBetweenShifts && gap > 0)
                        return $"Only {gap:F1}h rest between shift #{existing.Id} " +
                               $"(ends {existing.EndTime:HH:mm}) and new shift " +
                               $"(starts {start:HH:mm}). Minimum: {MinRestHoursBetweenShifts}h.";
                }
                if (existing.StartTime >= end)
                {
                    var gap = (existing.StartTime - end).TotalHours;
                    if (gap < MinRestHoursBetweenShifts && gap > 0)
                        return $"Only {gap:F1}h rest between new shift " +
                               $"(ends {end:HH:mm}) and shift #{existing.Id} " +
                               $"(starts {existing.StartTime:HH:mm}). Minimum: {MinRestHoursBetweenShifts}h.";
                }
            }
            return null;
        }

        // ── Availability ────────────────────────────────────

        public StaffAvailability SetAvailability(int staffId, DayOfWeek day,
            TimeSpan from, TimeSpan to)
        {
            if (from >= to)
                throw new ArgumentException("Available-from must be before available-to.");

            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null)
                throw new ArgumentException("Staff member not found.", nameof(staffId));

            _availability.RemoveAll(a => a.StaffId == staffId && a.DayOfWeek == day);

            var avail = new StaffAvailability
            {
                Id = _nextAvailId++,
                StaffId = staffId,
                StaffName = member.Name,
                DayOfWeek = day,
                AvailableFrom = from,
                AvailableTo = to
            };
            _availability.Add(avail);
            return avail;
        }

        public IReadOnlyList<StaffAvailability> GetAvailability(int staffId)
        {
            return _availability
                .Where(a => a.StaffId == staffId && a.IsActive)
                .OrderBy(a => a.DayOfWeek)
                .ToList();
        }

        public IReadOnlyList<StaffAvailability> GetAvailableStaffForDay(DayOfWeek day)
        {
            return _availability
                .Where(a => a.DayOfWeek == day && a.IsActive)
                .OrderBy(a => a.AvailableFrom)
                .ToList();
        }

        public bool IsStaffAvailable(int staffId, DateTime start, DateTime end)
        {
            var dayAvail = _availability
                .Where(a => a.StaffId == staffId && a.DayOfWeek == start.DayOfWeek && a.IsActive)
                .ToList();

            if (!dayAvail.Any()) return true;

            var shiftStart = start.TimeOfDay;
            var shiftEnd = end.TimeOfDay;
            if (shiftEnd <= shiftStart) shiftEnd = new TimeSpan(23, 59, 59);

            return dayAvail.Any(a => a.AvailableFrom <= shiftStart && a.AvailableTo >= shiftEnd);
        }

        // ── Coverage Analysis ───────────────────────────────

        public CoverageReport GetCoverageReport(DateTime date)
        {
            var dayShifts = GetShiftsForDate(date);
            var gaps = new List<string>();

            var morning = dayShifts.Count(s => s.StartTime.Hour < 12 && s.Type != ShiftType.OnCall);
            var afternoon = dayShifts.Count(s => s.StartTime.Hour >= 12 && s.StartTime.Hour < 17 && s.Type != ShiftType.OnCall);
            var evening = dayShifts.Count(s => s.StartTime.Hour >= 17 && s.Type != ShiftType.OnCall);

            if (morning < MinimumStaffPerPeriod)
                gaps.Add($"Morning: {morning}/{MinimumStaffPerPeriod} staff");
            if (afternoon < MinimumStaffPerPeriod)
                gaps.Add($"Afternoon: {afternoon}/{MinimumStaffPerPeriod} staff");
            if (evening < MinimumStaffPerPeriod)
                gaps.Add($"Evening: {evening}/{MinimumStaffPerPeriod} staff");

            var hasManager = dayShifts.Any(s =>
            {
                var member = _staff.FirstOrDefault(m => m.Id == s.StaffId);
                return member?.Role == StaffRole.Manager || member?.Role == StaffRole.ShiftLead;
            });

            if (!hasManager && dayShifts.Any())
                gaps.Add("No manager or shift lead on duty");

            return new CoverageReport
            {
                Date = date.Date,
                TotalShifts = dayShifts.Count,
                ConfirmedShifts = dayShifts.Count(s => s.IsConfirmed),
                UnconfirmedShifts = dayShifts.Count(s => !s.IsConfirmed),
                TotalStaffHours = dayShifts.Sum(s => s.DurationHours),
                MorningStaff = morning,
                AfternoonStaff = afternoon,
                EveningStaff = evening,
                HasManagerOnDuty = hasManager,
                MeetsMinimumCoverage = !gaps.Any(g => g.Contains("Morning") || g.Contains("Afternoon") || g.Contains("Evening")),
                CoverageGaps = gaps
            };
        }

        public IReadOnlyList<CoverageReport> GetWeeklyCoverage(DateTime weekStart)
        {
            return Enumerable.Range(0, 7)
                .Select(i => GetCoverageReport(weekStart.Date.AddDays(i)))
                .ToList();
        }

        public IReadOnlyList<CoverageReport> GetUnderstaffedDays(DateTime from, DateTime to)
        {
            var days = new List<CoverageReport>();
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                var report = GetCoverageReport(d);
                if (report.CoverageGaps.Any()) days.Add(report);
            }
            return days;
        }

        // ── Weekly Hours ────────────────────────────────────

        public StaffWeeklySummary GetWeeklySummary(int staffId, DateTime weekStart)
        {
            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null)
                throw new ArgumentException("Staff member not found.", nameof(staffId));

            var weekEnd = weekStart.Date.AddDays(7);
            var shifts = GetShiftsForStaff(staffId, weekStart.Date, weekEnd);
            var totalHours = shifts.Sum(s => s.DurationHours);

            return new StaffWeeklySummary
            {
                StaffId = staffId,
                StaffName = member.Name,
                Role = member.Role,
                WeekStart = weekStart.Date,
                ShiftCount = shifts.Count,
                TotalHours = totalHours,
                ExceedsMaxHours = totalHours > MaxWeeklyHours,
                BelowMinHours = totalHours > 0 && totalHours < 16,
                Shifts = shifts.ToList()
            };
        }

        public IReadOnlyList<StaffWeeklySummary> GetAllWeeklySummaries(DateTime weekStart)
        {
            return _staff
                .Where(s => s.IsActive)
                .Select(s => GetWeeklySummary(s.Id, weekStart))
                .OrderByDescending(s => s.TotalHours)
                .ToList();
        }

        public IReadOnlyList<StaffWeeklySummary> GetOvertimeRisk(DateTime weekStart, double additionalHours = 0)
        {
            return GetAllWeeklySummaries(weekStart)
                .Where(s => s.TotalHours + additionalHours > MaxWeeklyHours)
                .ToList();
        }

        // ── Shift Swap Requests ─────────────────────────────

        public ShiftSwapRequest RequestSwap(int staffId, int shiftId,
            SwapRequestType type, int? targetStaffId = null, string reason = null)
        {
            var shift = GetShift(shiftId);
            if (shift == null)
                throw new ArgumentException("Shift not found.", nameof(shiftId));
            if (shift.StaffId != staffId)
                throw new InvalidOperationException("Can only request swap for your own shift.");

            var member = _staff.FirstOrDefault(s => s.Id == staffId);
            if (member == null)
                throw new ArgumentException("Staff member not found.", nameof(staffId));

            StaffMember target = null;
            if (targetStaffId.HasValue)
            {
                target = _staff.FirstOrDefault(s => s.Id == targetStaffId.Value);
                if (target == null)
                    throw new ArgumentException("Target staff member not found.");
            }

            var request = new ShiftSwapRequest
            {
                Id = _nextSwapId++,
                RequestingStaffId = staffId,
                RequestingStaffName = member.Name,
                ShiftId = shiftId,
                TargetStaffId = targetStaffId,
                TargetStaffName = target?.Name,
                RequestType = type,
                Reason = reason
            };
            _swapRequests.Add(request);
            return request;
        }

        public bool ApproveSwap(int requestId, int? coveringStaffId = null)
        {
            var request = _swapRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null || request.Status != SwapRequestStatus.Pending)
                return false;

            var shift = GetShift(request.ShiftId);
            if (shift == null) return false;

            var coverer = coveringStaffId ?? request.TargetStaffId;
            if (coverer.HasValue)
            {
                var member = _staff.FirstOrDefault(s => s.Id == coverer.Value);
                if (member == null || !member.IsActive) return false;

                var conflicts = GetConflictingShifts(coverer.Value, shift.StartTime, shift.EndTime);
                if (conflicts.Any()) return false;

                shift.IsCovered = true;
                shift.CoveredByStaffId = coverer.Value;
            }
            else if (request.RequestType == SwapRequestType.Drop)
            {
                shift.IsCovered = false;
            }

            request.Status = SwapRequestStatus.Approved;
            request.ResolvedAt = _clock.Now;
            return true;
        }

        public bool DenySwap(int requestId)
        {
            var request = _swapRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null || request.Status != SwapRequestStatus.Pending)
                return false;

            request.Status = SwapRequestStatus.Denied;
            request.ResolvedAt = _clock.Now;
            return true;
        }

        public IReadOnlyList<ShiftSwapRequest> GetPendingSwapRequests()
        {
            return _swapRequests
                .Where(r => r.Status == SwapRequestStatus.Pending)
                .OrderBy(r => r.CreatedAt)
                .ToList();
        }

        public IReadOnlyList<ShiftSwapRequest> GetSwapHistory(int staffId)
        {
            return _swapRequests
                .Where(r => r.RequestingStaffId == staffId || r.TargetStaffId == staffId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
        }

        // ── Auto-Scheduling ─────────────────────────────────

        public IReadOnlyList<(StaffMember Staff, double RemainingHours, bool IsAvailable)>
            SuggestStaffForShift(DateTime start, DateTime end, DateTime weekStart)
        {
            var shiftDuration = (end - start).TotalHours;
            var suggestions = new List<(StaffMember Staff, double RemainingHours, bool IsAvailable)>();

            foreach (var member in _staff.Where(s => s.IsActive))
            {
                var conflicts = GetConflictingShifts(member.Id, start, end);
                if (conflicts.Any()) continue;

                var restViolation = CheckRestViolation(member.Id, start, end);
                if (restViolation != null) continue;

                var weekly = GetWeeklySummary(member.Id, weekStart);
                var remaining = MaxWeeklyHours - weekly.TotalHours;
                if (remaining < shiftDuration) continue;

                var isAvailable = IsStaffAvailable(member.Id, start, end);
                suggestions.Add((member, remaining, isAvailable));
            }

            return suggestions
                .OrderByDescending(s => s.IsAvailable)
                .ThenByDescending(s => s.RemainingHours)
                .ToList();
        }

        // ── Scheduling Fairness ─────────────────────────────

        public double GetSchedulingFairness(DateTime weekStart)
        {
            var summaries = GetAllWeeklySummaries(weekStart)
                .Where(s => s.TotalHours > 0)
                .ToList();

            if (summaries.Count < 2) return 0.0;

            var avg = summaries.Average(s => s.TotalHours);
            var variance = summaries.Average(s => Math.Pow(s.TotalHours - avg, 2));
            return Math.Sqrt(variance);
        }

        // ── Counts ──────────────────────────────────────────

        public int TotalShifts => _shifts.Count;
        public int TotalAvailabilityRecords => _availability.Count;
        public int TotalSwapRequests => _swapRequests.Count;
    }
}
