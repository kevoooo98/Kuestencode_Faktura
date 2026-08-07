namespace Kuestencode.Rapport.Models.Timesheets;

/// <summary>
/// Timesheet export data.
/// </summary>
public class TimesheetDto
{
    public TimesheetCustomerInfoDto Customer { get; set; } = new();
    public TimesheetProjectInfoDto? Project { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Title { get; set; } = "Tätigkeitsnachweis";
    public List<TimesheetProjectGroupDto> Groups { get; set; } = new();
    public decimal TotalHours { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? TotalAmount { get; set; }
    public bool ShowEmployeeColumn { get; set; }
}
