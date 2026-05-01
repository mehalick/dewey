namespace Dewey.Web.Shared;

public static class Errors
{
    // Friendlier message for surfaces. Stack/ex.Message goes to console for
    // dev visibility but never leaks to the UI verbatim.
    public static string Friendly(Exception ex, string fallback = "Something went wrong. Please try again.")
    {
        Console.Error.WriteLine(ex);
        return ex switch
        {
            HttpRequestException => "Couldn't reach the server. Check your connection.",
            TaskCanceledException => "The request took too long. Try again.",
            _ => fallback,
        };
    }
}
