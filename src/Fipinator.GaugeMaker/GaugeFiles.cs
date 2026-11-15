// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Text.Json.Nodes;
using Fipinator.Core;

namespace Fipinator.GaugeMaker;

/// <summary>Where the user's gauges live, the built-in templates, and making a new gauge from one.</summary>
public static class GaugeFiles
{
    /// <summary>Documents\FIPinator 3000\Gauges: the user's own gauges (next to their panels).</summary>
    public static string MyGaugesFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FIPinator 3000", "Gauges");

    public static string TemplatesFolder => Path.Combine(AppContext.BaseDirectory, "Templates");

    public sealed record Template(string Name, string Description, string Path, string? License = null);

    /// <summary>Built-in templates (shipped next to the program), sorted by name.</summary>
    public static IReadOnlyList<Template> Templates()
    {
        if (!Directory.Exists(TemplatesFolder)) return [];
        var list = new List<Template>();
        foreach (var file in Directory.GetFiles(TemplatesFolder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var gauge = GaugeDefinition.Load(file);
                list.Add(new Template(gauge.Name, gauge.Description ?? "", file, gauge.License));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                Log.Warn($"Gauge Maker: template {file} skipped: {ex.Message}");
            }
        }
        return [.. list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Copies a template into the user's gauges folder under a new name and returns the new file.
    /// Never overwrites: adds " (2)", " (3)"... if the name is taken. The font is pointed at the
    /// font shipped with the programs, so the copy draws the same anywhere.
    /// </summary>
    public static string CreateFromTemplate(Template template, string newName)
    {
        var json = JsonNode.Parse(File.ReadAllText(template.Path))!.AsObject();
        json["name"] = newName.Trim().Length > 0 ? newName.Trim() : template.Name;
        json["author"] = "Made with FIPinator 3000";
        if (json["font"]?.GetValue<string>() is { } font) json["font"] = Path.GetFileName(font);

        var path = SaveNew(json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), newName);
        Log.Info($"Gauge Maker: created {path} from template {template.Name}.");
        return path;
    }

    /// <summary>
    /// Saves a new gauge in the user's gauges folder and returns its file. Never overwrites:
    /// adds " (2)", " (3)"... if the name is taken. Writes to a temporary file first, so a
    /// failure can't leave a half-written gauge. A gauge without knobs that shows something a
    /// pilot sets with a knob (baro, course, heading bug, DH...) gets its standard knobs.
    /// </summary>
    public static string SaveNew(string json, string name)
    {
        try { json = KnobDefaults.Fill(json); }
        catch (System.Text.Json.JsonException) { } // not valid JSON: saved as it is; the gauge checks will say why
        Directory.CreateDirectory(MyGaugesFolder);
        var safe = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        if (string.IsNullOrWhiteSpace(safe)) safe = "My gauge";
        var path = Path.Combine(MyGaugesFolder, safe + ".json");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(MyGaugesFolder, $"{safe} ({n}).json");

        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path);
        KeepSpadCopy(path);
        return path;
    }

    /// <summary>
    /// SPAD.neXt users: the gauge's SPAD version is saved (or updated) by itself in the FIPinator SPAD Gauges
    /// folder, in the background. Nothing happens without SPAD.neXt on the PC; a problem is only logged.
    /// </summary>
    public static void KeepSpadCopy(string gaugePath)
    {
        if (!SpadExport.SpadInstalled()) return;
        Task.Run(() => SpadExport.SaveQuietly(gaugePath, SpadExport.MyGaugesFolder, "My gauges"));
    }
}
