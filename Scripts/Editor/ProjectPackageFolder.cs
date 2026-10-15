using System;
using System.Collections.Generic;

namespace Tauntastic.Packages.Editor
{
    /// <summary>A folder of this Unity project (under Assets or Packages) that contains a package.json, so the package already exists in the project as source code.</summary>
    public sealed class ProjectPackageFolder
    {
        /// <summary>Folder path relative to the project root, with forward slashes, for example Assets/Tauntastic/com.tauntastic.cloning.</summary>
        public string Path;

        /// <summary>The "name" of the package.json in the folder.</summary>
        public string PackageName;

        /// <summary>The "version" of the package.json in the folder, or an empty string.</summary>
        public string Version;

        /// <summary>Repository name taken from the "repository" url of the package.json (for example Tauntastic.Cloning), or an empty string.</summary>
        public string RepositoryName;

        /// <summary>The name of the folder itself.</summary>
        public string FolderName
        {
            get
            {
                string trimmed = (Path ?? string.Empty).TrimEnd('/');
                int slash = trimmed.LastIndexOf('/');
                return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
            }
        }

        /// <summary>Reads the package.json found in a project folder. Returns false when it is not a JSON object with a name.</summary>
        public static bool TryParse(string projectRelativePath, string json, out ProjectPackageFolder folder)
        {
            folder = null;

            object root;
            if (!SimpleJson.TryParse(json, out root))
                return false;

            var dictionary = root as Dictionary<string, object>;
            if (dictionary == null)
                return false;

            string name = SimpleJson.GetString(dictionary, "name");
            if (string.IsNullOrEmpty(name))
                return false;

            string url = string.Empty;
            object repository;
            if (dictionary.TryGetValue("repository", out repository))
            {
                if (repository is string)
                    url = (string)repository;
                else
                    url = SimpleJson.GetString(repository as Dictionary<string, object>, "url");
            }

            folder = new ProjectPackageFolder
            {
                Path = (projectRelativePath ?? string.Empty).Replace('\\', '/'),
                PackageName = name,
                Version = SimpleJson.GetString(dictionary, "version"),
                RepositoryName = RepositoryNameFromUrl(url),
            };
            return true;
        }

        /// <summary>The last part of a repository URL without ".git", or an empty string.</summary>
        public static string RepositoryNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;

            string trimmed = url.Trim().TrimEnd('/');
            int hash = trimmed.IndexOf('#');
            if (hash >= 0)
                trimmed = trimmed.Substring(0, hash);

            int slash = trimmed.LastIndexOf('/');
            string name = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        /// <summary>True when this folder is the source of the package with that name or of the repository with that name: the package name matches, the repository url of the package.json matches, or the folder is named after the package (com.tauntastic.x) or the repository (Tauntastic.X).</summary>
        public bool Matches(string packageName, string repositoryName)
        {
            if (!string.IsNullOrEmpty(packageName))
            {
                if (string.Equals(PackageName, packageName, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(FolderName, packageName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (!string.IsNullOrEmpty(repositoryName))
            {
                if (string.Equals(RepositoryName, repositoryName, StringComparison.OrdinalIgnoreCase))
                    return true;

                string folder = FolderName;
                if (folder.StartsWith("com.", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(folder.Substring(4), repositoryName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
