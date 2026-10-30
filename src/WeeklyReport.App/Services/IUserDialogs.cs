namespace WeeklyReport.App.Services;

public interface IUserDialogs
{
    bool Confirm(string message, string title);
    void Error(string message, string title);
}
