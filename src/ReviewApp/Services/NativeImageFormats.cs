using System.Runtime.InteropServices;

namespace ImageReviewTool.Services;

internal static class NativeImageFormats
{
    [DllImport("ReviewCore", CallingConvention = CallingConvention.Cdecl)]
    private static extern int review_core_is_supported_extension([MarshalAs(UnmanagedType.LPUTF8Str)] string extension);

    public static bool IsSupported(string extension)
    {
        try { return review_core_is_supported_extension(extension) != 0; }
        catch (DllNotFoundException) { return Managed(extension); }
        catch (BadImageFormatException) { return Managed(extension); }
        catch (EntryPointNotFoundException) { return Managed(extension); }
    }

    private static bool Managed(string extension) => new[]
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" }
        .Contains(extension.ToLowerInvariant());
}
