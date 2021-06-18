using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace FFXIVSettingsSync
{
    public class SettingsWatcher
    {
        #region Singleton

        private static readonly object _SingletonSync = new object();
        private static SettingsWatcher _SettingsWatcher;

        public static SettingsWatcher Instance
        {
            get
            {
                if (_SettingsWatcher != null)
                {
                    return _SettingsWatcher;
                }

                lock (_SingletonSync)
                {
                    return _SettingsWatcher ?? (_SettingsWatcher = new SettingsWatcher());
                }
            }
        }

        #endregion

        private const string _SquareEnixRegistryKeyLocation = "SOFTWARE\\WOW6432Node\\SquareEnix";
        private const string _FinalFantasyXIVSubKeyName = "FINAL FANTASY XIV";
        private const string _SubDirValueName = "SubDirName";
        private const string _MyGamesFolderName = "My Games";
        private const string _LogFileName = "FFXIVSettingsSync.log";
        private const int _QueueTimerIntervalMilliseconds = 500;
        private const string _DropboxSettingsFile = "Dropbox\\info.json";
        private const string _RemoteFFXIVSettingsSubFolderName = "FFXIV\\Settings Backup";

        private static readonly SHA256 _SHA256 = SHA256.Create();
        private static readonly string _LogFileLocation = Path.Combine(Path.GetTempPath(), _LogFileName);
        private static readonly string[] _IgnoreFolders = new[] { "downloads", "screenshots", "log" };
        private static readonly string[] _IgnoreFiles = new[] { "ffxiv.cfg", "ffxiv_boot.cfg" };
        private static readonly string[] _FileTypesToWatch = new[] { ".cfg", ".dat" };
        private static readonly string _DropboxSettingsLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _DropboxSettingsFile);
        private static readonly string _LocalFFXIVSettingsFolder = GetLocalFFXIVSettingsFolder();
        private static readonly string _RemoteFFXIVSettingsFolder = GetRemoteFFXIVSettingsFolder();

        private readonly object _SyncRoot = new object();

        private List<FileSystemWatcher> _LocalFileWatchers;
        private Dictionary<string, ChangedFileData> _ChangedLocalFiles;
        private List<FileSystemWatcher> _RemoteFileWatchers;
        private Dictionary<string, ChangedFileData> _ChangedRemoteFiles;
        private Timer _QueueTimer;
        private State _State = State.None;

        public State State
        {
            get { return _State; }
            set
            {
                if (_State == value) return;

                _State = value;
                OnStateChanged();
            }
        }

        public EventHandler StateChanged;

        private SettingsWatcher()
        {
            LogMessage($"Found Local FFXIV Settings at \"{_LocalFFXIVSettingsFolder}\"{Environment.NewLine}");
            LogMessage($"Remote FFXIV Settings will be stored at \"{_RemoteFFXIVSettingsFolder}\"{Environment.NewLine}");
        }

        public void Start()
        {
            LogMessage($"********** Start **********{Environment.NewLine}");

            Initialize();
            SynchronizeFiles();

            foreach (var watcher in _LocalFileWatchers)
            {
                watcher.EnableRaisingEvents = true;
            }

            foreach (var watcher in _RemoteFileWatchers)
            {
                watcher.EnableRaisingEvents = true;
            }

            State = State.Running;
        }

        public void Pause()
        {
            LogMessage($"********** Pause **********{Environment.NewLine}");

            lock (_SyncRoot)
            {
                _QueueTimer.Change(Timeout.Infinite, Timeout.Infinite);

                foreach (var watcher in _LocalFileWatchers)
                {
                    watcher.EnableRaisingEvents = false;
                }

                foreach (var watcher in _RemoteFileWatchers)
                {
                    watcher.EnableRaisingEvents = false;
                }
            }

            State = State.Paused;
        }

        public void Resume()
        {
            LogMessage($"********** Resume **********{Environment.NewLine}");

            lock (_SyncRoot)
            {
                foreach (var watcher in _LocalFileWatchers)
                {
                    watcher.EnableRaisingEvents = true;
                }

                foreach (var watcher in _RemoteFileWatchers)
                {
                    watcher.EnableRaisingEvents = true;
                }

                _QueueTimer.Change(_QueueTimerIntervalMilliseconds, Timeout.Infinite);
            }

            State = State.Running;
        }

        public void Stop()
        {
            LogMessage($"********** Stop **********{Environment.NewLine}");

            lock (_SyncRoot)
            {
                _QueueTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _QueueTimer.Dispose();

                foreach (var watcher in _LocalFileWatchers)
                {
                    watcher.EnableRaisingEvents = false;

                    watcher.Changed -= LocalFileWatcherChanged;
                    watcher.Created -= LocalFileWatcherCreated;
                    watcher.Error -= LocalFileWatcherError;

                    watcher.Dispose();
                }

                foreach (var watcher in _RemoteFileWatchers)
                {
                    watcher.EnableRaisingEvents = false;

                    watcher.Changed -= RemoteFileWatcherChanged;
                    watcher.Created -= RemoteFileWatcherCreated;
                    watcher.Error -= RemoteFileWatcherError;

                    watcher.Dispose();
                }
            }

            State = State.Stopped;
        }

        private void Initialize()
        {
            if (!String.IsNullOrEmpty(_RemoteFFXIVSettingsFolder) && !Directory.Exists(_RemoteFFXIVSettingsFolder))
            {
                try
                {
                    Directory.CreateDirectory(_RemoteFFXIVSettingsFolder);
                }
                catch
                {
                }
            }

            _QueueTimer = new Timer(QueueTimerElapsed);
            _ChangedLocalFiles = new Dictionary<string, ChangedFileData>();
            _LocalFileWatchers = new List<FileSystemWatcher>();

            if (!String.IsNullOrEmpty(_LocalFFXIVSettingsFolder) && Directory.Exists(_LocalFFXIVSettingsFolder))
            {
                foreach (var fileType in _FileTypesToWatch)
                {
                    var watcher = new FileSystemWatcher(_LocalFFXIVSettingsFolder, $"*{fileType}");

                    watcher.IncludeSubdirectories = true;
                    watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;

                    watcher.Changed += LocalFileWatcherChanged;
                    watcher.Created += LocalFileWatcherCreated;
                    watcher.Error += LocalFileWatcherError;

                    _LocalFileWatchers.Add(watcher);
                }
            }
            else
            {
                LogMessage($"Error: Unable to start local directory watchers - no folder was specified, or it does not exist.{Environment.NewLine}");
            }

            _ChangedRemoteFiles = new Dictionary<string, ChangedFileData>();
            _RemoteFileWatchers = new List<FileSystemWatcher>();

            if (!String.IsNullOrEmpty(_RemoteFFXIVSettingsFolder) && Directory.Exists(_RemoteFFXIVSettingsFolder))
            {
                foreach (var fileType in _FileTypesToWatch)
                {
                    var watcher = new FileSystemWatcher(_RemoteFFXIVSettingsFolder, $"*{fileType}");

                    watcher.IncludeSubdirectories = true;
                    watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;

                    watcher.Changed += RemoteFileWatcherChanged;
                    watcher.Created += RemoteFileWatcherCreated;
                    watcher.Error += RemoteFileWatcherError;

                    _RemoteFileWatchers.Add(watcher);
                }
            }
            else
            {
                LogMessage($"Error: Unable to start remote directory watchers - no folder was specified, or it does not exist.{Environment.NewLine}");
            }
        }

        private void OnStateChanged()
        {
            var handler = StateChanged;

            handler?.Invoke(this, EventArgs.Empty);
        }

        private void LocalFileWatcherChanged(object sender, FileSystemEventArgs e)
        {
            // We are not going to log here - file changes happen a lot, and we don't
            // want to spam the log file with these.  Instead, we will log at the point
            // where we pull the file from the queue.
            if (!IsInterestedInFile(e.FullPath)) return;

            QueueLocalFileForRemoteCopy(e.FullPath);
        }

        private void LocalFileWatcherCreated(object sender, FileSystemEventArgs e)
        {
            LogMessage($"{e.ChangeType}: {e.FullPath}{Environment.NewLine}");

            if (!IsInterestedInFile(e.FullPath)) return;

            QueueLocalFileForRemoteCopy(e.FullPath);
        }

        private void LocalFileWatcherError(object sender, ErrorEventArgs e)
        {
            LogMessage($"Error: {e.GetException().Message}{Environment.NewLine}");
        }

        private void RemoteFileWatcherChanged(object sender, FileSystemEventArgs e)
        {
            // We are not going to log here - file changes happen a lot, and we don't
            // want to spam the log file with these.  Instead, we will log at the point
            // where we pull the file from the queue.
            if (!IsInterestedInFile(e.FullPath)) return;

            QueueRemoteFileForLocalCopy(e.FullPath);
        }

        private void RemoteFileWatcherCreated(object sender, FileSystemEventArgs e)
        {
            LogMessage($"{e.ChangeType}: {e.FullPath}{Environment.NewLine}");

            if (!IsInterestedInFile(e.FullPath)) return;

            QueueRemoteFileForLocalCopy(e.FullPath);
        }

        private void RemoteFileWatcherError(object sender, ErrorEventArgs e)
        {
            LogMessage($"Error: {e.GetException().Message}{Environment.NewLine}");
        }

        private bool IsInterestedInFile(string path)
        {
            var extension = Path.GetExtension(path);

            if (_FileTypesToWatch.FirstOrDefault(f => f.Equals(extension, StringComparison.OrdinalIgnoreCase)) == null) return false;
            
            var fileName = Path.GetFileName(path).ToLower();

            if (_IgnoreFiles.FirstOrDefault(f => f.Equals(fileName)) != null) return false;

            foreach (var folder in _IgnoreFolders)
            {
                if (path.IndexOf($"/{folder}/") != -1) return false;
            }

            return true;
        }

        private void QueueLocalFileForRemoteCopy(string path)
        {
            lock (_SyncRoot)
            {
                if (_ChangedLocalFiles.TryGetValue(path, out ChangedFileData value))
                {
                    value.LastChangedTime = DateTime.Now;
                }
                else
                {
                    _ChangedLocalFiles.Add(path, new ChangedFileData());
                }

                _QueueTimer.Change(_QueueTimerIntervalMilliseconds, Timeout.Infinite);
            }
        }

        private void QueueRemoteFileForLocalCopy(string path)
        {
            lock (_SyncRoot)
            {
                if (_ChangedRemoteFiles.TryGetValue(path, out ChangedFileData value))
                {
                    value.LastChangedTime = DateTime.Now;
                }
                else
                {
                    _ChangedRemoteFiles.Add(path, new ChangedFileData());
                }

                _QueueTimer.Change(_QueueTimerIntervalMilliseconds, Timeout.Infinite);
            }
        }

        private void QueueTimerElapsed(object state)
        {
            lock (_SyncRoot)
            {
                ProcessLocalToRemoteInLock();
                ProcessRemoteToLocalInLock();

                if (_ChangedLocalFiles.Count > 0 || _ChangedRemoteFiles.Count > 0)
                {
                    _QueueTimer.Change(_QueueTimerIntervalMilliseconds, Timeout.Infinite);
                }
            }
        }

        private void ProcessLocalToRemoteInLock()
        {
            var keysToRemove = new HashSet<string>();

            foreach (var keyValuePair in _ChangedLocalFiles)
            {
                if (!keyValuePair.Value.IsReadyToCopy()) continue;

                // This log message was moved from the watcher event notification because it was far too
                // verbose at that level, since files were changing a lot.
                LogMessage($"Changed: {keyValuePair.Key}{Environment.NewLine}");

                var destination = GetRemoteDestination(keyValuePair.Key);

                if (!String.IsNullOrEmpty(destination))
                {
                    LogMessage($"Copying:{Environment.NewLine}");
                    LogMessage($"\tSource: {keyValuePair.Key}{Environment.NewLine}");
                    LogMessage($"\tDestination: {destination}{Environment.NewLine}");

                    if (FilesAreDifferent(keyValuePair.Key, destination))
                    {
                        SafeFileCopy(keyValuePair.Key, destination);
                    }
                    else
                    {
                        LogMessage($"\tCopy aborted: The source and destination files are the same.{Environment.NewLine}");
                    }
                }
                else
                {
                    LogMessage($"Unable to copy {keyValuePair.Key}!{Environment.NewLine}");
                    LogMessage($"\tUnable to determine the remote location - This file does not appear to be in the local FFXIV settings folder!{Environment.NewLine}");
                }

                if (!keysToRemove.Contains(keyValuePair.Key))
                {
                    keysToRemove.Add(keyValuePair.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _ChangedLocalFiles.Remove(key);
            }
        }

        private void ProcessRemoteToLocalInLock()
        {
            var keysToRemove = new HashSet<string>();

            foreach (var keyValuePair in _ChangedRemoteFiles)
            {
                if (!keyValuePair.Value.IsReadyToCopy()) continue;

                // This log message was moved from the watcher event notification because it was far too
                // verbose at that level, since files were changing a lot.
                LogMessage($"Changed: {keyValuePair.Key}{Environment.NewLine}");

                var destination = GetLocalDestination(keyValuePair.Key);

                if (!String.IsNullOrEmpty(destination))
                {
                    LogMessage($"Copying:{Environment.NewLine}");
                    LogMessage($"\tSource: {keyValuePair.Key}{Environment.NewLine}");
                    LogMessage($"\tDestination: {destination}{Environment.NewLine}");

                    if (FilesAreDifferent(keyValuePair.Key, destination))
                    {
                        SafeFileCopy(keyValuePair.Key, destination);
                    }
                    else
                    {
                        LogMessage($"\tCopy aborted: The source and destination files are the same.{Environment.NewLine}");
                    }
                }
                else
                {
                    LogMessage($"Unable to copy {keyValuePair.Key}!{Environment.NewLine}");
                    LogMessage($"\tUnable to determine the local location - This file does not appear to be in the remote FFXIV settings folder!{Environment.NewLine}");
                }

                if (!keysToRemove.Contains(keyValuePair.Key))
                {
                    keysToRemove.Add(keyValuePair.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _ChangedRemoteFiles.Remove(key);
            }
        }

        private static void SafeFileCopy(string source, string destination)
        {
            try
            {
                var destinationFolder = Path.GetDirectoryName(destination);

                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                File.Copy(source, destination, true);
            }
            catch (Exception ex)
            {
                LogMessage($"Error: {ex.Message}{Environment.NewLine}");
            }
        }

        private static bool FilesAreDifferent(string source, string destination)
        {
            // If the source file doesn't exist...then we have nothing to copy!
            if (!File.Exists(source)) return false;

            // If the destination file doesn't exist...then we must copy the file.
            if (!File.Exists(destination)) return true;

            // If both files exist, check to see if the contents are different using a hash.
            string sourceHash = String.Empty;
            string destinationHash = String.Empty;

            using (var stream = File.OpenRead(source))
            {
                foreach (var byteValue in _SHA256.ComputeHash(stream))
                {
                    sourceHash += byteValue.ToString("x2");
                }
            }

            using (var stream = File.OpenRead(destination))
            {
                foreach (var byteValue in _SHA256.ComputeHash(stream))
                {
                    destinationHash += byteValue.ToString("x2");
                }
            }

            return !sourceHash.Equals(destinationHash);
        }

        private string GetRemoteDestination(string localPath)
        {
            var retVal = String.Empty;

            if (localPath.StartsWith(_LocalFFXIVSettingsFolder, StringComparison.OrdinalIgnoreCase))
            {
                retVal = Path.Combine(_RemoteFFXIVSettingsFolder, localPath.Substring(_LocalFFXIVSettingsFolder.Length + 1));
            }

            return retVal;
        }

        private string GetLocalDestination(string remotePath)
        {
            var retVal = String.Empty;

            if (remotePath.StartsWith(_RemoteFFXIVSettingsFolder, StringComparison.OrdinalIgnoreCase))
            {
                retVal = Path.Combine(_LocalFFXIVSettingsFolder, remotePath.Substring(_RemoteFFXIVSettingsFolder.Length + 1));
            }

            return retVal;
        }

        public void SynchronizeFiles()
        {
            /*
             * Get List of Local and Remote Files (making sure to account for ignored files)
             *
             * Traverse the local directory, and add a new FileSet record.
             *     Set the LocalFileName, and set the RemoteFileName IF the remote file exists.
             * Traverse the Remote directory.  Add a new record if no matching records are found
             *     that have a RemoteFileName that matches.  Leave LocalFileName empty, since it's
             *     guaranteed not to be there at this point.
             *
             * foreach fileSet
             *     if has LocalFileName but no RemoteFileName
             *         copy local to remote
             *     if has RemoteFileName but no LocalFileName
             *         copy remote to local
             *     if has LocalFileName and RemoteFileName
             *         if LocalFileName and RemoteFileName contents are different (using hash)
             *             if local is newer than remote, then copy local to remote
             *             else if local is older than remote, then copy remote to local
             *             else log an error since the contents are different, but the modified dates are the same
            */
            State = State.Synchronizing;

            var fileSets = new List<FileSet>();
            var existingRemoteFiles = new HashSet<string>();
            var localFiles = Directory.GetFiles(_LocalFFXIVSettingsFolder, "*.*", SearchOption.AllDirectories);

            foreach (var file in localFiles)
            {
                if (!IsInterestedInFile(file)) continue;

                var fileSet = new FileSet { LocalFileName = file, LocalModified = File.GetLastWriteTime(file) };
                var remoteDestination = GetRemoteDestination(file);

                if (File.Exists(remoteDestination))
                {
                    existingRemoteFiles.Add(remoteDestination.ToLower());
                    fileSet.RemoteFileName = remoteDestination;
                    fileSet.RemoteModified = File.GetLastWriteTime(remoteDestination);
                }

                fileSets.Add(fileSet);
            }

            var remoteFiles = Directory.GetFiles(_RemoteFFXIVSettingsFolder, "*.*", SearchOption.AllDirectories);

            foreach (var file in remoteFiles)
            {
                if (existingRemoteFiles.Contains(file.ToLower())) continue;
                if (!IsInterestedInFile(file)) continue;

                fileSets.Add(new FileSet { RemoteFileName = file, RemoteModified = File.GetLastAccessTime(file) });
            }

            foreach (var fileSet in fileSets)
            {
                if (!String.IsNullOrEmpty(fileSet.LocalFileName) && String.IsNullOrEmpty(fileSet.RemoteFileName))
                {
                    // Local file exists, but the remote does not.  Copy local to remote.
                    SafeFileCopy(fileSet.LocalFileName, GetRemoteDestination(fileSet.LocalFileName));
                }
                else if (String.IsNullOrEmpty(fileSet.LocalFileName) && !String.IsNullOrEmpty(fileSet.RemoteFileName))
                {
                    // Remote file exists, but the local does not.  Copy remote to local.
                    SafeFileCopy(fileSet.RemoteFileName, GetLocalDestination(fileSet.RemoteFileName));
                }
                else
                {
                    // Both files exist.
                    if (FilesAreDifferent(fileSet.LocalFileName, fileSet.RemoteFileName))
                    {
                        if (fileSet.LocalModified > fileSet.RemoteModified)
                        {
                            // Copy local to remote.
                            SafeFileCopy(fileSet.LocalFileName, fileSet.RemoteFileName);
                        }
                        else if (fileSet.RemoteModified > fileSet.LocalModified)
                        {
                            // Copy remote to local.
                            SafeFileCopy(fileSet.RemoteFileName, fileSet.LocalFileName);
                        }
                        else
                        {
                            LogMessage($"The contents of \"{fileSet.LocalFileName}\" and \"{fileSet.RemoteFileName}\" are different, but the modified dates are the same.  These files cannot be synced.{Environment.NewLine}");
                        }
                    }
                }
            }
        }

        private static string GetLocalFFXIVSettingsFolder()
        {
            using (var squareEnixKey = Registry.LocalMachine.OpenSubKey(_SquareEnixRegistryKeyLocation))
            {
                if (squareEnixKey != null)
                {
                    var subKeyNames = squareEnixKey.GetSubKeyNames();

                    if (subKeyNames != null)
                    {
                        foreach (var subKeyName in subKeyNames)
                        {
                            if (!subKeyName.StartsWith(_FinalFantasyXIVSubKeyName, StringComparison.OrdinalIgnoreCase)) continue;

                            using (var subKey = squareEnixKey.OpenSubKey(subKeyName))
                            {
                                if (subKey != null)
                                {
                                    var subDirName = subKey.GetValue(_SubDirValueName);

                                    if (subDirName != null)
                                    {
                                        var subDirNameString = subDirName.ToString();
                                        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(_MyGamesFolderName, subDirNameString));

                                        if (!Directory.Exists(path)) continue;

                                        return path;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            throw new InvalidOperationException("Unable to locate the local Final Fantasy XIV settings location.");
        }

        private static string GetRemoteFFXIVSettingsFolder()
        {
            if (File.Exists(_DropboxSettingsLocation))
            {
                var contents = File.ReadAllText(_DropboxSettingsLocation);

                if (!String.IsNullOrEmpty(contents))
                {
                    var dropboxInfo = JsonSerializer.Deserialize<DropboxInfo>(contents);

                    if (Directory.Exists(dropboxInfo.Personal.Path))
                    {
                        return Path.Combine(dropboxInfo.Personal.Path, _RemoteFFXIVSettingsSubFolderName);
                    }
                }
            }

            throw new InvalidOperationException("Unable to locate the remote Final Fantasy XIV settings location.");
        }

        private static void LogMessage(string message)
        {
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            File.AppendAllText(_LogFileLocation, $"[{time}] {message}");
        }
    }
}
