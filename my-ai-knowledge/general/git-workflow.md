# Git Workflow Standards

## Before changing
- Check `git status` and the current branch. Never assume a clean tree. Separate your changes from pre-existing user changes.

## Protect work
- Preserve existing modifications, staged changes, and untracked files. Never overwrite or discard them without explicit permission.
- Do not use destructive commands to resolve uncertainty.

## Requires explicit authorization
- Staging and committing; creating, switching, renaming, deleting branches; push, pull, fetch, merge, rebase, cherry-pick.
- Destructive or irreversible operations: reset, clean, discarding checkout/restore, branch deletion, history rewriting, force-push.
- Never assume uncommitted work is disposable.

## Keep changes focused
- Only changes relevant to the task. No mixed refactoring, formatting, or cleanup.
- Review the full diff before finishing. Look for accidental edits, generated files, and secrets.
- Stage specific files, not blanket `git add -A`, when unrelated changes may exist.

## Conflicts
- Understand the intent of both sides; do not pick one automatically. Ask if the right resolution is unclear. Run tests afterward.

## Commits (only when authorized)
- Follow the project's convention first. Otherwise Conventional Commits: `type(scope): description` with `feat`, `fix`, `docs`, `refactor`, `test`, `chore`.
- Short imperative subject (about 50–72 characters). The body explains what and why, not how.
- Mark breaking changes with `!` or a `BREAKING CHANGE:` footer.
- Small, independently reviewable and revertible commits; keep each buildable.
