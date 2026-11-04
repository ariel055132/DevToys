using System.Globalization;
using System.Resources;

namespace DevToys.Blazor.BuiltInTools.Base64File;

internal static class Base64FileStrings
{
    private static readonly ResourceManager resourceManager = new(
        "DevToys.Blazor.BuiltInTools.Base64File.Base64File", typeof(Base64FileStrings).Assembly);

    internal static string ShortDisplayTitle => resourceManager.GetString(nameof(ShortDisplayTitle), CultureInfo.CurrentUICulture)!;
    internal static string LongDisplayTitle => resourceManager.GetString(nameof(LongDisplayTitle), CultureInfo.CurrentUICulture)!;
    internal static string Description => resourceManager.GetString(nameof(Description), CultureInfo.CurrentUICulture)!;
    internal static string SearchKeywords => resourceManager.GetString(nameof(SearchKeywords), CultureInfo.CurrentUICulture)!;
    internal static string Instructions => resourceManager.GetString(nameof(Instructions), CultureInfo.CurrentUICulture)!;
    internal static string Base64Text => resourceManager.GetString(nameof(Base64Text), CultureInfo.CurrentUICulture)!;
    internal static string DecodeAndSave => resourceManager.GetString(nameof(DecodeAndSave), CultureInfo.CurrentUICulture)!;
    internal static string SaveBase64 => resourceManager.GetString(nameof(SaveBase64), CultureInfo.CurrentUICulture)!;
    internal static string Encoded => resourceManager.GetString(nameof(Encoded), CultureInfo.CurrentUICulture)!;
    internal static string Saved => resourceManager.GetString(nameof(Saved), CultureInfo.CurrentUICulture)!;
    internal static string TextSaved => resourceManager.GetString(nameof(TextSaved), CultureInfo.CurrentUICulture)!;
    internal static string Working => resourceManager.GetString(nameof(Working), CultureInfo.CurrentUICulture)!;
    internal static string InvalidBase64 => resourceManager.GetString(nameof(InvalidBase64), CultureInfo.CurrentUICulture)!;
    internal static string SizeLimit => resourceManager.GetString(nameof(SizeLimit), CultureInfo.CurrentUICulture)!;
    internal static string FileError => resourceManager.GetString(nameof(FileError), CultureInfo.CurrentUICulture)!;
    internal static string DetectedFormat => resourceManager.GetString(nameof(DetectedFormat), CultureInfo.CurrentUICulture)!;
    internal static string UnknownFormat => resourceManager.GetString(nameof(UnknownFormat), CultureInfo.CurrentUICulture)!;
}
