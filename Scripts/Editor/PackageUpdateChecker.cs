using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Tauntastic.Packages.Editor
{
    /// <summary>
    /// Finds every Tauntastic package installed from a GitHub git URL, downloads the package.json at the head of its branch and
    /// compares the version with the installed one. Runs automatically (at most once per interval) and on demand from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageUpdateChecker
    {
        private const string _NAME_PREFIX = "com.tauntastic.";
        private const string _MENU_PATH = "Tools/Tauntastic/Packages/";
        private const int _TIMEOUT_SECONDS = 15;

        [Serializable]
        private class RemotePackageJson
        {
            public string version;
        }

        private sealed class PendingRequest
        {
            public PackageUpdateInfo Info;
            public UnityWebRequest Request;
        }

        private static readonly List<PendingRequest> _pending = new List<PendingRequest>();
        private static readonly List<PackageUpdateInfo> _results = new List<PackageUpdateInfo>();
        private static bool _userInitiated;

        /// <summary>The result of the latest check, one entry per installed Tauntastic git package.</summary>
        public static IReadOnlyList<PackageUpdateInfo> Results
        {
            get { return _results; }
        }

        /// <summary>True while a check is running.</summary>
        public static bool IsChecking
        {
            get { return _pending.Count > 0; }
        }

        /// <summary>Raised on the main thread whenever <see cref="Results"/> changes.</summary>
        public static event Action Changed;

        static PackageUpdateChecker()
        {
            EditorApplication.delayCall += RunAutomaticCheck;
        }

        private static void RunAutomaticCheck()
        {
            if (Application.isBatchMode || !PackageUpdateSettings.IsAutomaticCheckDue())
                return;
            if (Application.internetReachability == NetworkReachability.NotReachable)
                return;

            CheckNow(false);
        }

        [MenuItem(_MENU_PATH + "Check For Updates", false, PackageMenuPriority.CheckForUpdates)]
        private static void CheckFromMenu()
        {
            CheckNow(true);
        }

        /// <summary>Starts a check of all installed Tauntastic git packages. Does nothing when one is already running.</summary>
        /// <param name="userInitiated">When true the outcome is always reported and the window is shown, even when nothing changed.</param>
        public static void CheckNow(bool userInitiated)
        {
            if (IsChecking)
                return;

            _userInitiated = userInitiated;
            _results.Clear();

            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package.source != PackageSource.Git)
                    continue;
                if (!package.name.StartsWith(_NAME_PREFIX, StringComparison.OrdinalIgnoreCase))
                    continue;

                GitPackageReference reference;
                if (!GitPackageReference.TryParse(package.packageId, out reference))
                    continue;

                var info = new PackageUpdateInfo
                {
                    Name = package.name,
                    DisplayName = string.IsNullOrEmpty(package.displayName) ? package.name : package.displayName,
                    InstalledVersion = package.version,
                    Reference = reference,
                };
                _results.Add(info);

                string token = PackageUpdateSettings.GitHubToken;
                bool hasToken = !string.IsNullOrEmpty(token);

                // With a token use the GitHub API (clear error messages, official fine-grained token support);
                // without one use the plain raw file URL, which needs no login for public repositories.
                UnityWebRequest request = UnityWebRequest.Get(hasToken ? reference.ApiPackageJsonUrl : reference.RawPackageJsonUrl);
                request.timeout = _TIMEOUT_SECONDS;

                if (hasToken)
                {
                    request.SetRequestHeader("Authorization", "Bearer " + token);
                    request.SetRequestHeader("Accept", "application/vnd.github.raw+json");
                    request.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
                }
                request.SendWebRequest();
                _pending.Add(new PendingRequest { Info = info, Request = request });
            }

            // This package always comes first in the list, the rest follow alphabetically.
            _results.Sort((a, b) =>
            {
                bool aSelf = a.Name == PackageMenuPriority.SelfPackageName;
                bool bSelf = b.Name == PackageMenuPriority.SelfPackageName;
                if (aSelf != bSelf)
                    return aSelf ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            if (_pending.Count == 0)
            {
                FinishCheck();
                return;
            }

            EditorApplication.update += Poll;
            RaiseChanged();
        }

        private static void Poll()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingRequest pending = _pending[i];
                if (!pending.Request.isDone)
                    continue;

                Resolve(pending);
                pending.Request.Dispose();
                _pending.RemoveAt(i);
            }

            if (_pending.Count > 0)
            {
                RaiseChanged();
                return;
            }

            EditorApplication.update -= Poll;
            FinishCheck();
        }

        private static void Resolve(PendingRequest pending)
        {
            PackageUpdateInfo info = pending.Info;

            if (pending.Request.result != UnityWebRequest.Result.Success)
            {
                info.Status = PackageUpdateStatus.Failed;
                PackageFailureKind kind;
                info.Error = DescribeFailure(pending.Request, out kind);
                info.FailureKind = kind;
                return;
            }

            RemotePackageJson remote = null;
            try
            {
                remote = JsonUtility.FromJson<RemotePackageJson>(pending.Request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                info.Status = PackageUpdateStatus.Failed;
                info.Error = exception.Message;
                info.FailureKind = PackageFailureKind.InvalidResponse;
                return;
            }

            PackageVersion parsed;
            if (remote == null || !PackageVersion.TryParse(remote.version, out parsed))
            {
                info.Status = PackageUpdateStatus.Failed;
                info.Error = "The remote package.json has no valid version.";
                info.FailureKind = PackageFailureKind.InvalidResponse;
                return;
            }

            info.LatestVersion = remote.version;
            info.Status = PackageVersion.IsNewer(remote.version, info.InstalledVersion)
                ? PackageUpdateStatus.UpdateAvailable
                : PackageUpdateStatus.UpToDate;
        }

        [Serializable]
        private class GitHubError
        {
            public string message;
        }

        /// <summary>The "message" GitHub puts in its error responses (for example "Bad credentials" or "Not Found"), or an empty string.</summary>
        private static string GitHubMessage(UnityWebRequest request)
        {
            try
            {
                string body = request.downloadHandler != null ? request.downloadHandler.text : null;
                if (string.IsNullOrEmpty(body))
                    return string.Empty;

                GitHubError error = JsonUtility.FromJson<GitHubError>(body);
                return error != null && !string.IsNullOrEmpty(error.message) ? error.message : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string DescribeFailure(UnityWebRequest request, out PackageFailureKind kind)
        {
            string text = DescribeFailureCore(request, out kind);
            string gitHub = GitHubMessage(request);
            return string.IsNullOrEmpty(gitHub) ? text : text + " GitHub says: \"" + gitHub + "\" (HTTP " + request.responseCode + ").";
        }

        private static string DescribeFailureCore(UnityWebRequest request, out PackageFailureKind kind)
        {
            bool hasToken = !string.IsNullOrEmpty(PackageUpdateSettings.GitHubToken);

            switch (request.responseCode)
            {
                case 404:
                    kind = PackageFailureKind.NotFoundOrPrivate;
                    return hasToken
                        ? "Not found. The repository or its package.json does not exist, or the GitHub token cannot see it. A fine-grained token must have the repository owner as resource owner, list this repository and have Contents: Read (organisations may need to approve the token)."
                        : "Not found. The repository does not exist or is private. For a private repository add a GitHub token in Preferences > Tools > Tauntastic > Packages.";
                case 401:
                case 403:
                    kind = PackageFailureKind.TokenRejected;
                    return hasToken
                        ? "GitHub rejected the token (expired, revoked or without access). Check it in Preferences > Tools > Tauntastic > Packages."
                        : "GitHub refused the request. Add a GitHub token in Preferences > Tools > Tauntastic > Packages.";
                default:
                    kind = request.result == UnityWebRequest.Result.ConnectionError
                        ? PackageFailureKind.Network
                        : PackageFailureKind.InvalidResponse;
                    return request.error;
            }
        }

        private static void FinishCheck()
        {
            PackageUpdateSettings.LastCheckUtc = DateTime.UtcNow;
            RaiseChanged();

            var signature = new StringBuilder();
            int updateCount = 0;
            foreach (PackageUpdateInfo info in _results)
            {
                if (info.Status != PackageUpdateStatus.UpdateAvailable)
                    continue;

                updateCount++;
                signature.Append(info.Name).Append('@').Append(info.LatestVersion).Append(';');
            }

            bool alreadyAnnounced = signature.ToString() == PackageUpdateSettings.LastNotified;

            if (updateCount == 0)
            {
                PackageUpdateSettings.LastNotified = string.Empty;
                if (_userInitiated)
                {
                    int failed = _results.FindAll(r => r.Status == PackageUpdateStatus.Failed).Count;
                    if (_results.Count == 0)
                        Debug.Log("[Tauntastic.Packages] No Tauntastic packages installed from a GitHub git URL were found.");
                    else if (failed > 0)
                        Debug.LogWarning("[Tauntastic.Packages] " + failed + " of " + _results.Count + " packages could not be checked. See the Updates window.");
                    else
                        Debug.Log("[Tauntastic.Packages] All installed Tauntastic packages are up to date.");
                    PackageUpdateWindow.Open();
                }
                return;
            }

            if (alreadyAnnounced && !_userInitiated)
                return;

            PackageUpdateSettings.LastNotified = signature.ToString();

            foreach (PackageUpdateInfo info in _results)
            {
                if (info.Status == PackageUpdateStatus.UpdateAvailable)
                {
                    Debug.LogWarning("[Tauntastic.Packages] " + info.DisplayName + " " + info.LatestVersion +
                                     " is available (installed: " + info.InstalledVersion + "). " +
                                     "Open Tools > Tauntastic > Packages > Updates to update it. " + info.Reference.ChangelogUrl);
                }
            }

            if (_userInitiated || PackageUpdateSettings.OpenWindowOnUpdate)
                PackageUpdateWindow.Open();
        }

        /// <summary>Installs the latest version of the package from its branch through the Package Manager.</summary>
        public static void Update(PackageUpdateInfo info)
        {
            if (info == null || info.IsUpdating)
                return;

            info.IsUpdating = true;
            AddRequest request = Client.Add(info.Reference.UpdateUrl);
            RaiseChanged();

            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= wait;
                info.IsUpdating = false;

                if (request.Status == StatusCode.Success)
                    Debug.Log("[Tauntastic.Packages] Updated " + info.DisplayName + " to " + request.Result.version + ".");
                else
                    Debug.LogError("[Tauntastic.Packages] Could not update " + info.DisplayName + ": " + request.Error.message);

                RaiseChanged();
            };
            EditorApplication.update += wait;
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
                handler();
        }
    }
}
