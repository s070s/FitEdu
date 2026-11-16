namespace FitEdu.Application.Common;

/// <summary>
/// Which spellings of a question type belong to which kind. Seeds write "choice", "multiselect" and
/// "rating"; older schemas wrote "single-select", "multi-select" and "likert". The admin editor and the
/// student form read the same lists, through FitEdu.Web.Shared.AssessmentQuestionTypes.
/// </summary>
public static class AssessmentQuestionKinds
{
    /// <summary>One answer from a list.</summary>
    public static bool IsSingleChoice(string type) => type is "choice" or "single-select";

    /// <summary>Any number of answers from a list.</summary>
    public static bool IsMultiSelect(string type) => type is "multiselect" or "multi-select";

    /// <summary>Types that list options.</summary>
    public static bool HasOptions(string type) => IsSingleChoice(type) || IsMultiSelect(type);

    /// <summary>Types answered on a number scale.</summary>
    public static bool IsRatingScale(string type) => type is "rating" or "likert";
}
