using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a staff member who can be scheduled for shifts.
    /// </summary>
    public class StaffMember
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Role { get; set; } // e.g. "Cashier", "Manager", "Stock Clerk"
        public string Email { get; set; }
        public string Phone { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime HireDate { get; set; } = DateTime.Now;
        public int MaxHoursPerWeek { get; set; } = 40;

        /// <summary>Days the staff member prefers NOT to work.</summary>
        public List<DayOfWeek> DaysOff { get; set; } = new List<DayOfWeek>();
    }

    /// <summary>
    /// A single shift assignment for a staff member.
    /// </summary>
    public class Shift
    {
        public int Id { get; set; }
        public int StaffId { get; set; }
        public string StaffName { get; set; }
        public string StaffRole { get; set; }
        public DateTime Date { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public ShiftType Type { get; set; } = ShiftType.Regular;
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Duration in hours.</summary>
        public double Hours => (EndTime - StartTime).TotalHours;

        /// <summary>Formatted time range for display.</summary>
        public string TimeRange => $"{FormatTime(StartTime)} – {FormatTime(EndTime)}";

        private static string FormatTime(TimeSpan t) =>
            DateTime.Today.Add(t).ToString("h:mm tt");
    }

    public enum ShiftType
    {
        Regular,
        Opening,
        Closing,
        Cover,
        Training
    }

    /// <summary>
    /// A request from one staff member to swap a shift with another.
    /// </summary>
    public class ShiftSwapRequest
    {
        public int Id { get; set; }
        public int RequestingShiftId { get; set; }
        public int TargetShiftId { get; set; }
        public string RequestingStaffName { get; set; }
        public string TargetStaffName { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.Now;
        public SwapStatus Status { get; set; } = SwapStatus.Pending;
        public string Reason { get; set; }
        public string ManagerNote { get; set; }
    }

    public enum SwapStatus
    {
        Pending,
        Approved,
        Denied
    }

    /// <summary>
    /// Weekly schedule summary for a staff member.
    /// </summary>
    public class StaffWeeklySummary
    {
        public int StaffId { get; set; }
        public string StaffName { get; set; }
        public string Role { get; set; }
        public int MaxHoursPerWeek { get; set; }
        public double TotalHours { get; set; }
        public int ShiftCount { get; set; }
        public bool IsOverScheduled => TotalHours > MaxHoursPerWeek;
        public List<Shift> Shifts { get; set; } = new List<Shift>();
    }
}
