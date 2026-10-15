using System;
using System.Collections.Generic;

namespace Tauntastic.Packages.Editor
{
    /// <summary>A repository from a GitHub repository list response.</summary>
    public sealed class GitHubRepository
    {
        public string Owner;
        public string Name;
        public string Description;
        public bool IsPrivate;
        public bool IsArchived;
        public bool IsFork;

        /// <summary>HTTPS git URL that the Package Manager can install, for example https://github.com/owner/repo.git.</summary>
        public string CloneUrl
        {
            get { return "https://github.com/" + Owner + "/" + Name + ".git"; }
        }

        /// <summary>Parses the JSON array returned by the GitHub "list repositories" endpoints. Returns an empty list when the text is not such an array.</summary>
        public static List<GitHubRepository> ParseList(string json)
        {
            var result = new List<GitHubRepository>();

            object root;
            if (!SimpleJson.TryParse(json, out root))
                return result;

            var array = root as List<object>;
            if (array == null)
                return result;

            foreach (object item in array)
            {
                var repository = item as Dictionary<string, object>;
                if (repository == null)
                    continue;

                Dictionary<string, object> owner = SimpleJson.GetObject(repository, "owner");
                string ownerName = SimpleJson.GetString(owner, "login");
                string name = SimpleJson.GetString(repository, "name");
                if (string.IsNullOrEmpty(ownerName) || string.IsNullOrEmpty(name))
                    continue;

                result.Add(new GitHubRepository
                {
                    Owner = ownerName,
                    Name = name,
                    Description = SimpleJson.GetString(repository, "description"),
                    IsPrivate = SimpleJson.GetBool(repository, "private"),
                    IsArchived = SimpleJson.GetBool(repository, "archived"),
                    IsFork = SimpleJson.GetBool(repository, "fork"),
                });
            }

            return result;
        }

        /// <summary>True when the repository belongs to the owner and its name starts with the prefix, ignoring case (for example "Tauntastic.").</summary>
        public bool Matches(string owner, string namePrefix)
        {
            return string.Equals(Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                   Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
