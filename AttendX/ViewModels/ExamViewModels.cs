namespace AttendX.ViewModels;

public class ExamResultViewModel
{
    public ExamTimetable Exam { get; set; } = null!;
    public ExamMark? Mark { get; set; }
}
