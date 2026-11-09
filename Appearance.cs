using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace InterviewCompanion;

public sealed class AppearanceStore
{
    private readonly string path;
    public AppearanceStore(string? directory = null) => path = Path.Combine(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InterviewCompanion"), "appearance.json");
    public bool LoadDarkTheme() => File.Exists(path) && JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))?.DarkTheme == true;
    public void SaveDarkTheme(bool dark)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(new Preference(dark))); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private record Preference(bool DarkTheme);
}

public static class Appearance
{
    public static void Apply(bool dark)
    {
        var colors = new Dictionary<string, string>
        {
            ["AppBackground"] = dark ? "#11151B" : "#F4F6F8",
            ["Surface"] = dark ? "#1B2029" : "#FFFFFF",
            ["SoftSurface"] = dark ? "#263D30" : "#EAF3ED",
            ["FieldSurface"] = dark ? "#141920" : "#F8FAFC",
            ["MainText"] = dark ? "#EDF1F7" : "#202833",
            ["SecondaryText"] = dark ? "#ABB6C6" : "#5B6879",
            ["Line"] = dark ? "#343D4C" : "#DDE3EA",
            ["AccentButton"] = dark ? "#C5E8CE" : "#D6EBDC",
            ["SecondaryButton"] = dark ? "#D5DEEA" : "#E7EDF3",
            ["HoverButton"] = dark ? "#D8EBDD" : "#D3E1DA",
            ["PressedButton"] = dark ? "#AAD2B4" : "#B7D8C0",
            ["DisabledButton"] = dark ? "#9CAAA3" : "#D3D9D7",
            ["WarningText"] = dark ? "#FFCC86" : "#83520C"
        };
        foreach (var (key, color) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
    }
}
