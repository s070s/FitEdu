using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using FitEdu.Application.Common;

namespace FitEdu.Infrastructure.Data;

/// <summary>
/// EF Core value converter that transparently encrypts a string column at rest using ASP.NET
/// Core Data Protection (COMP-2). Applied to the special-category assessment answer/summary
/// columns so their plaintext never touches the database file, an unfiltered backup, or an
/// admin's raw table view — only the application, holding the DataProtection key ring, can read
/// them.
///
/// The ciphertext is non-deterministic (a fresh IV per write), so a converted column must never
/// be filtered, ordered, or indexed on. That holds here: the only reads are whole-value
/// projections and <c>AssessmentSubmissionSearchExtensions.ApplySort</c> deliberately excludes
/// these columns. Storing a string as a (longer) string keeps the column type <c>nvarchar</c>,
/// so no schema migration is required.
///
/// Reads are tolerant of legacy plaintext: a value written before this converter existed will
/// fail <see cref="IDataProtector.Unprotect"/> and is returned as-is, so an existing database is
/// not broken. A legacy plaintext value is not upgraded by merely reading the row: EF Core writes
/// only the columns that changed, so the value becomes ciphertext only when that column itself is
/// written again, and it stays plaintext until then.
/// </summary>
public sealed partial class EncryptedStringConverter : ValueConverter<string, string>
{
    public EncryptedStringConverter(IDataProtector protector, ILogger? logger = null)
        : base(
            plaintext => protector.Protect(plaintext),
            stored => Decrypt(protector, logger, stored))
    {
    }

    private static string Decrypt(IDataProtector protector, ILogger? logger, string stored)
    {
        try
        {
            return protector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            // A value written before encryption existed is plain JSON: return it as it is rather than
            // crashing the read (it becomes ciphertext when this column is written again). Anything
            // else is ciphertext this key ring cannot open (a lost or replaced key): the fixed marker
            // tells the page to say so, and the warning (never the value) tells the operator.
            if (IsJson(stored))
                return stored;

            if (logger is not null)
                LogUnreadableValue(logger);
            return UnreadableAnswers.Marker;
        }
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Source-generated log method (LoggerMessage) avoids the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Warning, Message = "An encrypted assessment value could not be decrypted with the current Data Protection keys; it reads as unreadable. Check that the key ring was not lost or replaced.")]
    private static partial void LogUnreadableValue(ILogger logger);
}
