using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IStaffScheduleRepository
    {
        // Staff
        IReadOnlyList<StaffMember> GetAllStaff();
        StaffMember GetStaffById(int id);
        void AddStaff(StaffMember staff);

        // Shifts
        IReadOnlyList<Shift> GetShiftsForWeek(DateTime weekStart);
        IReadOnlyList<Shift> GetShiftsForStaff(int staffId);
        Shift GetShiftById(int id);
        void AddShift(Shift shift);
        void RemoveShift(int id);

        // Swap Requests
        IReadOnlyList<ShiftSwapRequest> GetPendingSwaps();
        void RequestSwap(ShiftSwapRequest request);
        void ApproveSwap(int swapId, string managerNote);
        void DenySwap(int swapId, string managerNote);

        // Summaries
        List<StaffWeeklySummary> GetWeeklySummaries(DateTime weekStart);
    }

    public class InMemoryStaffScheduleRepository : IStaffScheduleRepository
    {
        private static readonly List<StaffMember> _staff = new List<StaffMember>();
        private static readonly List<Shift> _shifts = new List<Shift>();
        private static readonly List<ShiftSwapRequest> _swaps = new List<ShiftSwapRequest>();
        private static int _nextStaffId = 1;
        private static int _nextShiftId = 1;
        private static int _nextSwapId = 1;
        private static bool _seeded;

        public InMemoryStaffScheduleRepository()
        {
            if (!_seeded)
            {
                _seeded = true;
                Seed();
            }
        }

        private void Seed()
        {
            var staff = new[]
            {
                new StaffMember { Name = "Alex Rivera", Role = "Manager", Email = "alex@vidly.com", MaxHoursPerWeek = 40 },
                new StaffMember { Name = "Jordan Lee", Role = "Cashier", Email = "jordan@vidly.com", MaxHoursPerWeek = 32 },
                new StaffMember { Name = "Sam Patel", Role = "Stock Clerk", Email = "sam@vidly.com", MaxHoursPerWeek = 24 },
                new StaffMember { Name = "Casey Kim", Role = "Cashier", Email = "casey@vidly.com", MaxHoursPerWeek = 40 },
                new StaffMember { Name = "Taylor Brooks", Role = "Manager", Email = "taylor@vidly.com", MaxHoursPerWeek = 40,
                    DaysOff = new List<DayOfWeek> { DayOfWeek.Sunday } }
            };

            foreach (var s in staff) AddStaff(s);

            // Seed shifts for this week
            var monday = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1);
            if (monday > DateTime.Today) monday = monday.AddDays(-7);

            var shiftDefs = new[]
            {
                (staffIdx: 0, dayOffset: 0, start: "08:00", end: "16:00", type: ShiftType.Opening),
                (staffIdx: 1, dayOffset: 0, start: "12:00", end: "20:00", type: ShiftType.Closing),
                (staffIdx: 2, dayOffset: 0, start: "10:00", end: "14:00", type: ShiftType.Regular),
                (staffIdx: 3, dayOffset: 1, start: "08:00", end: "16:00", type: ShiftType.Opening),
                (staffIdx: 0, dayOffset: 1, start: "12:00", end: "20:00", type: ShiftType.Closing),
                (staffIdx: 4, dayOffset: 2, start: "08:00", end: "16:00", type: ShiftType.Opening),
                (staffIdx: 1, dayOffset: 2, start: "16:00", end: "22:00", type: ShiftType.Closing),
                (staffIdx: 2, dayOffset: 3, start: "09:00", end: "13:00", type: ShiftType.Regular),
                (staffIdx: 3, dayOffset: 3, start: "13:00", end: "21:00", type: ShiftType.Closing),
                (staffIdx: 0, dayOffset: 4, start: "08:00", end: "16:00", type: ShiftType.Opening),
                (staffIdx: 4, dayOffset: 4, start: "12:00", end: "20:00", type: ShiftType.Regular),
                (staffIdx: 1, dayOffset: 5, start: "10:00", end: "18:00", type: ShiftType.Regular),
                (staffIdx: 2, dayOffset: 5, start: "14:00", end: "22:00", type: ShiftType.Closing),
            };

            foreach (var (staffIdx, dayOffset, start, end, type) in shiftDefs)
            {
                var s = _staff[staffIdx];
                AddShift(new Shift
                {
                    StaffId = s.Id,
                    StaffName = s.Name,
                    StaffRole = s.Role,
                    Date = monday.AddDays(dayOffset),
                    StartTime = TimeSpan.Parse(start),
                    EndTime = TimeSpan.Parse(end),
                    Type = type
                });
            }
        }

        // Staff
        public IReadOnlyList<StaffMember> GetAllStaff() => _staff.Where(s => s.IsActive).ToList().AsReadOnly();
        public StaffMember GetStaffById(int id) => _staff.FirstOrDefault(s => s.Id == id);
        public void AddStaff(StaffMember staff) { staff.Id = _nextStaffId++; _staff.Add(staff); }

        // Shifts
        public IReadOnlyList<Shift> GetShiftsForWeek(DateTime weekStart)
        {
            var end = weekStart.AddDays(7);
            return _shifts.Where(s => s.Date >= weekStart && s.Date < end)
                          .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
                          .ToList().AsReadOnly();
        }

        public IReadOnlyList<Shift> GetShiftsForStaff(int staffId) =>
            _shifts.Where(s => s.StaffId == staffId).OrderBy(s => s.Date).ToList().AsReadOnly();

        public Shift GetShiftById(int id) => _shifts.FirstOrDefault(s => s.Id == id);

        public void AddShift(Shift shift) { shift.Id = _nextShiftId++; _shifts.Add(shift); }

        public void RemoveShift(int id) => _shifts.RemoveAll(s => s.Id == id);

        // Swaps
        public IReadOnlyList<ShiftSwapRequest> GetPendingSwaps() =>
            _swaps.Where(s => s.Status == SwapStatus.Pending).ToList().AsReadOnly();

        public void RequestSwap(ShiftSwapRequest request) { request.Id = _nextSwapId++; _swaps.Add(request); }

        public void ApproveSwap(int swapId, string managerNote)
        {
            var swap = _swaps.FirstOrDefault(s => s.Id == swapId);
            if (swap == null) return;

            swap.Status = SwapStatus.Approved;
            swap.ManagerNote = managerNote;

            // Perform the actual swap
            var shiftA = GetShiftById(swap.RequestingShiftId);
            var shiftB = GetShiftById(swap.TargetShiftId);
            if (shiftA != null && shiftB != null)
            {
                var tmpId = shiftA.StaffId;
                var tmpName = shiftA.StaffName;
                var tmpRole = shiftA.StaffRole;

                shiftA.StaffId = shiftB.StaffId;
                shiftA.StaffName = shiftB.StaffName;
                shiftA.StaffRole = shiftB.StaffRole;

                shiftB.StaffId = tmpId;
                shiftB.StaffName = tmpName;
                shiftB.StaffRole = tmpRole;
            }
        }

        public void DenySwap(int swapId, string managerNote)
        {
            var swap = _swaps.FirstOrDefault(s => s.Id == swapId);
            if (swap == null) return;
            swap.Status = SwapStatus.Denied;
            swap.ManagerNote = managerNote;
        }

        // Summaries
        public List<StaffWeeklySummary> GetWeeklySummaries(DateTime weekStart)
        {
            var weekShifts = GetShiftsForWeek(weekStart);
            return _staff.Where(s => s.IsActive).Select(s =>
            {
                var staffShifts = weekShifts.Where(sh => sh.StaffId == s.Id).ToList();
                return new StaffWeeklySummary
                {
                    StaffId = s.Id,
                    StaffName = s.Name,
                    Role = s.Role,
                    MaxHoursPerWeek = s.MaxHoursPerWeek,
                    TotalHours = staffShifts.Sum(sh => sh.Hours),
                    ShiftCount = staffShifts.Count,
                    Shifts = staffShifts
                };
            }).ToList();
        }
    }
}
