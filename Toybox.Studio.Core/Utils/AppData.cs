namespace Toybox.Studio.Utils;

/// <summary>
/// The root ~/.toybox folder under the user profile where all editor data lives (settings, themes, logs).
/// Lives in the dependency-free core so foundation code (e.g. the log file) can resolve it without taking
/// a dependency on the higher-level settings layer; <c>EditorSettings.BaseDirectory</c> forwards here.
/// Use this instead of hard-coding ".toybox" elsewhere.
/// </summary>
public static class AppData
{
    /// <summary>The root ~/.toybox folder under the user profile.</summary>
    public static string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toybox");
}
