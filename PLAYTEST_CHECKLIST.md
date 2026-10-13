# Playtest Checklist — School of the Dead (Stable Co-op Baseline)

Read-only testing plan. No gameplay changes. Use this to validate the current
stable playable co-op baseline before making further edits.

---

## 1. Quick Solo Test

- [ ] Scene loads with no console errors on Play
- [ ] Exactly **one** active Camera and **one** AudioListener (no duplicate warnings)
- [ ] Player spawns in the correct start room
- [ ] Movement, look, jump, and sprint work
- [ ] HUD shows: health, ammo, points, current round
- [ ] Starting weapon fires and reloads; ammo count decrements/refills correctly
- [ ] Zombies spawn and path toward the player
- [ ] Player can damage/kill zombies; kills award points
- [ ] Zombies can damage the player; health bar drops and regenerates
- [ ] Player death triggers game-over / round-end as designed
- [ ] Reach round 5 without a crash or soft-lock

---

## 2. Quick Co-op Test (Host + Client)

- [ ] Host can start/host a session
- [ ] Client can join the host
- [ ] Both players see each other move (position synced, no rubber-banding)
- [ ] Each player has exactly one Camera/AudioListener locally (no duplicates on join)
- [ ] Zombies spawn for both players and target both
- [ ] **Both** players can damage zombies
- [ ] **Both** players can take damage
- [ ] Points tracked per player (or shared, as designed) — confirm which
- [ ] HUD values correct on **both** host and client
- [ ] Doors purchased by one player open for both
- [ ] Downed player enters bleed-out; teammate can **revive**
- [ ] Dead player enters **spectate** correctly
- [ ] Round advances in sync for both players
- [ ] Reach round 5 in co-op without desync or crash

---

## 3. Round 1–5 Balance Checklist

For each round record: zombie count, spawn rate, player health/points at end.

- [ ] R1: light, survivable with starting weapon
- [ ] R2: slight increase, still comfortable
- [ ] R3: noticeable pressure; enough points to consider a wall buy / door
- [ ] R4: harder; encourages mystery box / better weapon
- [ ] R5: meaningful spike but not unfair
- [ ] Point economy: can afford first door + one weapon by ~R3
- [ ] Zombie health/damage scales smoothly (no sudden unfair jump)
- [ ] Spawn pacing keeps rounds moving (no long empty gaps, no overwhelming floods)

### Systems to exercise across the run
- [ ] **Doors**: open on purchase, price displayed, points deducted correctly
- [ ] **Door prices**: match intended values; no free or negative-cost doors
- [ ] **Weapon buying**: purchase, points deducted, weapon equipped/added
- [ ] **Wall buys**: prompt shows, price correct, ammo/weapon granted
- [ ] **Mystery box**: costs points, spins, grants a weapon, relocates if applicable
- [ ] **Stairs**: player can traverse up/down without getting stuck or falling through

---

## 4. Bugs to Watch For

- [ ] Duplicate Camera or AudioListener warnings (esp. after client join / revive)
- [ ] Any console errors or NullReferenceExceptions during play
- [ ] Zombies not spawning, stuck, or not pathing to players
- [ ] Zombies ignoring one player (usually the client)
- [ ] Damage not registering for client-side hits
- [ ] HUD values wrong/frozen/desynced on client (health, ammo, points, round)
- [ ] Points not deducting or double-deducting on purchases
- [ ] Doors opening only for one player
- [ ] Revive not working / stuck in downed or spectate state
- [ ] Falling through stairs or map geometry
- [ ] Round counter out of sync between host and client
- [ ] Weapon/ammo state not syncing after mystery box or wall buy

---

## 5. What to Record in Notes

- Test type (solo / co-op), and who was host vs client
- Round reached and where it broke (if it did)
- Exact console error text + the action that triggered it
- HUD screenshots at each round if a value looks wrong
- Point totals before/after each purchase
- Steps to reproduce any bug (deterministic vs intermittent)
- Frame drops or lag spikes and when they happened
- Which systems were NOT reached/tested this session

---

## 6. Safe Next Fixes After Testing

Prioritize low-risk, isolated fixes that don't touch map generation:

1. Fix any duplicate Camera / AudioListener setup (single-owner enforcement)
2. Resolve console errors / null refs surfaced during the run
3. Correct HUD sync issues (health/ammo/points/round on client)
4. Fix client-side damage registration if hits don't land
5. Correct door/wall-buy/box point deductions and open-for-all behavior
6. Repair revive / spectate state transitions
7. Fix stairs collision / fall-through
8. Tune round 1–5 balance values (counts, prices) only after mechanics are solid

> Save balance tuning and any content/prop/scene changes for a separate,
> deliberate pass — keep this baseline stable.
