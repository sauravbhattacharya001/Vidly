using System;
using System.Collections.Generic;

namespace Vidly.ViewModels
{
    using RosterShift = Vidly.Models.Roster.Shift;
    using RosterShiftType = Vidly.Models.Roster.ShiftType;
    using RosterStaffMember = Vidly.Models.Roster.StaffMember;
    using RosterShiftSwapRequest = Vidly.Models.Roster.ShiftSwapRequest;
    using RosterStaffWeeklySummary = Vidly.Models.Roster.StaffWeeklySummary;

    public class ScheduleWeekViewModel
    {
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public string WeekLabel => $"{WeekStart:MMM d} – {WeekEnd:MMM d, yyyy}";

        /// <summary>Shifts grouped by date, then by staff.</summary>
        public Dictionary<DateTime, List<RosterShift>> ShiftsByDate { get; set; }
            = new Dictionary<DateTime, List<RosterShift>>();

        public List<RosterStaffWeeklySummary> Summaries { get; set; }
            = new List<RosterStaffWeeklySummary>();

        public List<RosterStaffMember> Staff { get; set; }
            = new List<RosterStaffMember>();

        public List<RosterShiftSwapRequest> PendingSwaps { get; set; }
            = new List<RosterShiftSwapRequest>();

        public int TotalShifts { get; set; }
        public double TotalHours { get; set; }
    }

    public class ShiftCreateViewModel
    {
        public int StaffId { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public string StartTime { get; set; } = "09:00";
        public string EndTime { get; set; } = "17:00";
        public RosterShiftType Type { get; set; } = RosterShiftType.Regular;
        public string Notes { get; set; }
        public List<RosterStaffMember> AvailableStaff { get; set; }
            = new List<RosterStaffMember>();
    }

    public class SwapRequestViewModel
    {
        public int ShiftId { get; set; }
        public int TargetShiftId { get; set; }
        public string Reason { get; set; }
        public List<RosterShift> AvailableShifts { get; set; }
            = new List<RosterShift>();
        public RosterShift CurrentShift { get; set; }
    }
}
