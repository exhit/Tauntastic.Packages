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

        public GitPackageReference Reference;

        /// <summary>True while an update of this package is being installed.</summary>
        public bool IsUpdating;
    }
}
