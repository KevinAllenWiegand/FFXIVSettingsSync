using System.Text.Json.Serialization;

namespace FFXIVSettingsSync
{
    public class DropboxInfoPersonal
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }
}
