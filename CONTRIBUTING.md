# Contributing to SmartStreamer4

## Overview

SmartStreamer4 uses a branch-per-change workflow with pull requests. All development targets the `main` branch.

- **Listed collaborators** work directly in branches off `main` in this repo.
- **Outside contributors** fork the repo on GitHub, push branches to your fork, and open PRs from there.

For non-trivial changes (new features, refactors, anything touching FlexRadio or DAX-IQ integration), please open an issue first so we can agree on the approach before you invest time.

## Prerequisites

- .NET 8 SDK
- Windows (the app targets `net8.0-windows`)
- FlexLib API DLLs (intentionally excluded from version control — see README for setup)

## Workflow

### 1. Start from main

```
git checkout main && git pull
```

### 2. Create a focused branch

Name it after what it does:

```
git checkout -b fix/dax-iq-restart-on-band-change
git checkout -b feature/skimmer-port-config
```

Keep each branch to one bug or one feature. Small scope = fast review.

### 3. Make your changes

Run these gates before committing — both must pass:

```
dotnet build -warnaserror
dotnet test
```

### 4. Commit

Stage specific files rather than `git add -A`:

```
git add src/CwSkimmerWorkflowService.cs
git commit -m "restart DAX-IQ stream when slice changes bands"
```

Write commit messages that explain *why*, not just *what*. The diff shows what changed.

### 5. Push and open a pull request

Collaborators:

```
git push -u origin fix/dax-iq-restart-on-band-change
gh pr create --base main --title "Restart DAX-IQ stream on band change" --body "..."
```

Outside contributors: push to your fork and open the PR from the GitHub UI against `cdub89/SmartStreamer4:main`.

PR body should explain the motivation and any alternatives considered. A short paragraph is plenty for a small change. If the change depends on specific FlexRadio firmware, SmartSDR, or CW Skimmer versions, note them.

### 6. Review

- **Claude**: type `/ultrareview <PR#>` in Claude Code for a full multi-angle code review, or `/review` for a lighter pass.
- **@cdub89**: reviews and approves before merge.

## Reporting Bugs

When filing an issue, please include:

- SmartStreamer4 version (or commit hash)
- SmartSDR / FlexRadio firmware version
- CW Skimmer version (if relevant)
- Windows version
- Reproduction steps, expected vs. actual behavior
- Relevant log output

## Tips for Smooth Reviews

- Keep PRs under ~200 lines of diff (added/changed, excluding generated files).
- Run both gates before pushing — reviewers should see a clean diff, not fixup commits.
- Reference the issue number in the PR body if one exists.

## Merging and Releases

PRs are squash-merged into `main` after at least one approval. Releases are cut from `main` using `publish-release.ps1` and tagged `v<version>`.

## License

By submitting a pull request, you agree your contribution will be licensed under the project's MIT License (see [LICENSE](LICENSE)).
