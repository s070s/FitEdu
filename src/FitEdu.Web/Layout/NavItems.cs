using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Layout;

/// <summary>
/// The single source of the site navigation, shared by <see cref="NavMenu"/> (header and menu panel)
/// and <see cref="BottomNav"/> (mobile tab bar). List order is display order everywhere.
/// </summary>
public static class NavItems
{
    public const string AdminRole = "Admin";
    public const string StudentRole = "Student";

    public static readonly IReadOnlyList<NavItem> Guest =
    [
        new("", AppIcons.Home, () => NavMenuRes.Home, InHeader: true, InBottomNav: true),
        // The catalogue is public; it stays off the tab bar so guests keep four tabs.
        new("courses", AppIcons.Course, () => NavMenuRes.Courses, InHeader: true, ShortLabel: () => NavMenuRes.CoursesShort),
        new("exercises", AppIcons.Exercise, () => NavMenuRes.Exercises, InHeader: true, InBottomNav: true),
        new("blog", AppIcons.BlogArticle, () => NavMenuRes.Blog, InHeader: true),
        new("pricing", AppIcons.Pricing, () => NavMenuRes.Pricing, InHeader: true, InBottomNav: true, ShortLabel: () => NavMenuRes.PricingShort),
        new("login", AppIcons.Login, () => NavMenuRes.Login, InHeader: true, InBottomNav: true),
        new("register", AppIcons.Register, () => NavMenuRes.Register, InHeader: true, Highlight: true),
    ];

    public static readonly IReadOnlyList<NavItem> Student =
    [
        new("", AppIcons.Home, () => NavMenuRes.Home, InHeader: true, InBottomNav: true),
        new("courses", AppIcons.Course, () => NavMenuRes.Courses, InHeader: true, InBottomNav: true, ShortLabel: () => NavMenuRes.CoursesShort),
        new("exercises", AppIcons.Exercise, () => NavMenuRes.Exercises, InHeader: true, InBottomNav: true),
        new("paths", AppIcons.Path, () => NavMenuRes.Paths, InHeader: true, InBottomNav: true, ShortLabel: () => NavMenuRes.PathsShort),
        new("sessions", AppIcons.Session, () => NavMenuRes.Sessions, InHeader: true, ShortLabel: () => NavMenuRes.SessionsShort),
        new("assessments", AppIcons.Assessment, () => NavMenuRes.Assessments),
        new("my-certificates", AppIcons.Certificate, () => NavMenuRes.Certificates),
        new("chat", AppIcons.Chat, () => NavMenuRes.Chat),
        new("blog", AppIcons.BlogArticle, () => NavMenuRes.Blog),
        new("pricing", AppIcons.Pricing, () => NavMenuRes.Pricing),
        new("billing", AppIcons.Billing, () => NavMenuRes.Billing),
        new("profile", AppIcons.Profile, () => NavMenuRes.Profile, InBottomNav: true),
    ];

    public static readonly IReadOnlyList<NavItem> Admin =
    [
        new("admin/users", AppIcons.Users, () => NavMenuRes.Users, InHeader: true, InBottomNav: true),
        new("admin/courses", AppIcons.Course, () => NavMenuRes.Courses, InHeader: true, InBottomNav: true, ShortLabel: () => NavMenuRes.CoursesShort),
        new("admin/assessments", AppIcons.Assessment, () => NavMenuRes.Assessments, InHeader: true, InBottomNav: true, ShortLabel: () => NavMenuRes.AssessmentsShort),
        new("admin/analytics", AppIcons.Analytics, () => NavMenuRes.Analytics, InHeader: true, InBottomNav: true),
        new("admin/blog", AppIcons.BlogArticle, () => NavMenuRes.Blog, InHeader: true, InBottomNav: true),
        new("admin/categories", AppIcons.Categories, () => NavMenuRes.Categories),
        new("admin/testimonials", AppIcons.Testimonials, () => NavMenuRes.Testimonials),
        new("admin/course-reviews", AppIcons.Reviews, () => NavMenuRes.CourseReviews),
        new("admin/paths", AppIcons.Path, () => NavMenuRes.Paths),
        new("admin/sessions", AppIcons.Session, () => NavMenuRes.Sessions),
        new("chat", AppIcons.Chat, () => NavMenuRes.Chat),
    ];

    /// <summary>
    /// The list for the signed-in user. Admin wins over Student; a signed-in user with neither role
    /// gets no links (never the guest Login/Register links).
    /// </summary>
    public static IReadOnlyList<NavItem> ForUser(ClaimsPrincipal user)
    {
        if (user.IsInRole(AdminRole))
        {
            return Admin;
        }

        if (user.IsInRole(StudentRole))
        {
            return Student;
        }

        var isSignedIn = user.Identity?.IsAuthenticated == true;
        return isSignedIn ? [] : Guest;
    }

    /// <summary>The list for the cascading authentication state (guest links when there is none).</summary>
    public static async Task<IReadOnlyList<NavItem>> ForAuthenticationStateAsync(Task<AuthenticationState>? authenticationStateTask)
    {
        if (authenticationStateTask is null)
        {
            return Guest;
        }

        var state = await authenticationStateTask;
        return ForUser(state.User);
    }
}
