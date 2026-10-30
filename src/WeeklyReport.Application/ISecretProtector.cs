namespace WeeklyReport.Application;

public interface ISecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedValue);
}
