using System.Globalization;

namespace FitEdu.Web.Extensions;

/// <summary>
/// The "how many active courses" line of a plan (Pricing cards and the Billing plan card). One place
/// holds the unlimited threshold and the one-course case, so neither page can print "Up to 1 courses".
/// </summary>
public static class CourseLimitText
{
    /// <summary>A plan whose MaxCourses reaches this counts as unlimited (Pro stores int.MaxValue).</summary>
    public const int UnlimitedThreshold = 1000;

    private const int SingleCourse = 1;

    /// <summary>
    /// Picks the page's own wording for a course limit: the unlimited label, the singular label for
    /// exactly one course, or the counted format ("Up to {0} …") for everything else.
    /// </summary>
    public static string Describe(int maxCourses, string unlimitedLabel, string singleCourseLabel, string countFormat)
    {
        if (maxCourses >= UnlimitedThreshold)
            return unlimitedLabel;

        if (maxCourses == SingleCourse)
            return singleCourseLabel;

        return string.Format(CultureInfo.CurrentCulture, countFormat, maxCourses);
    }
}
