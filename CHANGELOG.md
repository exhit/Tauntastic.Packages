# Change Log:

## 1.0.0

- Initial release
- Update checker for Tauntastic packages installed from a GitHub git URL (compares the installed version with the package.json on the repository branch)
- Updates window, menu items and preferences page
- Added README.md, ARTIFICIAL_INTELLIGENCE_README.md and the editor class that injects it into Assets/ARTIFICIAL_INTELLIGENCE_README.md

## 1.1.0

- Added an optional GitHub token in Preferences to check packages in private repositories
- Clearer failure messages (repository not found or private, token rejected)
- Menu entries and the Packages package are always listed first

## 1.2.0

- Updates window: colored status flags, tinted row backgrounds and a summary strip for every status and every kind of failure (not found / private, token rejected, network error, invalid response)
- The number of available updates is shown in the window tab title

## 1.3.0

- With a GitHub token the check now uses the GitHub API (Bearer authorization) instead of the raw file URL, and failure messages include what GitHub answered (for example "Bad credentials")
- Clearer guidance for fine-grained tokens (resource owner, repository list, Contents: Read)

## 1.4.0

- Packages of your GitHub account (default owner exhit, repositories named Tauntastic.*) that contain a Unity package.json are listed even when they are not installed, with an INSTALLABLE flag and an Install button
- Dependencies foldout on every package: child dependencies (what it needs, direct and indirect) and parent dependencies (what needs it)
- Preferences: switch the discovery on or off and choose the GitHub owner
- Window: Show installable toggle, package description and minimum Unity version, PRIVATE flag
- Packages whose folder already exists in the project (the source project of the packages) get a SOURCE CODE flag instead of INSTALLABLE: there is no Install button, the folder path is shown and an Open folder button selects it

## 1.5.0

- The Updates window scans by itself when it opens with no results, limited by the "Hours between checks" preference (the last scan must be older than the interval)
- The default interval between automatic scans is now 2 hours (was 24); an interval you saved in the preferences is kept
- Clearer message in the empty window explaining when the next automatic scan happens

## 1.6.0

- Update all and Install all buttons in the Updates window: after a confirmation list they update (installed packages with a newer version) or install (installable packages) in one Package Manager operation, ordered from the top of the dependency tree (grandparents) to the bottom (grandchildren); a button only appears when it has something to do and source code packages never count
- Source code packages (the folder already exists in the project) are recognised as an update when their version is higher than the version in the repository (SOURCE AHEAD: UPDATE flag, counted in the summary strip and the tab title)
- Git for source code packages: a Git dropdown on each source code row (Commit if eligible, Push if eligible, Commit & Push if eligible) and a Source code dropdown in the toolbar (Commit all, Push all, Commit & Push all, shown only while something is eligible, with a confirmation list). The commit message is the version in the package.json; git runs in the background and its state (uncommitted changes, commits not pushed) is shown on each row
- Preferences: Test token button (asks GitHub whether the saved token is accepted, its rate limit and expiry, and describes the saved token without revealing it) and Test clipboard button (tests the clipboard text without saving it and compares it with the saved token); TOKEN REJECTED errors include the same token description
- Rows get a PUBLIC flag next to the PRIVATE one, for every package whose repository is in the owner's repository list (installed packages too)
- Every package row is a foldout that collapses to a single line (name, versions and flags); Expand all and Collapse all buttons in the toolbar. The collapsed state is kept when scripts reload
