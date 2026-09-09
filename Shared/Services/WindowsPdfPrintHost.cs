using GestionCommerciale.Shared.Services.Printing;
using Avalonia.Controls.ApplicationLifetimes;

namespace GestionCommerciale.Shared.Services;

internal static class WindowsPdfPrintHost
{
    public static async Task PrintAsync(string pdfPath, string documentTitle, CancellationToken cancellationToken = default)
    {
        var handle = IntPtr.Zero;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            handle = desktop.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        var result = await WindowsNativePdfPrinter.PrintAsync(pdfPath, documentTitle, handle, cancellationToken);

        if (result.CancelledByUser)
            return;

        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "L'impression a échoué.");
    }
}
