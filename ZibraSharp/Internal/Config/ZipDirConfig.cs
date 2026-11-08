using System.Text.Json.Serialization;

namespace ZibraSharp.Internal.Config
{
    /// <summary>
    /// Contents of zipdir.json.
    /// </summary>
    public sealed record ZipDirConfig(
        [property: JsonPropertyName("zipdir")] string ZipDir);
}
