using FitEdu.Domain.Entities;

namespace FitEdu.Application.Common;

/// <summary>
/// Reads the comma-separated equipment codes an admin types for a course. The API and the course
/// editor both use it, so the page rejects exactly the codes the server would.
/// </summary>
public static class EquipmentCodeList
{
    /// <summary>
    /// Distinct lowercase codes joined by "," with no spaces, the shape the catalogue's equipment
    /// filter matches against. Blank input gives null.
    /// </summary>
    public static string? Normalize(string? equipment)
    {
        if (string.IsNullOrWhiteSpace(equipment))
            return null;

        var codes = equipment
            .Split(Course.EquipmentSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => code.ToLowerInvariant())
            .Distinct();

        var joined = string.Join(Course.EquipmentSeparator, codes);
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>The first typed code (normalised) that is not in <see cref="Course.EquipmentCodes"/>, or null when all are known.</summary>
    public static string? FindUnknownCode(string? equipment)
    {
        var normalizedEquipment = Normalize(equipment);
        if (normalizedEquipment is null)
            return null;

        return normalizedEquipment
            .Split(Course.EquipmentSeparator)
            .FirstOrDefault(code => !Course.EquipmentCodes.Contains(code));
    }
}
