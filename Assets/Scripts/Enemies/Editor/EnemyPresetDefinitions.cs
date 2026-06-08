#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click factory that creates all five pre-defined enemy SO assets.
/// Individual presets are also available under Randomiz/Enemies/.
///
/// Files are saved to Assets/Enemies/{EnemyName}/ using EnemyAssetBuilder
/// so the assets match what the Enemy Creator window would produce.
/// </summary>
public static class EnemyPresetDefinitions
{
    private const string SaveFolder = "Assets/Enemies";

    // ─── Batch entry point ───────────────────────────────────────────────────

    [MenuItem("Randomiz/Enemies/Create All Presets")]
    public static void CreateAllPresets()
    {
        CreateWolf();
        CreateGoblinWarrior();
        CreateGoblinArcher();
        CreateSilverfish();
        CreateBandit();

        EditorUtility.DisplayDialog("Enemy Presets",
            "All five enemy presets created under Assets/Enemies/.\n\n" +
            "Remember to:\n" +
            "  • Assign the correct weapon assets to each part.\n" +
            "  • Set Vision → Block Mask to your walls layer.\n" +
            "  • Verify targetLayers on each weapon includes the player.",
            "Got it");
    }

    // ─── Wolf ────────────────────────────────────────────────────────────────

    [MenuItem("Randomiz/Enemies/Create Wolf")]
    public static void CreateWolf()
    {
        var enemy = MakeEnemy("Wolf");

        var part = new EnemyPartData
        {
            partName  = "Wolf",
            maxHearts = 10,          // 40 hp / 4 per heart
            moveSpeed = 5f,
            turnSpeed = 14f,
            wallSteer = true,
            weapon    = FindFirstSword(),
            vision    = DefaultVision(range: 10f, angle: 150f, alertRadius: 3f),
            idleMovement = Make<SOMove_Patrol>("Wolf Patrol", m =>
            {
                m.speedMultiplier = 0.6f;
            }),
            dodgeReaction = Make<SOMove_Dodge>("Wolf Evade", m =>
            {
                m.direction  = SOMove_Dodge.DodgeDir.AwaySideways;
                m.dodgeSpeed = 10f;
                m.burstTime  = 0.3f;
            }),
            dodgeChance       = 0.3f,
            dodgeReactionTime = 0.35f,
        };

        // Phase 1 — standard hunt (stays until HP < 30%)
        var phase1 = MakePhase("Hunt", decisionMin: 1f, decisionMax: 2f, noise: 0.2f);

        phase1.states.Add(new EnemyStateEntry
        {
            label      = "Chase",
            baseWeight = 60f,
            duration   = new Vector2(1.2f, 2.5f),
            movement   = Make<SOMove_Chase>("Wolf Chase", m =>
            {
                m.speedMultiplier = 1f;
                m.stopDistance    = 1.3f;
            }),
            attack = Make<SOAttack_Combo>("Wolf Bite", a =>
            {
                a.attackRange = 1.5f;
                a.strikes     = 2;
                a.timeBetween = 0.25f;
                a.cooldown    = 1.5f;
            }),
            modifiers =
            {
                Make<SOWMod_PlayerFar>("Close Gap", m => { m.distance = 3f; m.multiplier = 1.8f; }),
            },
        });

        phase1.states.Add(new EnemyStateEntry
        {
            label      = "Dodge Away",
            baseWeight = 30f,
            duration   = new Vector2(0.5f, 1f),
            movement   = Make<SOMove_Dodge>("Wolf Dodge", m =>
            {
                m.direction  = SOMove_Dodge.DodgeDir.AwaySideways;
                m.dodgeSpeed = 9f;
                m.burstTime  = 0.3f;
            }),
            attack = MakeNone("No Attack Dodge"),
            modifiers =
            {
                Make<SOWMod_PlayerAttacking>("Reactive Dodge", m => { m.window = 0.4f; m.multiplier = 3f; }),
            },
        });

        phase1.exitCondition = Make<SOCondition_HealthBelow>("Below 30pct", c => c.threshold = 0.3f);
        part.phases.Add(phase1);

        // Phase 2 — enraged (speed boost + howl)
        var phase2 = MakePhase("Enraged", decisionMin: 0.8f, decisionMax: 1.6f, noise: 0.15f);

        phase2.states.Add(new EnemyStateEntry
        {
            label      = "Enraged Chase",
            baseWeight = 70f,
            duration   = new Vector2(1f, 2f),
            movement   = Make<SOMove_Chase>("Wolf Run", m =>
            {
                m.speedMultiplier = 1.4f;    // runSpeed boost (7 / 5 ≈ 1.4)
                m.stopDistance    = 1.1f;
            }),
            attack = Make<SOAttack_Combo>("Wolf Flurry", a =>
            {
                a.attackRange = 1.5f;
                a.strikes     = 2;
                a.timeBetween = 0.2f;
                a.cooldown    = 1.2f;
            }),
        });

        phase2.states.Add(new EnemyStateEntry
        {
            label      = "Howl",
            baseWeight = 25f,
            duration   = new Vector2(1.2f, 1.5f),
            // Howl is an animation-only taunt: stationary + None attack
            // (an animation trigger named "Howl" is expected on the Animator).
            movement = Make<SOMove_Stationary>("Stand Still", _ => { }),
            attack   = MakeNone("Howl Taunt"),
            modifiers =
            {
                Make<SOWMod_AfterUsed>("Howl Cooldown", m => { m.cooldown = 8f; m.minMultiplier = 0f; }),
            },
        });

        phase2.exitCondition = MakeNever("Stay Enraged");
        part.phases.Add(phase2);

        enemy.parts.Add(part);
        Build(enemy);
    }

    // ─── Goblin Warrior ──────────────────────────────────────────────────────

    [MenuItem("Randomiz/Enemies/Create Goblin Warrior")]
    public static void CreateGoblinWarrior()
    {
        var enemy = MakeEnemy("GoblinWarrior");

        var part = new EnemyPartData
        {
            partName  = "Goblin Warrior",
            maxHearts = 8,           // ~30 hp
            moveSpeed = 3.5f,
            turnSpeed = 14f,
            wallSteer = true,
            weapon    = FindFirstSword(),
            vision    = DefaultVision(range: 9f, angle: 140f, alertRadius: 2.5f),
            idleMovement = Make<SOMove_Patrol>("Goblin Patrol", m =>
            {
                m.speedMultiplier = 0.5f;
            }),
            dodgeReaction = Make<SOMove_Dodge>("Goblin Dodge", m =>
            {
                m.direction  = SOMove_Dodge.DodgeDir.Sideways;
                m.dodgeSpeed = 9f;
                m.burstTime  = 0.28f;
            }),
            dodgeChance       = 0.4f,
            dodgeReactionTime = 0.35f,
        };

        var phase = MakePhase("Combat", decisionMin: 1f, decisionMax: 2.2f, noise: 0.25f);

        // Orbit at mid range, swing when close
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Orbit",
            baseWeight = 40f,
            duration   = new Vector2(1f, 2f),
            movement   = Make<SOMove_Orbit>("Orbit Player", m =>
            {
                m.speedMultiplier = 1f;
                m.radius          = 3f;
                m.direction       = 1;
                m.radiusStiffness = 1.2f;
            }),
            attack = Make<SOAttack_Melee>("Sword Swing", a =>
            {
                a.attackRange = 1.8f;
                a.cooldown    = 2f;
            }),
        });

        // Close-range pressure
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Chase",
            baseWeight = 35f,
            duration   = new Vector2(0.8f, 1.8f),
            movement   = Make<SOMove_Chase>("Close In", m =>
            {
                m.speedMultiplier = 1.1f;
                m.stopDistance    = 1.4f;
            }),
            attack = Make<SOAttack_Melee>("Sword Combo", a =>
            {
                a.attackRange = 1.8f;
                a.cooldown    = 2f;
            }),
            modifiers =
            {
                Make<SOWMod_PlayerFar>("Gap Close", m => { m.distance = 4f; m.multiplier = 2f; }),
            },
        });

        // Charge dash
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Charge",
            baseWeight = 25f,
            duration   = new Vector2(1.5f, 2.5f),
            movement   = Make<SOMove_Chase>("Pre-Charge", m =>
            {
                m.speedMultiplier = 0.3f;
                m.stopDistance    = 1.2f;
            }),
            attack = Make<SOAttack_Charge>("Dash Attack", a =>
            {
                a.triggerRange   = 8f;
                a.windupTime     = 0.55f;
                a.chargeSpeed    = 13f;
                a.chargeDuration = 0.45f;
                a.recoverTime    = 0.5f;
                a.cooldown       = 4f;
                a.damage         = 15f;
                a.hitRadius      = 0.7f;
            }),
            modifiers =
            {
                Make<SOWMod_PlayerFar>("Charge From Range", m => { m.distance = 5f; m.multiplier = 2.5f; }),
                Make<SOWMod_AfterUsed>("Charge Cooldown", m => { m.cooldown = 5f; m.minMultiplier = 0.05f; }),
            },
        });

        phase.exitCondition = MakeNever("Stay In Combat");
        part.phases.Add(phase);

        enemy.parts.Add(part);
        Build(enemy);
    }

    // ─── Goblin Archer ───────────────────────────────────────────────────────

    [MenuItem("Randomiz/Enemies/Create Goblin Archer")]
    public static void CreateGoblinArcher()
    {
        var enemy = MakeEnemy("GoblinArcher");

        var part = new EnemyPartData
        {
            partName  = "Goblin Archer",
            maxHearts = 5,           // ~20 hp
            moveSpeed = 2.5f,
            turnSpeed = 12f,
            wallSteer = true,
            weapon    = null,        // projectile-based — no melee weapon needed
            vision    = DefaultVision(range: 16f, angle: 100f, alertRadius: 2f),
            idleMovement = Make<SOMove_Patrol>("Archer Patrol", m =>
            {
                m.speedMultiplier = 0.45f;
            }),
        };

        var phase = MakePhase("Kite", decisionMin: 1.2f, decisionMax: 2.5f, noise: 0.2f);

        // Keep distance while shooting
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Strafe And Shoot",
            baseWeight = 60f,
            duration   = new Vector2(1.5f, 3f),
            movement   = Make<SOMove_Strafe>("Side Strafe", m =>
            {
                m.speedMultiplier = 1f;
                m.radius          = 9f;
                m.direction       = 1;
            }),
            attack = Make<SOAttack_Projectile>("Arrow Shot", a =>
            {
                a.fireRange        = 15f;
                a.cooldown         = 2.5f;
                a.projectileSpeed  = 12f;
                a.projectileDamage = 8f;
                a.projectileLife   = 6f;
                a.projectileRadius = 0.15f;
                a.muzzleOffset     = new Vector3(0f, 1.2f, 0.3f);
            }),
        });

        // Back away if player gets too close
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Flee",
            baseWeight = 30f,
            duration   = new Vector2(1f, 2f),
            movement   = Make<SOMove_Flee>("Back Away", m =>
            {
                m.speedMultiplier  = 1.2f;
                m.safeDistance     = 7f;
                m.faceWhileFleeing = true;
            }),
            attack = Make<SOAttack_Projectile>("Fleeing Shot", a =>
            {
                a.fireRange        = 15f;
                a.cooldown         = 3f;
                a.projectileSpeed  = 12f;
                a.projectileDamage = 8f;
                a.projectileLife   = 6f;
                a.projectileRadius = 0.15f;
                a.muzzleOffset     = new Vector3(0f, 1.2f, 0.3f);
            }),
            modifiers =
            {
                Make<SOWMod_PlayerNear>("Flee When Close", m => { m.distance = 5f; m.multiplier = 3f; }),
            },
        });

        phase.exitCondition = MakeNever("Stay Kiting");
        part.phases.Add(phase);

        enemy.parts.Add(part);
        Build(enemy);
    }

    // ─── Silverfish (Lepisma) ────────────────────────────────────────────────

    [MenuItem("Randomiz/Enemies/Create Silverfish")]
    public static void CreateSilverfish()
    {
        var enemy = MakeEnemy("Silverfish");

        var part = new EnemyPartData
        {
            partName  = "Silverfish",
            maxHearts = 4,           // ~15 hp, small and fragile
            moveSpeed = 7f,
            turnSpeed = 22f,
            wallSteer = true,
            weapon    = FindFirstSword(),
            vision    = DefaultVision(range: 8f, angle: 200f, alertRadius: 1.5f),
            idleMovement = Make<SOMove_Random>("Scuttle Idle", m =>
            {
                m.speedMultiplier = 0.8f;
                m.wanderRadius    = 3f;
                m.changeInterval  = 1.5f;
            }),
            // No dodge reaction — it's too erratic to be worth telegraphing
        };

        var phase = MakePhase("Swarm", decisionMin: 0.4f, decisionMax: 1f, noise: 0.35f);

        // Rapid zigzag approach
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Dart In",
            baseWeight = 55f,
            duration   = new Vector2(0.5f, 1.2f),
            movement   = Make<SOMove_Zigzag>("Zigzag Rush", m =>
            {
                m.speedMultiplier = 1f;
                m.width           = 2.5f;
                m.frequency       = 2.2f;
                m.stopDistance    = 0.7f;
            }),
            attack = Make<SOAttack_Combo>("Quick Nip", a =>
            {
                // 3 rapid bites in a burst
                a.attackRange = 0.8f;
                a.strikes     = 3;
                a.timeBetween = 0.15f;
                a.cooldown    = 0.8f;
            }),
        });

        // Erratic reposition
        phase.states.Add(new EnemyStateEntry
        {
            label      = "Scatter",
            baseWeight = 35f,
            duration   = new Vector2(0.3f, 0.7f),
            movement   = Make<SOMove_Random>("Erratic Move", m =>
            {
                m.speedMultiplier = 1.3f;
                m.wanderRadius    = 2f;
                m.changeInterval  = 0.5f;
            }),
            attack = MakeNone("No Attack Scatter"),
            modifiers =
            {
                Make<SOWMod_AfterUsed>("Brief Gap", m => { m.cooldown = 1f; m.minMultiplier = 0.1f; }),
            },
        });

        phase.exitCondition = MakeNever("Stay Swarming");
        part.phases.Add(phase);

        enemy.parts.Add(part);
        Build(enemy);
    }

    // ─── Bandit ──────────────────────────────────────────────────────────────

    [MenuItem("Randomiz/Enemies/Create Bandit")]
    public static void CreateBandit()
    {
        var enemy = MakeEnemy("Bandit");

        var part = new EnemyPartData
        {
            partName  = "Bandit",
            maxHearts = 13,          // ~50 hp
            moveSpeed = 3f,
            turnSpeed = 13f,
            wallSteer = true,
            weapon    = FindFirstSword(),
            vision    = DefaultVision(range: 11f, angle: 140f, alertRadius: 3f),
            idleMovement = Make<SOMove_Patrol>("Bandit Patrol", m =>
            {
                m.speedMultiplier = 0.55f;
            }),
            dodgeReaction = Make<SOMove_Dodge>("Bandit Evade", m =>
            {
                m.direction  = SOMove_Dodge.DodgeDir.AwaySideways;
                m.dodgeSpeed = 12f;
                m.burstTime  = 0.38f;
            }),
            dodgeChance       = 0.5f,
            dodgeReactionTime = 0.4f,
        };

        // Phase 1 — composed fighter (until 50% HP)
        var phase1 = MakePhase("Composed", decisionMin: 1.2f, decisionMax: 2.5f, noise: 0.2f);

        // Sequence: slash → parry stance → counter
        var seqAttack = Make<SOAttack_Sequence>("Slash Parry Counter", seq =>
        {
            seq.loop = false;
            seq.steps = new System.Collections.Generic.List<SOAttack_Sequence.Step>
            {
                new SOAttack_Sequence.Step
                {
                    attack   = Make<SOAttack_Melee>("Slash", a => { a.attackRange = 1.8f; a.cooldown = 0.8f; }),
                    duration = 1.0f,
                },
                new SOAttack_Sequence.Step
                {
                    // Parry stance: stationary for a moment (no damage output).
                    attack   = MakeNone("Parry Stance"),
                    duration = 0.6f,
                },
                new SOAttack_Sequence.Step
                {
                    attack   = Make<SOAttack_Melee>("Counter Strike", a => { a.attackRange = 2f; a.cooldown = 0.5f; }),
                    duration = 0.8f,
                },
            };
        });

        phase1.states.Add(new EnemyStateEntry
        {
            label      = "Slash Sequence",
            baseWeight = 40f,
            duration   = new Vector2(2.4f, 3f),
            movement   = Make<SOMove_Orbit>("Pressure Orbit", m =>
            {
                m.speedMultiplier = 0.9f;
                m.radius          = 2.5f;
                m.direction       = 1;
                m.radiusStiffness = 1.2f;
            }),
            attack = seqAttack,
        });

        phase1.states.Add(new EnemyStateEntry
        {
            label      = "Charge",
            baseWeight = 30f,
            duration   = new Vector2(1.8f, 2.5f),
            movement   = Make<SOMove_Chase>("Charge Approach", m =>
            {
                m.speedMultiplier = 0.4f;
                m.stopDistance    = 1.2f;
            }),
            attack = Make<SOAttack_Charge>("Bandit Charge", a =>
            {
                a.triggerRange   = 7f;
                a.windupTime     = 0.6f;
                a.chargeSpeed    = 12f;
                a.chargeDuration = 0.45f;
                a.recoverTime    = 0.55f;
                a.cooldown       = 5f;
                a.damage         = 15f;
                a.hitRadius      = 0.75f;
            }),
            modifiers =
            {
                Make<SOWMod_AfterUsed>("Charge CD", m => { m.cooldown = 6f; m.minMultiplier = 0f; }),
            },
        });

        phase1.states.Add(new EnemyStateEntry
        {
            label      = "Dodge",
            baseWeight = 30f,
            duration   = new Vector2(0.5f, 1f),
            movement   = Make<SOMove_Dodge>("Tactical Dodge", m =>
            {
                m.direction  = SOMove_Dodge.DodgeDir.AwaySideways;
                m.dodgeSpeed = 11f;
                m.burstTime  = 0.38f;
            }),
            attack = MakeNone("No Attack Dodge"),
            modifiers =
            {
                Make<SOWMod_PlayerAttacking>("Dodge On Attack", m => { m.window = 0.5f; m.multiplier = 4f; }),
            },
        });

        phase1.exitCondition = Make<SOCondition_HealthBelow>("Half Health", c => c.threshold = 0.5f);
        part.phases.Add(phase1);

        // Phase 2 — desperate flurry (below 50% HP)
        var phase2 = MakePhase("Desperate", decisionMin: 0.8f, decisionMax: 1.8f, noise: 0.15f);

        phase2.states.Add(new EnemyStateEntry
        {
            label      = "Desperate Flurry",
            baseWeight = 65f,
            duration   = new Vector2(1.5f, 2.5f),
            movement   = Make<SOMove_Chase>("Flurry Chase", m =>
            {
                m.speedMultiplier = 1.2f;
                m.stopDistance    = 1.1f;
            }),
            attack = Make<SOAttack_Combo>("Desperate Combo", a =>
            {
                a.attackRange = 1.8f;
                a.strikes     = 4;
                a.timeBetween = 0.18f;
                a.cooldown    = 1.8f;
            }),
        });

        phase2.states.Add(new EnemyStateEntry
        {
            label      = "Desperate Dodge",
            baseWeight = 35f,
            duration   = new Vector2(0.4f, 0.9f),
            movement   = Make<SOMove_Dodge>("Frantic Dodge", m =>
            {
                m.direction  = SOMove_Dodge.DodgeDir.AwaySideways;
                m.dodgeSpeed = 13f;
                m.burstTime  = 0.35f;
            }),
            attack = MakeNone("No Attack Desperation"),
            modifiers =
            {
                Make<SOWMod_PlayerAttacking>("Panic Dodge", m => { m.window = 0.5f; m.multiplier = 5f; }),
                Make<SOWMod_LowHealth>("Panic More", m => { m.threshold = 0.3f; m.multiplier = 2f; }),
            },
        });

        phase2.exitCondition = MakeNever("Stay Desperate");
        part.phases.Add(phase2);

        enemy.parts.Add(part);
        Build(enemy);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Instantiates a SOEnemy with the given internal name, ready for population.</summary>
    private static SOEnemy MakeEnemy(string name)
    {
        var e = ScriptableObject.CreateInstance<SOEnemy>();
        e.name      = name;
        e.enemyName = name;
        e.parts.Clear();
        return e;
    }

    /// <summary>Creates a SOEnemyPhase with common settings.</summary>
    private static SOEnemyPhase MakePhase(string label, float decisionMin, float decisionMax, float noise)
    {
        var p = ScriptableObject.CreateInstance<SOEnemyPhase>();
        p.name             = label;
        p.label            = label;
        p.decisionInterval = new Vector2(decisionMin, decisionMax);
        p.noise            = noise;
        p.hideFlags        = HideFlags.DontSave;
        return p;
    }

    /// <summary>Creates an EnemyVision struct with sensible per-enemy defaults.</summary>
    private static EnemyVision DefaultVision(float range, float angle, float alertRadius,
                                              float loseSightDelay = 3f)
    {
        return new EnemyVision
        {
            range          = range,
            angle          = angle,
            alertRadius    = alertRadius,
            loseSightDelay = loseSightDelay,
            blockMask      = LayerMask.GetMask("Default"),
        };
    }

    /// <summary>Creates and optionally configures any ScriptableObject, marking it DontSave.</summary>
    private static T Make<T>(string name, Action<T> configure) where T : ScriptableObject
    {
        var so = ScriptableObject.CreateInstance<T>();
        so.name     = name;
        so.hideFlags = HideFlags.DontSave;
        configure?.Invoke(so);
        return so;
    }

    /// <summary>Creates an SOAttack_None with an optional animation trigger name.</summary>
    private static SOAttack_None MakeNone(string name)
    {
        var so = ScriptableObject.CreateInstance<SOAttack_None>();
        so.name     = name;
        so.hideFlags = HideFlags.DontSave;
        return so;
    }

    /// <summary>Creates a SOCondition_Never (phase stays active indefinitely).</summary>
    private static SOCondition_Never MakeNever(string name)
    {
        var so = ScriptableObject.CreateInstance<SOCondition_Never>();
        so.name     = name;
        so.hideFlags = HideFlags.DontSave;
        return so;
    }

    /// <summary>
    /// Finds the first SOSword in the project. Returns null and logs a warning if
    /// none is found — the enemy can still be used but will deal no melee damage.
    /// </summary>
    private static SOItem FindFirstSword()
    {
        string[] guids = AssetDatabase.FindAssets("t:SOSword");
        if (guids.Length == 0)
        {
            Debug.LogWarning("[EnemyPresets] No SOSword found in project. " +
                             "Assign a weapon to the part manually.");
            return null;
        }
        return AssetDatabase.LoadAssetAtPath<SOItem>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    /// <summary>Passes the in-memory enemy to EnemyAssetBuilder and pings the result.</summary>
    private static void Build(SOEnemy enemy)
    {
        var result = EnemyAssetBuilder.Build(enemy, SaveFolder);
        Debug.Log($"[EnemyPresets] Created {enemy.enemyName}: {result.assetPath}");
        Selection.activeObject = result.prefab;
        EditorGUIUtility.PingObject(result.prefab);
    }
}
#endif
