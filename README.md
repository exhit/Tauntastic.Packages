# Tauntastic.Packages

Tells you when a newer version of an installed Tauntastic package is available. Unity's Package Manager does not notify you about updates of packages installed from a git URL; this package fills that gap with a version checker, an Updates window and one-click updates.

- Package: `com.tauntastic.packages` (v1.5.0, Unity 2023.2+)
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

The automatic check runs when the Editor starts, at most once every 2 hours (the "Hours between checks" preference, default 2), and silently does nothing when you are offline, in batch mode, or when the check is switched off. Opening the Updates window while it has no results also starts a scan by itself, under the same limit: it only scans when the last scan is older than the interval, otherwise it waits for **Check now**.

Which branch is followed:

| Git URL in the manifest | Version compared with |
|---|---|
| no `#ref`, or a commit hash, or a version tag (`#1.2.0`) | the repository's default branch (`HEAD`) |
| a branch name (`#main`, `#develop`) | that branch |

`?path=` sub folders are supported. Updating re-installs the package from the branch URL (without the commit hash or tag), which moves a pinned package to the branch head.

## Using it

- **Tools > Tauntastic > Packages > Updates** (always the first entries of the Tauntastic menu) - the Updates window: installed vs. latest version, status, **Update**, **Changelog** and **Repository** buttons, **Check now**.
- **Tools > Tauntastic > Packages > Check For Updates** - runs a check immediately and always reports the outcome.
- **Preferences > Tools > Tauntastic > Packages** - switch the automatic check on or off, set the hours between checks, choose whether the window opens by itself, list installable packages (and the GitHub owner to search) and set the GitHub token.

## Installable packages

Besides the installed packages, the window lists **Tauntastic packages that exist in your GitHub account but are not installed in this project**:

1. It lists the repositories of the configured owner (default `exhit`, change it in Preferences) whose name starts with `Tauntastic.`. Without a token only public repositories are listed; with a token private ones are included too.
2. For every repository that is not installed it reads the `package.json` at the repository root. Only repositories that have one with a `name` and a valid `version` are Unity packages; the rest are left out.
3. Those packages get the `INSTALLABLE` flag, their description, version and minimum Unity version, and an **Install** button that adds the package from its git URL through the Package Manager. Unity installs the git-URL dependencies written in its `package.json` as well. **Show installable** in the toolbar hides them and **List installable packages** in Preferences turns the discovery off.

Discovery uses the GitHub API (at most one request per 100 repositories, plus one `package.json` request per repository). Without a token the API allows 60 requests per hour per IP address; add a token if you hit that limit.

### Source code

If the project already contains the folder of a package (this is the case in the source project where you develop the packages), that package is **not** offered for installation. It gets the `SOURCE CODE` flag instead, an info box with the message "the folder for this package already exists in the project" and the folder path; the row shows **Installed in folder** with the folder path and, on the next line, **Source code version** with the version written in the folder's `package.json` and an **Open folder** button that selects the folder in the Project window. There is no Install button for these packages.

The project is scanned for `package.json` files under `Assets/` and `Packages/` once per check. A folder counts as the package when any of these match: the `name` in its `package.json`, the repository in its `repository.url`, or the folder name (`com.tauntastic.x` for the package `com.tauntastic.x`, or for the repository `Tauntastic.X`).

## Dependencies foldout

Every package row has a **Dependencies** foldout, built from the `dependencies` of the `package.json` files of all known packages (installed and installable):

- **Depends on (child dependencies)** - everything the package needs, direct first, with indirect dependencies indented underneath.
- **Required by (parent dependencies)** - every known Tauntastic package that depends on it, directly or indirectly.
- Each name shows its state: `installed 1.2.0`, `installable 1.0.0` or `not installed` (for example Unity packages that are not in the project).

## Status flags

Each package row has a colored flag, a tinted background and a colored edge. A strip of flags with counts sits under the toolbar, and the tab title shows the number of available updates, for example `Tauntastic Packages (2)`.

| Flag | Color | Meaning |
|---|---|---|
| `UP TO DATE` | green | The installed version is the latest. |
| `UPDATE AVAILABLE` | orange | A newer version is on the branch. |
| `INSTALLABLE` | teal | A Unity package of your GitHub account that is not installed in this project. |
| `SOURCE CODE` | indigo | A package of your GitHub account whose folder already exists in this project, so it is not installed from git. |
| `PRIVATE` | dark gray | Extra flag: the repository is private. |
| `CHECKING` | gray | The request is still running. |
| `NOT FOUND / PRIVATE` | red | HTTP 404: the repository or its package.json does not exist, or it is private and no token (or one without access) was sent. |
| `TOKEN REJECTED` | purple | HTTP 401 or 403: GitHub refused the request or the token. |
| `NETWORK ERROR` | blue | The request did not complete: offline, timeout or connection problem. |
| `INVALID RESPONSE` | amber | GitHub answered but the package.json could not be read or has no valid version. |

## Good to know

- Only GitHub HTTPS git URLs are supported. Packages from a registry are already handled by the Package Manager, and packages inside `Assets/` or `Packages/` folders have no remote to check.
- A package can only be reported if its `package.json` version is raised when you release. Bump the version (and write the `CHANGELOG.md` entry) before pushing.
- A private GitHub repository can only be checked with a token: create a fine-grained personal access token with read-only **Contents** access to those repositories and paste it into **Preferences > Tools > Tauntastic > Packages > GitHub token**. When a token is set, checks go through the GitHub API (`api.github.com`) so errors are explicit; the window then appends what GitHub says (for example "Bad credentials" or "Not Found"). The token is stored in plain text in this machine's EditorPrefs (never in the project) and is only sent to GitHub. A fine-grained token must use the repository owner as **resource owner**, list the repositories, and have **Contents: Read-only** (organisations may need to approve it); a classic token needs the `repo` scope. Without it, a private repository shows "Not found. The repository does not exist or is private" in the window.

## API

```csharp
using Tauntastic.Packages.Editor;

PackageUpdateChecker.CheckNow(userInitiated: true);
foreach (PackageUpdateInfo info in PackageUpdateChecker.Results)
    Debug.Log($"{info.Name}: {info.InstalledVersion} -> {info.LatestVersion} ({info.Status})");

PackageUpdateChecker.Changed += () => { /* results changed */ };
PackageUpdateChecker.Update(info);              // install the latest version of an installed package
PackageUpdateChecker.Install(info);             // install a package with Status == Installable
PackageUpdateChecker.GetDependencies(info);     // child dependencies (direct and indirect)
PackageUpdateChecker.GetDependents(info);       // parent dependencies (what depends on it)
PackageVersion.IsNewer("1.2.0", "1.1.0");       // true
```

## License

MIT - see [LICENSE](LICENSE).
