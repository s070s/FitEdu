namespace FitEdu.Domain.Identity;

/// <summary>
/// The names of the Identity roles the site is built on, so a role is never written as a string
/// in the services. The web project's navigation (<c>NavItems</c>) reuses these two.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Student = "Student";
}
