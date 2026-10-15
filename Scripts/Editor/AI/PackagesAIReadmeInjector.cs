#if UNITY_EDITOR
// Generated from ARTIFICIAL_INTELLIGENCE_README.md of com.tauntastic.packages.
// Keeps the paragraph below in sync with Assets/ARTIFICIAL_INTELLIGENCE_README.md so AI agents can read how to use this package.
namespace Tauntastic.AIReadme
{
    [UnityEditor.InitializeOnLoad]
    internal static class PackagesAIReadmeInjector
    {
        private const string FileName = "ARTIFICIAL_INTELLIGENCE_README.md";
        private const string Header = "# ARTIFICIAL INTELLIGENCE README";
        private const string PackageKey = "Package \"com.tauntastic.packages\"";

        private const string Paragraph = @"Package ""com.tauntastic.packages"" (Editor-only assembly and namespace Tauntastic.Packages.Editor, no dependencies): update notifications for Tauntastic packages installed from a GitHub git URL, which the Package Manager does not provide. It works on its own once imported, so do not write code to start it: on Editor start (at most once per interval, default 24 hours, skipped in batch mode and when offline) PackageUpdateChecker lists the registered packages whose name starts with ""com.tauntastic."" and whose source is Git, parses each package id with GitPackageReference.TryParse (GitHub HTTPS only; supports ?path= sub folders; a commit hash or version tag ref means follow the default branch HEAD, any other ref is a branch), downloads raw.githubusercontent.com/<owner>/<repo>/<branch>/package.json with UnityWebRequest, and compares its ""version"" with the installed one using PackageVersion.IsNewer(candidate, current) (major.minor.patch with optional pre-release tag; a release is newer than its pre-release). The Updates window shows one colored flag per package and per kind of failure (PackageFailureKind: NotFoundOrPrivate red, TokenRejected purple, Network blue, InvalidResponse amber; UpToDate green, UpdateAvailable orange, Checking gray, all from PackageStatusStyle), a strip of counts, and the number of updates in the tab title. When updates exist it logs a warning per package with the changelog URL and opens the PackageUpdateWindow once per distinct set of updates. Menus (kept first in Tools > Tauntastic by negative MenuItem priorities in PackageMenuPriority, and this package is also sorted first in the results list): Tools > Tauntastic > Packages > Updates (window with Check now, Update, Changelog and Repository buttons) and Check For Updates (forced, always reports). Settings are per-user EditorPrefs under Preferences > Tools > Tauntastic > Packages (PackageUpdateSettings: Enabled, IntervalHours, OpenWindowOnUpdate). From code use PackageUpdateChecker.CheckNow(bool userInitiated), the read-only Results (PackageUpdateInfo with Name, DisplayName, InstalledVersion, LatestVersion, Status Checking, UpToDate, UpdateAvailable or Failed, Error, Reference) and the Changed event, and PackageUpdateChecker.Update(info) which calls Client.Add with the branch URL (this unpins commit hashes and tags). For updates to be detected, a package repository must raise ""version"" in its package.json when releasing, and private GitHub repositories are only checked when the user sets an optional token in the preferences (PackageUpdateSettings.GitHubToken, a fine-grained personal access token with read-only Contents access, stored in plain text in EditorPrefs and sent only as an Authorization Bearer header to api.github.com, which is used instead of the raw URL whenever a token is set so errors include GitHub's own message), otherwise a 404 is reported as ""not found or private""; never log, print or commit the token. Packages stored inside Assets or Packages folders, registry packages and non-GitHub hosts are ignored. Never reference these Editor types from runtime code, and do not poll faster than the interval to avoid hitting GitHub rate limits.";

        static PackagesAIReadmeInjector()
        {
            UnityEditor.EditorApplication.delayCall += Inject;
        }

        private static void Inject()
        {
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
            {
                UnityEditor.EditorApplication.delayCall += Inject;
                return;
            }

            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, FileName);
                bool fileExisted = System.IO.File.Exists(path);
                string existingText = fileExisted ? Normalize(System.IO.File.ReadAllText(path)) : string.Empty;

                var paragraphs = new System.Collections.Generic.List<string>();
                if (fileExisted)
                {
                    foreach (string block in existingText.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = block.Trim();
                        if (trimmed.Length > 0)
                            paragraphs.Add(trimmed);
                    }
                }
                else
                {
                    paragraphs.Add(Header);
                }

                // Identical paragraph already present: add nothing.
                bool alreadyPresent = paragraphs.Contains(Paragraph);

                // Drop every other paragraph that refers to this same package (outdated versions).
                paragraphs.RemoveAll(p => p.StartsWith(PackageKey, System.StringComparison.Ordinal) && !string.Equals(p, Paragraph, System.StringComparison.Ordinal));

                if (!alreadyPresent)
                    paragraphs.Add(Paragraph);

                string newText = string.Join("\n\n", paragraphs) + "\n";
                if (fileExisted && string.Equals(newText, existingText, System.StringComparison.Ordinal))
                    return;

                System.IO.File.WriteAllText(path, newText, new System.Text.UTF8Encoding(false));
                UnityEditor.AssetDatabase.ImportAsset("Assets/" + FileName);
            }
            catch (System.Exception exception)
            {
                UnityEngine.Debug.LogWarning("[PackagesAIReadmeInjector] Could not update Assets/" + FileName + ": " + exception.Message);
            }
        }

        private static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
#endif