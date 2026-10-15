using System;
using UnityEditor;
using UnityEngine;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Editor window (Tools > Tauntastic > Packages > Updates) that lists the installed Tauntastic git packages with their installed and latest versions, a colored status flag per package and per kind of failure, and lets you update them.</summary>
    public class PackageUpdateWindow : EditorWindow
    {
        private const string _TITLE = "Tauntastic Packages";
        private const float _ROW_TINT_ALPHA = 0.18f;

        private static GUIStyle _flagStyle;
        private Vector2 _scroll;

        /// <summary>Opens the window, or brings it to the front if it is already open.</summary>
        [MenuItem("Tools/Tauntastic/Packages/Updates", false, PackageMenuPriority.Updates)]
        public static void Open()
        {
            var window = GetWindow<PackageUpdateWindow>(_TITLE);
            window.minSize = new Vector2(420f, 200f);
            window.Show();
        }

        private void OnEnable()
        {
            PackageUpdateChecker.Changed += OnResultsChanged;
            UpdateTitle();
        }

        private void OnDisable()
        {
            PackageUpdateChecker.Changed -= OnResultsChanged;
        }

        private void OnResultsChanged()
        {
            UpdateTitle();
            Repaint();
        }

        /// <summary>Shows the number of available updates in the tab title, for example "Tauntastic Packages (2)".</summary>
        private void UpdateTitle()
        {
            int updates = 0;
            foreach (PackageUpdateInfo info in PackageUpdateChecker.Results)
            {
                if (info.Status == PackageUpdateStatus.UpdateAvailable)
                    updates++;
            }

            string title = updates > 0 ? _TITLE + " (" + updates + ")" : _TITLE;
            if (titleContent.text != title)
                titleContent = new GUIContent(title);
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSummary();

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

                DateTime last = PackageUpdateSettings.LastCheckUtc;
                string text = last == DateTime.MinValue ? "Never checked" : "Last check: " + last.ToLocalTime().ToString("g");
                GUILayout.Label(text, EditorStyles.miniLabel);

                if (GUILayout.Button("Preferences", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    SettingsService.OpenUserPreferences(PackageUpdateSettings.PreferencesPath);
            }
        }

        /// <summary>A strip of flags with the number of packages in each state that has any.</summary>
        private static void DrawSummary()
        {
            var results = PackageUpdateChecker.Results;
            if (results.Count == 0)
                return;

            int updates = 0, upToDate = 0, checking = 0;
            var failures = new int[Enum.GetValues(typeof(PackageFailureKind)).Length];

            foreach (PackageUpdateInfo info in results)
            {
                switch (info.Status)
                {
                    case PackageUpdateStatus.UpdateAvailable: updates++; break;
                    case PackageUpdateStatus.UpToDate: upToDate++; break;
                    case PackageUpdateStatus.Failed: failures[(int)info.FailureKind]++; break;
                    default: checking++; break;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (updates > 0) DrawFlag(updates + " UPDATE AVAILABLE", PackageStatusStyle.UpdateAvailable);
                if (checking > 0) DrawFlag(checking + " CHECKING", PackageStatusStyle.Checking);
                foreach (PackageFailureKind kind in Enum.GetValues(typeof(PackageFailureKind)))
                {
                    int count = failures[(int)kind];
                    if (count > 0)
                        DrawFlag(count + " " + PackageStatusStyle.GetFlag(kind), PackageStatusStyle.GetColor(kind));
                }
                if (upToDate > 0) DrawFlag(upToDate + " UP TO DATE", PackageStatusStyle.UpToDate);
                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawRow(PackageUpdateInfo info)
        {
            Color color = PackageStatusStyle.GetColor(info);

            Rect rowRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rowRect, new Color(color.r, color.g, color.b, _ROW_TINT_ALPHA));
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), color);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(info.DisplayName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                DrawFlag(PackageStatusStyle.GetFlag(info), color);
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

            EditorGUILayout.EndVertical();
        }

        /// <summary>Draws a small colored badge with white text.</summary>
        private static void DrawFlag(string text, Color color)
        {
            if (_flagStyle == null)
            {
                _flagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(6, 6, 2, 2),
                };
                _flagStyle.normal.textColor = Color.white;
            }

            var content = new GUIContent(text);
            float width = _flagStyle.CalcSize(content).x + 4f;
            Rect rect = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width));

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, content, _flagStyle);
        }
    }
}
