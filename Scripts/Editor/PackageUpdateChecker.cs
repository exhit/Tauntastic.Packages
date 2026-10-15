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
    /// compares the version with the installed one. It also lists the repositories of the configured GitHub owner whose name starts
    /// with "Tauntastic." and that hold a Unity package, so packages that are not installed yet show up as installable.
    /// Runs automatically (at most once per interval) and on demand from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageUpdateChecker
    {
        private const string _NAME_PREFIX = "com.tauntastic.";
        private const string _MENU_PATH = "Tools/Tauntastic/Packages/";
        private const int _TIMEOUT_SECONDS = 15;
        private const int _PAGE_SIZE = 100;
        private const int _MAX_PAGES = 10;

        /// <summary>A running download of one package.json together with the package it belongs to and whether that package is only a candidate found in the GitHub repository list.</summary>
        private sealed class PendingRequest
        {
            public PackageUpdateInfo Info;
            public UnityWebRequest Request;

            /// <summary>True for a repository found through the GitHub list that is not installed; it only becomes a result when it turns out to be a Unity package.</summary>
            public bool IsCandidate;
        }

        private static readonly List<PendingRequest> _pending = new List<PendingRequest>();
        private static readonly List<PackageUpdateInfo> _results = new List<PackageUpdateInfo>();
        private static readonly List<GitHubRepository> _repositories = new List<GitHubRepository>();
        private static UnityWebRequest _listRequest;
        private static int _listPage;
        private static bool _userInitiated;
        private static bool _polling;
        private static PackageDependencyGraph _graph = new PackageDependencyGraph();
        private static List<ProjectPackageFolder> _projectFolders;

        /// <summary>The result of the latest check: installed Tauntastic git packages and, when discovery is on, installable ones.</summary>
        public static IReadOnlyList<PackageUpdateInfo> Results
        {
            get { return _results; }
        }

        /// <summary>True while a check is running.</summary>
        public static bool IsChecking
        {
            get { return _pending.Count > 0 || _listRequest != null; }
        }

        /// <summary>Why listing the GitHub repositories failed, or null when it worked or was not tried.</summary>
        public static string DiscoveryError { get; private set; }

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

        /// <summary>Called when the Updates window opens: scans when there are no results yet, the automatic check is on and the last scan is older than the interval in the preferences.</summary>
        public static void ScanIfEmptyAndDue()
        {
            if (_results.Count > 0 || IsChecking || Application.isBatchMode)
                return;
            if (!PackageUpdateSettings.IsAutomaticCheckDue())
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

        /// <summary>Starts a check of all installed Tauntastic git packages and of the GitHub account. Does nothing when one is already running.</summary>
        /// <param name="userInitiated">When true the outcome is always reported and the window is shown, even when nothing changed.</param>
        public static void CheckNow(bool userInitiated)
        {
            if (IsChecking)
                return;

            _userInitiated = userInitiated;
            _results.Clear();
            _repositories.Clear();
            _projectFolders = null;
            DiscoveryError = null;

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
                    Description = package.description,
                    Reference = reference,
                    IsInstalled = true,
                };
                _results.Add(info);
                StartManifestRequest(info, false);
            }

            SortResults();
            StartDiscovery();

            if (!IsChecking)
            {
                FinishCheck();
                return;
            }

            if (!_polling)
            {
                _polling = true;
                EditorApplication.update += Poll;
            }
            RaiseChanged();
        }

        private static UnityWebRequest CreateRequest(string url)
        {
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = _TIMEOUT_SECONDS;

            string token = PackageUpdateSettings.GitHubToken;
            if (!string.IsNullOrEmpty(token))
                request.SetRequestHeader("Authorization", "Bearer " + token);

            request.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
            return request;
        }

        private static void StartManifestRequest(PackageUpdateInfo info, bool isCandidate)
        {
            // With a token use the GitHub API (clear error messages, official fine-grained token support);
            // without one use the plain raw file URL, which needs no login for public repositories.
            bool hasToken = !string.IsNullOrEmpty(PackageUpdateSettings.GitHubToken);
            UnityWebRequest request = CreateRequest(hasToken ? info.Reference.ApiPackageJsonUrl : info.Reference.RawPackageJsonUrl);
            if (hasToken)
                request.SetRequestHeader("Accept", "application/vnd.github.raw+json");

            request.SendWebRequest();
            _pending.Add(new PendingRequest { Info = info, Request = request, IsCandidate = isCandidate });
        }

        private static void StartDiscovery()
        {
            if (!PackageUpdateSettings.DiscoverPackages)
                return;

            _listPage = 1;
            SendListRequest();
        }

        private static void SendListRequest()
        {
            string owner = PackageUpdateSettings.GitHubOwner;
            bool hasToken = !string.IsNullOrEmpty(PackageUpdateSettings.GitHubToken);

            // With a token the authenticated list also contains private repositories; it is filtered by owner afterwards.
            string url = hasToken
                ? "https://api.github.com/user/repos?per_page=" + _PAGE_SIZE + "&affiliation=owner,organization_member&page=" + _listPage
                : "https://api.github.com/users/" + owner + "/repos?per_page=" + _PAGE_SIZE + "&type=owner&page=" + _listPage;

            _listRequest = CreateRequest(url);
            _listRequest.SetRequestHeader("Accept", "application/vnd.github+json");
            _listRequest.SendWebRequest();
        }

        private static void Poll()
        {
            if (_listRequest != null && _listRequest.isDone)
                HandleListResponse();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingRequest pending = _pending[i];
                if (!pending.Request.isDone)
                    continue;

                Resolve(pending);
                pending.Request.Dispose();
                _pending.RemoveAt(i);
            }

            if (IsChecking)
            {
                RaiseChanged();
                return;
            }

            EditorApplication.update -= Poll;
            _polling = false;
            FinishCheck();
        }

        private static void HandleListResponse()
        {
            UnityWebRequest request = _listRequest;
            _listRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                PackageFailureKind ignored;
                DiscoveryError = "Could not list the repositories of " + PackageUpdateSettings.GitHubOwner + ": " + DescribeFailure(request, out ignored);
                request.Dispose();
                return;
            }

            string body = request.downloadHandler.text;
            request.Dispose();

            List<GitHubRepository> page = GitHubRepository.ParseList(body);
            _repositories.AddRange(page);

            if (page.Count >= _PAGE_SIZE && _listPage < _MAX_PAGES)
            {
                _listPage++;
                SendListRequest();
                return;
            }

            StartCandidateRequests();
        }

        /// <summary>For every repository of the owner named Tauntastic.* that is not installed, reads its package.json to find out whether it is a Unity package.</summary>
        private static void StartCandidateRequests()
        {
            string owner = PackageUpdateSettings.GitHubOwner;

            foreach (GitHubRepository repository in _repositories)
            {
                if (!repository.Matches(owner, PackageUpdateSettings.RepositoryNamePrefix))
                    continue;
                if (IsRepositoryInstalled(repository))
                    continue;

                GitPackageReference reference;
                if (!GitPackageReference.TryParse("candidate@" + repository.CloneUrl, out reference))
                    continue;

                var info = new PackageUpdateInfo
                {
                    Name = repository.Name,
                    DisplayName = repository.Name,
                    Description = repository.Description,
                    Reference = reference,
                    IsInstalled = false,
                    IsPrivateRepository = repository.IsPrivate,
                };
                StartManifestRequest(info, true);
            }
        }

        private static bool IsRepositoryInstalled(GitHubRepository repository)
        {
            foreach (PackageUpdateInfo info in _results)
            {
                if (info.IsInstalled && info.Reference != null &&
                    string.Equals(info.Reference.Owner, repository.Owner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(info.Reference.Repository, repository.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void Resolve(PendingRequest pending)
        {
            PackageUpdateInfo info = pending.Info;

            if (pending.Request.result != UnityWebRequest.Result.Success)
            {
                // A repository without a root package.json is simply not a Unity package: leave it out of the list.
                if (pending.IsCandidate && pending.Request.responseCode == 404)
                    return;

                info.Status = PackageUpdateStatus.Failed;
                PackageFailureKind kind;
                info.Error = DescribeFailure(pending.Request, out kind);
                info.FailureKind = kind;
                if (pending.IsCandidate)
                    AddResult(info);
                return;
            }

            PackageManifest manifest;
            if (!PackageManifest.TryParse(pending.Request.downloadHandler.text, out manifest))
            {
                // Not a Unity package (no name and valid version in the package.json).
                if (pending.IsCandidate)
                    return;

                info.Status = PackageUpdateStatus.Failed;
                info.Error = "The remote package.json has no valid name and version.";
                info.FailureKind = PackageFailureKind.InvalidResponse;
                return;
            }

            info.Manifest = manifest;
            info.LatestVersion = manifest.Version;
            info.Unity = manifest.Unity;
            if (!string.IsNullOrEmpty(manifest.Description))
                info.Description = manifest.Description;

            if (pending.IsCandidate)
            {
                // The same package may already be installed from another URL.
                if (FindByName(manifest.Name) != null)
                    return;

                info.Name = manifest.Name;
                info.DisplayName = string.IsNullOrEmpty(manifest.DisplayName) ? manifest.Name : manifest.DisplayName;
                // The project already holds the folder of this package (this is its source project): never offer to install it again.
                ProjectPackageFolder folder = FindProjectFolder(manifest.Name, info.Reference.Repository);
                info.SourceFolderPath = folder != null ? folder.Path : null;
                info.SourceFolderVersion = folder != null ? folder.Version : string.Empty;
                info.Status = folder != null ? PackageUpdateStatus.SourceFolder : PackageUpdateStatus.Installable;
                AddResult(info);
                return;
            }

            info.Status = PackageVersion.IsNewer(manifest.Version, info.InstalledVersion)
                ? PackageUpdateStatus.UpdateAvailable
                : PackageUpdateStatus.UpToDate;
        }

        /// <summary>The folder of this project (under Assets or Packages) that holds the package, or null. Found by its package.json: same package name, same repository, or a folder named after the package.</summary>
        private static ProjectPackageFolder FindProjectFolder(string packageName, string repositoryName)
        {
            foreach (ProjectPackageFolder folder in GetProjectFolders())
            {
                if (folder.Matches(packageName, repositoryName))
                    return folder;
            }
            return null;
        }

        /// <summary>Every package.json under the project's Assets and Packages folders, scanned once per check.</summary>
        private static List<ProjectPackageFolder> GetProjectFolders()
        {
            if (_projectFolders != null)
                return _projectFolders;

            var folders = new List<ProjectPackageFolder>();
            try
            {
                string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
                ScanForPackageJson(folders, root, Application.dataPath);
                ScanForPackageJson(folders, root, System.IO.Path.Combine(root, "Packages"));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Tauntastic.Packages] Could not scan the project for package folders: " + exception.Message);
            }

            _projectFolders = folders;
            return folders;
        }

        private static void ScanForPackageJson(List<ProjectPackageFolder> folders, string projectRoot, string directory)
        {
            if (!System.IO.Directory.Exists(directory))
                return;

            foreach (string file in System.IO.Directory.EnumerateFiles(directory, "package.json", System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    string folderPath = System.IO.Path.GetDirectoryName(file);
                    string relative = folderPath.Substring(projectRoot.Length).TrimStart('\\', '/');

                    ProjectPackageFolder folder;
                    if (ProjectPackageFolder.TryParse(relative, System.IO.File.ReadAllText(file), out folder))
                        folders.Add(folder);
                }
                catch (System.IO.IOException)
                {
                    // An unreadable file is simply not a package folder.
                }
            }
        }

        private static void AddResult(PackageUpdateInfo info)
        {
            _results.Add(info);
            SortResults();
        }

        /// <summary>This package first, then installed packages, then installable ones; each group alphabetical.</summary>
        private static void SortResults()
        {
            _results.Sort((a, b) =>
            {
                bool aSelf = a.Name == PackageMenuPriority.SelfPackageName;
                bool bSelf = b.Name == PackageMenuPriority.SelfPackageName;
                if (aSelf != bSelf)
                    return aSelf ? -1 : 1;
                if (a.IsInstalled != b.IsInstalled)
                    return a.IsInstalled ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>The known package with this package name (installed or installable), or null.</summary>
        public static PackageUpdateInfo FindByName(string packageName)
        {
            foreach (PackageUpdateInfo info in _results)
            {
                if (string.Equals(info.Name, packageName, StringComparison.OrdinalIgnoreCase))
                    return info;
            }
            return null;
        }

        /// <summary>Everything the package depends on (its child dependencies), direct and indirect, depth first.</summary>
        public static List<DependencyEntry> GetDependencies(PackageUpdateInfo info)
        {
            return _graph.GetDependencies(info.Name);
        }

        /// <summary>Every known package that depends on this one (its parent dependencies), direct and indirect, depth first.</summary>
        public static List<DependencyEntry> GetDependents(PackageUpdateInfo info)
        {
            return _graph.GetDependents(info.Name);
        }

        /// <summary>Short text about a package name found in a dependency list: installed version, installable, or external.</summary>
        public static string DescribeDependency(string packageName)
        {
            PackageUpdateInfo known = FindByName(packageName);
            if (known != null)
            {
                if (known.IsInstalled)
                    return "installed " + known.InstalledVersion;
                if (known.Status == PackageUpdateStatus.Installable)
                    return "installable " + known.LatestVersion;
                if (known.Status == PackageUpdateStatus.SourceFolder)
                    return "source code in the project";
                return "unavailable";
            }

            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.Equals(package.name, packageName, StringComparison.OrdinalIgnoreCase))
                    return "installed " + package.version;
            }

            return "not installed";
        }

        private static void RebuildGraph()
        {
            var graph = new PackageDependencyGraph();
            foreach (PackageUpdateInfo info in _results)
            {
                if (info.Manifest != null)
                    graph.Add(info.Name, info.Manifest.Dependencies.Keys);
            }
            _graph = graph;
        }

        /// <summary>Shape of the error body GitHub returns, used to read its message.</summary>
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
                        ? "GitHub rejected the token (expired, revoked, without access, or the API rate limit was hit). Check it in Preferences > Tools > Tauntastic > Packages."
                        : "GitHub refused the request (possibly the unauthenticated API rate limit of 60 requests per hour). Add a GitHub token in Preferences > Tools > Tauntastic > Packages.";
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
            RebuildGraph();
            RaiseChanged();

            var signature = new StringBuilder();
            int updateCount = 0;
            int installableCount = 0;
            foreach (PackageUpdateInfo info in _results)
            {
                if (info.Status == PackageUpdateStatus.Installable)
                    installableCount++;

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
                        Debug.Log("[Tauntastic.Packages] No Tauntastic packages were found.");
                    else if (failed > 0)
                        Debug.LogWarning("[Tauntastic.Packages] " + failed + " of " + _results.Count + " packages could not be checked. See the Updates window.");
                    else
                        Debug.Log("[Tauntastic.Packages] All installed Tauntastic packages are up to date." +
                                  (installableCount > 0 ? " " + installableCount + " more can be installed." : string.Empty));

                    if (!string.IsNullOrEmpty(DiscoveryError))
                        Debug.LogWarning("[Tauntastic.Packages] " + DiscoveryError);
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
            InstallOrUpdate(info, "Updated");
        }

        /// <summary>Installs a package that is only found in the GitHub account through the Package Manager. Its own dependencies given as git URLs in its package.json are installed by Unity as well.</summary>
        public static void Install(PackageUpdateInfo info)
        {
            InstallOrUpdate(info, "Installed");
        }

        private static void InstallOrUpdate(PackageUpdateInfo info, string pastTense)
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
                    Debug.Log("[Tauntastic.Packages] " + pastTense + " " + info.DisplayName + " " + request.Result.version + ".");
                else
                    Debug.LogError("[Tauntastic.Packages] Could not install " + info.DisplayName + ": " + request.Error.message);

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
