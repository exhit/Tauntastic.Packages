using System;
using UnityEditor;

namespace Tauntastic.Packages.Editor
{
    /// <summary>User-level (per machine) preferences of the update checker, stored in EditorPrefs.</summary>
    public static class PackageUpdateSettings
    {
        public const string PreferencesPath = "Tools/Tauntastic/Packages";

        private const string _PREFIX = "Tauntastic.Packages.";
        private const string _ENABLED = _PREFIX + "Enabled";
        private const string _INTERVAL_HOURS = _PREFIX + "IntervalHours";
        private const string _OPEN_WINDOW = _PREFIX + "OpenWindowOnUpdate";
        private const string _LAST_CHECK = _PREFIX + "LastCheckUtcTicks";
        private const string _LAST_NOTIFIED = _PREFIX + "LastNotified";
        private const string _GITHUB_TOKEN = _PREFIX + "GitHubToken";

        /// <summary>Whether the checker runs automatically when the Editor starts.</summary>
        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(_ENABLED, true); }
            set { EditorPrefs.SetBool(_ENABLED, value); }
        }

        /// <summary>Minimum hours between two automatic checks.</summary>
        public static int IntervalHours
        {
            get { return Math.Max(1, EditorPrefs.GetInt(_INTERVAL_HOURS, 24)); }
            set { EditorPrefs.SetInt(_INTERVAL_HOURS, Math.Max(1, value)); }
        }

        /// <summary>Whether the updates window opens by itself when new versions are found.</summary>
        public static bool OpenWindowOnUpdate
        {
            get { return EditorPrefs.GetBool(_OPEN_WINDOW, true); }
            set { EditorPrefs.SetBool(_OPEN_WINDOW, value); }
        }

        /// <summary>When the last check finished (UTC).</summary>
        public static DateTime LastCheckUtc
        {
            get
            {
                long ticks;
                if (long.TryParse(EditorPrefs.GetString(_LAST_CHECK, "0"), out ticks) && ticks > 0)
                    return new DateTime(ticks, DateTimeKind.Utc);
                return DateTime.MinValue;
            }
            set { EditorPrefs.SetString(_LAST_CHECK, value.Ticks.ToString()); }
        }

        /// <summary>
        /// Optional GitHub personal access token with read access to repository contents, used to check private repositories.
        /// Stored in plain text in this machine's EditorPrefs and only ever sent to raw.githubusercontent.com.
        /// </summary>
        public static string GitHubToken
        {
            get { return EditorPrefs.GetString(_GITHUB_TOKEN, string.Empty); }
            set { EditorPrefs.SetString(_GITHUB_TOKEN, (value ?? string.Empty).Trim()); }
        }

        /// <summary>Signature of the set of updates the user was last told about, so the same updates are announced only once.</summary>
        public static string LastNotified
        {
            get { return EditorPrefs.GetString(_LAST_NOTIFIED, string.Empty); }
            set { EditorPrefs.SetString(_LAST_NOTIFIED, value ?? string.Empty); }
        }

        /// <summary>True when the automatic check is enabled and the interval since the last check has passed.</summary>
        public static bool IsAutomaticCheckDue()
        {
            if (!Enabled)
                return false;
            return DateTime.UtcNow - LastCheckUtc >= TimeSpan.FromHours(IntervalHours);
        }
    }
}
