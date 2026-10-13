namespace FitEdu.Application.Common;

/// <summary>
/// The user id the course services get for a visitor who is not signed in. No enrolment, lesson
/// completion or review belongs to it, so every course reads as not joined and without progress.
/// </summary>
public static class AnonymousUser
{
    public const string Id = "";
}
