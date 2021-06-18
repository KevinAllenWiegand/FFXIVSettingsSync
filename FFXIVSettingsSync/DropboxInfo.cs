using System.Text.Json.Serialization;

namespace FFXIVSettingsSync
{
    public class DropboxInfo
    {
        [JsonPropertyName("personal")]
        public DropboxInfoPersonal Personal { get; set; }
    }
}
