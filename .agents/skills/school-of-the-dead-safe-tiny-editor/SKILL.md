---
name: school-of-the-dead-safe-tiny-editor
description: Make one small, safe, targeted C# fix in the Unity project School of the Dead only when explicit edit permission is given, avoiding risky rewrites and unrelated changes.
---

# School of the Dead Safe Tiny Editor

Use this skill only when I clearly give permission to edit my Unity project.

## Main Goal

Make one small, safe, targeted fix in my Unity C# project **School of the Dead**.

I am new to coding, so avoid risky rewrites and explain everything clearly.

## Hard Rules

* Fix only the issue I asked about.
* Do not rewrite whole systems.
* Do not delete files.
* Do not rename files.
* Do not rename public classes.
* Do not rename serialized fields unless absolutely required.
* Do not move scripts, prefabs, scenes, or assets.
* Do not create duplicate managers or duplicate gameplay systems.
* Do not scan the whole project unless the issue truly requires it.
* Do not change unrelated gameplay.
* Do not make polish changes unless I asked for polish.
* Stop after the requested fix.

## Before Editing

Before making changes:

1. Restate the exact bug or task.
2. Identify the smallest set of files likely involved.
3. Inspect only those files first.
4. Explain the planned fix in one short paragraph.
5. Then make the smallest safe change.

## Unity Safety Rules

Be very careful with:

* Netcode ownership
* server authority
* RPCs
* NetworkObject and NetworkBehaviour logic
* player health
* zombie damage
* zombie spawning
* down/revive logic
* weapon ownership
* mystery box rewards
* buyable door state
* points spending
* round progression
* Animator references
* CharacterController references
* Camera references
* prefab and Inspector fields

If the fix requires Unity Inspector setup, do not fake it in code. Tell me exactly what to assign in Unity.

## Multiplayer Rules

For multiplayer gameplay:

* Damage should usually be server-authoritative.
* Zombie spawning should usually be server-authoritative.
* Door purchases should usually be validated by the server.
* Points changes should not be trusted only on the client.
* Do not let clients freely change shared game state.
* Preserve host/client behavior.

## When To Stop

Stop and report instead of editing if:

* the issue is unclear
* the required fix touches too many systems
* the project has compile errors unrelated to the task
* the fix requires scene or prefab setup you cannot safely infer
* there are multiple possible causes and more testing is needed

## Output Format After Editing

Use this format:

### Fixed

Explain what was fixed.

### Files changed

List every changed file.

### Why this should work

Explain simply.

### What I need to check in Unity

List Inspector, prefab, scene, or Play Mode checks.

### Play Mode test

Give me exact steps to test.

### What not to touch next

Warn me about any related system that should not be changed yet.

### Next tiny task

Suggest one small follow-up task only.
