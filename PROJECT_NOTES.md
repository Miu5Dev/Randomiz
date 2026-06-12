# Randomiz — Project Notes

Running log of systems, decisions, and open issues. Add to it as work continues.

---

## Animation architecture (REWRITTEN — state-machine driven)

The old system polled ~15 independent Animator flags with no context, which let
invalid things happen (attacking during a ledge grab) and caused the targeting
rotation desync. Replaced with a single authority:

```
PlayerStateMachine (player root, DefaultExecutionOrder -50)
  └─ Current : PlayerAnimState   (Locomotion/Targeting/Airborne/StepUp/Wallhug/
                                  LedgeHang/Climb/HitStun/Dash)
  └─ CanAct  : bool              (may an attack/use-item START now?)
  └─ OnStateChanged event
       ▲ derives state from PlayerMovement each frame, strict priority order
       │
   ┌───┴─────────────┬──────────────────────┐
PlayerAnimator    EquipHandler           PlayerIK / modules
(reflects state   (gates the real        (read PlayerIK.State; only the
 into Animator,    attack/damage by        relevant module drives limbs)
 gates triggers    CanAct)
 by CanAct, fires
 procedural hops)
```

- `PlayerAnimState.cs` — the state enum + `PlayerStateRules.CanAct` /
  `IsProcedural`.
- `PlayerStateMachine.cs` — derives ONE state from PlayerMovement; the single
  source of truth. Does not move the player.
- `PlayerAnimator.cs` — reflects `Current` into Animator params; `OnAttack` and
  `EquipHandler.OnAttackInput` both check `CanAct`, so attack-during-ledge is gone.

### Procedural animation (editable)
- `Procedural/ProceduralClip.cs` — ScriptableObject of time-based local offset
  curves (pos/rot). Create → Animation → Procedural Clip. Presets via the custom
  editor (Hop/Dip/Recoil/Lean) + live scrubber preview.
- `Procedural/ProceduralAnimator.cs` — on the model; plays a clip as an additive
  offset in LateUpdate (undo-then-apply, never accumulates).
- Wired: `PlayerAnimator` plays `stepUpHop` on entering StepUp, `landDip` on
  landing. Assign these ProceduralClip assets in the PlayerAnimator inspector.

### Setup additions for the rewrite
- Add **PlayerStateMachine** to the player root (next to PlayerMovement).
- Add **ProceduralAnimator** to the model object (same as Animator); assign
  `stepUpHop` / `landDip` clips on PlayerAnimator if wanted.

---

## Animation system (player) — clips/IK detail

Built procedurally + Mixamo clips. Key scripts:

| Script | Role |
|--------|------|
| `Assets/Scripts/Animation/PlayerAnimator.cs` | Drives the Animator from PlayerMovement state. |
| `Assets/Scripts/Animation/AnimParams.cs` | Central parameter names + cached hashes. |
| `Assets/Scripts/Animation/Editor/AnimatorControllerBuilder.cs` | Generates the controller. **Tools → Animation → Build Player Animator Controller**. |
| `Assets/Scripts/Animation/Editor/AnimationClipImporter.cs` | Sets loop / in-place / no-sink on clips. **Tools → Animation → Fix Player Animation Clips**. |
| `Assets/Scripts/Animation/IK/*` | `PlayerIK` (coordinator) + FootIK, HandIK, LookAtIK, HitReactIK, WallhugIK, LedgeHangIK. |

### Setup reminders
- Player model object holds: `Animator` (+ its Avatar), `PlayerAnimator`, `PlayerIK`, and the IK modules.
- The Animator object **must be the same as `PlayerMovement.modelTransform`** (the one that rotates).
- Each Animator **layer needs "IK Pass" ON** or no IK runs.
- **Apply Root Motion is forced OFF** in `PlayerAnimator.Awake` (movement is script-driven).
- After importing clips: run **Fix Player Animation Clips**, then **Build Player Animator Controller**.

### IK conflict resolution
`PlayerIK` zeroes all four limb goals each frame, then runs modules in `Priority`
order (low first, high wins): FootIK 0 < LookAt/HitReact 5 < HandIK 10 < WallhugIK 20
< LedgeHangIK 25. References to PlayerMovement/PlayerLedgeGrab are resolved lazily
(no Awake race).

### Procedural ledge hang (LedgeHangIK)
- Hands anchored to each **shoulder projected onto the wall plane** (from ledge data
  `LedgeTopPoint` + `LedgeWallNormal`), raised to the ledge — always on-wall and
  in-reach. Clamped to `armReach` so the arm never flings out.
- Feet raycast toward the wall (reuses `PhysicsController.collisionMask`, no layers
  to set up); plant if a wall is below, else weight 0 so legs hang relaxed.
- Hand-over-hand gait synced to lateral move speed.

### Climb (fixed)
`PlayerLedgeGrab.TickClimb` is now **purely timer-driven** (`ledgeClimbDuration`,
set to the climb clip length in `PlayerAnimator.Awake`). It always completes, so the
ClimbUp state can't loop/lock. The Animator exit is by the `IsClimbingLedge` bool.
Climbing/Roll clips are flattened in the importer (`heightFromFeet`) so the script's
motion isn't doubled by root motion.

---

## Targeting + strafe (IN PROGRESS — see open issue)

Goal: while targeting, the model faces the target (or a fixed forward when no enemy)
and plays **strafe** animations (Basic Locomotion Pack: walk + left/right strafe),
head looks at the target via LookAtIK. Movement is relative to the model facing.

- `AnimatorControllerBuilder` adds a `TargetStrafe` state: `FreeformCartesian2D`
  blend on `MoveX` (local right) × `MoveY` (local fwd). Enter/leave by `IsTargeting`.
- `PlayerAnimator` computes MoveX/MoveY from velocity projected on
  `PlayerMovement.modelTransform` right/forward, normalised by `moveSpeed`.
- `PlayerMovement` sets the model facing **authoritatively at the END of FixedUpdate**
  (face target, or hold `_targetFacing` captured on entry when no enemy).

### OPEN ISSUE: model still rotates toward movement direction during targeting
Symptoms (confirmed with the user):
- Body rotates **physically** (whole transform), only **while moving**, not when
  only the camera moves.
- Animator shows `TargetStrafe` active, yet a debug log showed
  `targetingSystem.IsTargeting == FALSE` inside `PlayerMovement.FixedUpdate`.
- Single PlayerMovement and single TargetingSystem in the scene (no duplicates).

Root-cause hypothesis: **`IsTargeting` (polled) reads a transient `false`** during
hold-to-target (`TargetingSystem.HandleTargetInput` calls `SetTargeting(false)` on a
frame where the held button reports `!pressed`). On those frames PlayerMovement's
`else if (!isTargeting) HandleRotation(moveDir)` ran and slerped the model toward the
move direction; the Animator's 0.15 s transition damping kept it visually in
TargetStrafe, so the two states looked simultaneous.

Fixes applied (latest, needs in-game verification):
- PlayerMovement & PlayerAnimator now use an **event-driven** targeting flag
  (`TargetingSystem.OnTargetingChanged`, subscribed in `Start`) instead of polling
  `IsTargeting`. `OnTargetingChanged` only fires on real state changes, so no flicker.
  Exposed as `PlayerMovement.IsTargeting`.
- Removed the earlier grace-timer "latch" attempts.
- Model facing during targeting is written **last** in FixedUpdate, with no slerp
  (hard `LookRotation`), so stray rotations can't accumulate.

If it STILL rotates after this:
1. Confirm whether the input is truly held (the targeting button may be sending
   `pressed=false` repeatedly — check the input action / `OnTargetInputEvent`
   source). If `OnTargetingChanged(false)` is actually firing mid-hold, the bug is
   upstream in `TargetingSystem.HandleTargetInput` / the input binding, not movement.
2. Verify no **root motion** leaks: even with `applyRootMotion=false`, a re-imported
   strafe clip with baked root yaw shouldn't rotate, but re-import the Basic
   Locomotion Pack FBXs to be safe (`lockRootRotation` is set by the importer).
3. Check the strafe clips actually face forward in-place (preview them); a Mixamo
   strafe that turns in the clip would read as turning.

---

## Combat / feedback
- `HealthSystem` with i-frames (`invincibilityDuration`), emits `OnDamagedEvent`.
- `HitReaction` (player): knockback + Zelda blink via cel shader `_HitAlpha`/`_HitFlashAmount`.
- `HitFlash` (enemies): red `_HitFlashAmount` pulse.
- `CameraShake` on the Camera: trauma-based, compares attacker/victim by `transform.root`.
- `MeleeHitbox` + `WeaponAnimationRelay`: animation-driven moving weapon hitbox
  (SOSword `damageMode` chooses OverlapBox vs MovingHitbox).

## VFX / SFX (feedback effects)

One-shot visual + audio feedback, decoupled via EventBus. Spawn a VFX prefab and
play an SFX clip per named action.

| Script | Role |
|--------|------|
| `Assets/Scripts/VFX/VFXPlayer.cs` | Scene singleton. Bridges EventBus → effects; shared `AudioSource` for one-shots. `Play(action, pos, parent?)` is the public entry. |
| `Assets/Scripts/VFX/SOVFXProfile.cs` | Asset holding a list of `VFXBinding`. `GetBinding(action)` lookup. Action-name constants live here (single source of truth). |
| `Assets/Scripts/VFX/VFXBinding.cs` | One mapping: `actionName` + `vfxPrefab` (+ `duration`, `attachToSource`) + `sfxClip` (+ `sfxVolume`). |

**What triggers each action** (so a binding actually fires):
- `OnDamagedEvent` → `TakeDamage`; `OnDieEvent` → `Death` / `EnemyDeath` (by tag).
- `OnAttackInputEvent` (pressed) → `AttackSwing`, but **only for a real sword swing**.
  VFXPlayer subscribes at **priority 1** (above EquipHandler@0) so it reads the
  *pre-press* equipped item and plays only when `EquipedItem is SOSword` **and**
  `PlayerStateMachine.CanAct`. An empty-hand draw (first press from empty), a potion
  sip, a ranged weapon, or a press the player can't act on all stay silent.
- `OnItemPickedUpEvent` → `ItemPickup` (every acquisition: chest / shop / key / grant).
  Save-load uses `InventoryHandler.RestoreInventory` (bypasses `AddItem`), so loading a
  save does **not** replay the pickup cue.
- `OnPlayerLocomotionStateEvent` → `Dash` / `LedgeGrab` / `LedgeClimb` / `Land`, fired on
  the **rising edge** of each flag (VFXPlayer keeps the previous state; first event after
  enable only captures the baseline so `Land` doesn't fire on spawn).
- `WallhugEnter` is fired at its **source**: `PlayerWallhug.TryEnter` calls
  `VFXPlayer.PlayWallhugEnter(...)` the instant `isWallhugging` is set true. It used to
  ride the locomotion event's rising edge like the others, but that proved unreliable in
  practice, so it was moved to the entry point and removed from `OnLocomotionState`.
- Footstep coroutine polls `PlayerMovement` velocity → `WalkStep` (interval scales walk↔run).
- Static helpers for code-driven hits: `PlayAttackHit` / `PlayCheckpoint` / `PlayBossSlam`
  (call from the relevant systems).

> Gotcha: an unconfigured action is a **silent skip**, and the event-driven locomotion
> effects (`Dash` / `LedgeGrab` / `LedgeClimb` / `Land`) do nothing until
> `OnPlayerLocomotionStateEvent` carries the flag — `isDashing` / `isClimbingLedge` were
> added to that event so dash/ledge-climb SFX work.

### Setup
- `Randomiz > ★ Game Setup Wizard` → **🎆 Character SFX / VFX**: assign/create the
  `SOVFXProfile`, fill the per-action table (SFX clip + volume, VFX prefab + lifetime),
  tune footstep timing, then **Apply** (writes the profile + footstep fields on VFXPlayer).
- The wizard's **🔎 Scan** warns if `VFXPlayer.profile` is unassigned.

## Enemies
Modular SO system under `Assets/Scripts/Enemies/` (SOEnemy → parts → phases →
weighted decision states; movement/attack/condition/weight-modifier SOs). Built via
**Tools → Enemy Creator** (also `Enemy Presets → Create Goblin`). Auto-adds `HitFlash`.

## Randomizer / shop

`RandomizerSystem` distributes the run's items across **chests AND shop slots** from
one shared `SOItemPool` (state in `RandomizerState`, keyed by `locationId`). Shop
slots are first-class locations (`shop_{npcId}_{i}`), so any item — progression sword
included — appears in exactly one place, never duplicated.

### Tier progression delivery (chests + shop)
Progression weapons are always delivered **in order**: regardless of which tier a
location advertises, the player receives the next tier they still need.
- **Chests** — `ChestBehaviour.ResolveItem`: tier ≤ owned → filler; tier == owned+1 →
  give it; sequence break → swap item with another unopened location holding the
  needed tier (keeps the run beatable), else filler.
- **Shop** — `ShopInventory` resolves each weapon slot to the next tier for that
  family (`ResolveProgression` → `FindNextFamilyWeapon`) and **shows/sells that** at
  its honest price. `EnsureGenerated()` re-resolves whenever the player's weapon tier
  changes, so re-opening the shop advances the displayed tier. So you can **buy any
  weapon slot and always get the correct order**; a slot disappears once that family
  is maxed. Per-family ownership comes from `InventoryHandler.GetHighestWeaponTierOfType`.

> Wiring: `NPCData.shopItemPool` must be the SAME `SOItemPool` asset the chests and
> RandomizerSystem use (one shared run state) or slots won't resolve. Sold state is
> slot-scoped in `SaveManager` (resets on new game, restored on load) — not a per-NPC file.

## Shaders
See `Assets/Shaders/SHADERS.md` (cel shading, per-material outline + auto smooth
normals, proximity dither, hit feedback, shadow-config notes).
