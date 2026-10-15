using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Tauntastic.Packages.Editor
{
    /// <summary>What can be done with the git repository of a package folder right now.</summary>
    public sealed class GitState
    {
        /// <summary>False when the git executable could not be found.</summary>
        public bool GitAvailable = true;

        /// <summary>True when the folder is itself the root of a git repository (it has its own .git).</summary>
        public bool IsRepository;

        public string Branch = string.Empty;

        /// <summary>True when a remote named origin exists.</summary>
        public bool HasRemote;

        /// <summary>True when the current branch tracks a remote branch.</summary>
        public bool HasUpstream;

        /// <summary>True when the repository has at least one commit.</summary>
        public bool HasCommits;

        /// <summary>Number of files with uncommitted changes (modified, added, deleted or untracked).</summary>
        public int ChangedFiles;

        /// <summary>Number of commits that are not on the remote branch yet.</summary>
        public int AheadCount;

        /// <summary>Why the state could not be read, or an empty string.</summary>
        public string Error = string.Empty;

        /// <summary>There are uncommitted changes to commit.</summary>
        public bool CanCommit
        {
            get { return IsRepository && ChangedFiles > 0; }
        }

        /// <summary>There are commits that were not pushed: more commits than the remote branch has, or a branch without a remote branch yet.</summary>
        public bool CanPush
        {
            get { return IsRepository && HasRemote && HasCommits && (HasUpstream ? AheadCount > 0 : true); }
        }

        /// <summary>Committing, pushing or both would do something.</summary>
        public bool CanCommitAndPush
        {
            get { return CanCommit || CanPush; }
        }

        /// <summary>One line for the user, for example "main: 3 changed files, 1 commit not pushed".</summary>
        public string Describe()
        {
            if (!GitAvailable)
                return "Git was not found. Install Git or add it to the PATH.";
            if (!string.IsNullOrEmpty(Error))
                return "Git: " + Error;
            if (!IsRepository)
                return "This folder is not the root of a git repository.";

            var parts = new List<string>();
            parts.Add(ChangedFiles == 0 ? "no uncommitted changes" : ChangedFiles + (ChangedFiles == 1 ? " changed file" : " changed files"));
            if (!HasRemote)
                parts.Add("no remote named origin");
            else if (!HasUpstream)
                parts.Add(HasCommits ? "branch not on the remote yet" : "no commits yet");
            else
                parts.Add(AheadCount == 0 ? "nothing to push" : AheadCount + (AheadCount == 1 ? " commit not pushed" : " commits not pushed"));

            return (string.IsNullOrEmpty(Branch) ? string.Empty : Branch + ": ") + string.Join(", ", parts.ToArray());
        }
    }

    /// <summary>The outcome of a git command.</summary>
    public sealed class GitResult
    {
        public bool Success;
        public int ExitCode;
        public string Output = string.Empty;
        public string Error = string.Empty;

        /// <summary>The most useful text to show: the error output, otherwise the normal output.</summary>
        public string Message
        {
            get { return (string.IsNullOrEmpty(Error) ? Output : Error).Trim(); }
        }
    }

    /// <summary>Runs git for a package folder: reads its state and commits and pushes. Blocking; call it from a background thread. Needs only the git executable, no Unity.</summary>
    public static class GitRepository
    {
        private const int _READ_TIMEOUT_MS = 20000;
        private const int _PUSH_TIMEOUT_MS = 120000;

        private static string _gitPath;
        private static bool _searched;

        /// <summary>The git executable that is used, or null when none was found.</summary>
        public static string GitPath
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _gitPath = FindGit();
                }
                return _gitPath;
            }
        }

        /// <summary>Forgets the cached git location so the next call searches again.</summary>
        public static void ResetGitSearch()
        {
            _searched = false;
            _gitPath = null;
        }

        /// <summary>Reads the git state of a folder: uncommitted changes and commits that are not pushed.</summary>
        public static GitState Inspect(string folder)
        {
            var state = new GitState();

            if (GitPath == null)
            {
                state.GitAvailable = false;
                return state;
            }

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                state.Error = "The folder does not exist.";
                return state;
            }

            string dotGit = Path.Combine(folder, ".git");
            state.IsRepository = Directory.Exists(dotGit) || File.Exists(dotGit);
            if (!state.IsRepository)
                return state;

            GitResult branch = Run(folder, "rev-parse --abbrev-ref HEAD", _READ_TIMEOUT_MS);
            state.HasCommits = branch.Success;
            if (branch.Success)
                state.Branch = branch.Output.Trim();
            else
            {
                // A repository without commits has no HEAD yet.
                GitResult symbolic = Run(folder, "symbolic-ref --short HEAD", _READ_TIMEOUT_MS);
                if (symbolic.Success)
                    state.Branch = symbolic.Output.Trim();
            }

            state.HasRemote = Run(folder, "remote get-url origin", _READ_TIMEOUT_MS).Success;

            GitResult status = Run(folder, "status --porcelain", _READ_TIMEOUT_MS);
            if (!status.Success)
            {
                state.Error = status.Message;
                return state;
            }
            state.ChangedFiles = CountLines(status.Output);

            GitResult upstream = Run(folder, "rev-parse --abbrev-ref --symbolic-full-name @{u}", _READ_TIMEOUT_MS);
            state.HasUpstream = upstream.Success;
            if (state.HasUpstream)
            {
                GitResult ahead = Run(folder, "rev-list --count @{u}..HEAD", _READ_TIMEOUT_MS);
                int count;
                if (ahead.Success && int.TryParse(ahead.Output.Trim(), out count))
                    state.AheadCount = count;
            }

            return state;
        }

        /// <summary>Stages every change and commits it with the given message.</summary>
        public static GitResult Commit(string folder, string message)
        {
            GitResult add = Run(folder, "add -A", _READ_TIMEOUT_MS);
            if (!add.Success)
                return add;

            return Run(folder, "commit -m " + Quote(message), _READ_TIMEOUT_MS);
        }

        /// <summary>Pushes the current branch to origin, creating the remote branch when it does not exist yet.</summary>
        public static GitResult Push(string folder)
        {
            GitResult upstream = Run(folder, "rev-parse --abbrev-ref --symbolic-full-name @{u}", _READ_TIMEOUT_MS);
            return Run(folder, upstream.Success ? "push" : "push -u origin HEAD", _PUSH_TIMEOUT_MS);
        }

        private static int CountLines(string text)
        {
            int count = 0;
            foreach (string line in (text ?? string.Empty).Split('\n'))
            {
                if (line.Trim().Length > 0)
                    count++;
            }
            return count;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static GitResult Run(string workingDirectory, string arguments, int timeoutMilliseconds)
        {
            var result = new GitResult();
            string git = GitPath;
            if (git == null)
            {
                result.Error = "Git was not found.";
                return result;
            }

            try
            {
                var info = new ProcessStartInfo(git, arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                // Never wait for a password prompt that nobody can answer, and do not take the index lock for read-only commands.
                info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                info.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";

                var output = new StringBuilder();
                var error = new StringBuilder();

                using (var process = new Process { StartInfo = info })
                {
                    process.OutputDataReceived += (sender, e) => { if (e.Data != null) { lock (output) output.AppendLine(e.Data); } };
                    process.ErrorDataReceived += (sender, e) => { if (e.Data != null) { lock (error) error.AppendLine(e.Data); } };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        result.Error = "git " + arguments + " timed out.";
                        return result;
                    }

                    // The overload without a timeout also waits until the redirected output has been read completely.
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }

                lock (output) result.Output = output.ToString();
                lock (error) result.Error = error.ToString();
                result.Success = result.ExitCode == 0;
            }
            catch (Exception exception)
            {
                result.Error = exception.Message;
            }

            return result;
        }

        private static string FindGit()
        {
            // 1. Whatever "git" resolves to on the PATH.
            if (CanRun("git"))
                return "git";

            // 2. Common install locations.
            var candidates = new List<string>();
            string programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            string programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");

            if (!string.IsNullOrEmpty(programFiles)) candidates.Add(Path.Combine(programFiles, "Git", "cmd", "git.exe"));
            if (!string.IsNullOrEmpty(programFilesX86)) candidates.Add(Path.Combine(programFilesX86, "Git", "cmd", "git.exe"));
            if (!string.IsNullOrEmpty(localAppData))
            {
                candidates.Add(Path.Combine(localAppData, "Programs", "Git", "cmd", "git.exe"));

                // GitHub Desktop ships its own git inside its newest app-* folder.
                string desktop = Path.Combine(localAppData, "GitHubDesktop");
                if (Directory.Exists(desktop))
                {
                    string[] apps = Directory.GetDirectories(desktop, "app-*");
                    Array.Sort(apps, StringComparer.OrdinalIgnoreCase);
                    Array.Reverse(apps);
                    foreach (string app in apps)
                        candidates.Add(Path.Combine(app, "resources", "app", "git", "cmd", "git.exe"));
                }
            }

            candidates.Add("/usr/bin/git");
            candidates.Add("/usr/local/bin/git");
            candidates.Add("/opt/homebrew/bin/git");

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate) && CanRun(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool CanRun(string executable)
        {
            try
            {
                var info = new ProcessStartInfo(executable, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using (var process = Process.Start(info))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    return process.WaitForExit(10000) && process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
