using System;
using System.Text.RegularExpressions;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Minimal semantic version (major.minor.patch with an optional pre-release tag) that can be parsed from a package.json version string and compared.</summary>
    public struct PackageVersion : IComparable<PackageVersion>
    {
        private static readonly Regex _pattern =
            new Regex(@"^\s*v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.\-]+))?(?:\+[0-9A-Za-z.\-]+)?\s*$");

        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;

        /// <summary>Pre-release tag such as "preview.1", or an empty string for a release.</summary>
        public readonly string PreRelease;

        public PackageVersion(int major, int minor, int patch, string preRelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? string.Empty;
        }

        /// <summary>Parses strings like "1.2.3", "1.2", "v1.2.3" or "1.2.3-preview.1". Returns false when the text is not a version.</summary>
        public static bool TryParse(string text, out PackageVersion version)
        {
            version = default(PackageVersion);
            if (string.IsNullOrEmpty(text))
                return false;

            Match match = _pattern.Match(text);
            if (!match.Success)
                return false;

            int major, minor = 0, patch = 0;
            if (!int.TryParse(match.Groups[1].Value, out major))
                return false;
            if (match.Groups[2].Success && !int.TryParse(match.Groups[2].Value, out minor))
                return false;
            if (match.Groups[3].Success && !int.TryParse(match.Groups[3].Value, out patch))
                return false;

            version = new PackageVersion(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : string.Empty);
            return true;
        }

        /// <summary>Orders by major, minor and patch; a release is newer than any pre-release of the same numbers.</summary>
        public int CompareTo(PackageVersion other)
        {
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

            bool thisPre = !string.IsNullOrEmpty(PreRelease);
            bool otherPre = !string.IsNullOrEmpty(other.PreRelease);
            if (thisPre != otherPre) return thisPre ? -1 : 1;

            return string.CompareOrdinal(PreRelease, other.PreRelease);
        }

        /// <summary>True when the text <paramref name="candidate"/> is a higher version than <paramref name="current"/>; false if either cannot be parsed.</summary>
        public static bool IsNewer(string candidate, string current)
        {
            PackageVersion a, b;
            if (!TryParse(candidate, out a) || !TryParse(current, out b))
                return false;
            return a.CompareTo(b) > 0;
        }

        public override string ToString()
        {
            string core = Major + "." + Minor + "." + Patch;
            return string.IsNullOrEmpty(PreRelease) ? core : core + "-" + PreRelease;
        }
    }
}
