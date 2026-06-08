using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject asset that holds a list of <see cref="VFXBinding"/> entries,
/// each mapping a named action to a VFX prefab and SFX clip.
/// Create via Assets > Create > Randomiz > VFX Profile.
/// </summary>
[CreateAssetMenu(menuName = "Randomiz/VFX Profile", fileName = "VFXProfile")]
public class SOVFXProfile : ScriptableObject
{
    // ── Predefined action-name constants ──────────────────────────────────────
    // Keep these in sync with the action strings used in VFXPlayer and other
    // callers so refactors stay compiler-checked.
    public const string WALK_STEP          = "WalkStep";
    public const string LAND               = "Land";
    public const string ATTACK_SWING       = "AttackSwing";
    public const string ATTACK_HIT         = "AttackHit";
    public const string TAKE_DAMAGE        = "TakeDamage";
    public const string DEATH              = "Death";
    public const string DASH               = "Dash";
    public const string WALLHUG_ENTER      = "WallhugEnter";
    public const string LEDGE_GRAB         = "LedgeGrab";
    public const string LEDGE_CLIMB        = "LedgeClimb";
    public const string ITEM_PICKUP        = "ItemPickup";
    public const string CHECKPOINT_ACTIVATE = "CheckpointActivate";
    public const string ENEMY_DEATH        = "EnemyDeath";
    public const string BOSS_SLAM          = "BossSlam";

    /// <summary>All action-name constants in a single array, useful for editor dropdowns.</summary>
    public static readonly string[] AllActionNames =
    {
        WALK_STEP, LAND, ATTACK_SWING, ATTACK_HIT, TAKE_DAMAGE, DEATH,
        DASH, WALLHUG_ENTER, LEDGE_GRAB, LEDGE_CLIMB, ITEM_PICKUP,
        CHECKPOINT_ACTIVATE, ENEMY_DEATH, BOSS_SLAM
    };

    // ── Data ──────────────────────────────────────────────────────────────────
    [SerializeField]
    private List<VFXBinding> bindings = new List<VFXBinding>();

    /// <summary>Read-only access to the full binding list (used by the editor window).</summary>
    public List<VFXBinding> Bindings => bindings;

    // ── Lookup ────────────────────────────────────────────────────────────────
    /// <summary>
    /// Returns the <see cref="VFXBinding"/> whose <c>actionName</c> matches
    /// <paramref name="actionName"/>, or <c>null</c> if none is found.
    /// O(n) — profiles are small so a dictionary would be premature optimisation.
    /// </summary>
    public VFXBinding GetBinding(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return null;

        for (int i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].actionName == actionName)
                return bindings[i];
        }

        return null;
    }
}
