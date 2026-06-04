using UnityEngine;

/// <summary>
/// Mutable per-enemy data bus passed to every modular pattern, condition and
/// weight modifier each FixedUpdate. The <see cref="EnemyController"/> owns a
/// single instance and reuses it (no per-frame allocation).
///
/// Patterns READ context info (player position, ground, timers) and WRITE the
/// desired <see cref="velocity"/>. The controller applies gravity / navigation
/// on top of whatever a movement pattern requests.
///
/// Runtime state that would otherwise live on a ScriptableObject (which is a
/// SHARED asset) must live here instead — see the opaque state slots below.
/// </summary>
public class EnemyContext
{
    // ─── Identity / components ───────────────────────────────────────────────
    public EnemyController   controller;
    public Transform         self;
    public PhysicsController  physics;
    public HealthSystem       health;
    public Transform          player;
    public BossGroup          bossGroup;
    public SOItem             weapon;        // per-enemy Instantiate() copy

    // ─── Static configuration (copied from EnemyPartData) ────────────────────
    public float     moveSpeed   = 2.5f;
    public float     turnSpeed   = 12f;
    public bool      canFly;
    public bool      canClimb;
    public bool      wallSteer;
    public Transform[] patrolPoints;
    public Vector3   spawnPosition;

    // ─── Per-frame state ─────────────────────────────────────────────────────
    public Vector3    velocity;     // patterns write here
    public GroundInfo ground;
    public float      deltaTime;
    public float      phaseTimer;   // seconds inside the current phase
    public float      stateTimer;   // seconds inside the current decision state

    // ─── Opaque runtime slots owned by the active movement / attack pattern ──
    // A pattern allocates its own small state class in Enter() and casts it back
    // in Tick(). Allocation only happens on state transitions (~once a second).
    public object movementState;
    public object attackState;

    // ─── Decision-evaluation scratch (set by the controller per candidate) ───
    // Read by weight modifiers like WMod_AfterUsed while scoring a state.
    public float evalTimeSinceUsed;
    public int   evalTimesUsed;

    // Seconds since the player last pressed attack near this enemy (large if never).
    // Read by WMod_PlayerAttacking to drive reactive dodging.
    public float timeSincePlayerAttack = 999f;

    // ─── Derived helpers ─────────────────────────────────────────────────────

    public bool HasPlayer => player != null;

    public float HealthNormalized => health != null ? health.Normalized : 1f;

    /// <summary>Flat (XZ) vector from this enemy to the player.</summary>
    public Vector3 ToPlayerFlat
    {
        get
        {
            if (player == null) return Vector3.zero;
            Vector3 d = player.position - self.position;
            d.y = 0f;
            return d;
        }
    }

    public float DistanceToPlayer =>
        player == null ? float.PositiveInfinity
                       : Vector3.Distance(self.position, player.position);

    public float FlatDistanceToPlayer => ToPlayerFlat.magnitude;

    // ─── Velocity helpers ────────────────────────────────────────────────────

    public void SetHorizontalVelocity(Vector3 horizontal)
    {
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;
    }

    public void StopHorizontal()
    {
        velocity.x = 0f;
        velocity.z = 0f;
    }

    /// <summary>Smoothly rotate (yaw only) to face a flat direction.</summary>
    public void FaceDirection(Vector3 flatDir)
    {
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        self.rotation = Quaternion.Slerp(self.rotation, target, turnSpeed * deltaTime);
    }

    public void FacePlayer()
    {
        if (player != null) FaceDirection(ToPlayerFlat);
    }
}
