using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Editor window (Tools > Tauntastic > Packages > Updates) that lists the installed Tauntastic git packages and the Tauntastic packages found in the GitHub account that are not installed yet, with a colored status flag per package and per kind of failure, update and install buttons, and a Dependencies foldout.</summary>
    public class PackageUpdateWindow : EditorWindow
    {
        private const string _TITLE = "Tauntastic Packages";
        private const float _ROW_TINT_ALPHA = 0.18f;
        private const float _INDENT_PER_LEVEL = 14f;

        private static GUIStyle _flagStyle;
        private readonly HashSet<string> _dependencyFoldouts = new HashSet<string>();
        private Vector2 _scroll;
        private bool _showInstallable = true;

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

            // An empty window scans by itself, limited by the "hours between checks" preference.
            EditorApplication.delayCall += PackageUpdateChecker.ScanIfEmptyAndDue;
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

            if (!string.IsNullOrEmpty(PackageUpdateChecker.DiscoveryError))
                EditorGUILayout.HelpBox(PackageUpdateChecker.DiscoveryError, MessageType.Warning);

            var results = PackageUpdateChecker.Results;
            if (results.Count == 0)
            {
                string message;
                if (PackageUpdateChecker.IsChecking)
                    message = "Checking...";
                else if (!PackageUpdateSettings.Enabled)
                    message = "No scan results yet. Automatic scans are switched off in the preferences. Press Check now to scan.";
                else
                    message = "No scan results yet. The window scans by itself when the last scan is more than " +
                              PackageUpdateSettings.IntervalHours + " hour(s) old (see the preferences). Press Check now to scan immediately.";

                EditorGUILayout.HelpBox(message, MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (PackageUpdateInfo info in results)
            {
                if (!_showInstallable && (info.Status == PackageUpdateStatus.Installable || info.Status == PackageUpdateStatus.SourceFolder))
                    continue;
                DrawRow(info);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(PackageUpdateChecker.IsChecking))
                {
                    if (GUILayout.Button("Check now", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                        PackageUpdateChecker.CheckNow(true);
                }

                _showInstallable = GUILayout.Toggle(_showInstallable, "Show installable", EditorStyles.toolbarButton, GUILayout.Width(110f));

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

            int updates = 0, upToDate = 0, installable = 0, sourceFolders = 0, checking = 0;
            var failures = new int[Enum.GetValues(typeof(PackageFailureKind)).Length];

            foreach (PackageUpdateInfo info in results)
            {
                switch (info.Status)
                {
                    case PackageUpdateStatus.UpdateAvailable: updates++; break;
                    case PackageUpdateStatus.UpToDate: upToDate++; break;
                    case PackageUpdateStatus.Installable: installable++; break;
                    case PackageUpdateStatus.SourceFolder: sourceFolders++; break;
                    case PackageUpdateStatus.Failed: failures[(int)info.FailureKind]++; break;
                    default: checking++; break;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (updates > 0) DrawFlag(updates + " UPDATE AVAILABLE", PackageStatusStyle.UpdateAvailable);
                if (installable > 0) DrawFlag(installable + " INSTALLABLE", PackageStatusStyle.Installable);
                if (sourceFolders > 0) DrawFlag(sourceFolders + " SOURCE CODE", PackageStatusStyle.SourceFolder);
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

        private void DrawRow(PackageUpdateInfo info)
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
                if (info.IsPrivateRepository)
                    DrawFlag("PRIVATE", new Color(0.35f, 0.35f, 0.35f));
                DrawFlag(PackageStatusStyle.GetFlag(info), color);
            }

            if (!string.IsNullOrEmpty(info.Description))
                EditorGUILayout.LabelField(info.Description, EditorStyles.wordWrappedMiniLabel);

            if (info.Status == PackageUpdateStatus.SourceFolder)
            {
                EditorGUILayout.LabelField("Installed in folder", info.SourceFolderPath);
                EditorGUILayout.LabelField("Source code version", string.IsNullOrEmpty(info.SourceFolderVersion) ? "-" : info.SourceFolderVersion);
            }
            else if (info.IsInstalled)
                EditorGUILayout.LabelField("Installed", info.InstalledVersion);
            else
                EditorGUILayout.LabelField("Installed", "not installed");
            EditorGUILayout.LabelField("Latest", string.IsNullOrEmpty(info.LatestVersion) ? "-" : info.LatestVersion);
            if (!string.IsNullOrEmpty(info.Unity))
                EditorGUILayout.LabelField("Unity", info.Unity + " or newer");

            if (info.Status == PackageUpdateStatus.Failed && !string.IsNullOrEmpty(info.Error))
                EditorGUILayout.HelpBox(info.Error, MessageType.Warning);

            if (info.Status == PackageUpdateStatus.SourceFolder)
            {
                EditorGUILayout.HelpBox(
                    "Source code: the folder for this package already exists in the project:\n" + info.SourceFolderPath +
                    "\nThis is the source of the package, so it is not installed from git.",
                    MessageType.Info);
            }

            DrawDependencies(info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (info.Status == PackageUpdateStatus.SourceFolder)
                {
                    if (GUILayout.Button("Open folder", GUILayout.Width(90f)))
                        OpenSourceFolder(info.SourceFolderPath);
                }
                else if (info.Status == PackageUpdateStatus.Installable)
                {
                    using (new EditorGUI.DisabledScope(info.IsUpdating))
                    {
                        if (GUILayout.Button(info.IsUpdating ? "Installing..." : "Install", GUILayout.Width(90f)))
                            PackageUpdateChecker.Install(info);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(info.Status != PackageUpdateStatus.UpdateAvailable || info.IsUpdating))
                    {
                        if (GUILayout.Button(info.IsUpdating ? "Updating..." : "Update", GUILayout.Width(90f)))
                            PackageUpdateChecker.Update(info);
                    }
                }

                if (GUILayout.Button("Changelog", GUILayout.Width(90f)))
                    Application.OpenURL(info.Reference.ChangelogUrl);
                if (GUILayout.Button("Repository", GUILayout.Width(90f)))
                    Application.OpenURL(info.Reference.RepositoryUrl);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>Selects and pings the folder in the Project window, or shows it in the file browser when it is not an asset folder.</summary>
        private static void OpenSourceFolder(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
                return;

            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectRelativePath);
            if (folder != null)
            {
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
                return;
            }

            EditorUtility.RevealInFinder(projectRelativePath);
        }

        /// <summary>The Dependencies foldout: what this package depends on (child dependencies) and which known packages depend on it (parent dependencies), with indirect ones indented.</summary>
        private void DrawDependencies(PackageUpdateInfo info)
        {
            if (info.Manifest == null)
                return;

            List<DependencyEntry> children = PackageUpdateChecker.GetDependencies(info);
            List<DependencyEntry> parents = PackageUpdateChecker.GetDependents(info);

            bool expanded = _dependencyFoldouts.Contains(info.Name);
            bool now = EditorGUILayout.Foldout(expanded, "Dependencies (" + children.Count + " required, " + parents.Count + " required by)", true);
            if (now != expanded)
            {
                if (now) _dependencyFoldouts.Add(info.Name);
                else _dependencyFoldouts.Remove(info.Name);
            }

            if (!now)
                return;

            EditorGUI.indentLevel++;
            DrawDependencyList("Depends on (child dependencies)", children, "Nothing: this package has no dependencies.");
            DrawDependencyList("Required by (parent dependencies)", parents, "No known Tauntastic package depends on it.");
            EditorGUI.indentLevel--;
        }

        private static void DrawDependencyList(string title, List<DependencyEntry> entries, string emptyText)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (entries.Count == 0)
            {
                EditorGUILayout.LabelField(emptyText, EditorStyles.miniLabel);
                return;
            }

            foreach (DependencyEntry entry in entries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space((entry.Depth - 1) * _INDENT_PER_LEVEL + 12f);
                    GUILayout.Label((entry.Depth > 1 ? "└ " : string.Empty) + entry.Name, EditorStyles.label);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(PackageUpdateChecker.DescribeDependency(entry.Name) + (entry.Depth > 1 ? "  (indirect)" : string.Empty),
                        EditorStyles.miniLabel);
                }
            }
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
