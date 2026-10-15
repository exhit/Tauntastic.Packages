using System.Collections.Generic;
using UnityEditor;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Preferences page (Preferences > Tauntastic > Packages) for the update checker: on/off, check interval and whether the window opens by itself.</summary>
    public static class PackageUpdateSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(PackageUpdateSettings.PreferencesPath, SettingsScope.User)
            {
                label = "Packages",
                keywords = new HashSet<string> { "tauntastic", "package", "update", "version", "git" },
                guiHandler = _ =>
                {
                    EditorGUILayout.Space();

                    PackageUpdateSettings.Enabled = EditorGUILayout.Toggle("Check for updates", PackageUpdateSettings.Enabled);

                    using (new EditorGUI.DisabledScope(!PackageUpdateSettings.Enabled))
                    {
                        PackageUpdateSettings.IntervalHours =
                            EditorGUILayout.IntField("Hours between checks", PackageUpdateSettings.IntervalHours);
                        PackageUpdateSettings.OpenWindowOnUpdate =
                            EditorGUILayout.Toggle("Open window when an update is found", PackageUpdateSettings.OpenWindowOnUpdate);
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Packages not installed yet", EditorStyles.boldLabel);
                    PackageUpdateSettings.DiscoverPackages =
                        EditorGUILayout.Toggle("List installable packages", PackageUpdateSettings.DiscoverPackages);
                    using (new EditorGUI.DisabledScope(!PackageUpdateSettings.DiscoverPackages))
                    {
                        PackageUpdateSettings.GitHubOwner =
                            EditorGUILayout.TextField("GitHub user or organisation", PackageUpdateSettings.GitHubOwner);
                    }
                    EditorGUILayout.HelpBox(
                        "Repositories of this owner whose name starts with \"" + PackageUpdateSettings.RepositoryNamePrefix +
                        "\" and that contain a Unity package.json at their root are listed as installable. Private repositories need the token below.",
                        MessageType.None);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Private repositories", EditorStyles.boldLabel);
                    PackageUpdateSettings.GitHubToken =
                        EditorGUILayout.PasswordField("GitHub token (optional)", PackageUpdateSettings.GitHubToken);
                    EditorGUILayout.HelpBox(
                        "Only needed to check packages in private GitHub repositories. Create a fine-grained personal access token with " +
                        "read-only \"Contents\" access to those repositories. It is stored in plain text in this machine's EditorPrefs " +
                        "(never in the project) and is only sent to raw.githubusercontent.com.",
                        MessageType.Info);

                    EditorGUILayout.Space();
                    if (UnityEngine.GUILayout.Button("Open Updates window", UnityEngine.GUILayout.Width(180f)))
                        PackageUpdateWindow.Open();
                },
            };
        }
    }
}
