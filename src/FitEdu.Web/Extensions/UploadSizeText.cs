using FitEdu.Application.Common;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>The "file is too large" message the upload forms show, with the limit written in whole megabytes.</summary>
public static class UploadSizeText
{
    private const long BytesPerMegabyte = 1024 * 1024;

    public static string TooLarge(long maxBytes) =>
        ResourceFormat.Fill(AdminRes.FileTooLargeFormat, maxBytes / BytesPerMegabyte);
}
