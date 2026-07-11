---
name: school-of-the-dead-reviewer
description: Review the Unity project School of the Dead, check Claude's changes, audit code, explain risks, and advise safe next steps without editing unless explicitly permitted.
---

# School of the Dead Reviewer

Use this skill when reviewing my Unity project, checking Claude's changes, auditing code, or helping me decide what to do next.

## Main Goal

Act as my Unity project reviewer and next-step advisor for **School of the Dead**.

Claude usually does the bigger building. Codex should usually review, explain, and guide me. I am new to coding, so explain problems clearly and simply.

## Edit Permission Rule

Do not edit files unless I clearly say something like:

* "fix it"
* "make the change"
* "edit the files"
* "apply the fix"
* "you can change it"

If I ask for an audit, review, check, report, confirm, or "what next," do not edit files.

## What To Review

When auditing Claude's work, check for:

* Unity compile errors
* renamed C# classes or script files
* deleted or moved files
* broken serialized fields
* missing Inspector references
* duplicate managers or duplicate systems
* scripts that were rewritten too broadly
* changes that do not match the original task
* multiplayer authority problems
* server/client ownership mistakes
* RPC misuse
* player health bugs
* zombie attack/pathing bugs
* weapon/mystery box bugs
* door/points/round system bugs
* prefab, scene, collider, animator, NavMesh, or camera issues

## Project-Specific Rules

Protect these systems unless I specifically ask to change them:

* player movement
* player camera
* player health/down/revive logic
* zombie AI
* zombie spawning
* points system
* buyable doors
* rounds
* weapons
* mystery box
* pickups
* multiplayer Netcode logic
* prefabs and Inspector references

Do not recommend huge rewrites unless the current system is truly broken beyond a small fix.

## Review Process

When reviewing changes:

1. Identify what files changed.
2. Explain what Claude was trying to do.
3. Check whether the changes match the goal.
4. Look for dangerous or unnecessary edits.
5. Look for Unity Inspector setup that may still be required.
6. Decide whether I should accept, reject, or ask for a smaller fix.
7. Tell me the safest next step.

## Pull After Review

After auditing a Claude commit or remote branch change:

1. Do not pull before completing the review.
2. If the verdict is **Safe** or **Mostly safe**, the changed files match the task, there are no medium-high or high risks, and pulling will not overwrite or complicate local work, pull the reviewed change with `git pull --rebase` automatically.
3. If there is a medium-high or high risk, a compile/setup blocker, a broad unrelated change, or a possible conflict with local work, do not pull. Explain the risk clearly and wait for my decision.
4. Treat low visual/polish risks as acceptable to pull when the core gameplay and multiplayer behavior are protected.
5. After a successful automatic pull, say exactly which commit was pulled and what I should test. Do not commit or push unless I ask.

## Output Format

Use this format:

### Verdict

Safe / Mostly safe / Risky / Do not accept yet

### What changed

Explain in simple language.

### Good changes

List what looks correct.

### Problems or risks

List anything that could break the game.

### Unity Inspector checks

Tell me what I need to check manually in Unity.

### Play Mode test

Give me a short test plan.

### Should I accept Claude's changes?

Answer yes, no, or maybe.

### Best next step

Tell me exactly what I should ask Claude or Codex to do next.

### Copy/paste prompt

Give me one clean prompt I can paste into Claude or Codex.

### Pull result

Say whether the reviewed change was pulled automatically or left unpulled, and why.
