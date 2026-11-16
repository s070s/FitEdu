using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using FitEdu.Web.Shared.Components.Forms;

namespace FitEdu.Web.Tests;

/// <summary>
/// A browser file with a chosen size and no data, so a test can pick a 500 MB video without
/// allocating it. Reading it fails, as a real file over the limit does.
/// </summary>
internal sealed class FakeBrowserFile(string name, long size, string contentType) : IBrowserFile
{
    public string Name { get; } = name;
    public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
    public long Size { get; } = size;
    public string ContentType { get; } = contentType;

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
        Size > maxAllowedSize
            ? throw new IOException($"Supplied file with size {Size} bytes exceeds the maximum of {maxAllowedSize} bytes.")
            : new MemoryStream(new byte[Size]);

    /// <summary>Raises the picker's change as the browser does when this file is chosen.</summary>
    public static void Choose(IRenderedComponent<FilePicker> picker, IBrowserFile file)
    {
        var inputChanged = picker.FindComponent<InputFile>().Instance.OnChange;
        var args = new InputFileChangeEventArgs([file]);
        picker.InvokeAsync(() => inputChanged.InvokeAsync(args)).GetAwaiter().GetResult();
    }
}
