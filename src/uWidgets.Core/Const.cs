namespace uWidgets.Core;

/// <summary>
/// Constants for the application.
/// </summary>
public static class Const
{
    /// <summary>
    /// The name of the application.
    /// </summary>
    public const string AppName = "uWidgets";
    /// <summary>
    /// The folder containing the application assemblies and content files.
    /// </summary>
    /// <remarks>
    /// <see cref="Environment.ProcessPath"/> cannot be used here because hosted scenarios,
    /// including the Avalonia designer, run the application through <c>dotnet.exe</c>.
    /// In those cases it points to the .NET installation instead of the application's output.
    /// </remarks>
    public static readonly string CurrentFolder = AppContext.BaseDirectory;
    /// <summary>
    /// The folder with the widgets.
    /// </summary>
    public static readonly string WidgetsFolder = Path.Combine(CurrentFolder, WidgetsFolderName);
    /// <summary>
    /// The path to the application settings file.
    /// </summary>
    public static readonly string AppSettingsFile = Path.Combine(CurrentFolder, AppSettingsFileName);
    /// <summary>
    /// The path to the layout file.
    /// </summary>
    public static readonly string LayoutFile = Path.Combine(CurrentFolder, LayoutFileName);
    
    private static string WidgetsFolderName => "Widgets";
    private static string AppSettingsFileName => "appSettings.json";
    private static string LayoutFileName => "layout.json";
}
