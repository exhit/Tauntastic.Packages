namespace Tauntastic.Packages.Editor
{
    /// <summary>Progress of the update check of one package.</summary>
    public enum PackageUpdateStatus
    {
        Checking,
        UpToDate,
        UpdateAvailable,

        /// <summary>The package exists in the GitHub account as a Unity package but is not installed in this project.</summary>
        Installable,

        /// <summary>The package is in the GitHub account and the project already has its folder (under Assets or Packages), so this is the source project of the package and it must not be installed again.</summary>
        SourceFolder,

        Failed,
    }

    /// <summary>Why the check of a package failed.</summary>
    public enum PackageFailureKind
    {
        None,

        /// <summary>HTTP 404: the repository or its package.json does not exist, or the repository is private and no (or an unauthorised) token was sent.</summary>
        NotFoundOrPrivate,

        /// <summary>HTTP 401 or 403: GitHub refused the request or the token.</summary>
        TokenRejected,

        /// <summary>The request did not complete: offline, timeout, DNS or other connection problem.</summary>
        Network,

        /// <summary>GitHub answered but the package.json could not be read or has no valid version.</summary>
        InvalidResponse,
    }

    /// <summary>What is known about one installed Tauntastic git package: its installed version, the latest version on GitHub and where to find it.</summary>
    public sealed class PackageUpdateInfo
    {
        public string Name;
        public string DisplayName;
        public string InstalledVersion;
        public string LatestVersion;
        public PackageUpdateStatus Status = PackageUpdateStatus.Checking;

        /// <summary>Why the check failed, when <see cref="Status"/> is Failed.</summary>
        public string Error;

        /// <summary>The kind of failure, when <see cref="Status"/> is Failed; None otherwise.</summary>
        public PackageFailureKind FailureKind;

        public GitPackageReference Reference;

        /// <summary>True when the package is installed in this project; false for packages that are only found in the GitHub account.</summary>
        public bool IsInstalled;

        /// <summary>Project relative path of the folder that holds this package inside the project (for example Assets/Tauntastic/com.tauntastic.cloning), or null.</summary>
        public string SourceFolderPath;

        /// <summary>Version written in the package.json of the source code, or an empty string.</summary>
        public string SourceFolderVersion;

        /// <summary>Full path of the source code folder on disk (for status SourceFolder), or null.</summary>
        public string SourceFolderAbsolutePath;

        /// <summary>True when the version in the source code folder is higher than the version in the GitHub repository, so the repository needs this update (commit and push it).</summary>
        public bool SourceAhead;

        /// <summary>The git state of the source code folder (uncommitted changes, unpushed commits); null until it was read.</summary>
        public GitState Git;

        /// <summary>True while a commit or push of this source folder is running.</summary>
        public bool IsGitBusy;

        /// <summary>True when the repository is private. Only meaningful when <see cref="IsVisibilityKnown"/> is true.</summary>
        public bool IsPrivateRepository;

        /// <summary>True when the GitHub repository list told whether the repository is private or public; false when the repository was not in the list (for example no token and the repository is private, or discovery is off).</summary>
        public bool IsVisibilityKnown;

        /// <summary>Description from the package.json (or the repository when the package.json has none).</summary>
        public string Description;

        /// <summary>Minimum Unity version from the package.json, or an empty string.</summary>
        public string Unity;

        /// <summary>The package.json at the head of the followed branch, used for the dependency lists; null until it was read.</summary>
        public PackageManifest Manifest;

        /// <summary>True while an update of this package is being installed.</summary>
        public bool IsUpdating;
    }
}
