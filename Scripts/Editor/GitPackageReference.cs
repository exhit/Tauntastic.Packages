using System;
using System.Text.RegularExpressions;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Where a git package lives on GitHub, parsed from the package id Unity reports (for example "com.x.y@https://github.com/owner/repo.git?path=sub#main"), and the URLs derived from it.</summary>
    public sealed class GitPackageReference
    {
        private static readonly Regex _commitHash = new Regex(@"^[0-9a-fA-F]{7,40}$");
        private static readonly Regex _versionTag = new Regex(@"^v?\d+(\.\d+)*([\-+].*)?$");

        /// <summary>Package name, such as com.tauntastic.cloning.</summary>
        public string PackageName { get; private set; }

        public string Owner { get; private set; }
        public string Repository { get; private set; }

        /// <summary>Sub folder of the repository that holds the package (the ?path= query), or an empty string.</summary>
        public string Path { get; private set; }

        /// <summary>Branch to follow, or null when the dependency has no ref, a commit hash or a version tag (then the default branch is followed).</summary>
        public string Branch { get; private set; }

        /// <summary>The ref exactly as written in the package id (branch, tag or commit hash), or null.</summary>
        public string RequestedRef { get; private set; }

        /// <summary>The repository page, for example https://github.com/owner/repo.</summary>
        public string RepositoryUrl
        {
            get { return "https://github.com/" + Owner + "/" + Repository; }
        }

        /// <summary>URL of the package.json at the head of the followed branch.</summary>
        public string RawPackageJsonUrl
        {
            get
            {
                string folder = string.IsNullOrEmpty(Path) ? string.Empty : Path.Trim('/') + "/";
                return "https://raw.githubusercontent.com/" + Owner + "/" + Repository + "/" +
                       (Branch ?? "HEAD") + "/" + folder + "package.json";
            }
        }

        /// <summary>Git URL to hand to the Package Manager to update: follows the branch, never a pinned commit or tag.</summary>
        public string UpdateUrl
        {
            get
            {
                string url = "https://github.com/" + Owner + "/" + Repository + ".git";
                if (!string.IsNullOrEmpty(Path))
                    url += "?path=" + Path;
                if (!string.IsNullOrEmpty(Branch))
                    url += "#" + Branch;
                return url;
            }
        }

        /// <summary>Link to the changelog on GitHub.</summary>
        public string ChangelogUrl
        {
            get
            {
                string folder = string.IsNullOrEmpty(Path) ? string.Empty : Path.Trim('/') + "/";
                return RepositoryUrl + "/blob/" + (Branch ?? "HEAD") + "/" + folder + "CHANGELOG.md";
            }
        }

        /// <summary>Parses a package id of a git package. Returns false for non-GitHub or unrecognised ids.</summary>
        public static bool TryParse(string packageId, out GitPackageReference reference)
        {
            reference = null;
            if (string.IsNullOrEmpty(packageId))
                return false;

            int at = packageId.IndexOf('@');
            if (at <= 0 || at == packageId.Length - 1)
                return false;

            string name = packageId.Substring(0, at);
            string spec = packageId.Substring(at + 1).Trim();

            if (spec.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
                spec = spec.Substring(4);

            string refPart = null;
            int hash = spec.LastIndexOf('#');
            if (hash >= 0)
            {
                refPart = spec.Substring(hash + 1);
                spec = spec.Substring(0, hash);
            }

            string path = string.Empty;
            int query = spec.IndexOf('?');
            if (query >= 0)
            {
                string queryText = spec.Substring(query + 1);
                spec = spec.Substring(0, query);
                foreach (string pair in queryText.Split('&'))
                {
                    if (pair.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
                        path = pair.Substring(5);
                }
            }

            const string prefix = "https://github.com/";
            if (!spec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string[] segments = spec.Substring(prefix.Length).Trim('/').Split('/');
            if (segments.Length < 2 || segments[0].Length == 0 || segments[1].Length == 0)
                return false;

            string repository = segments[1];
            if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                repository = repository.Substring(0, repository.Length - 4);

            string branch = null;
            if (!string.IsNullOrEmpty(refPart) && !_commitHash.IsMatch(refPart) && !_versionTag.IsMatch(refPart))
                branch = refPart;

            reference = new GitPackageReference
            {
                PackageName = name,
                Owner = segments[0],
                Repository = repository,
                Path = path,
                Branch = branch,
                RequestedRef = string.IsNullOrEmpty(refPart) ? null : refPart,
            };
            return true;
        }
    }
}
