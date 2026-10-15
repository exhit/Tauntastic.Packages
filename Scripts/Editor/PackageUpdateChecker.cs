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
            LoadSavedScanOrScan();
        }

        /// <summary>Called when the Updates window opens: shows the saved scan when it is still fresh, otherwise scans (when the automatic check is on).</summary>
        public static void ScanIfEmptyAndDue()
        {
            LoadSavedScanOrScan();
        }

        /// <summary>
        /// Fills the results when there are none. The newest saved scan is shown when it is younger than "hours between checks" and the
        /// installed packages are still the same as in that scan; no network request is made then. Otherwise a new scan is started, if the
        /// automatic check is on and we are online. With the automatic check off the saved scan is shown whatever its age.
        /// </summary>
        private static void LoadSavedScanOrScan()
        {
            if (_results.Count > 0 || IsChecking || Application.isBatchMode)
                return;

            PackageScanSnapshot snapshot = null;
            try
            {
                snapshot = Store.LoadLatestScan();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Tauntastic.Packages] Could not read the saved scan: " + exception.Message);
            }

            bool usable = snapshot != null && snapshot.MatchesInstalled(GetInstalledGitPackages()) && SourceFoldersExist(snapshot);
            bool fresh = snapshot != null && snapshot.IsFresh(DateTime.UtcNow, PackageUpdateSettings.IntervalHours);

            if (usable && (fresh || !PackageUpdateSettings.Enabled))
            {
                ApplySnapshot(snapshot);
                return;
            }

            if (!PackageUpdateSettings.Enabled)
                return;

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                if (usable)
                    ApplySnapshot(snapshot);
                return;
            }

            CheckNow(false);
        }

        private static PackageScanStore _store;

        /// <summary>The folder (Library/Tauntastic.Packages of this project, not versioned) where every scan and the window state are saved.</summary>
        public static PackageScanStore Store
        {
            get
            {
                if (_store == null)
                {
                    string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
                    _store = new PackageScanStore(System.IO.Path.Combine(root, "Library", "Tauntastic.Packages"));
                }
                return _store;
            }
        }

        /// <summary>When the scan that is shown finished (UTC), or null when there is no scan.</summary>
        public static DateTime? ShownScanUtc { get; private set; }

        /// <summary>True when the results shown were loaded from a saved scan instead of a scan made in this session.</summary>
        public static bool ShownFromSavedScan { get; private set; }

        /// <summary>The installed Tauntastic git packages now: package name to version.</summary>
        private static Dictionary<string, string> GetInstalledGitPackages()
        {
            var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
            {
                GitPackageReference reference;
                if (package.source == PackageSource.Git &&
                    package.name.StartsWith(_NAME_PREFIX, StringComparison.OrdinalIgnoreCase) &&
                    GitPackageReference.TryParse(package.packageId, out reference))
                    installed[package.name] = package.version;
            }
            return installed;
        }

        private static bool SourceFoldersExist(PackageScanSnapshot snapshot)
        {
            foreach (PackageUpdateInfo info in snapshot.Packages)
            {
                if (!string.IsNullOrEmpty(info.SourceFolderPath) && !System.IO.Directory.Exists(ToAbsolutePath(info.SourceFolderPath)))
                    return false;
            }
            return true;
        }

        private static void ApplySnapshot(PackageScanSnapshot snapshot)
        {
            _results.Clear();
            foreach (PackageUpdateInfo info in snapshot.Packages)
            {
                if (!string.IsNullOrEmpty(info.SourceFolderPath))
                    info.SourceFolderAbsolutePath = ToAbsolutePath(info.SourceFolderPath);
                _results.Add(info);
            }

            DiscoveryError = string.IsNullOrEmpty(snapshot.DiscoveryError) ? null : snapshot.DiscoveryError;
            ShownScanUtc = snapshot.SavedUtc;
            ShownFromSavedScan = true;

            SortResults();
            RebuildGraph();
            RefreshGitStates();
            RaiseChanged();
        }

        /// <summary>Saves the results as a scan file. With replaceLatest the newest file is corrected instead of adding one (used after a push).</summary>
        private static void SaveCurrentScan(bool replaceLatest)
        {
            try
            {
                var snapshot = new PackageScanSnapshot
                {
                    SavedUtc = replaceLatest && ShownScanUtc.HasValue ? ShownScanUtc.Value : DateTime.UtcNow,
                    Owner = PackageUpdateSettings.GitHubOwner,
                    DiscoveryError = DiscoveryError ?? string.Empty,
                    Packages = new List<PackageUpdateInfo>(_results),
                };
                Store.SaveScan(snapshot, replaceLatest);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Tauntastic.Packages] Could not save the scan: " + exception.Message);
            }
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
            // The repository list also tells whether each installed package's repository is private or public.
            foreach (PackageUpdateInfo installed in _results)
            {
                if (!installed.IsInstalled || installed.Reference == null)
                    continue;

                foreach (GitHubRepository listed in _repositories)
                {
                    if (string.Equals(listed.Owner, installed.Reference.Owner, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(listed.Name, installed.Reference.Repository, StringComparison.OrdinalIgnoreCase))
                    {
                        installed.IsPrivateRepository = listed.IsPrivate;
                        installed.IsVisibilityKnown = true;
                        break;
                    }
                }
            }

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
                    IsVisibilityKnown = true,
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
                info.SourceFolderAbsolutePath = folder != null ? ToAbsolutePath(folder.Path) : null;

                // The source code is ahead when its version is higher than the version in the repository: that counts as an update for the repository.
                info.SourceAhead = folder != null && PackageVersion.IsNewer(folder.Version, manifest.Version);
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
                        ? "GitHub rejected the token (expired, revoked, without access, or the API rate limit was hit). Check it in Preferences > Tools > Tauntastic > Packages (use Test token). " + GitHubTokenTester.Describe(PackageUpdateSettings.GitHubToken)
                        : "GitHub refused the request (possibly the unauthenticated API rate limit of 60 requests per hour). Add a GitHub token in Preferences > Tools > Tauntastic > Packages.";
                default:
                    kind = request.result == UnityWebRequest.Result.ConnectionError
                        ? PackageFailureKind.Network
                        : PackageFailureKind.InvalidResponse;
                    return request.error;
            }
        }

        private static string ToAbsolutePath(string projectRelativePath)
        {
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(root, projectRelativePath));
        }

        private static void FinishCheck()
        {
            RefreshGitStates();
            PackageUpdateSettings.LastCheckUtc = DateTime.UtcNow;
            RebuildGraph();

            // Every scan is saved, so the window can show it again without scanning (see LoadSavedScanOrScan).
            ShownScanUtc = DateTime.UtcNow;
            ShownFromSavedScan = false;
            SaveCurrentScan(false);
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
                                     "Open Tools > Tauntastic > Packages to update it. " + info.Reference.ChangelogUrl);
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

        // ---------------------------------------------------------------------------------------------------------------------
        // Source code packages: git state, commit and push
        // ---------------------------------------------------------------------------------------------------------------------

        /// <summary>What to do with the git repositories of source code packages.</summary>
        public enum GitAction
        {
            Commit,
            Push,
            CommitAndPush,
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> _mainThreadQueue =
            new System.Collections.Concurrent.ConcurrentQueue<Action>();
        private static int _activeJobs;
        private static bool _pumping;

        /// <summary>True while a commit or push of source code packages is running.</summary>
        public static bool IsGitBusy { get; private set; }

        /// <summary>Runs work on a background thread (git blocks) and then the callback on the main thread.</summary>
        private static void StartJob(Action background, Action onMainThread)
        {
            System.Threading.Interlocked.Increment(ref _activeJobs);
            EnsurePump();

            System.Threading.Tasks.Task.Run(() =>
            {
                Exception failure = null;
                try
                {
                    background();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                _mainThreadQueue.Enqueue(() =>
                {
                    System.Threading.Interlocked.Decrement(ref _activeJobs);
                    if (failure != null)
                        Debug.LogError("[Tauntastic.Packages] Git operation failed: " + failure.Message);
                    else
                        onMainThread();
                });
            });
        }

        private static void EnsurePump()
        {
            if (_pumping)
                return;

            _pumping = true;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            Action action;
            while (_mainThreadQueue.TryDequeue(out action))
                action();

            if (System.Threading.Volatile.Read(ref _activeJobs) == 0 && _mainThreadQueue.IsEmpty)
            {
                EditorApplication.update -= Pump;
                _pumping = false;
            }
        }

        /// <summary>Reads the git state (uncommitted changes, unpushed commits) of every source code package in the background.</summary>
        public static void RefreshGitStates()
        {
            foreach (PackageUpdateInfo info in _results)
            {
                if (info.Status != PackageUpdateStatus.SourceFolder || string.IsNullOrEmpty(info.SourceFolderAbsolutePath))
                    continue;

                PackageUpdateInfo target = info;
                string folder = info.SourceFolderAbsolutePath;
                GitState state = null;

                StartJob(
                    () => { state = GitRepository.Inspect(folder); },
                    () =>
                    {
                        target.Git = state;
                        RaiseChanged();
                    });
            }
        }

        /// <summary>The source code packages that the action would do something for, ordered from the top of the dependency tree to the bottom.</summary>
        public static List<PackageUpdateInfo> GetEligibleSourcePackages(GitAction action)
        {
            var byName = new Dictionary<string, PackageUpdateInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageUpdateInfo info in _results)
            {
                if (info.Status != PackageUpdateStatus.SourceFolder || info.IsGitBusy || info.Git == null)
                    continue;

                if (IsEligible(info.Git, action) && !byName.ContainsKey(info.Name))
                    byName.Add(info.Name, info);
            }

            var ordered = new List<PackageUpdateInfo>();
            foreach (string name in _graph.GetInstallOrder(byName.Keys))
                ordered.Add(byName[name]);
            return ordered;
        }

        /// <summary>True when the action would do something for a repository in this state.</summary>
        public static bool IsEligible(GitState state, GitAction action)
        {
            if (state == null)
                return false;

            switch (action)
            {
                case GitAction.Commit: return state.CanCommit;
                case GitAction.Push: return state.CanPush;
                default: return state.CanCommitAndPush;
            }
        }

        /// <summary>
        /// Commits (message = the version in the package.json) and/or pushes the given source code packages, one after the other, in the
        /// order given. A package is only touched when it is eligible at that moment, so this is safe to call on a stale list.
        /// </summary>
        public static void RunGit(GitAction action, List<PackageUpdateInfo> targets)
        {
            if (IsGitBusy || targets == null || targets.Count == 0)
                return;

            var work = new List<PackageUpdateInfo>(targets);
            foreach (PackageUpdateInfo info in work)
                info.IsGitBusy = true;
            IsGitBusy = true;
            RaiseChanged();

            bool doCommit = action != GitAction.Push;
            bool doPush = action != GitAction.Commit;
            var outcomes = new List<string>();
            var failures = new List<string>();
            var pushedVersions = new Dictionary<PackageUpdateInfo, string>();

            StartJob(
                () =>
                {
                    foreach (PackageUpdateInfo info in work)
                    {
                        string folder = info.SourceFolderAbsolutePath;
                        string version = ReadVersion(folder, info.SourceFolderVersion);
                        string name = info.DisplayName;

                        if (doCommit)
                        {
                            GitState before = GitRepository.Inspect(folder);
                            if (before.CanCommit)
                            {
                                GitResult commit = GitRepository.Commit(folder, version);
                                if (commit.Success)
                                    outcomes.Add(name + ": committed \"" + version + "\".");
                                else
                                {
                                    failures.Add(name + ": commit failed: " + commit.Message);
                                    continue;
                                }
                            }
                        }

                        if (doPush)
                        {
                            GitState before = GitRepository.Inspect(folder);
                            if (before.CanPush)
                            {
                                GitResult push = GitRepository.Push(folder);
                                if (push.Success)
                                {
                                    outcomes.Add(name + ": pushed.");
                                    pushedVersions[info] = version;
                                }
                                else
                                    failures.Add(name + ": push failed: " + push.Message);
                            }
                        }
                    }
                },
                () =>
                {
                    foreach (PackageUpdateInfo info in work)
                    {
                        info.IsGitBusy = false;

                        string pushed;
                        if (pushedVersions.TryGetValue(info, out pushed) && !string.IsNullOrEmpty(pushed))
                        {
                            // GitHub's raw files are cached for a few minutes, so do not wait for the next scan to show the new version.
                            info.LatestVersion = pushed;
                            info.SourceAhead = false;
                        }
                    }

                    IsGitBusy = false;

                    foreach (string line in outcomes)
                        Debug.Log("[Tauntastic.Packages] " + line);
                    foreach (string line in failures)
                        Debug.LogError("[Tauntastic.Packages] " + line);
                    if (outcomes.Count == 0 && failures.Count == 0)
                        Debug.Log("[Tauntastic.Packages] Nothing to commit or push.");

                    // The pushed versions changed the results: correct the newest saved scan so it does not bring the old versions back.
                    SaveCurrentScan(true);
                    RefreshGitStates();
                    RaiseChanged();
                });
        }

        /// <summary>The version written in the package.json of a folder, or the fallback.</summary>
        private static string ReadVersion(string folder, string fallback)
        {
            try
            {
                object root;
                string file = System.IO.Path.Combine(folder, "package.json");
                if (System.IO.File.Exists(file) && SimpleJson.TryParse(System.IO.File.ReadAllText(file), out root))
                {
                    string version = SimpleJson.GetString(root as Dictionary<string, object>, "version");
                    if (!string.IsNullOrEmpty(version))
                        return version;
                }
            }
            catch (System.IO.IOException)
            {
            }

            return string.IsNullOrEmpty(fallback) ? "update" : fallback;
        }

        /// <summary>True while an "update all" operation is running.</summary>
        public static bool IsUpdatingAll { get; private set; }

        /// <summary>
        /// Every package that can be updated (UpdateAvailable) or installed (Installable), ordered from the top of the dependency tree
        /// to the bottom: grandparents first, then parents, then grandchildren. Source folders, failed and up-to-date packages are left out.
        /// </summary>
        public static List<PackageUpdateInfo> GetPendingInTreeOrder(bool includeUpdates = true, bool includeInstalls = true)
        {
            var byName = new Dictionary<string, PackageUpdateInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageUpdateInfo info in _results)
            {
                bool pending = (includeUpdates && info.Status == PackageUpdateStatus.UpdateAvailable) || (includeInstalls && info.Status == PackageUpdateStatus.Installable);
                if (pending && !info.IsUpdating && !byName.ContainsKey(info.Name))
                    byName.Add(info.Name, info);
            }

            var ordered = new List<PackageUpdateInfo>();
            foreach (string name in _graph.GetInstallOrder(byName.Keys))
                ordered.Add(byName[name]);
            return ordered;
        }

        /// <summary>
        /// Updates and installs every pending package in one Package Manager operation, in the order of <see cref="GetPendingInTreeOrder"/>
        /// (grandparents to grandchildren). One operation means Unity resolves everything together and reloads scripts only once.
        /// </summary>
        public static void UpdateAll(bool includeUpdates = true, bool includeInstalls = true)
        {
            if (IsUpdatingAll || IsChecking)
                return;

            List<PackageUpdateInfo> pending = GetPendingInTreeOrder(includeUpdates, includeInstalls);
            if (pending.Count == 0)
                return;

            var urls = new string[pending.Count];
            for (int i = 0; i < pending.Count; i++)
            {
                urls[i] = pending[i].Reference.UpdateUrl;
                pending[i].IsUpdating = true;
            }

            IsUpdatingAll = true;
            AddAndRemoveRequest request = Client.AddAndRemove(urls);
            RaiseChanged();

            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= wait;
                IsUpdatingAll = false;
                foreach (PackageUpdateInfo info in pending)
                    info.IsUpdating = false;

                if (request.Status == StatusCode.Success)
                    Debug.Log("[Tauntastic.Packages] Updated or installed " + pending.Count + " packages: " + string.Join(", ", ToNames(pending)) + ".");
                else
                    Debug.LogError("[Tauntastic.Packages] Update all failed, nothing was changed: " + request.Error.message);

                RaiseChanged();
            };
            EditorApplication.update += wait;
        }

        private static List<string> ToNames(List<PackageUpdateInfo> infos)
        {
            var names = new List<string>();
            foreach (PackageUpdateInfo info in infos)
                names.Add(info.DisplayName);
            return names;
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
                handler();
        }
    }
}
