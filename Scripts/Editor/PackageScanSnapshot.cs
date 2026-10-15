using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tauntastic.Packages.Editor
{
    /// <summary>The result of one scan, saved to disk: when it finished, what the GitHub listing reported, and every package with its versions, status, flags and dependencies.</summary>
    public sealed class PackageScanSnapshot
    {
        private const int _FORMAT_VERSION = 1;

        /// <summary>When the scan finished (UTC).</summary>
        public DateTime SavedUtc = DateTime.UtcNow;

        /// <summary>Why listing the GitHub repositories failed in that scan, or an empty string.</summary>
        public string DiscoveryError = string.Empty;

        /// <summary>The GitHub owner that was searched.</summary>
        public string Owner = string.Empty;

        /// <summary>One entry per package of the scan.</summary>
        public List<PackageUpdateInfo> Packages = new List<PackageUpdateInfo>();

        /// <summary>True when the scan is newer than the given number of hours, so it is still good enough to show without scanning again.</summary>
        public bool IsFresh(DateTime nowUtc, int intervalHours)
        {
            return nowUtc - SavedUtc < TimeSpan.FromHours(Math.Max(1, intervalHours));
        }

        /// <summary>
        /// True when the installed packages of the scan are exactly the installed packages now (same names and versions). A scan whose installed
        /// packages differ is out of date even when it is recent, for example after a package was updated or installed.
        /// </summary>
        public bool MatchesInstalled(IDictionary<string, string> installedNow)
        {
            var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageUpdateInfo info in Packages)
            {
                if (info.IsInstalled)
                    saved[info.Name] = info.InstalledVersion ?? string.Empty;
            }

            if (saved.Count != installedNow.Count)
                return false;

            foreach (KeyValuePair<string, string> pair in installedNow)
            {
                string version;
                if (!saved.TryGetValue(pair.Key, out version) || !string.Equals(version, pair.Value ?? string.Empty, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        /// <summary>Writes the scan as JSON text.</summary>
        public string ToJson()
        {
            var packages = new List<object>();
            foreach (PackageUpdateInfo info in Packages)
                packages.Add(ToDictionary(info));

            var root = new Dictionary<string, object>();
            root["formatVersion"] = _FORMAT_VERSION;
            root["savedUtc"] = SavedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            root["owner"] = Owner ?? string.Empty;
            root["discoveryError"] = DiscoveryError ?? string.Empty;
            root["packages"] = packages;
            return SimpleJsonWriter.Serialize(root);
        }

        /// <summary>Reads a scan written by <see cref="ToJson"/>. Returns false when the text is not a valid scan of this format.</summary>
        public static bool TryParse(string json, out PackageScanSnapshot snapshot)
        {
            snapshot = null;

            object parsed;
            if (!SimpleJson.TryParse(json, out parsed))
                return false;

            var root = parsed as Dictionary<string, object>;
            if (root == null)
                return false;

            object format;
            if (!root.TryGetValue("formatVersion", out format) || !(format is double) || (int)(double)format != _FORMAT_VERSION)
                return false;

            DateTime saved;
            if (!DateTime.TryParse(SimpleJson.GetString(root, "savedUtc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out saved))
                return false;

            object list;
            var array = root.TryGetValue("packages", out list) ? list as List<object> : null;
            if (array == null)
                return false;

            var result = new PackageScanSnapshot
            {
                SavedUtc = saved,
                Owner = SimpleJson.GetString(root, "owner"),
                DiscoveryError = SimpleJson.GetString(root, "discoveryError"),
            };

            foreach (object item in array)
            {
                PackageUpdateInfo info;
                if (FromDictionary(item as Dictionary<string, object>, out info))
                    result.Packages.Add(info);
            }

            snapshot = result;
            return true;
        }

        private static Dictionary<string, object> ToDictionary(PackageUpdateInfo info)
        {
            var d = new Dictionary<string, object>();
            d["name"] = info.Name ?? string.Empty;
            d["displayName"] = info.DisplayName ?? string.Empty;
            d["installedVersion"] = info.InstalledVersion ?? string.Empty;
            d["latestVersion"] = info.LatestVersion ?? string.Empty;
            d["status"] = info.Status.ToString();
            d["error"] = info.Error ?? string.Empty;
            d["failureKind"] = info.FailureKind.ToString();
            d["isInstalled"] = info.IsInstalled;
            d["isPrivateRepository"] = info.IsPrivateRepository;
            d["isVisibilityKnown"] = info.IsVisibilityKnown;
            d["description"] = info.Description ?? string.Empty;
            d["unity"] = info.Unity ?? string.Empty;
            d["sourceFolderPath"] = info.SourceFolderPath ?? string.Empty;
            d["sourceFolderVersion"] = info.SourceFolderVersion ?? string.Empty;
            d["sourceAhead"] = info.SourceAhead;
            d["referenceUrl"] = info.Reference != null ? info.Reference.UpdateUrl : string.Empty;

            if (info.Manifest != null)
            {
                var dependencies = new Dictionary<string, object>();
                foreach (KeyValuePair<string, string> pair in info.Manifest.Dependencies)
                    dependencies[pair.Key] = pair.Value ?? string.Empty;

                var manifest = new Dictionary<string, object>();
                manifest["name"] = info.Manifest.Name ?? string.Empty;
                manifest["version"] = info.Manifest.Version ?? string.Empty;
                manifest["displayName"] = info.Manifest.DisplayName ?? string.Empty;
                manifest["description"] = info.Manifest.Description ?? string.Empty;
                manifest["unity"] = info.Manifest.Unity ?? string.Empty;
                manifest["dependencies"] = dependencies;
                d["manifest"] = manifest;
            }

            return d;
        }

        private static bool FromDictionary(Dictionary<string, object> d, out PackageUpdateInfo info)
        {
            info = null;
            if (d == null)
                return false;

            string name = SimpleJson.GetString(d, "name");
            if (string.IsNullOrEmpty(name))
                return false;

            PackageUpdateStatus status;
            if (!Enum.TryParse(SimpleJson.GetString(d, "status"), out status))
                return false;

            PackageFailureKind failure;
            if (!Enum.TryParse(SimpleJson.GetString(d, "failureKind"), out failure))
                failure = PackageFailureKind.None;

            GitPackageReference reference;
            if (!GitPackageReference.TryParse(name + "@" + SimpleJson.GetString(d, "referenceUrl"), out reference))
                return false;

            var result = new PackageUpdateInfo
            {
                Name = name,
                DisplayName = SimpleJson.GetString(d, "displayName"),
                InstalledVersion = SimpleJson.GetString(d, "installedVersion"),
                LatestVersion = SimpleJson.GetString(d, "latestVersion"),
                Status = status,
                Error = SimpleJson.GetString(d, "error"),
                FailureKind = failure,
                IsInstalled = SimpleJson.GetBool(d, "isInstalled"),
                IsPrivateRepository = SimpleJson.GetBool(d, "isPrivateRepository"),
                IsVisibilityKnown = SimpleJson.GetBool(d, "isVisibilityKnown"),
                Description = SimpleJson.GetString(d, "description"),
                Unity = SimpleJson.GetString(d, "unity"),
                SourceFolderPath = NullIfEmpty(SimpleJson.GetString(d, "sourceFolderPath")),
                SourceFolderVersion = SimpleJson.GetString(d, "sourceFolderVersion"),
                SourceAhead = SimpleJson.GetBool(d, "sourceAhead"),
                Reference = reference,
            };

            Dictionary<string, object> manifest = SimpleJson.GetObject(d, "manifest");
            if (manifest != null)
            {
                var restored = new PackageManifest
                {
                    Name = SimpleJson.GetString(manifest, "name"),
                    Version = SimpleJson.GetString(manifest, "version"),
                    DisplayName = SimpleJson.GetString(manifest, "displayName"),
                    Description = SimpleJson.GetString(manifest, "description"),
                    Unity = SimpleJson.GetString(manifest, "unity"),
                };

                Dictionary<string, object> dependencies = SimpleJson.GetObject(manifest, "dependencies");
                if (dependencies != null)
                {
                    foreach (KeyValuePair<string, object> pair in dependencies)
                        restored.Dependencies[pair.Key] = pair.Value as string ?? string.Empty;
                }

                result.Manifest = restored;
            }

            info = result;
            return true;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
