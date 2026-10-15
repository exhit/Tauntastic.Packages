using System.Collections.Generic;
using System.Globalization;

namespace Tauntastic.Packages.Editor
{
    /// <summary>What the Updates window remembers between sessions: which package rows and Dependencies foldouts are collapsed or open, the toolbar choices, and the scroll position.</summary>
    public sealed class PackageWindowState
    {
        /// <summary>Names of the packages whose row is collapsed to a single line.</summary>
        public List<string> CollapsedPackages = new List<string>();

        /// <summary>Names of the packages whose Dependencies foldout is open.</summary>
        public List<string> OpenDependencyFoldouts = new List<string>();

        /// <summary>The "Show installable" toggle.</summary>
        public bool ShowInstallable = true;

        /// <summary>Vertical scroll position of the package list.</summary>
        public float ScrollY;

        /// <summary>Writes the state as JSON text.</summary>
        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            root["collapsedPackages"] = ToList(CollapsedPackages);
            root["openDependencyFoldouts"] = ToList(OpenDependencyFoldouts);
            root["showInstallable"] = ShowInstallable;
            root["scrollY"] = (double)ScrollY;
            return SimpleJsonWriter.Serialize(root);
        }

        /// <summary>Reads a state written by <see cref="ToJson"/>. Returns false when the text is not valid; missing values keep their defaults.</summary>
        public static bool TryParse(string json, out PackageWindowState state)
        {
            state = null;

            object parsed;
            if (!SimpleJson.TryParse(json, out parsed))
                return false;

            var root = parsed as Dictionary<string, object>;
            if (root == null)
                return false;

            var result = new PackageWindowState();
            result.CollapsedPackages = ReadStrings(root, "collapsedPackages");
            result.OpenDependencyFoldouts = ReadStrings(root, "openDependencyFoldouts");

            object value;
            if (root.TryGetValue("showInstallable", out value) && value is bool)
                result.ShowInstallable = (bool)value;
            if (root.TryGetValue("scrollY", out value) && value is double)
                result.ScrollY = (float)(double)value;

            state = result;
            return true;
        }

        /// <summary>A short text that changes whenever a choice changes (collapsed rows, open foldouts, the toggle), used to save only when something changed. The scroll position is not part of it, so scrolling does not write files.</summary>
        public string Signature()
        {
            return string.Join("|", CollapsedPackages.ToArray()) + "#" + string.Join("|", OpenDependencyFoldouts.ToArray()) + "#" +
                   (ShowInstallable ? "1" : "0");
        }

        private static List<object> ToList(List<string> values)
        {
            var list = new List<object>();
            foreach (string value in values)
                list.Add(value);
            return list;
        }

        private static List<string> ReadStrings(Dictionary<string, object> root, string key)
        {
            var result = new List<string>();
            object value;
            var array = root.TryGetValue(key, out value) ? value as List<object> : null;
            if (array == null)
                return result;

            foreach (object item in array)
            {
                string text = item as string;
                if (!string.IsNullOrEmpty(text) && !result.Contains(text))
                    result.Add(text);
            }
            return result;
        }
    }
}
