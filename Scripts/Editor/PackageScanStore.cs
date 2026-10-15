using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Keeps every scan and the window state as JSON files in a folder (the project's Library folder, which is not versioned). The newest scans are kept, older ones are deleted.</summary>
    public sealed class PackageScanStore
    {
        /// <summary>How many scans are kept.</summary>
        public const int MaxScans = 20;

        private const string _SCAN_PREFIX = "scan-";
        private const string _WINDOW_STATE_FILE = "window-state.json";

        private readonly string _directory;

        /// <summary>The folder that holds the files.</summary>
        public string Directory
        {
            get { return _directory; }
        }

        public PackageScanStore(string directory)
        {
            _directory = directory;
        }

        /// <summary>Saves a scan as a new file and deletes the oldest ones beyond <see cref="MaxScans"/>. With replaceLatest the newest file is overwritten instead (used to correct the latest scan after a push). Returns the path.</summary>
        public string SaveScan(PackageScanSnapshot snapshot, bool replaceLatest)
        {
            System.IO.Directory.CreateDirectory(_directory);

            string path = null;
            if (replaceLatest)
            {
                List<string> existing = ListScanFiles();
                if (existing.Count > 0)
                    path = existing[0];
            }

            if (path == null)
            {
                string baseName = _SCAN_PREFIX + snapshot.SavedUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssfff");
                path = Path.Combine(_directory, baseName + "Z.json");
                for (int counter = 2; File.Exists(path); counter++)
                    path = Path.Combine(_directory, baseName + "-" + counter + "Z.json");
            }

            File.WriteAllText(path, snapshot.ToJson(), new UTF8Encoding(false));
            Prune();
            return path;
        }

        /// <summary>The newest scan that can be read, or null when there is none. A corrupt file is skipped.</summary>
        public PackageScanSnapshot LoadLatestScan()
        {
            foreach (string path in ListScanFiles())
            {
                PackageScanSnapshot snapshot;
                try
                {
                    if (PackageScanSnapshot.TryParse(File.ReadAllText(path), out snapshot))
                        return snapshot;
                }
                catch (IOException)
                {
                }
            }
            return null;
        }

        /// <summary>The paths of all saved scans, newest first.</summary>
        public List<string> ListScanFiles()
        {
            var files = new List<string>();
            if (!System.IO.Directory.Exists(_directory))
                return files;

            files.AddRange(System.IO.Directory.GetFiles(_directory, _SCAN_PREFIX + "*.json"));
            files.Sort(StringComparer.Ordinal);
            files.Reverse();
            return files;
        }

        /// <summary>Saves the window state.</summary>
        public void SaveWindowState(PackageWindowState state)
        {
            System.IO.Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, _WINDOW_STATE_FILE), state.ToJson(), new UTF8Encoding(false));
        }

        /// <summary>The saved window state, or a default state when none was saved or the file is unreadable.</summary>
        public PackageWindowState LoadWindowState()
        {
            string path = Path.Combine(_directory, _WINDOW_STATE_FILE);
            try
            {
                PackageWindowState state;
                if (File.Exists(path) && PackageWindowState.TryParse(File.ReadAllText(path), out state))
                    return state;
            }
            catch (IOException)
            {
            }
            return new PackageWindowState();
        }

        private void Prune()
        {
            List<string> files = ListScanFiles();
            for (int i = MaxScans; i < files.Count; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
