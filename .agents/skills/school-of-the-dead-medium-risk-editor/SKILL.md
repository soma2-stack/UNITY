---
name: school-of-the-dead-medium-risk-editor
description: Carefully handle medium-risk and high-risk Unity edits in School of the Dead, including multi-file gameplay changes, networking, AI, weapons, UI, prefabs, scenes, and project structure.
---

# School of the Dead Medium-Risk Editor

Use this skill for careful medium-risk and high-risk file editing in the School of the Dead Unity project. It can make small fixes when needed, but its main job is work that could affect multiple systems, gameplay behavior, networking, save/state logic, AI, weapons, zombies, UI, prefabs, scenes, or Unity project structure.

## When To Use

Use this skill when a task affects more than one file, more than one system, or important gameplay behavior.

Typical tasks include:

- Zombie AI, spawning, pathfinding, health, damage, or death behavior.
- Multiplayer authority, ownership, RPCs, named messages, NetworkObjects, or replicated state.
- Player health, downed, revive, death, or game-over logic.
- Weapons, firing, ammo, reloads, ballistics, Mystery Box behavior, or pickups.
- Buyable doors, rounds, points, perks, animations, state machines, or save/state logic.
- Prefab definitions, scene wiring, Inspector references, Unity project settings, tags, layers, or refactors.

Do not use this skill for isolated typo fixes, comment-only changes, or small UI text adjustments unless they are required to complete a larger task.

## Workflow

Follow this order: Inspect, identify risk, ask only when needed, edit carefully, validate, and report.

### Inspect Before Editing

- Read the relevant files before changing them.
- Understand how the current system works and who owns each action: Player, Zombie, Manager, RPC handler, event listener, or MonoBehaviour lifecycle.
- Search for related scripts, prefabs, scene references, Animator states, events, delegates, and dependencies.
- Do not guess file names, assume ownership, or rewrite systems blindly.

For example, before changing zombie damage, find the zombie damage/death path, player health receiver, scoring/reward calls, and any server/client synchronization. Before changing a door purchase, find the interactable, player points logic, round/game manager, and server validation path.

### Identify Risk

Check whether the task:

- Touches multiplayer authority, ownership, RPCs, NetworkObject state, or replicated variables.
- Changes public fields, serialized Inspector values, method signatures, or component hookups.
- Touches a manager, event system, core loop, prefab, scene, layer, tag, or project setting.
- Renames, moves, deletes, or broadens code in a way that can break references.

Treat networking, damage, death, revive, scoring, zombie spawning, door purchases, item pickups, and game-over logic as high-risk.

### Ask Questions When Needed

Ask one short question before editing only when the task is ambiguous, risky, or has multiple valid approaches. Ask especially when the change could:

- Delete behavior or broadly overwrite scenes/prefabs.
- Change multiplayer authority or server/client ownership.
- Change game balance.
- Rename public fields or break Inspector references.
- Require scene, prefab, or project-settings edits whose target is unclear.

If the user gave enough detail, continue without asking.

### Make Careful Targeted Edits

- Prefer small, controlled edits over full rewrites.
- Preserve existing names, public fields, serialized fields, method names, Inspector references, and Unity component hookups unless the task requires a change.
- Do not delete, rename, move, or rewrite files unless clearly necessary.
- Avoid unrelated formatting, cleanup, or gameplay changes.
- Use Find/Replace carefully: verify a single file first, then expand only when justified.

For example, add one new zombie attack method and update the existing state path instead of rewriting the entire AI controller.

### Protect Unity Project Safety

- Be careful with `.meta` files, prefab references, scene references, serialized fields, tags, layers, sorting orders, materials, and Inspector-assigned values.
- Do not assume Unity will repair broken references automatically.
- If a scene or prefab must change, explain exactly why and what will change before editing.
- Avoid large scene or prefab rewrites unless specifically requested.
- Never hand-edit raw Unity YAML when an Editor API or Unity Editor tool can make the change safely.

### Protect Multiplayer And Networking

Before editing networking code:

1. Identify whether the server, host, client, owner, or all clients own the action.
2. Check whether the state is already synchronized by an RPC, named message, replicated variable, or manager callback.
3. Preserve existing RPC signatures, parameter names, and reliability settings unless the task explicitly requires a change.
4. Do not move critical logic to the wrong authority side. Clients may request actions; the server should confirm shared gameplay state.

Keep zombie deaths, player damage, revives, scoring, spawns, door purchases, pickups, and game-over flow on their established authoritative path.

### Validate After Editing

- Check for compile errors, duplicate definitions, missing references, mismatched method signatures, and obvious null or bounds risks.
- Verify changed public fields and Inspector references are still compatible.
- Run the safest relevant compile or validation check when available; do not run a full build unless requested.
- If Unity cannot be run, say exactly what was checked and what needs manual Unity testing.

### Report Clearly

After editing, report:

- Files inspected.
- Files changed, including whether each was edited, created, deleted, or renamed.
- What changed in plain language.
- Why the task was medium or high risk.
- What still needs testing in Unity, including host/client tests when networking is involved.
- Any assumptions made.
- Any partial completion and what remains.

## Refuse Destructive Project Actions Unless Confirmed

Do not mass-delete files, reset or rewrite Git history, remove large systems, broadly overwrite scenes or prefabs, change file extensions, convert scripts to plain text, or remove `.meta` files unless the user explicitly confirms the exact action and reason.

## Default Editing Behavior

For every medium-risk or high-risk task:

1. Inspect.
2. Identify risk and ownership.
3. Ask a short question only if needed.
4. Edit carefully and preserve existing structure.
5. Validate with the safest relevant checks.
6. Report the changes, risks, assumptions, and Unity tests.

Keep the tone direct, careful, and practical. Use School of the Dead examples when useful, while keeping the workflow broadly useful for risky Unity edits in this project.
