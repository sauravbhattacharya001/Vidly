using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a scheduled shift for a staff member.
    /// </summary>
    public class Shift
    {
        public int Id { get; set; }

        [Required]
        public int StaffId { get; set; }

        public string StaffName { get; set; }

        [Required]
        [DataType(DataType.DateTime)]
        public DateTime StartTime { get; set; }

        [Required]
        [DataType(DataType.DateTime)]
        public DateTime EndTime { get; set; }

        [Required]
        public ShiftType Type { get; set; }

        public bool IsConfirmed { get; set; }

        public bool IsCovered { get; set; }

        public int? CoveredByStaffId { get; set; }

        [StringLength(500)]
        public string Notes { get; set; }

        public double DurationHours =>
            Math.Max(0, (EndTime - StartTime).TotalHours);

        public DateTime ShiftDate => StartTime.Date;

        public bool OverlapsWith(DateTime otherStart, DateTime otherEnd)
        {
            return StartTime < otherEnd && EndTime > otherStart;
        }
    }

    /// <summary>
    /// Type of shift defining the time-of-day block a staff member works.
    /// </summary>
    public enum ShiftType
    {
        [Display(Name = "Morning")]
        Morning = 1,
        [Display(Name = "Afternoon")]
        Afternoon = 2,
        [Display(Name = "Evening")]
        Evening = 3,
        [Display(Name = "Full Day")]
        FullDay = 4,
        [Display(Name = "Split Shift")]
        Split = 5,
        [Display(Name = "On Call")]
        OnCall = 6
    }

    /// <summary>
    /// A staff member's recurring weekly availability window.
    /// Used by the scheduler to assign shifts within available hours.
    /// </summary>
    public class StaffAvailability
    {
        public int Id { get; set; }

        [Required]
        public int StaffId { get; set; }

        public string StaffName { get; set; }

        [Required]
        public DayOfWeek DayOfWeek { get; set; }

        [Required]
        public TimeSpan AvailableFrom { get; set; }

        [Required]
        public TimeSpan AvailableTo { get; set; }

        public bool IsActive { get; set; } = true;

        public double AvailableHours =>
            Math.Max(0, (AvailableTo - AvailableFrom).TotalHours);
    }

    /// <summary>
    /// A request from a staff member to swap, drop, or get coverage for a shift.
    /// Requires approval before the change takes effect.
    /// </summary>
    public class ShiftSwapRequest
    {
        public int Id { get; set; }

        [Required]
        public int RequestingStaffId { get; set; }

        public string RequestingStaffName { get; set; }

        [Required]
        public int ShiftId { get; set; }

        public int? TargetStaffId { get; set; }

        public string TargetStaffName { get; set; }

        [Required]
        public SwapRequestType RequestType { get; set; }

        [StringLength(500)]
        public string Reason { get; set; }

        public SwapRequestStatus Status { get; set; } = SwapRequestStatus.Pending;

        [DataType(DataType.DateTime)]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [DataType(DataType.DateTime)]
        public DateTime? ResolvedAt { get; set; }
    }

    /// <summary>
    /// The type of shift change being requested.
    /// </summary>
    public enum SwapRequestType
    {
        /// <summary>Exchange shifts with another staff member.</summary>
        Swap = 1,
        /// <summary>Drop a shift entirely (no replacement).</summary>
        Drop = 2,
        /// <summary>Request another staff member to cover the shift.</summary>
        Cover = 3
    }

    /// <summary>
    /// Approval status of a shift swap/drop/cover request.
    /// </summary>
    public enum SwapRequestStatus
    {
        /// <summary>Request is awaiting manager review.</summary>
        Pending = 1,
        /// <summary>Request was approved and the schedule updated.</summary>
        Approved = 2,
        /// <summary>Request was denied by a manager.</summary>
        Denied = 3,
        /// <summary>Request expired without being resolved.</summary>
        Expired = 4
    }

    /// <summary>
    /// Daily staffing coverage report showing shift counts, staff hours,
    /// and whether minimum coverage requirements are met.
    /// </summary>
    public class CoverageReport
    {
        public DateTime Date { get; set; }
        public int TotalShifts { get; set; }
        public int ConfirmedShifts { get; set; }
        public int UnconfirmedShifts { get; set; }
        public double TotalStaffHours { get; set; }
        public int MorningStaff { get; set; }
        public int AfternoonStaff { get; set; }
        public int EveningStaff { get; set; }
        public bool HasManagerOnDuty { get; set; }
        public bool MeetsMinimumCoverage { get; set; }
        public List<string> CoverageGaps { get; set; } = new List<string>();
    }

    /// <summary>
    /// Weekly summary of a staff member's scheduled shifts, hours worked,
    /// and compliance with minimum/maximum hour policies.
    /// </summary>
    public class StaffWeeklySummary
    {
        public int StaffId { get; set; }
        public string StaffName { get; set; }
        public StaffRole Role { get; set; }
        public DateTime WeekStart { get; set; }
        public int ShiftCount { get; set; }
        public double TotalHours { get; set; }
        public bool ExceedsMaxHours { get; set; }
        public bool BelowMinHours { get; set; }
        public List<Shift> Shifts { get; set; } = new List<Shift>();
    }
}
