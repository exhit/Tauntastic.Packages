using System.Collections.Generic;

namespace Tauntastic.Packages.Editor
{
    /// <summary>The parts of a Unity package.json that the update checker needs: name, version, display name, description, minimum Unity version and dependencies.</summary>
    public sealed class PackageManifest
    {
        public string Name;
        public string Version;
        public string DisplayName;
        public string Description;
        public string Unity;

        /// <summary>Dependency package name to version (or git URL), in the order written.</summary>
        public Dictionary<string, string> Dependencies = new Dictionary<string, string>();

        /// <summary>Reads a package.json. Returns false unless it is a JSON object with a name and a valid version, which is what makes a repository a Unity package.</summary>
        public static bool TryParse(string json, out PackageManifest manifest)
        {
            manifest = null;

            object root;
            if (!SimpleJson.TryParse(json, out root))
                return false;

            var dictionary = root as Dictionary<string, object>;
            if (dictionary == null)
                return false;

            string name = SimpleJson.GetString(dictionary, "name");
            string version = SimpleJson.GetString(dictionary, "version");
            PackageVersion parsed;
            if (string.IsNullOrEmpty(name) || !PackageVersion.TryParse(version, out parsed))
                return false;

            var result = new PackageManifest
            {
                Name = name,
                Version = version,
                DisplayName = SimpleJson.GetString(dictionary, "displayName"),
                Description = SimpleJson.GetString(dictionary, "description"),
                Unity = SimpleJson.GetString(dictionary, "unity"),
            };

            Dictionary<string, object> dependencies = SimpleJson.GetObject(dictionary, "dependencies");
            if (dependencies != null)
            {
                foreach (KeyValuePair<string, object> pair in dependencies)
                    result.Dependencies[pair.Key] = pair.Value == null ? string.Empty : pair.Value.ToString();
            }

            manifest = result;
            return true;
        }
    }
}
