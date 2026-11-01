namespace FlatOut4SaveEditor.Services;

// "today at 9:51 AM", "yesterday at 3:44 PM", "4 days ago", or the date once it is a week old.
public static class RelativeTime
{
    public static string Describe(DateTime local)
    {
        int days = (DateTime.Now.Date - local.Date).Days;
        return days switch
        {
            <= 0 => $"today at {local:t}",
            1 => $"yesterday at {local:t}",
            < 7 => $"{days} days ago",
            _ => $"{local:d}"
        };
    }
}
