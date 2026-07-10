---
name: school-of-the-dead-scene-commit-guard
description: Guard commits for the School of the Dead Unity project. Use when staging, committing, pushing, reviewing Git status, or preparing scene/prefab/asset changes for GitHub. Detect unintended Unity scene, prefab, serialized-field, meta, and private-settings changes before they are committed.
---

# School of the Dead Scene Commit Guard

Use this skill before staging or committing Unity project changes.

## Main Goal

Protect School of the Dead from accidental Git commits, especially changes Unity saved alongside the work the user actually intended to share.

## Rules

- Do not edit project files during a commit review unless the user clearly gives edit permission.
- Do not use `git add .` or stage every changed file by default.
- Never commit `.claude/settings.local.json`.
- Do not hand-edit raw Unity YAML.
- Do not revert, reset, or discard unrelated local changes.
- Do not commit scenes, prefabs, or `.meta` files just because Unity touched them.
- If a scene file contains both intended and unclear changes, stop and show the user the exact risky hunks before staging anything from that scene.
- Commit only after the user approves the exact intended file list.

## Commit Review Workflow

1. Run `git status --short --branch --untracked-files=all`.
2. Run `git diff --stat` and inspect every changed scene (`.unity`), prefab (`.prefab`), and `.meta` file with `git diff`.
3. Classify every change as intended, Unity-generated but required, unrelated, unclear, or dangerous.
4. Treat these as dangerous until the user confirms them:
   - deleted GameObjects, prefab instances, or scene roots
   - changed parent transforms or missing scene-root entries
   - `m_IsActive: 0` added to model/prefab children
   - changed Inspector references, colliders, cameras, animators, NavMesh, or NetworkObjects
   - added serialized fields across many scene entries
   - missing `.meta` files for new tracked Unity assets
5. Explain the exact commit scope in plain language before staging.
6. Stage only the approved files. If a scene or prefab contains mixed intended and risky changes, do not stage it until the user decides how to separate or keep those changes.
7. Run `git diff --cached --check` and inspect `git diff --cached --stat` before committing.
8. After committing, report the commit hash, pushed/not-pushed status, and every remaining local change.

## Unity-Specific Checks

- A new C# script normally needs its Unity-generated `.meta` committed with it.
- New imported models, sounds, textures, videos, and materials normally need matching `.meta` files.
- A scene marker used by gameplay must be an independent world marker, not a child of an object that will move to it.
- Do not assume Unity's automatic serialization changes are harmless. Review them before committing.

## Output Format

### Commit verdict

Safe to stage / Need user decision / Do not stage yet

### Intended changes

List only the files the user asked to share.

### Blocked changes

List every unrelated, unclear, or dangerous file and explain why it is excluded.

### Exact staged scope

List the files that will be committed. Say clearly if nothing was staged.

### Commit result

After an approved commit, give the commit hash and pushed/not-pushed status.

### Remaining local changes

List what remains uncommitted so the user does not accidentally add it later.
