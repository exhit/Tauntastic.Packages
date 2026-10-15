using System;
using System.Collections.Generic;

namespace Tauntastic.Packages.Editor
{
    /// <summary>One package in a dependency listing and how many levels away from the starting package it is (1 = direct).</summary>
    public struct DependencyEntry
    {
        public string Name;
        public int Depth;

        public DependencyEntry(string name, int depth)
        {
            Name = name;
            Depth = depth;
        }
    }

    /// <summary>Directed graph of package dependencies. Answers "what does this package depend on" (its child dependencies, direct and indirect) and "what depends on this package" (its parent dependencies).</summary>
    public sealed class PackageDependencyGraph
    {
        private readonly Dictionary<string, List<string>> _children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _parents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers a package and the names of the packages it depends on.</summary>
        public void Add(string package, IEnumerable<string> dependencies)
        {
            if (string.IsNullOrEmpty(package))
                return;

            List<string> children = GetList(_children, package);
            if (dependencies == null)
                return;

            foreach (string dependency in dependencies)
            {
                if (string.IsNullOrEmpty(dependency) || string.Equals(dependency, package, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!Contains(children, dependency))
                    children.Add(dependency);

                List<string> parents = GetList(_parents, dependency);
                if (!Contains(parents, package))
                    parents.Add(package);
            }
        }

        /// <summary>Everything the package depends on, directly and indirectly, depth first. Each package is listed once.</summary>
        public List<DependencyEntry> GetDependencies(string package)
        {
            return Walk(_children, package);
        }

        /// <summary>Every package that depends on this one, directly and indirectly, depth first. Each package is listed once.</summary>
        public List<DependencyEntry> GetDependents(string package)
        {
            return Walk(_parents, package);
        }

        /// <summary>
        /// Orders packages from the top of the dependency tree to the bottom: a package always comes before the packages it depends on
        /// (grandparents first, then parents, then grandchildren). Packages on the same level are sorted by name. Packages that depend on
        /// each other in a cycle are put together at the end of the order they are found in.
        /// </summary>
        public List<string> GetInstallOrder(IEnumerable<string> packages)
        {
            var remaining = new List<string>();
            if (packages != null)
            {
                foreach (string package in packages)
                {
                    if (!string.IsNullOrEmpty(package) && !Contains(remaining, package))
                        remaining.Add(package);
                }
            }

            // For every package: which of the other given packages depend on it (directly or through packages that are not in the list).
            var dependents = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string package in remaining)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DependencyEntry entry in GetDependents(package))
                {
                    if (Contains(remaining, entry.Name))
                        set.Add(entry.Name);
                }
                dependents[package] = set;
            }

            var ordered = new List<string>();
            while (remaining.Count > 0)
            {
                var level = new List<string>();
                foreach (string package in remaining)
                {
                    bool blocked = false;
                    foreach (string dependent in dependents[package])
                    {
                        if (Contains(remaining, dependent))
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (!blocked)
                        level.Add(package);
                }

                // Only a dependency cycle leaves nothing free: take everything that is left.
                if (level.Count == 0)
                    level.AddRange(remaining);

                level.Sort(StringComparer.OrdinalIgnoreCase);
                ordered.AddRange(level);
                remaining.RemoveAll(p => Contains(level, p));
            }

            return ordered;
        }

        private static List<DependencyEntry> Walk(Dictionary<string, List<string>> edges, string start)
        {
            var result = new List<DependencyEntry>();
            if (string.IsNullOrEmpty(start))
                return result;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            visited.Add(start);
            Visit(edges, start, 1, visited, result);
            return result;
        }

        private static void Visit(Dictionary<string, List<string>> edges, string node, int depth, HashSet<string> visited, List<DependencyEntry> result)
        {
            List<string> next;
            if (!edges.TryGetValue(node, out next))
                return;

            foreach (string name in next)
            {
                if (!visited.Add(name))
                    continue;

                result.Add(new DependencyEntry(name, depth));
                Visit(edges, name, depth + 1, visited, result);
            }
        }

        private static List<string> GetList(Dictionary<string, List<string>> map, string key)
        {
            List<string> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<string>();
                map[key] = list;
            }
            return list;
        }

        private static bool Contains(List<string> list, string value)
        {
            foreach (string item in list)
            {
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
