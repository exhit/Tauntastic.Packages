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
                    if (UnityEngine.GUILayout.Button("Open Updates window", UnityEngine.GUILayout.Width(180f)))
                        PackageUpdateWindow.Open();
                },
            };
        }
    }
}
