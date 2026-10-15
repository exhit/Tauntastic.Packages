# Tauntastic.Packages

Tells you when a newer version of an installed Tauntastic package is available. Unity's Package Manager does not notify you about updates of packages installed from a git URL; this package fills that gap with a version checker, an Updates window and one-click updates.

- Package: `com.tauntastic.packages` (v1.1.0, Unity 2023.2+)
- Assembly / namespace: `Tauntastic.Packages.Editor` (Editor-only)
- Dependencies: none
- AI agents: see [ARTIFICIAL_INTELLIGENCE_README.md](ARTIFICIAL_INTELLIGENCE_README.md)

## Installation

```
https://github.com/exhit/Tauntastic.Packages.git
```

## How it works

1. It lists the installed packages whose name starts with `com.tauntastic.` and that were installed from a **GitHub git URL**.
2. For each one it downloads `package.json` from the head of the package's branch on `raw.githubusercontent.com` (public repositories need no login; private ones need the optional GitHub token below).
3. It compares the `version` there with the installed version (`major.minor.patch`, optional `-preview` tag). A higher remote version means an update is available.
4. If updates are found it logs a warning per package (with the changelog link) and opens the Updates window. The same set of updates is announced only once.

The automatic check runs when the Editor starts, at most once every 24 hours, and silently does nothing when you are offline, in batch mode, or when the check is switched off.

Which branch is followed:

| Git URL in the manifest | Version compared with |
|---|---|
| no `#ref`, or a commit hash, or a version tag (`#1.2.0`) | the repository's default branch (`HEAD`) |
| a branch name (`#main`, `#develop`) | that branch |

`?path=` sub folders are supported. Updating re-installs the package from the branch URL (without the commit hash or tag), which moves a pinned package to the branch head.

## Using it

- **Tools > Tauntastic > Packages > Updates** (always the first entries of the Tauntastic menu) - the Updates window: installed vs. latest version, status, **Update**, **Changelog** and **Repository** buttons, **Check now**.
- **Tools > Tauntastic > Packages > Check For Updates** - runs a check immediately and always reports the outcome.
- **Preferences > Tools > Tauntastic > Packages** - switch the automatic check on or off, set the hours between checks and choose whether the window opens by itself.

## Good to know

- Only GitHub HTTPS git URLs are supported. Packages from a registry are already handled by the Package Manager, and packages inside `Assets/` or `Packages/` folders have no remote to check.
- A package can only be reported if its `package.json` version is raised when you release. Bump the version (and write the `CHANGELOG.md` entry) before pushing.
- A private GitHub repository can only be checked with a token: create a fine-grained personal access token with read-only **Contents** access to those repositories and paste it into **Preferences > Tools > Tauntastic > Packages > GitHub token**. It is stored in plain text in this machine's EditorPrefs (never in the project) and only sent to `raw.githubusercontent.com`. Without it, a private repository shows "Not found. The repository does not exist or is private" in the window.

## API

```csharp
using Tauntastic.Packages.Editor;

PackageUpdateChecker.CheckNow(userInitiated: true);
foreach (PackageUpdateInfo info in PackageUpdateChecker.Results)
    Debug.Log($"{info.Name}: {info.InstalledVersion} -> {info.LatestVersion} ({info.Status})");

PackageUpdateChecker.Changed += () => { /* results changed */ };
PackageUpdateChecker.Update(info);              // install the latest version
PackageVersion.IsNewer("1.2.0", "1.1.0");       // true
```

## License

MIT - see [LICENSE](LICENSE).
