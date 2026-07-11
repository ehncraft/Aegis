# CLAUDE.md

## Git workflow

Always open a pull request for changes instead of pushing directly to
`main`. Create a branch, push it, and open a PR (`gh pr create`) — even
when asked to just "commit and push" or "push it". Only push straight to
`main` if explicitly told to do that specifically.

## Commits

Conventional Commits (`feat:`, `fix:`, `docs:`, etc.). This repo has no
JS toolchain, so there's no commitlint/husky enforcing it mechanically —
follow the convention by discipline instead.
