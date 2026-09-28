using System.Text.Json;

namespace DeltaHarmonica.Core;

public sealed class AppSettings
{
    public int BaseMidi { get; set; } = 60;
    public int Transpose { get; set; }
    public double Speed { get; set; } = 1.0;
    public string Hotkey { get; set; } = "F8";
    public int LibraryWidth { get; set; } = 290;

    public static AppSettings Load(string path)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new(); }
        catch (FileNotFoundException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
