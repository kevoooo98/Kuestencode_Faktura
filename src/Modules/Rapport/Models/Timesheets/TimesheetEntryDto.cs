namespace Kuestencode.Rapport.Models.Timesheets;

/// <summary>
/// Single entry in a timesheet.
/// </summary>
public class TimesheetEntryDto
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public string? EmployeeName { get; set; }
}
