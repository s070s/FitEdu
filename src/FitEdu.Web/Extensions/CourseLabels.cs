using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>Localized labels for a course's <see cref="CourseLevel"/> and <see cref="FitnessGoal"/>.</summary>
public static class CourseLabels
{
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
}
