using System;
using System.Collections.Generic;

namespace Vidly.ViewModels
{
    public class ScheduleWeekViewModel
    {
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public string WeekLabel => $"{WeekStart:MMM d} – {WeekEnd:MMM d, yyyy}";

        /// <summary>Shifts grouped by date, then by staff.</summary>
        public Dictionary<DateTime, List<Models.Shift>> ShiftsByDate { get; set; }
            = new Dictionary<DateTime, List<Models.Shift>>();

        public List<Models.StaffWeeklySummary> Summaries { get; set; }
            = new List<Models.StaffWeeklySummary>();

        public List<Models.StaffMember> Staff { get; set; }
            = new List<Models.StaffMember>();

        public List<Models.ShiftSwapRequest> PendingSwaps { get; set; }
            = new List<Models.ShiftSwapRequest>();

        public int TotalShifts { get; set; }
        public double TotalHours { get; set; }
    }

    public class ShiftCreateViewModel
    {
        public int StaffId { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public string StartTime { get; set; } = "09:00";
        public string EndTime { get; set; } = "17:00";
        public Models.ShiftType Type { get; set; } = Models.ShiftType.Regular;
        public string Notes { get; set; }
        public List<Models.StaffMember> AvailableStaff { get; set; }
            = new List<Models.StaffMember>();
    }

    public class SwapRequestViewModel
    {
        public int ShiftId { get; set; }
        public int TargetShiftId { get; set; }
        public string Reason { get; set; }
        public List<Models.Shift> AvailableShifts { get; set; }
            = new List<Models.Shift>();
        public Models.Shift CurrentShift { get; set; }
    }
}
