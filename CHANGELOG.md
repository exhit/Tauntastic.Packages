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
