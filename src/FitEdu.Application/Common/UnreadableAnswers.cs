namespace FitEdu.Application.Common;

/// <summary>
/// The text an encrypted assessment column reads back as when its stored value cannot be decrypted
/// (a lost or replaced Data Protection key ring). A page that shows answers compares against
/// <see cref="Marker"/> and says the answers can no longer be read, instead of showing ciphertext.
/// </summary>
public static class UnreadableAnswers
{
    /// <summary>Not valid JSON on purpose: no reader can mistake it for real answers.</summary>
    public const string Marker = "fitedu:answers-unreadable";

    public static bool IsMarker(string? value) => value == Marker;
}
