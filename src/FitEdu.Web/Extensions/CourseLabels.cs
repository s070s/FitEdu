using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Localized labels for a course's <see cref="CourseLevel"/>, <see cref="FitnessGoal"/> and
/// equipment codes (<see cref="Course.EquipmentCodes"/>).
/// </summary>
public static class CourseLabels
{
    /// <summary>CourseRes holds one key per equipment code: Equipment_none, Equipment_bands, …</summary>
    public const string EquipmentPrefix = "Equipment_";

    public static string Level(CourseLevel level) => level switch
    {
        CourseLevel.Beginner => CourseRes.Level_Beginner,
        CourseLevel.Intermediate => CourseRes.Level_Intermediate,
        CourseLevel.Advanced => CourseRes.Level_Advanced,
        _ => level.ToString()
    };

    public static string Goal(FitnessGoal goal) => goal switch
    {
        FitnessGoal.GeneralFitness => CourseRes.Goal_GeneralFitness,
        FitnessGoal.Strength => CourseRes.Goal_Strength,
        FitnessGoal.MuscleGain => CourseRes.Goal_MuscleGain,
        FitnessGoal.FatLoss => CourseRes.Goal_FatLoss,
        FitnessGoal.Endurance => CourseRes.Goal_Endurance,
        FitnessGoal.Mobility => CourseRes.Goal_Mobility,
        FitnessGoal.Recovery => CourseRes.Goal_Recovery,
        FitnessGoal.Nutrition => CourseRes.Goal_Nutrition,
        _ => goal.ToString()
    };

    /// <summary>The label of one stored equipment code ("bands" → "Bands"); an unknown code is shown as it is.</summary>
    public static string Equipment(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var trimmedCode = code.Trim();

        // ResourceManager follows the current UI culture, like the generated CourseRes properties.
        var label = CourseRes.ResourceManager.GetString(EquipmentPrefix + trimmedCode, CourseRes.Culture);
        return label ?? trimmedCode;
    }

    /// <summary>The labels of a course's comma-joined equipment codes ("none,bands"), in stored order.</summary>
    public static IReadOnlyList<string> EquipmentList(string? equipment)
    {
        if (string.IsNullOrWhiteSpace(equipment))
            return [];

        return equipment
            .Split(Course.EquipmentSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Equipment)
            .ToList();
    }
}
