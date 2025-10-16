using System.Text.Json.Serialization;

namespace BlazorApp5.Hosting;

/// <summary>
/// Represents the metadata that describes a plugin assembly.
/// </summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("entryType")]
    public string? EntryType { get; set; }

    public bool TryValidate(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            error = "Plugin id is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Plugin name is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            error = "Plugin version is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Assembly))
        {
            error = "Plugin assembly is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(EntryType))
        {
            error = "Plugin entry type is missing.";
            return false;
        }

        error = null;
        return true;
    }
}
