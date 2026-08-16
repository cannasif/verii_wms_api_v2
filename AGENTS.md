# WMS v2 Development Rules

These instructions apply to the entire `verii_wms_api_v2` repository.

## Preserve existing work

- Treat every existing tracked or untracked change as user-owned work unless its origin is explicitly established.
- Before editing, inspect `git status`, the relevant diff, and recent commits. Understand overlapping work before changing the same files.
- Never discard, overwrite, reset, stash, revert, amend, or clean existing work without explicit user approval.
- Do not use destructive Git commands such as `git reset --hard`, `git checkout --`, `git restore`, or `git clean` against user changes.
- Do not pull, rebase, merge, or switch branches while the worktree is dirty unless the user explicitly approves a safe integration plan.
- When incoming commits overlap local work, stop and report the exact files and conflicts before resolving them.

## Scope and architecture

- Work only in the active WMS v2 repositories. Do not use legacy WMS repositories as a source of behavior unless the user explicitly requests it.
- Follow the repository's existing architecture, module boundaries, naming, dependency direction, API contracts, persistence patterns, and transaction conventions.
- Prefer the smallest change that completely satisfies the requested behavior.
- Do not perform unrelated cleanup, formatting, refactoring, dependency upgrades, migration rewrites, or generated-file changes.
- Never apply migrations, seed data, or write to production/external systems without explicit approval.

## Micro-commit discipline

- When the user authorizes commits, create small, independently understandable commits that each represent one coherent concern.
- Separate schema/migration changes, domain behavior, application services, API contracts, tests, and documentation when they can be reviewed independently.
- Never mix unrelated fixes or user-owned pre-existing changes into a commit.
- Stage explicit files or hunks; do not use broad staging such as `git add .` when unrelated changes exist.
- Before every commit, inspect the staged diff and verify that it contains only the intended change.
- Use clear commit messages that state the behavior or purpose, not merely the files changed.
- Do not amend, squash, rebase, push, or open a pull request unless the user explicitly asks for that action.

## Verification and handoff

- Run the narrowest relevant tests after each coherent change, then broader build or test commands in proportion to risk.
- Check database, transaction, authorization, tenant/warehouse isolation, concurrency, and stock-integrity implications for affected behavior.
- Do not hide failing checks. Distinguish failures caused by the current change from pre-existing failures.
- At handoff, report changed files, verification performed, remaining risks, and any remote commits that still need integration.
