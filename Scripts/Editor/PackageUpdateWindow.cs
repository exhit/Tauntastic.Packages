using UnityEditor;
using UnityEngine;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Editor window (Tools > Tauntastic > Packages > Updates) that lists the installed Tauntastic git packages with their installed and latest versions, and lets you update them.</summary>
    public class PackageUpdateWindow : EditorWindow
    {
        private Vector2 _scroll;

        /// <summary>Opens the window, or brings it to the front if it is already open.</summary>
        [MenuItem("Tools/Tauntastic/Packages/Updates", false, PackageMenuPriority.Updates)]
        public static void Open()
        {
            var window = GetWindow<PackageUpdateWindow>("Tauntastic Packages");
            window.minSize = new Vector2(420f, 200f);
            window.Show();
        }

        private void OnEnable()
        {
            PackageUpdateChecker.Changed += Repaint;
        }

        private void OnDisable()
        {
            PackageUpdateChecker.Changed -= Repaint;
        }

        private void OnGUI()
        {
            DrawToolbar();

            var results = PackageUpdateChecker.Results;
            if (results.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    PackageUpdateChecker.IsChecking
                        ? "Checking..."
                        : "No Tauntastic package installed from a GitHub git URL was found yet. Press Check now.",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (PackageUpdateInfo info in results)
                DrawRow(info);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(PackageUpdateChecker.IsChecking))
                {
                    if (GUILayout.Button("Check now", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                        PackageUpdateChecker.CheckNow(true);
                }

                GUILayout.FlexibleSpace();

                System.DateTime last = PackageUpdateSettings.LastCheckUtc;
                string text = last == System.DateTime.MinValue ? "Never checked" : "Last check: " + last.ToLocalTime().ToString("g");
                GUILayout.Label(text, EditorStyles.miniLabel);

                if (GUILayout.Button("Preferences", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    SettingsService.OpenUserPreferences(PackageUpdateSettings.PreferencesPath);
            }
        }

        private static void DrawRow(PackageUpdateInfo info)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(info.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(StatusText(info), EditorStyles.miniBoldLabel);
                }

                EditorGUILayout.LabelField("Installed", info.InstalledVersion);
                EditorGUILayout.LabelField("Latest", string.IsNullOrEmpty(info.LatestVersion) ? "-" : info.LatestVersion);

                if (info.Status == PackageUpdateStatus.Failed && !string.IsNullOrEmpty(info.Error))
                    EditorGUILayout.HelpBox(info.Error, MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(info.Status != PackageUpdateStatus.UpdateAvailable || info.IsUpdating))
                    {
                        if (GUILayout.Button(info.IsUpdating ? "Updating..." : "Update", GUILayout.Width(90f)))
                            PackageUpdateChecker.Update(info);
                    }

                    if (GUILayout.Button("Changelog", GUILayout.Width(90f)))
                        Application.OpenURL(info.Reference.ChangelogUrl);
                    if (GUILayout.Button("Repository", GUILayout.Width(90f)))
                        Application.OpenURL(info.Reference.RepositoryUrl);
                }
            }
        }

        private static string StatusText(PackageUpdateInfo info)
        {
            switch (info.Status)
            {
                case PackageUpdateStatus.UpdateAvailable: return "Update available";
                case PackageUpdateStatus.UpToDate: return "Up to date";
                case PackageUpdateStatus.Failed: return "Check failed";
                default: return "Checking...";
            }
        }
    }
}
