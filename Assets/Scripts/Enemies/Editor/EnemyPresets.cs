using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click example enemies built entirely in code (no GUID juggling). They use
/// the exact same modular pieces the Enemy Creator window does, so after
/// generating you can open the asset / window and tweak everything freely.
/// </summary>
public static class EnemyPresets
{
    private const string SaveFolder = "Assets/Enemies";

    [MenuItem("Tools/Enemy Presets/Create Goblin")]
    public static void CreateGoblin()
    {
        var enemy = ScriptableObject.CreateInstance<SOEnemy>();
        enemy.name = "Goblin";
        enemy.enemyName = "Goblin";
        enemy.parts.Clear();

        var part = new EnemyPartData
        {
            partName  = "Goblin",
            maxHearts = 2,
            moveSpeed = 3.2f,
            turnSpeed = 14f,
            canFly    = false,
            canClimb  = false,
            wallSteer = true,
            weapon    = FindFirstSword(),
            vision = new EnemyVision
            {
                range          = 10f,
                angle          = 140f,
                alertRadius    = 2.5f,
                loseSightDelay = 3f,
                // Walls only — must NOT include the player's layer or line-of-sight
                // would be "blocked" by the player itself. Adjust to your setup.
                blockMask      = LayerMask.GetMask("Default"),
            },
            // Wanders calmly until it spots the player.
            idleMovement = Make<SOMove_Random>("Idle Wander", m =>
            {
                m.speedMultiplier = 0.4f;
                m.wanderRadius    = 4f;
                m.changeInterval  = 3f;
            }),

            // Reflex dodge — a reliable interrupt when the player swings nearby.
            dodgeReaction = Make<SOMove_Dodge>("Evade", m =>
            {
                m.direction  = SOMove_Dodge.DodgeDir.AwaySideways;
                m.dodgeSpeed = 14f;
                m.burstTime  = 0.4f;
            }),
            dodgeChance       = 0.6f,
            dodgeReactionTime = 0.4f,
        };

        var phase = ScriptableObject.CreateInstance<SOEnemyPhase>();
        phase.name = "Hunt";
        phase.label = "Hunt";
        phase.decisionInterval = new Vector2(1.0f, 2.0f);
        phase.noise = 0.25f;

        // ── Sneak in and poke ──────────────────────────────────────────────
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Sneak In",
            baseWeight = 50f,
            duration   = new Vector2(0.8f, 1.6f),
            movement   = Make<SOMove_Chase>("Sneak Chase", m =>
            {
                m.speedMultiplier = 0.45f;   // slow, stalking approach
                m.stopDistance    = 1.4f;
            }),
            attack = Make<SOAttack_Melee>("Poke", a =>
            {
                a.attackRange = 1.7f;
                a.cooldown    = 1.0f;
            }),
            modifiers =
            {
                Make<SOWMod_PlayerFar>("Approach When Far", m => { m.distance = 2.0f; m.multiplier = 2.0f; }),
                Make<SOWMod_AfterUsed>("Don't Re-approach", m => { m.cooldown = 2.0f; m.minMultiplier = 0.2f; }),
            },
        });

        // ── Hit-and-run retreat ────────────────────────────────────────────
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Retreat",
            baseWeight = 35f,
            duration   = new Vector2(0.6f, 1.2f),
            movement   = Make<SOMove_Flee>("Back Off", m =>
            {
                m.speedMultiplier   = 1.1f;
                m.safeDistance      = 4.5f;
                m.faceWhileFleeing  = true;
            }),
            attack = ScriptableObject.CreateInstance<SOAttack_None>(),
            modifiers =
            {
                Make<SOWMod_PlayerNear>("Flee When Close", m => { m.distance = 2.2f; m.multiplier = 2.5f; }),
            },
        });

        // ── Circle strafe, then re-approach ────────────────────────────────
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Circle",
            baseWeight = 22f,
            duration   = new Vector2(0.8f, 1.5f),
            movement   = Make<SOMove_Strafe>("Side Step", m =>
            {
                m.speedMultiplier = 0.9f;
                m.radius          = 2.6f;
                m.direction       = UnityEngine.Random.value < 0.5f ? 1 : -1;
            }),
            attack = ScriptableObject.CreateInstance<SOAttack_None>(),
            modifiers =
            {
                Make<SOWMod_PlayerNear>("Juke When Close", m => { m.distance = 3.0f; m.multiplier = 1.5f; }),
            },
        });

        var never = ScriptableObject.CreateInstance<SOCondition_Never>();
        never.name = "Stay In Hunt";
        phase.exitCondition = never;

        part.phases.Add(phase);
        enemy.parts.Add(part);

        var result = EnemyAssetBuilder.Build(enemy, SaveFolder);

        if (part.weapon == null)
            Debug.LogWarning("[EnemyPresets] No SOSword found in the project — the Goblin has no weapon. " +
                             "Assign one in its part data so it can deal melee damage.");

        EditorUtility.DisplayDialog("Enemy Presets",
            $"Created Goblin:\n• {result.assetPath}\n• {result.prefabPath}\n\n" +
            "Remember: set the part's Vision → Block Mask to your walls layer, and make " +
            "sure the weapon's targetLayers includes the player.", "Got it");

        Selection.activeObject = result.prefab;
        EditorGUIUtility.PingObject(result.prefab);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static T Make<T>(string name, Action<T> configure) where T : ScriptableObject
    {
        var so = ScriptableObject.CreateInstance<T>();
        so.name = name;
        so.hideFlags = HideFlags.DontSave;
        configure?.Invoke(so);
        return so;
    }

    private static SOItem FindFirstSword()
    {
        string[] guids = AssetDatabase.FindAssets("t:SOSword");
        if (guids.Length == 0) return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<SOItem>(path);
    }
}
