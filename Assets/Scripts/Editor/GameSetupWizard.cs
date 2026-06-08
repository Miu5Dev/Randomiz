#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Randomiz.UI;

/// <summary>
/// One-stop setup wizard. Randomiz > ★ Game Setup Wizard.
///
/// Automates every tedious setup step:
///   • Creates all scene singletons and UI systems in one click.
///   • Validates and auto-fixes the player prefab.
///   • Creates pre-wired scene objects (chest, door, NPC, checkpoint, etc).
///   • Runs a scene health-check and flags problems.
/// </summary>
public class GameSetupWizard : EditorWindow
{
    // ── Inspector ─────────────────────────────────────────────────────────
    private SOItemPool _mainPool;
    private string     _gameplaySceneName = "Game";

    private Vector2        _scroll;
    private readonly List<string> _log = new();

    // ── NPC builder state ─────────────────────────────────────────────────
    private bool           _npcFoldout = true;
    private string         _npcName    = "Villager";
    private string         _npcId      = "npc_villager";
    private string         _npcDialogue = "Hello, traveler!\nWelcome to our village.";
    private bool           _npcIsShop;
    private NPCPersonality _npcPersonality = NPCPersonality.FairMerchant;
    private SOItemPool     _npcShopPool;
    private int            _npcShopSize = 4;

    // ─────────────────────────────────────────────────────────────────────
    [MenuItem("Randomiz/★ Game Setup Wizard", priority = -200)]
    public static void Open()
    {
        var w = GetWindow<GameSetupWizard>("Randomiz Setup");
        w.minSize = new Vector2(440, 640);
        w.Show();
    }

    // ─────────────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        BigLabel("★  RANDOMIZ SETUP WIZARD  ★");
        EditorGUILayout.Space(6);

        // ── References ──────────────────────────────────────────────────
        Section("REFERENCES  (fill these first)");
        _mainPool = (SOItemPool)EditorGUILayout.ObjectField(
            new GUIContent("Main Item Pool", "The SOItemPool used by chests and the shop."),
            _mainPool, typeof(SOItemPool), false);
        _gameplaySceneName = EditorGUILayout.TextField(
            new GUIContent("Gameplay Scene Name", "Exact scene name used by SaveManager.LoadScene."),
            _gameplaySceneName);
        EditorGUILayout.Space(8);

        // ── 1 Scene Setup ────────────────────────────────────────────────
        Section("1.  SCENE SETUP");
        ColorBtn("▶  Setup Full Game Scene",   SetupGameScene,    new Color(0.4f, 1f, 0.5f));
        ColorBtn("▶  Setup Main Menu Scene",   SetupMainMenu,     new Color(0.4f, 0.85f, 1f));
        EditorGUILayout.Space(8);

        // ── 2 Player ─────────────────────────────────────────────────────
        Section("2.  PLAYER PREFAB");
        ColorBtn("🔍  Validate & Auto-Fix Player",   ValidatePlayer,  Color.yellow);
        ColorBtn("⚙   Add IK + Anim to Model Child", AddIKToModel,   new Color(1f, 0.82f, 0.4f));
        EditorGUILayout.Space(8);

        // ── 3 Create Objects ─────────────────────────────────────────────
        Section("3.  CREATE SCENE OBJECTS");
        using (new GUILayout.HorizontalScope())
        {
            SmallBtn("Chest",       CreateChest);
            SmallBtn("Door",        CreateDoor);
            SmallBtn("Checkpoint",  CreateCheckpoint);
        }
        using (new GUILayout.HorizontalScope())
        {
            SmallBtn("Enemy Zone",   CreateSpawnZone);
            SmallBtn("Cinematic",    CreateCinematicTrigger);
        }
        EditorGUILayout.Space(6);
        DrawNpcBuilder();
        EditorGUILayout.Space(8);

        // ── 4 Validate ───────────────────────────────────────────────────
        Section("4.  SCENE HEALTH CHECK");
        ColorBtn("🔎  Scan for Problems", ScanScene, new Color(1f, 0.85f, 0.35f));
        EditorGUILayout.Space(6);

        // ── Log ──────────────────────────────────────────────────────────
        if (_log.Count > 0)
        {
            Section("LOG");
            foreach (var line in _log)
            {
                var s = new GUIStyle(EditorStyles.wordWrappedLabel);
                if (line.StartsWith("❌"))      s.normal.textColor = new Color(1f,  0.35f, 0.35f);
                else if (line.StartsWith("✓"))  s.normal.textColor = new Color(0.4f, 1f,   0.4f);
                else if (line.StartsWith("⚠"))  s.normal.textColor = new Color(1f,  0.85f, 0.25f);
                EditorGUILayout.LabelField(line, s);
            }
            if (GUILayout.Button("Clear Log")) _log.Clear();
        }

        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. SCENE SETUP
    // ─────────────────────────────────────────────────────────────────────

    void SetupGameScene()
    {
        _log.Clear();

        // ── GameSystems ──────────────────────────────────────────────────
        var gs = FindOrCreate("GameSystems");
        EnsureComp<SaveManager>(gs);
        var sm = gs.GetComponent<SaveManager>();
        if (sm != null) WriteString(sm, "gameplaySceneName", _gameplaySceneName);
        EnsureComp<CheckpointManager>(gs);
        EnsureComp<BossTracker>(gs);
        EnsureComp<KeyInventory>(gs);
        Log("✓ GameSystems  (SaveManager · CheckpointManager · BossTracker · KeyInventory)");

        // ── InputSystem ──────────────────────────────────────────────────
        EnsureComp<InputSystem>(FindOrCreate("InputSystem"));
        Log("✓ InputSystem");

        // ── EventSystem (UI clicks need the new Input System module) ──────
        EnsureEventSystem();

        // ── Randomizer ───────────────────────────────────────────────────
        var rndGO = FindOrCreate("Randomizer");
        EnsureComp<RandomizerSystem>(rndGO);
        EnsureComp<TestSceneBootstrap>(rndGO);
        var rs = rndGO.GetComponent<RandomizerSystem>();
        var tb = rndGO.GetComponent<TestSceneBootstrap>();
        if (_mainPool != null && rs != null) WriteObj(rs, "pool", _mainPool);
        if (rs != null && tb != null)        WriteObj(tb, "randomizerSystem", rs);
        Log("✓ Randomizer  (RandomizerSystem · TestSceneBootstrap)");

        // ── UI ───────────────────────────────────────────────────────────
        FindOrCreate("DialogueUI").EnsureComp<DialogueUI>();
        FindOrCreate("ShopUI").EnsureComp<ShopUI>();
        FindOrCreate("PauseMenu").EnsureComp<PauseMenuUI>();
        FindOrCreate("PickupPopup").EnsureComp<PickupPopupUI>();
        FindOrCreate("DeathScreen").EnsureComp<DeathScreenUI>();
        Log("✓ UI  (DialogueUI · ShopUI · PauseMenuUI · PickupPopup · DeathScreen)");

        FindOrCreate("MinimapManager").EnsureComp<MinimapManager>();
        FindOrCreate("MinimapUI").EnsureComp<MinimapUI>();
        Log("✓ Minimap  (MinimapManager · MinimapUI)");

        FindOrCreate("VFXPlayer").EnsureComp<VFXPlayer>();
        Log("✓ VFXPlayer");

        Log("\n✅  Game scene ready — hit Play to test.");
    }

    void SetupMainMenu()
    {
        _log.Clear();
        var gs = FindOrCreate("GameSystems");
        EnsureComp<SaveManager>(gs);
        var sm = gs.GetComponent<SaveManager>();
        if (sm != null) WriteString(sm, "gameplaySceneName", _gameplaySceneName);
        FindOrCreate("InputSystem").EnsureComp<InputSystem>();
        EnsureEventSystem();
        FindOrCreate("MainMenu").EnsureComp<MainMenuUI>();
        Log("✅  Main menu scene ready.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. PLAYER
    // ─────────────────────────────────────────────────────────────────────

    void ValidatePlayer()
    {
        _log.Clear();
        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm == null)
        {
            Log("❌  No PlayerMovement found in scene. Place the player prefab first.");
            return;
        }
        var root = pm.gameObject;
        Log($"Root: {root.name}");

        // Tag
        if (root.CompareTag("Player")) Log("  ✓ Tag = Player");
        else { root.tag = "Player"; Log("  ⚠ Tag set to 'Player'"); }

        // Root components (adds if missing)
        AutoAdd<PhysicsController>(root);
        AutoAdd<PlayerMovement>(root);
        AutoAdd<PlayerStateMachine>(root);
        AutoAdd<InventoryHandler>(root);
        AutoAdd<EquipHandler>(root);
        AutoAdd<HealthSystem>(root);
        AutoAdd<QuickslotManager>(root);

        // InventoryHandler fields
        var inv = root.GetComponent<InventoryHandler>();
        if (inv != null)
        {
            var so = new SerializedObject(inv);
            if (_mainPool != null && so.FindProperty("itemPool").objectReferenceValue == null)
            {
                WriteObj(inv, "itemPool", _mainPool);
                Log("  ⚠ itemPool was empty → assigned MainPool");
            }
            else Log(so.FindProperty("itemPool").objectReferenceValue != null
                ? "  ✓ itemPool assigned"
                : "  ❌ InventoryHandler.itemPool not assigned");

            Log(so.FindProperty("defaultBottle").objectReferenceValue != null
                ? "  ✓ defaultBottle assigned"
                : "  ❌ InventoryHandler.defaultBottle not assigned — assign your Empty Bottle SOPotion");
        }

        // Critical refs
        Log(pm.modelTransform != null ? "  ✓ modelTransform" : "  ❌ PlayerMovement.modelTransform not set");
        Log(pm.cameraTarget  != null ? "  ✓ cameraTarget"  : "  ❌ PlayerMovement.cameraTarget not set");

        // EquipHandler pivot
        var eq = root.GetComponent<EquipHandler>();
        if (eq != null)
            Log(eq.ItemsPivotPoint != null ? "  ✓ ItemsPivotPoint" : "  ❌ EquipHandler.ItemsPivotPoint not set");

        // Model child
        var anim = root.GetComponentInChildren<Animator>();
        if (anim == null) Log("  ❌ No Animator in children — add Animator to the model child");
        else
        {
            Log($"  Model GO: {anim.gameObject.name}");
            Log(anim.GetComponent<PlayerIK>() != null
                ? "  ✓ IK components present"
                : "  ⚠ IK not yet set up — click 'Add IK + Anim to Model Child'");
        }

        EditorUtility.SetDirty(root);
        Log("\n✅  Validation done. Fix remaining ❌ items manually.");
    }

    void AddIKToModel()
    {
        _log.Clear();

        // Find the Animator child of the player
        var pm = FindFirstObjectByType<PlayerMovement>();
        Animator anim = pm != null
            ? pm.GetComponentInChildren<Animator>()
            : FindFirstObjectByType<Animator>();

        if (anim == null) { Log("❌  No Animator found. Place the player in the scene first."); return; }
        var model = anim.gameObject;
        Log($"Target model GO: {model.name}");

        AutoAdd<PlayerAnimator>(model);
        AutoAdd<PlayerIK>(model);
        AutoAdd<FootIK>(model);
        AutoAdd<LookAtIK>(model);
        AutoAdd<WallhugIK>(model);
        AutoAdd<LedgeHangIK>(model);
        AutoAdd<HitReactIK>(model);
        AutoAdd<HandIK>(model);
        AutoAdd<AnimationLogger>(model);
        AutoAdd<ProceduralAnimator>(model);
        AutoAdd<AnimationTransitionHelper>(model);

        EditorUtility.SetDirty(model);
        Log("\n✅  Done. IMPORTANT: open the Animator window → Base Layer → enable  IK Pass ✓");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. CREATE SCENE OBJECTS
    // ─────────────────────────────────────────────────────────────────────

    void CreateChest()
    {
        var go = SceneGO("Chest");

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(1f, 0.8f, 0.8f);

        var interactable = go.AddComponent<Interactable>();
        var chest        = go.AddComponent<ChestBehaviour>();

        if (_mainPool != null) WriteObj(chest, "pool", _mainPool);

        // Wire OnUse → OpenInteract (no-arg, so UnityEvent can call it)
        UnityEventTools.AddPersistentListener(interactable.OnUse, chest.OpenInteract);

        Finish(go, "Chest created.\n  → Set unique Location Id (or rename the GO)\n  → Assign pool if not auto-filled");
    }

    void CreateDoor()
    {
        var go = SceneGO("Door");

        // Interaction trigger
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(2f, 3f, 1f);

        // Visual placeholder
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "DoorMesh";
        cube.transform.SetParent(go.transform, false);
        cube.transform.localScale = new Vector3(1.5f, 3f, 0.2f);
        DestroyImmediate(cube.GetComponent<BoxCollider>());

        go.AddComponent<Interactable>();
        go.AddComponent<DoorController>(); // auto-wires OnUse in its own Start()

        Finish(go, "Door created.\n  → Set Required Key Id in DoorController\n  → Replace DoorMesh with your real mesh");
    }

    void CreateCheckpoint()
    {
        var go = SceneGO("Checkpoint");

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(3f, 2.5f, 3f);

        go.AddComponent<CheckpointTrigger>();
        // CheckpointTrigger auto-fills checkpointId from GO name in Awake

        Finish(go, "Checkpoint created.\n  → Rename the GO to its unique id (e.g. 'cp_start')");
    }

    // NPC builder — define name, dialogue, shop settings, then create assets + GO.
    void DrawNpcBuilder()
    {
        _npcFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_npcFoldout, "🧙  NPC Builder");
        if (_npcFoldout)
        {
            EditorGUI.indentLevel++;
            _npcName = EditorGUILayout.TextField("Name", _npcName);
            _npcId   = EditorGUILayout.TextField(
                new GUIContent("Id (save key)", "Stable unique id — save key + shop RNG seed."), _npcId);

            EditorGUILayout.LabelField("Dialogue (one line per row):");
            _npcDialogue = EditorGUILayout.TextArea(_npcDialogue, GUILayout.MinHeight(54));

            _npcIsShop = EditorGUILayout.Toggle("Is Shopkeeper", _npcIsShop);
            if (_npcIsShop)
            {
                EditorGUI.indentLevel++;
                _npcPersonality = (NPCPersonality)EditorGUILayout.EnumPopup("Personality", _npcPersonality);
                _npcShopPool = (SOItemPool)EditorGUILayout.ObjectField(
                    new GUIContent("Shop Pool", "Stock is rolled from this. Defaults to Main Item Pool."),
                    _npcShopPool != null ? _npcShopPool : _mainPool, typeof(SOItemPool), false);
                _npcShopSize = EditorGUILayout.IntSlider("Stock Size", _npcShopSize, 1, 10);
                EditorGUI.indentLevel--;
            }

            ColorBtn("➕  Create Configured NPC", CreateConfiguredNPC, new Color(0.7f, 0.85f, 1f));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void CreateConfiguredNPC()
    {
        _log.Clear();

        const string folder = "Assets/GameData/NPCs";
        EnsureFolder(folder);

        string safe = SanitizeFileName(string.IsNullOrWhiteSpace(_npcName) ? "NPC" : _npcName);

        // 1. Dialogue asset (one conversation; each non-empty row is a spoken line).
        var dlg = ScriptableObject.CreateInstance<SODialogueLine>();
        dlg.lines = (_npcDialogue ?? "")
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        string dlgPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Dialogue_{safe}.asset");
        AssetDatabase.CreateAsset(dlg, dlgPath);

        // 2. NPCData asset.
        var npcData = ScriptableObject.CreateInstance<NPCData>();
        npcData.npcName          = _npcName;
        npcData.npcId            = string.IsNullOrWhiteSpace(_npcId) ? "npc_" + safe.ToLower() : _npcId;
        npcData.dialoguePool     = new[] { dlg };
        npcData.isShopkeeper     = _npcIsShop;
        npcData.personality      = _npcPersonality;
        npcData.shopItemPool     = _npcIsShop ? (_npcShopPool != null ? _npcShopPool : _mainPool) : null;
        npcData.shopInventorySize = Mathf.Max(1, _npcShopSize);
        string dataPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/NPC_{safe}.asset");
        AssetDatabase.CreateAsset(npcData, dataPath);
        AssetDatabase.SaveAssets();

        Log($"✓ Assets: {dlgPath}");
        Log($"✓ Assets: {dataPath}");
        if (_npcIsShop && _npcShopPool == null && _mainPool == null)
            Log("⚠ Shopkeeper has no shop pool (set Main Item Pool or a Shop Pool) — shop will be empty.");

        // 3. Scene GameObject, pre-wired.
        var go = SceneGO(_npcName);
        var col = go.AddComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.height = 2f;
        col.center = Vector3.up;

        var interactable = go.AddComponent<Interactable>();
        var npc          = go.AddComponent<NPCController>();
        WriteObj(npc, "data", npcData);

        if (_npcIsShop)
        {
            var shop = go.AddComponent<ShopInventory>();
            WriteObj(shop, "data", npcData);
        }

        UnityEventTools.AddPersistentListener(interactable.OnUse, npc.OnInteract);

        Finish(go, $"NPC '{_npcName}' created{(_npcIsShop ? " (shopkeeper)" : "")} with data + dialogue assets.");
    }

    void CreateSpawnZone()
    {
        var go = SceneGO("EnemySpawnZone");

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(12f, 4f, 12f);

        go.AddComponent<EnemySpawnZone>();

        Finish(go, "Enemy Spawn Zone created.\n  → Assign SOEnemy types + counts\n  → Set a unique Zone Id");
    }

    void CreateCinematicTrigger()
    {
        var go = SceneGO("CinematicTrigger");

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(3f, 3f, 3f);

        go.AddComponent<CinematicTriggerVolume>();

        Finish(go, "Cinematic Trigger created.\n  → Assign a CinematicSequence asset");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. HEALTH CHECK
    // ─────────────────────────────────────────────────────────────────────

    void ScanScene()
    {
        _log.Clear();

        Log("── Core singletons ──");
        CheckType<SaveManager>("SaveManager");
        CheckType<CheckpointManager>("CheckpointManager");
        CheckType<BossTracker>("BossTracker");
        CheckType<KeyInventory>("KeyInventory");
        CheckType<InputSystem>("InputSystem");
        CheckType<RandomizerSystem>("RandomizerSystem");

        // Duplicate input router → every input fires twice → toggles (pause/inventory) break.
        int inputCount = FindObjectsByType<InputSystem>(FindObjectsSortMode.None).Length;
        if (inputCount > 1)
            Log($"  ❌ {inputCount} InputSystem components found — must be exactly 1 " +
                "(duplicates fire every input twice and break pause/inventory toggles)");

        // EventSystem → required for clickable UI buttons (pause/shop/menu).
        int esCount = FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length;
        if (esCount == 0)      Log("  ⚠ No EventSystem — auto-created at runtime, but add one for clarity");
        else if (esCount > 1)  Log($"  ❌ {esCount} EventSystems — must be exactly 1");

        Log("\n── UI systems ──");
        CheckType<DialogueUI>("DialogueUI");
        CheckType<ShopUI>("ShopUI");
        CheckType<PauseMenuUI>("PauseMenuUI");
        CheckType<PickupPopupUI>("PickupPopup");
        CheckType<DeathScreenUI>("DeathScreen");
        CheckType<MinimapManager>("MinimapManager");
        CheckType<VFXPlayer>("VFXPlayer");

        Log("\n── Player ──");
        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm == null)
        {
            Log("❌  PlayerMovement not in scene");
        }
        else
        {
            Log($"✓  Player: {pm.name}");
            if (!pm.CompareTag("Player"))          Log("  ❌ Tag is not 'Player'");
            if (pm.modelTransform == null)          Log("  ❌ modelTransform not assigned");
            if (pm.cameraTarget   == null)          Log("  ❌ cameraTarget not assigned");

            var inv = pm.GetComponent<InventoryHandler>();
            if (inv != null)
            {
                var so = new SerializedObject(inv);
                if (so.FindProperty("defaultBottle").objectReferenceValue == null)
                    Log("  ❌ InventoryHandler.defaultBottle not assigned");
                if (so.FindProperty("itemPool").objectReferenceValue == null)
                    Log("  ❌ InventoryHandler.itemPool not assigned");
            }

            var anim = pm.GetComponentInChildren<Animator>();
            if (anim == null)
                Log("  ❌ No Animator on model child");
            else
            {
                if (anim.runtimeAnimatorController == null)
                    Log("  ❌ Animator has no Controller assigned");
                if (anim.avatar == null)
                    Log("  ❌ Animator has no Avatar assigned (required for Humanoid IK)");
                if (anim.GetComponent<PlayerIK>() == null)
                    Log("  ⚠  IK not set up on model — run 'Add IK + Anim to Model Child'");
                bool ikPass = false;
                for (int i = 0; i < anim.layerCount; i++)
                {
                    // Can't query IK pass at runtime, so we just check if PlayerIK exists
                    // as a proxy. Remind the user regardless.
                }
                Log("  ⚠  Remember: Animator window → Base Layer → IK Pass ✓");
            }
        }

        Log("\n── World objects ──");
        var chests = FindObjectsByType<ChestBehaviour>(FindObjectsSortMode.None);
        Log($"✓  Chests: {chests.Length}");
        var noId = chests.Where(c => string.IsNullOrEmpty(c.locationId)).ToArray();
        if (noId.Length > 0) Log($"  ❌ {noId.Length} chest(s) have empty Location Id");

        var zones = FindObjectsByType<EnemySpawnZone>(FindObjectsSortMode.None);
        Log($"✓  Enemy Spawn Zones: {zones.Length}");

        var cps = FindObjectsByType<CheckpointTrigger>(FindObjectsSortMode.None);
        Log(cps.Length > 0 ? $"✓  Checkpoints: {cps.Length}" : "⚠  No checkpoints in scene");

        var rs = FindFirstObjectByType<RandomizerSystem>();
        if (rs != null)
        {
            var so = new SerializedObject(rs);
            if (so.FindProperty("pool").objectReferenceValue == null)
                Log("❌  RandomizerSystem.pool not assigned");
            else
                Log("✓  RandomizerSystem has pool");
        }

        Log("\n✅  Scan complete.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────

    // Creates GO, places it in front of the scene camera.
    static GameObject SceneGO(string name)
    {
        var go = new GameObject(name);
        if (SceneView.lastActiveSceneView != null)
        {
            var cam = SceneView.lastActiveSceneView.camera;
            go.transform.position = cam.transform.position + cam.transform.forward * 5f;
        }
        return go;
    }

    static void Finish(GameObject go, string msg)
    {
        Undo.RegisterCreatedObjectUndo(go, $"Create {go.name}");
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"[Wizard] {msg}", go);
    }

    static GameObject FindOrCreate(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) return go;
        go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return go;
    }

    // Ensures one EventSystem driven by the new Input System (clickable UI).
    void EnsureEventSystem()
    {
        var es = FindFirstObjectByType<EventSystem>();
        InputSystemUIInputModule module;

        if (es == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            module = go.AddComponent<InputSystemUIInputModule>();
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            Log("✓ EventSystem  (InputSystemUIInputModule)");
        }
        else
        {
            // Swap a legacy module that would throw under the new Input System.
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                DestroyImmediate(legacy);
                Log("⚠ Removed legacy StandaloneInputModule from EventSystem");
            }
            module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = Undo.AddComponent<InputSystemUIInputModule>(es.gameObject);
            Log("✓ EventSystem present");
        }

        // Without default UI actions, the module registers no clicks.
        if (module != null && module.point == null)
            module.AssignDefaultActions();
    }

    // Recursively creates an Asset folder path (e.g. "Assets/GameData/NPCs") if absent.
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf   = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static string SanitizeFileName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    // Adds component if not already present.
    static void EnsureComp<T>(GameObject go) where T : Component
    {
        if (go.GetComponent<T>() == null) Undo.AddComponent<T>(go);
    }

    // Adds component and logs result.
    void AutoAdd<T>(GameObject go) where T : Component
    {
        if (go.GetComponent<T>() == null)
        {
            Undo.AddComponent<T>(go);
            Log($"  + Added {typeof(T).Name}");
        }
        else Log($"  ✓ {typeof(T).Name}");
    }

    // Sets a serialized string field.
    static void WriteString(Component c, string field, string value)
    {
        var so = new SerializedObject(c);
        var p  = so.FindProperty(field);
        if (p == null) return;
        p.stringValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Sets a serialized object-reference field.
    static void WriteObj(Component c, string field, Object value)
    {
        var so = new SerializedObject(c);
        var p  = so.FindProperty(field);
        if (p == null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    void CheckType<T>(string label) where T : Component
    {
        var found = FindFirstObjectByType<T>();
        Log(found != null ? $"  ✓ {label}" : $"  ❌ {label} missing");
    }

    void Log(string msg) => _log.Add(msg);

    // ─────────────────────────────────────────────────────────────────────
    // GUI HELPERS
    // ─────────────────────────────────────────────────────────────────────

    static void BigLabel(string text)
    {
        var s = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        EditorGUILayout.LabelField(text, s, GUILayout.Height(28));
    }

    static void Section(string title)
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    }

    static void ColorBtn(string label, System.Action action, Color color)
    {
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = color;
        if (GUILayout.Button(label, GUILayout.Height(30))) action();
        GUI.backgroundColor = prev;
    }

    static void SmallBtn(string label, System.Action action)
    {
        if (GUILayout.Button(label, GUILayout.Height(30))) action();
    }
}

// Extension so callers can write go.EnsureComp<T>() instead of static calls.
internal static class WizardGOExt
{
    public static T EnsureComp<T>(this GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }
}
#endif
