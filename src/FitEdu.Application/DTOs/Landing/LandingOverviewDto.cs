namespace FitEdu.Application.DTOs;

/// <summary>
/// The public home page's numbers and showcase: published courses and their lessons, the exercise
/// library size, the number of members, the published self-checks (assessments), the most-enrolled
/// courses, and whether an admin uploaded a custom hero photo (served by <c>GET api/site/background-image</c>).
/// </summary>
public record LandingOverviewDto(
    int CourseCount,
    int LessonCount,
    int ExerciseCount,
    int MemberCount,
    int AssessmentCount,
    IReadOnlyList<CourseListItemDto> FeaturedCourses,
    bool HasCustomHeroImage
);
