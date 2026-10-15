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
