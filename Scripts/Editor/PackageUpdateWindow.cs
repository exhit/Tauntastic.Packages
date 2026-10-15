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

        /// <summary>Names of the packages whose row is collapsed to a single line; kept when scripts reload.</summary>
        [SerializeField] private List<string> _collapsed = new List<string>();
        private static GUIStyle _foldoutStyle;

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
                if (info.Status == PackageUpdateStatus.UpdateAvailable || info.SourceAhead)
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

                DrawUpdateAllButton();
                DrawGitAllButton();

                _showInstallable = GUILayout.Toggle(_showInstallable, "Show installable", EditorStyles.toolbarButton, GUILayout.Width(110f));

                if (GUILayout.Button("Expand all", EditorStyles.toolbarButton, GUILayout.Width(75f)))
                    _collapsed.Clear();
                if (GUILayout.Button("Collapse all", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    CollapseAll();

                GUILayout.FlexibleSpace();

                DateTime last = PackageUpdateSettings.LastCheckUtc;
                string text = last == DateTime.MinValue ? "Never checked" : "Last check: " + last.ToLocalTime().ToString("g");
                GUILayout.Label(text, EditorStyles.miniLabel);

                if (GUILayout.Button("Preferences", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    SettingsService.OpenUserPreferences(PackageUpdateSettings.PreferencesPath);
            }
        }

        /// <summary>The "Update all" toolbar button: asks for confirmation, then updates and installs every pending package from the top of the dependency tree to the bottom.</summary>
        private static void DrawUpdateAllButton()
        {
            // "Update all" only exists while installed packages have a newer version; "Install all" only while installable packages exist.
            // Packages that are source code of this project (SOURCE CODE) never count for either.
            DrawBatchButton("Update all", true, false);
            DrawBatchButton("Install all", false, true);
        }

        private static void DrawBatchButton(string verb, bool includeUpdates, bool includeInstalls)
        {
            var pending = PackageUpdateChecker.GetPendingInTreeOrder(includeUpdates, includeInstalls);
            if (pending.Count == 0 && !PackageUpdateChecker.IsUpdatingAll)
                return;

            bool busy = PackageUpdateChecker.IsChecking || PackageUpdateChecker.IsUpdatingAll;
            string label = PackageUpdateChecker.IsUpdatingAll ? "Working..." : verb + " (" + pending.Count + ")";
            var content = new GUIContent(label, verb + " every " + (includeUpdates ? "UPDATE AVAILABLE" : "INSTALLABLE") +
                                                " package, from the grandparents to the grandchildren of the dependency tree.");

            using (new EditorGUI.DisabledScope(busy || pending.Count == 0))
            {
                if (!GUILayout.Button(content, EditorStyles.toolbarButton, GUILayout.Width(100f)))
                    return;
            }

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("These packages will be " + (includeUpdates ? "updated" : "installed") + ", in this order (top of the dependency tree first):");
            builder.AppendLine();
            for (int i = 0; i < pending.Count; i++)
            {
                PackageUpdateInfo info = pending[i];
                string action = info.Status == PackageUpdateStatus.Installable
                    ? "install " + info.LatestVersion
                    : info.InstalledVersion + " > " + info.LatestVersion;
                builder.AppendLine((i + 1) + ". " + info.DisplayName + "  (" + action + ")");
            }

            if (EditorUtility.DisplayDialog(verb + " Tauntastic packages", builder.ToString(), verb, "Cancel"))
                PackageUpdateChecker.UpdateAll(includeUpdates, includeInstalls);
        }

        /// <summary>A strip of flags with the number of packages in each state that has any.</summary>
        private static void DrawSummary()
        {
            var results = PackageUpdateChecker.Results;
            if (results.Count == 0)
                return;

            int updates = 0, upToDate = 0, installable = 0, sourceFolders = 0, sourceAhead = 0, checking = 0;
            var failures = new int[Enum.GetValues(typeof(PackageFailureKind)).Length];

            foreach (PackageUpdateInfo info in results)
            {
                switch (info.Status)
                {
                    case PackageUpdateStatus.UpdateAvailable: updates++; break;
                    case PackageUpdateStatus.UpToDate: upToDate++; break;
                    case PackageUpdateStatus.Installable: installable++; break;
                    case PackageUpdateStatus.SourceFolder:
                        sourceFolders++;
                        if (info.SourceAhead) sourceAhead++;
                        break;
                    case PackageUpdateStatus.Failed: failures[(int)info.FailureKind]++; break;
                    default: checking++; break;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (updates > 0) DrawFlag(updates + " UPDATE AVAILABLE", PackageStatusStyle.UpdateAvailable);
                if (installable > 0) DrawFlag(installable + " INSTALLABLE", PackageStatusStyle.Installable);
                if (sourceAhead > 0) DrawFlag(sourceAhead + " SOURCE AHEAD: UPDATE", PackageStatusStyle.UpdateAvailable);
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

        /// <summary>Collapses every package to its header line.</summary>
        private void CollapseAll()
        {
            _collapsed.Clear();
            foreach (PackageUpdateInfo info in PackageUpdateChecker.Results)
            {
                if (!_collapsed.Contains(info.Name))
                    _collapsed.Add(info.Name);
            }
        }

        /// <summary>The versions of a package in a few characters, for the header line of a collapsed row.</summary>
        private static string VersionSummary(PackageUpdateInfo info)
        {
            string latest = string.IsNullOrEmpty(info.LatestVersion) ? "-" : info.LatestVersion;

            if (info.Status == PackageUpdateStatus.SourceFolder)
                return "source " + (string.IsNullOrEmpty(info.SourceFolderVersion) ? "-" : info.SourceFolderVersion) + "  |  repo " + latest;
            if (info.IsInstalled)
                return info.Status == PackageUpdateStatus.UpdateAvailable ? info.InstalledVersion + " > " + latest : info.InstalledVersion;
            return "latest " + latest;
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
                if (_foldoutStyle == null)
                    _foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

                // The whole package is a foldout: the header line stays, everything below it can be collapsed away.
                bool expanded = !_collapsed.Contains(info.Name);
                // The foldout gets a rect exactly as wide as its title (plus the arrow); a layout foldout would stretch over the line and the
                // version text drawn after it would be printed on top of the name.
                var title = new GUIContent(info.DisplayName);
                float titleWidth = _foldoutStyle.CalcSize(title).x + 18f;
                Rect foldoutRect = GUILayoutUtility.GetRect(titleWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(titleWidth));
                bool nowExpanded = EditorGUI.Foldout(foldoutRect, expanded, title, true, _foldoutStyle);
                if (nowExpanded != expanded)
                {
                    if (nowExpanded) _collapsed.Remove(info.Name);
                    else _collapsed.Add(info.Name);
                }

                // A collapsed row is a single line, so show the versions next to the name.
                if (!nowExpanded)
                    GUILayout.Label(VersionSummary(info), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (info.IsVisibilityKnown)
                {
                    if (info.IsPrivateRepository)
                        DrawFlag("PRIVATE", PackageStatusStyle.PrivateRepository);
                    else
                        DrawFlag("PUBLIC", PackageStatusStyle.PublicRepository);
                }
                if (info.SourceAhead)
                    DrawFlag("SOURCE AHEAD: UPDATE", PackageStatusStyle.UpdateAvailable);
                DrawFlag(PackageStatusStyle.GetFlag(info), color);
            }

            if (_collapsed.Contains(info.Name))
            {
                EditorGUILayout.EndVertical();
                return;
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

                if (info.SourceAhead)
                {
                    EditorGUILayout.HelpBox(
                        "The source code version (" + info.SourceFolderVersion + ") is higher than the version in the repository (" +
                        info.LatestVersion + "): this is an update for the repository. Commit and push it with the Git menu.",
                        MessageType.Warning);
                }

                string gitLine = info.IsGitBusy ? "working..." : info.Git != null ? info.Git.Describe() : "reading the git state...";
                EditorGUILayout.LabelField("Git", gitLine);
            }

            DrawDependencies(info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (info.Status == PackageUpdateStatus.SourceFolder)
                {
                    if (GUILayout.Button("Open folder", GUILayout.Width(90f)))
                        OpenSourceFolder(info.SourceFolderPath);

                    DrawGitDropdown(info);
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

        /// <summary>The Git dropdown of a source code row: commit, push, or both, each only when it is eligible for this package.</summary>
        private static void DrawGitDropdown(PackageUpdateInfo info)
        {
            using (new EditorGUI.DisabledScope(info.IsGitBusy || PackageUpdateChecker.IsGitBusy))
            {
                if (!EditorGUILayout.DropdownButton(new GUIContent("Git", "Commit and push this package's source code."), FocusType.Passive, GUILayout.Width(70f)))
                    return;
            }

            Rect rect = GUILayoutUtility.GetLastRect();
            GitState git = info.Git;
            var targets = new List<PackageUpdateInfo> { info };
            var menu = new GenericMenu();

            AddGitItem(menu, "Commit if eligible", git, PackageUpdateChecker.GitAction.Commit,
                git != null ? git.ChangedFiles + (git.ChangedFiles == 1 ? " changed file" : " changed files") : string.Empty,
                "nothing to commit", targets);
            AddGitItem(menu, "Push if eligible", git, PackageUpdateChecker.GitAction.Push,
                git != null ? (git.HasUpstream ? git.AheadCount + (git.AheadCount == 1 ? " commit" : " commits") : "new branch") : string.Empty,
                "nothing to push", targets);
            AddGitItem(menu, "Commit & Push if eligible", git, PackageUpdateChecker.GitAction.CommitAndPush,
                string.Empty, "nothing to commit or push", targets);

            menu.DropDown(rect);
        }

        private static void AddGitItem(GenericMenu menu, string label, GitState git, PackageUpdateChecker.GitAction action,
            string detailWhenEligible, string detailWhenNot, List<PackageUpdateInfo> targets)
        {
            bool eligible = PackageUpdateChecker.IsEligible(git, action);
            string detail = git == null ? "git state not read yet"
                : !git.GitAvailable ? "Git not found"
                : eligible ? detailWhenEligible : detailWhenNot;
            string text = string.IsNullOrEmpty(detail) ? label : label + " (" + detail + ")";

            if (eligible)
                menu.AddItem(new GUIContent(text), false, () => PackageUpdateChecker.RunGit(action, targets));
            else
                menu.AddDisabledItem(new GUIContent(text));
        }

        /// <summary>The toolbar dropdown for all source code packages: commit all, push all, or both, shown only while something is eligible.</summary>
        private static void DrawGitAllButton()
        {
            var commit = PackageUpdateChecker.GetEligibleSourcePackages(PackageUpdateChecker.GitAction.Commit);
            var push = PackageUpdateChecker.GetEligibleSourcePackages(PackageUpdateChecker.GitAction.Push);
            var both = PackageUpdateChecker.GetEligibleSourcePackages(PackageUpdateChecker.GitAction.CommitAndPush);

            if (both.Count == 0 && !PackageUpdateChecker.IsGitBusy)
                return;

            var content = new GUIContent(
                PackageUpdateChecker.IsGitBusy ? "Git working..." : "Source code (" + both.Count + ")",
                "Commit and push the source code of the packages in this project. The commit message is the version in the package.json.");
            Rect rect = GUILayoutUtility.GetRect(content, EditorStyles.toolbarDropDown, GUILayout.Width(130f));

            using (new EditorGUI.DisabledScope(PackageUpdateChecker.IsGitBusy || PackageUpdateChecker.IsChecking))
            {
                if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, EditorStyles.toolbarDropDown))
                    return;
            }

            var menu = new GenericMenu();
            AddGitAllItem(menu, "Commit all if eligible", PackageUpdateChecker.GitAction.Commit, commit);
            AddGitAllItem(menu, "Push all if eligible", PackageUpdateChecker.GitAction.Push, push);
            AddGitAllItem(menu, "Commit & Push all if eligible", PackageUpdateChecker.GitAction.CommitAndPush, both);
            menu.DropDown(rect);
        }

        private static void AddGitAllItem(GenericMenu menu, string label, PackageUpdateChecker.GitAction action, List<PackageUpdateInfo> eligible)
        {
            var content = new GUIContent(label + " (" + eligible.Count + ")");
            if (eligible.Count == 0)
            {
                menu.AddDisabledItem(content);
                return;
            }

            menu.AddItem(content, false, () => ConfirmAndRunGit(label, action, eligible));
        }

        private static void ConfirmAndRunGit(string title, PackageUpdateChecker.GitAction action, List<PackageUpdateInfo> eligible)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("These packages will be processed, in this order (top of the dependency tree first):");
            builder.AppendLine();
            for (int i = 0; i < eligible.Count; i++)
                builder.AppendLine((i + 1) + ". " + eligible[i].DisplayName + "  (" + eligible[i].Git.Describe() + ")");

            builder.AppendLine();
            builder.AppendLine(action == PackageUpdateChecker.GitAction.Commit
                ? "Each commit message is the version in the package.json. Nothing is pushed."
                : action == PackageUpdateChecker.GitAction.Push
                    ? "Existing commits are pushed to origin. Nothing new is committed."
                    : "Each commit message is the version in the package.json, then the commits are pushed to origin.");

            if (EditorUtility.DisplayDialog(title, builder.ToString(), "Run", "Cancel"))
                PackageUpdateChecker.RunGit(action, eligible);
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
