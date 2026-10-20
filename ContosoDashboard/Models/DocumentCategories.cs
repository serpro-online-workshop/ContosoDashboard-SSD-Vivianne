namespace ContosoDashboard.Models;

public static class DocumentCategories
{
    public static readonly IReadOnlyList<string> All =
    [
        "Project Documents",
        "Team Resources",
        "Personal Files",
        "Reports",
        "Presentations",
        "Other"
    ];

    public static bool IsValid(string? category) =>
        category is not null && All.Contains(category, StringComparer.Ordinal);
}