using System;

namespace FFXIVSettingsSync
{
    public class ChangedFileData
    {
        private const int _CopyThresholdMilliseconds = 5000;

        public DateTime LastChangedTime { get; set; } = DateTime.Now;

        public bool IsReadyToCopy()
        {
            return DateTime.Now.Subtract(LastChangedTime).TotalMilliseconds >= _CopyThresholdMilliseconds;
        }
    }
}
