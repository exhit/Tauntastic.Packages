namespace Tauntastic.Packages.Editor
{
    /// <summary>Progress of the update check of one package.</summary>
    public enum PackageUpdateStatus
    {
        Checking,
        UpToDate,
        UpdateAvailable,
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

        /// <summary>True while an update of this package is being installed.</summary>
        public bool IsUpdating;
    }
}
