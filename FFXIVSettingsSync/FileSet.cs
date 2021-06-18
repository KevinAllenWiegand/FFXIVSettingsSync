using System;

namespace FFXIVSettingsSync
{
    public class FileSet
    {
        public string LocalFileName { get; set; }
        public DateTime LocalModified { get; set; }
        public string RemoteFileName { get; set; }
        public DateTime RemoteModified { get; set; }
    }
}
