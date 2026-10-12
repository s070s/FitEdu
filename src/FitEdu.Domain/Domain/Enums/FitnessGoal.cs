namespace FitEdu.Domain.Enums;

/// <summary>
/// Main training goal of a course or a user. Stored as int; never renumber existing values.
/// </summary>
public enum FitnessGoal
{
    GeneralFitness = 0,
    Strength = 1,
    MuscleGain = 2,
    FatLoss = 3,
    Endurance = 4,
    Mobility = 5,
    Recovery = 6,
    Nutrition = 7
}
