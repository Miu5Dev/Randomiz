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

    // ── Item / Weapon builder state ───────────────────────────────────────
    private bool             _itemFoldout;
    private int              _itemTypeIndex;
    private string           _itemSaveFolder = "Assets/GameData/Items";
    private ScriptableObject _itemDraft;       // in-progress item instance
    private Editor           _itemDraftEditor;
    private System.Type[]    _itemTypes;        // cached non-abstract SOItem subclasses

    // ── Chest builder state ───────────────────────────────────────────────
    private bool      _chestFoldout        = true;
    private GameObject _chestModelPrefab;
    private string    _chestLidName        = "Lid";
    private Vector3   _chestOpenEuler      = new(-105f, 0f, 0f);
    private float     _chestOpenDuration   = 0.5f;
    private Vector3   _chestColliderSize   = new(1f, 0.8f, 0.8f);
    private Vector3   _chestColliderCenter = new(0f, 0f, 0f);

    // ── Weapon visual builder state ───────────────────────────────────────
    private bool       _weaponFoldout    = true;
    private SOItem     _wpnSwordAsset;
    private GameObject _wpnSwordPrefab;
    private string     _wpnSwordGripName   = "OffHandGrip";
    private string     _wpnSwordHitboxName = "WeaponPivot";
    private SOItem     _wpnSlingAsset;
    private GameObject _wpnSlingPrefab;
    private SOItem     _wpnGrappleAsset;
    private GameObject _wpnGrapplePrefab;

    // ── Audio setup state ─────────────────────────────────────────────────
    private bool         _audioFoldout    = true;
    private AudioClip    _bgmDefaultTrack;
    private SOBGMProfile _bgmProfile;

    // ── Character SFX / VFX state ─────────────────────────────────────────
    private bool         _charVfxFoldout      = true;
    private SOVFXProfile _vfxProfile;
    private float        _vfxStepMinSpeed     = 1.5f;
    private float        _vfxStepWalkInterval = 0.5f;
    private float        _vfxStepRunInterval  = 0.3f;
    private float        _vfxRunThreshold     = 5f;
    // Parallel arrays indexed by SOVFXProfile.AllActionNames
    private AudioClip[]  _bindSfx;
    private GameObject[] _bindPrefab;
    private float[]      _bindVolume;
    private float[]      _bindDuration;

    // ── UI theme editor state ─────────────────────────────────────────────
    private SOUITheme _themeTarget;
    private Editor    _themeEditor;

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
        using (new GUILayout.HorizontalScope())
        {
            SmallBtn("Crystal Boss", CreateCrystalBoss);
            SmallBtn("Boss Area",    CreateBossArea);
            SmallBtn("Player Spawn", CreatePlayerSpawn);
        }
        using (new GUILayout.HorizontalScope())
        {
            SmallBtn("Music Zone",   CreateMusicZone);
            SmallBtn("Ambient Zone", CreateAmbientZone);
        }
        EditorGUILayout.Space(6);
        DrawNpcBuilder();
        EditorGUILayout.Space(4);
        DrawChestBuilder();
        EditorGUILayout.Space(4);
        DrawWeaponVisualBuilder();
        EditorGUILayout.Space(4);
        DrawAudioSetup();
        EditorGUILayout.Space(4);
        DrawCharacterVFXSetup();
        EditorGUILayout.Space(4);
        DrawItemBuilder();
        EditorGUILayout.Space(8);

        // ── 4 UI Theme (custom images) ────────────────────────────────────
        DrawThemeEditor();

        // ── 5 Validate ───────────────────────────────────────────────────
        Section("5.  SCENE HEALTH CHECK");
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
        EnsureComp<RunTracker>(gs);
        Log("✓ GameSystems  (SaveManager · CheckpointManager · BossTracker · KeyInventory · RunTracker)");

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
        FindOrCreate("VictoryScreen").EnsureComp<VictoryScreenUI>();
        FindOrCreate("BossIntroPopup").EnsureComp<BossIntroPopupUI>();
        FindOrCreate("CoinHUD").EnsureComp<CoinHUD>();
        Log("✓ UI  (DialogueUI · ShopUI · PauseMenuUI · PickupPopup · DeathScreen · VictoryScreen · BossIntroPopup · CoinHUD)");

        FindOrCreate("MinimapManager").EnsureComp<MinimapManager>();
        FindOrCreate("MinimapUI").EnsureComp<MinimapUI>();
        Log("✓ Minimap  (MinimapManager · MinimapUI)");

        FindOrCreate("VFXPlayer").EnsureComp<VFXPlayer>();
        Log("✓ VFXPlayer");

        // ── MusicManager ─────────────────────────────────────────────────
        var mmGO = FindOrCreate("MusicManager");
        EnsureComp<MusicManager>(mmGO);
        var mm = mmGO.GetComponent<MusicManager>();
        if (_bgmDefaultTrack != null) WriteObj(mm, "defaultTrack", _bgmDefaultTrack);
        if (_bgmProfile      != null) WriteObj(mm, "bossProfile",  _bgmProfile);
        Log("✓ MusicManager");

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
        FindOrCreate("MusicManager").EnsureComp<MusicManager>();
        Log("✅  Main menu scene ready.  (MusicManager added — set Default Track in its inspector)");
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

        // Model child — declared early so pivot check can use it.
        var anim = root.GetComponentInChildren<Animator>();
        if (anim == null) Log("  ❌ No Animator in children — add Animator to the model child");
        else
        {
            Log($"  Model GO: {anim.gameObject.name}");
            Log(anim.GetComponent<PlayerIK>() != null
                ? "  ✓ IK components present"
                : "  ⚠ IK not yet set up — click 'Add IK + Anim to Model Child'");
        }

        // EquipHandler pivot
        var eq = root.GetComponent<EquipHandler>();
        if (eq != null)
        {
            if (eq.ItemsPivotPoint == null)
            {
                Log("  ❌ EquipHandler.ItemsPivotPoint not set → run '🦴 Create Hand Pivot' in Weapon Visual Builder.");
            }
            else
            {
                // Verify it is under the right hand bone, not a static root like [GFX].
                bool underHandBone = false;
                if (anim != null && anim.avatar != null && anim.avatar.isHuman)
                {
                    Transform hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    if (hand != null)
                    {
                        Transform t = eq.ItemsPivotPoint;
                        while (t != null) { if (t == hand) { underHandBone = true; break; } t = t.parent; }
                    }
                }
                if (underHandBone)
                    Log($"  ✓ ItemsPivotPoint under Right Hand bone");
                else
                    Log($"  ⚠ ItemsPivotPoint ({eq.ItemsPivotPoint.name}) is NOT under the Right Hand bone — weapons won't follow the hand animation. Run '🦴 Create Hand Pivot'.");
            }
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
        WriteString(chest, "locationId", UniqueChestId());

        // Wire OnUse → OpenInteract (no-arg, so UnityEvent can call it)
        UnityEventTools.AddPersistentListener(interactable.OnUse, chest.OpenInteract);

        Finish(go, "Chest created.\n  → Rename Location Id if desired\n  → Assign pool if not auto-filled");
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

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
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

    // ── Chest builder ─────────────────────────────────────────────────────

    void DrawChestBuilder()
    {
        _chestFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_chestFoldout, "🪣  Chest Builder");
        if (_chestFoldout)
        {
            EditorGUI.indentLevel++;
            _chestModelPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Chest Model", "Prefab with the chest mesh. Placed as a child of the chest GO."),
                _chestModelPrefab, typeof(GameObject), false);

            _chestLidName = EditorGUILayout.TextField(
                new GUIContent("Lid Child Name", "Name of the child Transform that rotates when opening (e.g. 'Lid')."),
                _chestLidName);

            _chestOpenEuler = EditorGUILayout.Vector3Field(
                new GUIContent("Open Euler Offset", "Euler degrees added to the lid's local rotation when fully open."),
                _chestOpenEuler);

            _chestOpenDuration = EditorGUILayout.FloatField(
                new GUIContent("Open Duration (s)", "Seconds for the opening animation."),
                _chestOpenDuration);

            EditorGUILayout.Space(2);
            _chestColliderSize   = EditorGUILayout.Vector3Field(
                new GUIContent("Collider Size",   "Size of the interaction trigger BoxCollider."),
                _chestColliderSize);
            _chestColliderCenter = EditorGUILayout.Vector3Field(
                new GUIContent("Collider Center", "Local offset of the interaction trigger BoxCollider."),
                _chestColliderCenter);

            ColorBtn("➕  Create Configured Chest", CreateConfiguredChest, new Color(0.9f, 0.78f, 0.45f));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void CreateConfiguredChest()
    {
        _log.Clear();
        var go = SceneGO("Chest");

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size   = _chestColliderSize;
        col.center = _chestColliderCenter;

        var interactable = go.AddComponent<Interactable>();
        var chest        = go.AddComponent<ChestBehaviour>();

        // Instantiate the visual model as a child.
        Transform lid = null;
        if (_chestModelPrefab != null)
        {
            var modelGO = (GameObject)PrefabUtility.InstantiatePrefab(_chestModelPrefab, go.transform);
            modelGO.name = "Model";
            modelGO.transform.localPosition = Vector3.zero;
            modelGO.transform.localRotation = Quaternion.identity;

            if (!string.IsNullOrWhiteSpace(_chestLidName))
            {
                lid = FindInHierarchy(modelGO.transform, _chestLidName);
                if (lid != null) Log($"✓ Lid '{_chestLidName}' found and assigned.");
                else             Log($"⚠ No child named '{_chestLidName}' found — assign Lid Transform manually.");
            }
        }
        else Log("⚠ No model assigned — chest has no visual. Assign Chest Model and recreate, or set up manually.");

        // Single SerializedObject pass — multiple SO instances on the same component can clobber each other.
        var so = new SerializedObject(chest);
        if (_mainPool != null) so.FindProperty("pool").objectReferenceValue = _mainPool;
        so.FindProperty("locationId").stringValue            = UniqueChestId();
        so.FindProperty("lidTransform").objectReferenceValue = lid;
        so.FindProperty("openEulerOffset").vector3Value      = _chestOpenEuler;
        so.FindProperty("openDuration").floatValue           = _chestOpenDuration;
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(interactable.OnUse, chest.OpenInteract);

        Finish(go, "Chest created.\n  → Set unique Location Id (or rename the GO)\n  → Assign pool if not auto-filled");
    }

    void CreateSpawnZone()
    {
        var go = SceneGO("EnemySpawnZone");

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(12f, 4f, 12f);

        go.AddComponent<EnemySpawnZone>();

        Finish(go, "Enemy Spawn Zone created.\n  → Assign SOEnemy types + counts\n  → Set a unique Zone Id");
    }

    void CreateCinematicTrigger()
    {
        var go = SceneGO("CinematicTrigger");

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(3f, 3f, 3f);

        go.AddComponent<CinematicTriggerVolume>();

        Finish(go, "Cinematic Trigger created.\n  → Assign a CinematicSequence asset");
    }

    // ── Boss / spawn scene objects ─────────────────────────────────────────

    void CreateCrystalBoss()
    {
        var go = SceneGO("CrystalBoss");

        // Damage collider on the ROOT — the player's melee targets transform.root, so the
        // hit must resolve here (where the HealthSystem lives). Kinematic Rigidbody keeps
        // moving the collider cheap (no static-collider churn).
        var col = go.AddComponent<BoxCollider>();
        col.size   = new Vector3(1.4f, 2.4f, 1.4f);
        col.center = new Vector3(0f, 1.2f, 0f);

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        // Visual placeholder (no collider of its own).
        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesh.name = "CrystalMesh";
        mesh.transform.SetParent(go.transform, false);
        mesh.transform.localScale    = new Vector3(1.2f, 2.2f, 1.2f);
        mesh.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        mesh.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        DestroyImmediate(mesh.GetComponent<Collider>());
        var meshRenderer = mesh.GetComponent<Renderer>();

        go.AddComponent<HealthSystem>();
        var boss = go.AddComponent<CrystalBossController>();
        go.AddComponent<BossHealthBar>();

        WriteObj(boss, "crystalRenderer", meshRenderer);

        Finish(go, "Crystal Boss created.\n" +
                   "  → Set Max Hearts on HealthSystem (total boss HP)\n" +
                   "  → Assign Waves (SOEnemy + counts) on CrystalBossController\n" +
                   "  → Put this GO on a layer included in your sword's Target Layers\n" +
                   "  → Set Boss Name on CrystalBossController + BossHealthBar");
    }

    void CreateBossArea()
    {
        var go = SceneGO("BossArea");

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(14f, 6f, 14f);

        go.AddComponent<BossArea>();

        Finish(go, "Boss Area created.\n" +
                   "  → Assign 'Boss To Activate' (a disabled boss in the scene) or 'Boss Prefab'\n" +
                   "  → Set Boss Name / Boss Id (or leave blank to use the boss's own)");
    }

    void CreatePlayerSpawn()
    {
        var go = SceneGO("PlayerSpawn");
        go.AddComponent<PlayerSpawnPoint>();
        Finish(go, "Player Spawn created.\n  → Position it where the player should respawn before any checkpoint.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3b. AUDIO SETUP
    // ─────────────────────────────────────────────────────────────────────

    void DrawAudioSetup()
    {
        _audioFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_audioFoldout, "🎵  Audio Setup");
        if (_audioFoldout)
        {
            EditorGUI.indentLevel++;

            // ── MusicManager ──────────────────────────────────────────────
            var mm = FindFirstObjectByType<MusicManager>();

            if (mm == null)
                EditorGUILayout.HelpBox("No MusicManager in scene. Run '▶ Setup Full Game Scene' or assign " +
                    "the fields and click Apply.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox($"✓  MusicManager found: {mm.gameObject.name}", MessageType.None);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Music Manager", EditorStyles.boldLabel);

            _bgmDefaultTrack = (AudioClip)EditorGUILayout.ObjectField(
                new GUIContent("Default Track",
                    "Plays when no MusicZone is active (overworld / hub area)."),
                _bgmDefaultTrack, typeof(AudioClip), false);

            _bgmProfile = (SOBGMProfile)EditorGUILayout.ObjectField(
                new GUIContent("Boss BGM Profile",
                    "Maps bossId strings to AudioClips. Boss encounters crossfade automatically.\n" +
                    "Create via Assets > Create > Randomiz > BGM Profile."),
                _bgmProfile, typeof(SOBGMProfile), false);

            EditorGUILayout.Space(2);
            using (new GUILayout.HorizontalScope())
            {
                ColorBtn("✔  Apply to MusicManager", ApplyAudioSettings, new Color(0.55f, 1f, 0.85f));
                if (GUILayout.Button("New BGM Profile", GUILayout.Height(30))) CreateBGMProfileAsset();
            }

            EditorGUILayout.Space(6);

            // ── Quick-create info ─────────────────────────────────────────
            EditorGUILayout.LabelField("World Audio Zones", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Music Zone  — crossfades BGM when player enters (priority-based: boss > dungeon > overworld).\n" +
                "Ambient Zone — loops an ambient clip (wind, cave drip, etc.) independently of BGM.",
                MessageType.None);

            var musicZones   = FindObjectsByType<MusicZone>(FindObjectsSortMode.None);
            var ambientZones = FindObjectsByType<AmbientSoundZone>(FindObjectsSortMode.None);
            EditorGUILayout.LabelField($"Music Zones in scene: {musicZones.Length}   |   " +
                                       $"Ambient Zones: {ambientZones.Length}",
                EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void ApplyAudioSettings()
    {
        _log.Clear();

        var mmGO = FindOrCreate("MusicManager");
        EnsureComp<MusicManager>(mmGO);
        var mm = mmGO.GetComponent<MusicManager>();

        var so = new SerializedObject(mm);
        if (_bgmDefaultTrack != null)
        {
            so.FindProperty("defaultTrack").objectReferenceValue = _bgmDefaultTrack;
            Log($"✓ defaultTrack → {_bgmDefaultTrack.name}");
        }
        else Log("⚠ Default Track not set — silence outside music zones.");

        if (_bgmProfile != null)
        {
            so.FindProperty("bossProfile").objectReferenceValue = _bgmProfile;
            Log($"✓ bossProfile → {_bgmProfile.name}");
        }
        else Log("⚠ Boss BGM Profile not set — boss encounters won't change music.");

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mmGO);
        Log("✅  MusicManager configured.");
    }

    void CreateBGMProfileAsset()
    {
        _log.Clear();
        const string folder = "Assets/GameData";
        EnsureFolder(folder);

        var profile = ScriptableObject.CreateInstance<SOBGMProfile>();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/BGMProfile.asset");
        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();

        _bgmProfile = profile;
        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
        Log($"✓ Created {path}  — add one entry per boss (bossId must match OnBossEncounterStartedEvent.bossId).");
    }

    void CreateMusicZone()
    {
        var go = SceneGO("MusicZone");
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(20f, 6f, 20f);
        go.AddComponent<MusicZone>();
        Finish(go, "Music Zone created.\n  → Assign Clip and set Priority (overworld=0, dungeon=10, boss=20)\n  → Resize the BoxCollider to cover the area");
    }

    void CreateAmbientZone()
    {
        var go = SceneGO("AmbientZone");
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(12f, 5f, 12f);
        go.AddComponent<AmbientSoundZone>();
        Finish(go, "Ambient Sound Zone created.\n  → Assign Clip (wind, cave, water, etc.)\n  → Adjust Max Volume and Fade Speed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3e. CHARACTER SFX / VFX SETUP
    // ─────────────────────────────────────────────────────────────────────

    void EnsureBindArrays()
    {
        int n = SOVFXProfile.AllActionNames.Length;
        if (_bindSfx == null || _bindSfx.Length != n)
            _bindSfx = new AudioClip[n];
        if (_bindPrefab == null || _bindPrefab.Length != n)
            _bindPrefab = new GameObject[n];
        if (_bindVolume == null || _bindVolume.Length != n)
        {
            float[] prev = _bindVolume;
            _bindVolume = new float[n];
            for (int i = 0; i < n; i++) _bindVolume[i] = prev != null && i < prev.Length ? prev[i] : 1f;
        }
        if (_bindDuration == null || _bindDuration.Length != n)
        {
            float[] prev = _bindDuration;
            _bindDuration = new float[n];
            for (int i = 0; i < n; i++) _bindDuration[i] = prev != null && i < prev.Length ? prev[i] : 2f;
        }
    }

    void DrawCharacterVFXSetup()
    {
        _charVfxFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_charVfxFoldout, "🎆  Character SFX / VFX");
        if (!_charVfxFoldout) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        EditorGUI.indentLevel++;
        EnsureBindArrays();

        // ── VFXPlayer status ──────────────────────────────────────────────
        var vfxPlayer = FindFirstObjectByType<VFXPlayer>();
        if (vfxPlayer == null)
            EditorGUILayout.HelpBox(
                "No VFXPlayer in scene. Run '▶ Setup Full Game Scene' first, or the Apply button creates one.",
                MessageType.Warning);
        else
            EditorGUILayout.HelpBox($"✓  VFXPlayer: {vfxPlayer.gameObject.name}", MessageType.None);

        EditorGUILayout.Space(3);

        // ── Profile asset ─────────────────────────────────────────────────
        EditorGUILayout.LabelField("VFX Profile", EditorStyles.boldLabel);
        using (new GUILayout.HorizontalScope())
        {
            _vfxProfile = (SOVFXProfile)EditorGUILayout.ObjectField(
                new GUIContent("Profile Asset",
                    "SOVFXProfile asset with all SFX + VFX bindings for the player.\n" +
                    "Create one below, fill the table, then click Apply."),
                _vfxProfile, typeof(SOVFXProfile), false);
            if (GUILayout.Button("New", GUILayout.Width(46), GUILayout.Height(18)))
                CreateVFXProfileAsset();
        }

        if (_vfxProfile != null)
        {
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("↓ Load bindings from Profile", GUILayout.Height(20)))
                    LoadBindingsFromProfile();
                if (GUILayout.Button("↓ Load timing from VFXPlayer", GUILayout.Height(20)))
                    LoadTimingFromVFXPlayer();
            }
        }

        EditorGUILayout.Space(5);

        // ── Binding table ─────────────────────────────────────────────────
        EditorGUILayout.LabelField("Action Bindings  (SFX clip + volume  |  VFX prefab + lifetime)", EditorStyles.boldLabel);

        var hdr = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Action",      hdr, GUILayout.Width(136));
            GUILayout.Label("SFX Clip",    hdr, GUILayout.MinWidth(70));
            GUILayout.Label("Vol",         hdr, GUILayout.Width(36));
            GUILayout.Label("VFX Prefab",  hdr, GUILayout.MinWidth(70));
            GUILayout.Label("Dur(s)",      hdr, GUILayout.Width(40));
        }
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        string[] actions = SOVFXProfile.AllActionNames;
        for (int i = 0; i < actions.Length; i++)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(VfxActionEmoji(actions[i]) + actions[i], GUILayout.Width(136));
                _bindSfx[i] = (AudioClip)EditorGUILayout.ObjectField(
                    _bindSfx[i], typeof(AudioClip), false, GUILayout.MinWidth(70));
                _bindVolume[i]   = EditorGUILayout.FloatField(_bindVolume[i], GUILayout.Width(36));
                _bindPrefab[i]   = (GameObject)EditorGUILayout.ObjectField(
                    _bindPrefab[i], typeof(GameObject), false, GUILayout.MinWidth(70));
                _bindDuration[i] = EditorGUILayout.FloatField(_bindDuration[i], GUILayout.Width(40));
            }
        }

        EditorGUILayout.Space(6);

        // ── Footstep timing ───────────────────────────────────────────────
        EditorGUILayout.LabelField("Footstep Timing", EditorStyles.boldLabel);
        _vfxStepMinSpeed     = EditorGUILayout.FloatField(
            new GUIContent("Min Speed (m/s)",      "Minimum horizontal speed before footstep sounds fire."),
            _vfxStepMinSpeed);
        _vfxStepWalkInterval = EditorGUILayout.FloatField(
            new GUIContent("Walk Interval (s)",    "Seconds between footstep sounds at walk speed."),
            _vfxStepWalkInterval);
        _vfxStepRunInterval  = EditorGUILayout.FloatField(
            new GUIContent("Run Interval (s)",     "Seconds between footstep sounds at run speed."),
            _vfxStepRunInterval);
        _vfxRunThreshold     = EditorGUILayout.FloatField(
            new GUIContent("Run Speed Threshold",  "Horizontal speed (m/s) at which the run interval activates."),
            _vfxRunThreshold);

        EditorGUILayout.Space(4);
        ColorBtn("✔  Apply SFX / VFX to VFXPlayer", ApplyCharacterVFXSettings, new Color(0.55f, 1f, 0.85f));

        EditorGUI.indentLevel--;
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    static string VfxActionEmoji(string a)
    {
        switch (a)
        {
            case SOVFXProfile.WALK_STEP:            return "👣 ";
            case SOVFXProfile.LAND:                 return "🔻 ";
            case SOVFXProfile.ATTACK_SWING:         return "⚔ ";
            case SOVFXProfile.ATTACK_HIT:           return "💥 ";
            case SOVFXProfile.TAKE_DAMAGE:          return "🛡 ";
            case SOVFXProfile.DEATH:                return "💀 ";
            case SOVFXProfile.DASH:                 return "💨 ";
            case SOVFXProfile.WALLHUG_ENTER:        return "🧱 ";
            case SOVFXProfile.LEDGE_GRAB:           return "🤜 ";
            case SOVFXProfile.LEDGE_CLIMB:          return "⬆ ";
            case SOVFXProfile.ITEM_PICKUP:          return "📦 ";
            case SOVFXProfile.CHECKPOINT_ACTIVATE:  return "🚩 ";
            case SOVFXProfile.ENEMY_DEATH:          return "☠ ";
            case SOVFXProfile.BOSS_SLAM:            return "💢 ";
            default:                                return "";
        }
    }

    void ApplyCharacterVFXSettings()
    {
        _log.Clear();
        EnsureBindArrays();

        var go       = FindOrCreate("VFXPlayer");
        EnsureComp<VFXPlayer>(go);
        var vfxComp  = go.GetComponent<VFXPlayer>();
        var playerSO = new SerializedObject(vfxComp);

        // ── Profile assignment ────────────────────────────────────────────
        if (_vfxProfile == null)
        {
            Log("⚠ No VFX Profile assigned — create one with the New button, fill the table, then Apply.");
        }
        else
        {
            playerSO.FindProperty("profile").objectReferenceValue = _vfxProfile;
            Log($"✓ profile → {_vfxProfile.name}");

            // Write bindings into the profile asset via SerializedObject.
            var profileSO = new SerializedObject(_vfxProfile);
            var bindList  = profileSO.FindProperty("bindings");
            string[] actions = SOVFXProfile.AllActionNames;

            for (int i = 0; i < actions.Length; i++)
            {
                bool hasSfx = _bindSfx[i] != null;
                bool hasVfx = _bindPrefab[i] != null;
                if (!hasSfx && !hasVfx) continue;

                // Find or create entry for this action name.
                int idx = -1;
                for (int j = 0; j < bindList.arraySize; j++)
                {
                    if (bindList.GetArrayElementAtIndex(j)
                        .FindPropertyRelative("actionName").stringValue == actions[i])
                    {
                        idx = j;
                        break;
                    }
                }
                if (idx == -1)
                {
                    bindList.InsertArrayElementAtIndex(bindList.arraySize);
                    idx = bindList.arraySize - 1;
                }

                var entry = bindList.GetArrayElementAtIndex(idx);
                entry.FindPropertyRelative("actionName").stringValue         = actions[i];
                entry.FindPropertyRelative("sfxClip").objectReferenceValue   = _bindSfx[i];
                entry.FindPropertyRelative("sfxVolume").floatValue           = _bindVolume[i];
                entry.FindPropertyRelative("vfxPrefab").objectReferenceValue = _bindPrefab[i];
                entry.FindPropertyRelative("duration").floatValue            = _bindDuration[i];

                Log($"  ✓ [{actions[i]}]  sfx={(_bindSfx[i] != null ? _bindSfx[i].name : "—")}  " +
                    $"vfx={(_bindPrefab[i] != null ? _bindPrefab[i].name : "—")}");
            }

            profileSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(_vfxProfile);
        }

        // ── Footstep timing ───────────────────────────────────────────────
        playerSO.FindProperty("stepMinSpeed").floatValue      = _vfxStepMinSpeed;
        playerSO.FindProperty("stepIntervalWalk").floatValue  = _vfxStepWalkInterval;
        playerSO.FindProperty("stepIntervalRun").floatValue   = _vfxStepRunInterval;
        playerSO.FindProperty("runSpeedThreshold").floatValue = _vfxRunThreshold;

        playerSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(go);

        Log($"✓ Footstep: min={_vfxStepMinSpeed}  walk={_vfxStepWalkInterval}s  " +
            $"run={_vfxStepRunInterval}s  runAt≥{_vfxRunThreshold}");
        Log("✅  Character SFX/VFX applied.");
    }

    void CreateVFXProfileAsset()
    {
        const string folder = "Assets/GameData";
        EnsureFolder(folder);
        var profile = ScriptableObject.CreateInstance<SOVFXProfile>();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/VFXProfile.asset");
        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();
        _vfxProfile = profile;
        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
        Log($"✓ Created {path}  — fill the binding table above and click Apply.");
    }

    void LoadBindingsFromProfile()
    {
        if (_vfxProfile == null) return;
        EnsureBindArrays();
        string[] actions = SOVFXProfile.AllActionNames;
        for (int i = 0; i < actions.Length; i++)
        {
            VFXBinding b = _vfxProfile.GetBinding(actions[i]);
            if (b == null) continue;
            _bindSfx[i]      = b.sfxClip;
            _bindVolume[i]   = b.sfxVolume;
            _bindPrefab[i]   = b.vfxPrefab;
            _bindDuration[i] = b.duration;
        }
        Log($"✓ Bindings loaded from {_vfxProfile.name}.");
    }

    void LoadTimingFromVFXPlayer()
    {
        var vp = FindFirstObjectByType<VFXPlayer>();
        if (vp == null) { Log("❌ No VFXPlayer in scene."); return; }
        var so = new SerializedObject(vp);
        _vfxStepMinSpeed     = so.FindProperty("stepMinSpeed").floatValue;
        _vfxStepWalkInterval = so.FindProperty("stepIntervalWalk").floatValue;
        _vfxStepRunInterval  = so.FindProperty("stepIntervalRun").floatValue;
        _vfxRunThreshold     = so.FindProperty("runSpeedThreshold").floatValue;
        var prof = so.FindProperty("profile").objectReferenceValue as SOVFXProfile;
        if (prof != null && _vfxProfile == null) _vfxProfile = prof;
        Log($"✓ Timing loaded: min={_vfxStepMinSpeed}  walk={_vfxStepWalkInterval}s  " +
            $"run={_vfxStepRunInterval}s  runAt≥{_vfxRunThreshold}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3d. ITEM / WEAPON BUILDER  (create assets with parameters)
    // ─────────────────────────────────────────────────────────────────────

    // Generic creator for any non-abstract SOItem subclass (sword, potion, money…).
    // Renders the chosen type's full inspector inline, then writes it to an asset.
    void DrawItemBuilder()
    {
        _itemFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_itemFoldout, "🗡  Item / Weapon Builder");
        if (_itemFoldout)
        {
            EditorGUI.indentLevel++;
            EnsureItemTypes();

            if (_itemTypes.Length == 0)
            {
                EditorGUILayout.HelpBox("No concrete SOItem types found.", MessageType.Warning);
            }
            else
            {
                string[] names = _itemTypes.Select(NiceTypeName).ToArray();
                int newIndex = EditorGUILayout.Popup("Type", _itemTypeIndex, names);
                if (newIndex != _itemTypeIndex || _itemDraft == null)
                {
                    _itemTypeIndex = newIndex;
                    RebuildItemDraft();
                }

                _itemSaveFolder = EditorGUILayout.TextField("Save Folder", _itemSaveFolder);

                if (_itemDraft != null)
                {
                    if (_itemDraftEditor == null) _itemDraftEditor = Editor.CreateEditor(_itemDraft);
                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    _itemDraftEditor.OnInspectorGUI();
                    EditorGUILayout.EndVertical();
                }

                ColorBtn("➕  Create Item Asset", CreateItemAsset, new Color(0.82f, 0.72f, 1f));
            }

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Open Enemy Creator…"))
                EnemyCreatorWindow.Open();

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void EnsureItemTypes()
    {
        if (_itemTypes != null) return;
        _itemTypes = TypeCache.GetTypesDerivedFrom<SOItem>()
                              .Where(t => !t.IsAbstract)
                              .OrderBy(t => t.Name)
                              .ToArray();
    }

    void RebuildItemDraft()
    {
        if (_itemDraftEditor != null) { DestroyImmediate(_itemDraftEditor); _itemDraftEditor = null; }
        if (_itemDraft != null)       { DestroyImmediate(_itemDraft);       _itemDraft = null; }
        if (_itemTypes == null || _itemTypes.Length == 0) return;

        _itemTypeIndex = Mathf.Clamp(_itemTypeIndex, 0, _itemTypes.Length - 1);
        _itemDraft = ScriptableObject.CreateInstance(_itemTypes[_itemTypeIndex]);
        _itemDraft.hideFlags = HideFlags.DontSave;
    }

    void CreateItemAsset()
    {
        _log.Clear();
        if (_itemDraft == null) { Log("❌  No item to create."); return; }

        EnsureFolder(_itemSaveFolder);

        // File name from the item's itemName field, falling back to the type name.
        var so = new SerializedObject(_itemDraft);
        string itemName = so.FindProperty("itemName")?.stringValue;
        string safe = SanitizeFileName(string.IsNullOrWhiteSpace(itemName) ? _itemDraft.GetType().Name : itemName);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{_itemSaveFolder}/{safe}.asset");

        // Persist a copy so the live draft stays editable for making more items.
        var asset = (ScriptableObject)Object.Instantiate(_itemDraft);
        asset.hideFlags = HideFlags.None;
        asset.name = safe;
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        Log($"✓ Created {path}");
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    static string NiceTypeName(System.Type t)
    {
        // SOSword → "Sword", SOPotion → "Potion".
        string n = t.Name;
        if (n.StartsWith("SO")) n = n.Substring(2);
        return ObjectNames.NicifyVariableName(n);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3c. WEAPON VISUAL BUILDER
    // ─────────────────────────────────────────────────────────────────────

    void DrawWeaponVisualBuilder()
    {
        _weaponFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_weaponFoldout, "⚔  Weapon Visual Builder");
        if (_weaponFoldout)
        {
            EditorGUI.indentLevel++;

            // ── Step 0: Hand pivot ────────────────────────────────────────
            WeaponSubHeader("Step 0 — Hand Pivot");
            DrawHandPivotStatus();
            using (new GUILayout.HorizontalScope())
            {
                SmallBtn("🦴 Create Hand Pivot (Right Hand)", SetupHandPivot);
                SmallBtn("📌 Move Visuals to Pivot",          MoveVisualsToHandPivot);
            }
            EditorGUILayout.Space(6);

            // ── Step 1: Weapon assets + prefabs ───────────────────────────
            WeaponSubHeader("Step 1 — Weapon Assets");
            _wpnSwordAsset = (SOItem)EditorGUILayout.ObjectField(
                new GUIContent("Sword Asset", "The SOSword ScriptableObject asset."),
                _wpnSwordAsset, typeof(SOItem), false);
            _wpnSwordPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Visual Prefab", "3D model prefab to show in the player's hand."),
                _wpnSwordPrefab, typeof(GameObject), false);
            _wpnSwordGripName = EditorGUILayout.TextField(
                new GUIContent("Off-Hand Grip", "Name of the child Transform for HandIK (left-hand grip target)."),
                _wpnSwordGripName);
            _wpnSwordHitboxName = EditorGUILayout.TextField(
                new GUIContent("Hitbox Origin", "Name of the child Transform used as the OverlapBox origin (SOSword.hitboxOriginName)."),
                _wpnSwordHitboxName);

            EditorGUILayout.Space(4);
            WeaponSubHeader("Slingshot");
            _wpnSlingAsset = (SOItem)EditorGUILayout.ObjectField(
                new GUIContent("Slingshot Asset", "The SOSlingShot ScriptableObject asset."),
                _wpnSlingAsset, typeof(SOItem), false);
            _wpnSlingPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Visual Prefab", "3D model prefab to show in the player's hand."),
                _wpnSlingPrefab, typeof(GameObject), false);

            EditorGUILayout.Space(4);
            WeaponSubHeader("Grapple / Rope");
            _wpnGrappleAsset = (SOItem)EditorGUILayout.ObjectField(
                new GUIContent("Grapple Asset", "The SOGrappleHook ScriptableObject asset."),
                _wpnGrappleAsset, typeof(SOItem), false);
            _wpnGrapplePrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Visual Prefab", "3D model prefab to show in the player's hand."),
                _wpnGrapplePrefab, typeof(GameObject), false);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Step 0: create the hand pivot first (must be under the Right Hand bone, not [GFX]).\n" +
                "Step 1: assign weapon assets + prefabs.\n" +
                "Step 2: click Setup — then select each [Visual]/Model child to position it in the Scene view.",
                MessageType.Info);

            ColorBtn("➕  Setup Weapon Visuals on Player", SetupWeaponVisuals, new Color(0.55f, 0.88f, 1f));
            ColorBtn("🔧  Fix & Refresh (strip colliders + restore visibility)", FixWeaponVisuals, new Color(1f, 0.88f, 0.45f));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // Shows a one-line status of the current ItemsPivotPoint directly in the foldout.
    void DrawHandPivotStatus()
    {
        var eq = FindFirstObjectByType<EquipHandler>();
        if (eq == null)
        {
            EditorGUILayout.HelpBox("No EquipHandler in scene — place the player first.", MessageType.Warning);
            return;
        }

        if (eq.ItemsPivotPoint == null)
        {
            EditorGUILayout.HelpBox("ItemsPivotPoint is NOT assigned. Click 🦴 to fix.", MessageType.Error);
            return;
        }

        // Check whether the pivot is actually under a hand bone.
        bool underHandBone = false;
        var anim = eq.GetComponentInChildren<Animator>();
        if (anim != null && anim.avatar != null && anim.avatar.isHuman)
        {
            Transform hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null)
            {
                Transform t = eq.ItemsPivotPoint;
                while (t != null) { if (t == hand) { underHandBone = true; break; } t = t.parent; }
            }
        }

        if (underHandBone)
            EditorGUILayout.HelpBox($"✓  ItemsPivotPoint: {eq.ItemsPivotPoint.name}  (under Right Hand bone)", MessageType.None);
        else
            EditorGUILayout.HelpBox(
                $"⚠  ItemsPivotPoint → {eq.ItemsPivotPoint.name}  is NOT under the Right Hand bone.\n" +
                "Weapons will not follow the hand animation. Click 🦴 to create a proper pivot.",
                MessageType.Warning);
    }

    void SetupHandPivot()
    {
        _log.Clear();

        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm == null) { Log("❌  PlayerMovement not found — place the player in the scene first."); return; }

        var anim = pm.GetComponentInChildren<Animator>();
        if (anim == null) { Log("❌  No Animator found on a child of the player."); return; }

        if (anim.avatar == null || !anim.avatar.isHuman)
        {
            Log("❌  Animator Avatar is not set to Humanoid.");
            Log("  → Select the player model's FBX → Rig tab → Animation Type: Humanoid → Apply.");
            return;
        }

        Transform handBone = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (handBone == null)
        {
            Log("❌  Right Hand bone not found in the Avatar.");
            Log("  → Check your Avatar configuration (Rig → Configure Avatar).");
            return;
        }
        Log($"✓ Right Hand bone: {handBone.name}");

        // Reuse existing pivot under the hand bone if present.
        Transform pivot = handBone.Find("ItemsPivotPoint");
        if (pivot == null)
        {
            var pivotGO = new GameObject("ItemsPivotPoint");
            pivotGO.transform.SetParent(handBone, false);
            Undo.RegisterCreatedObjectUndo(pivotGO, "Create ItemsPivotPoint");
            pivot = pivotGO.transform;
            Log($"✓ Created 'ItemsPivotPoint' under '{handBone.name}'.");
        }
        else
        {
            Log($"✓ 'ItemsPivotPoint' already exists under '{handBone.name}'.");
        }

        // Assign to EquipHandler.
        var eq = pm.GetComponent<EquipHandler>() ?? pm.GetComponentInChildren<EquipHandler>();
        if (eq != null)
        {
            var so = new SerializedObject(eq);
            so.FindProperty("ItemsPivotPoint").objectReferenceValue = pivot;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(eq);
            Log("✓ EquipHandler.ItemsPivotPoint assigned.");
        }
        else Log("⚠ EquipHandler not found on player — assign ItemsPivotPoint manually.");

        Log("\n✅  Hand pivot ready. If you already created weapon visuals under [GFX], click '📌 Move Visuals to Pivot'.");
    }

    void MoveVisualsToHandPivot()
    {
        _log.Clear();

        var eq = FindFirstObjectByType<EquipHandler>();
        if (eq == null) { Log("❌  EquipHandler not found."); return; }

        Transform pivot = eq.ItemsPivotPoint;
        if (pivot == null)
        {
            Log("❌  ItemsPivotPoint not assigned — run '🦴 Create Hand Pivot' first.");
            return;
        }

        var visuals = FindObjectsByType<WeaponVisual>(FindObjectsSortMode.None);
        if (visuals.Length == 0)
        {
            Log("⚠ No WeaponVisual found in scene — nothing to move.");
            return;
        }

        int moved = 0;
        foreach (var wv in visuals)
        {
            if (wv.transform.parent == pivot)
            {
                Log($"  ✓ {wv.gameObject.name} — already under ItemsPivotPoint.");
                continue;
            }
            Undo.SetTransformParent(wv.transform, pivot, $"Reparent {wv.gameObject.name}");
            wv.transform.localPosition = Vector3.zero;
            wv.transform.localRotation = Quaternion.identity;
            EditorUtility.SetDirty(wv.gameObject);
            Log($"  ✓ Moved {wv.gameObject.name} → under ItemsPivotPoint.");
            moved++;
        }

        if (moved > 0)
            Log($"\n✅  {moved} visual(s) reparented. Select [Visual]/Model in the Scene view to reposition.");
        else
            Log("\n✅  All visuals already under ItemsPivotPoint.");
    }

    void FixWeaponVisuals()
    {
        _log.Clear();
        var visuals = FindObjectsByType<WeaponVisual>(FindObjectsSortMode.None);
        if (visuals.Length == 0) { Log("⚠ No WeaponVisual found in scene."); return; }

        int strippedTotal = 0;
        foreach (var wv in visuals)
        {
            int stripped = 0;
            foreach (var col in wv.GetComponentsInChildren<Collider>(true))
            { DestroyImmediate(col); stripped++; }
            foreach (var rb in wv.GetComponentsInChildren<Rigidbody>(true))
            { DestroyImmediate(rb); stripped++; }

            // Restore renderer visibility for edit-mode positioning.
            foreach (var r in wv.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;

            if (stripped > 0)
                Log($"✓ {wv.gameObject.name}: {stripped} physics component(s) stripped.");

            EditorUtility.SetDirty(wv.gameObject);
            strippedTotal += stripped;
        }

        Log(strippedTotal == 0
            ? $"✓ No physics components found in {visuals.Length} WeaponVisual(s) — already clean."
            : $"✓ Total stripped: {strippedTotal}");
        Log("✓ All renderers restored to visible.");
        Log("\n✅  Select [WeaponVisual]/Model in the hierarchy and use Move/Rotate/Scale.");
    }

    void SetupWeaponVisuals()
    {
        _log.Clear();

        var eq = FindFirstObjectByType<EquipHandler>();
        if (eq == null) { Log("❌  EquipHandler not found — place the player prefab first."); return; }

        Transform pivot = eq.ItemsPivotPoint;
        if (pivot == null) { Log("❌  EquipHandler.ItemsPivotPoint is not assigned — set it first."); return; }

        Camera cam = Camera.main ?? FindFirstObjectByType<Camera>();

        int created = 0;

        if (_wpnSwordAsset != null)
        {
            CreateWeaponSlot(pivot, "SwordVisual", _wpnSwordAsset, _wpnSwordPrefab, 0, cam);
            created++;
        }
        else Log("⚠ Sword asset not set — skipping SwordVisual.");

        if (_wpnSlingAsset != null)
        {
            CreateWeaponSlot(pivot, "SlingshotVisual", _wpnSlingAsset, _wpnSlingPrefab, 1, cam);
            created++;
        }
        else Log("⚠ Slingshot asset not set — skipping SlingshotVisual.");

        if (_wpnGrappleAsset != null)
        {
            CreateWeaponSlot(pivot, "GrappleVisual", _wpnGrappleAsset, _wpnGrapplePrefab, 2, cam);
            created++;
        }
        else Log("⚠ Grapple asset not set — skipping GrappleVisual.");

        if (created == 0) { Log("❌  No weapon assets assigned — nothing created."); return; }

        EditorUtility.SetDirty(eq.gameObject);
        Log("\n✅  Done. Crosshair UI fields are optional — assign them manually if needed.");
    }

    // setupType: 0=sword  1=slingshot  2=grapple
    void CreateWeaponSlot(Transform pivot, string slotName, SOItem asset,
                          GameObject visualPrefab, int setupType, Camera cam)
    {
        if (pivot.Find(slotName) != null)
        {
            Log($"⚠ {slotName} already exists — skipping (delete it to recreate).");
            return;
        }

        var slotGO = new GameObject(slotName);
        slotGO.transform.SetParent(pivot, false);
        Undo.RegisterCreatedObjectUndo(slotGO, $"Create {slotName}");

        if (visualPrefab != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, slotGO.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale    = Vector3.one;

            // Strip physics — a held weapon visual must never block player movement.
            int stripped = 0;
            foreach (var col in slotGO.GetComponentsInChildren<Collider>(true))
            { DestroyImmediate(col); stripped++; }
            foreach (var rb in slotGO.GetComponentsInChildren<Rigidbody>(true))
            { DestroyImmediate(rb); stripped++; }
            if (stripped > 0)
                Log($"  ({stripped} physics component(s) stripped from {visualPrefab.name})");
        }
        else
        {
            Log($"⚠ {slotName}: no visual prefab assigned — GO created without mesh.");
        }

        var wv   = slotGO.AddComponent<WeaponVisual>();
        var wvSO = new SerializedObject(wv);
        wvSO.FindProperty("itemAsset").objectReferenceValue = asset;
        wvSO.ApplyModifiedPropertiesWithoutUndo();

        if (setupType == 0) // Sword
        {
            EnsureWeaponChild(slotGO.transform, _wpnSwordGripName);
            EnsureWeaponChild(slotGO.transform, _wpnSwordHitboxName);
            Log($"✓ SwordVisual — WeaponVisual + '{_wpnSwordGripName}' + '{_wpnSwordHitboxName}'");
            Log($"  → Select  ItemsPivotPoint/SwordVisual/Model  and use Move/Rotate/Scale to fit the hand.");
        }
        else if (setupType == 1) // Slingshot
        {
            var firePoint = EnsureWeaponChild(slotGO.transform, "FirePoint");
            var sb   = slotGO.AddComponent<SlingshotBehaviour>();
            var sbSO = new SerializedObject(sb);
            sbSO.FindProperty("firePoint").objectReferenceValue = firePoint;
            if (cam != null) sbSO.FindProperty("gameplayCamera").objectReferenceValue = cam;
            sbSO.ApplyModifiedPropertiesWithoutUndo();
            Log($"✓ SlingshotVisual — WeaponVisual + SlingshotBehaviour + FirePoint");
            Log($"  → Select  ItemsPivotPoint/SlingshotVisual/Model  and use Move/Rotate/Scale to fit the hand.");
            Log($"  → Move FirePoint to the tip of the slingshot (projectile spawn origin).");
            if (cam == null) Log("  ⚠ No camera found — assign gameplayCamera on SlingshotBehaviour manually.");
        }
        else if (setupType == 2) // Grapple
        {
            var handPoint = EnsureWeaponChild(slotGO.transform, "HandPoint");
            var gb   = slotGO.AddComponent<GrappleHookBehaviour>();
            var gbSO = new SerializedObject(gb);
            gbSO.FindProperty("handPoint").objectReferenceValue = handPoint;
            if (cam != null) gbSO.FindProperty("gameplayCamera").objectReferenceValue = cam;
            gbSO.ApplyModifiedPropertiesWithoutUndo();
            Log($"✓ GrappleVisual — WeaponVisual + GrappleHookBehaviour + HandPoint");
            Log($"  → Select  ItemsPivotPoint/GrappleVisual/Model  and use Move/Rotate/Scale to fit the hand.");
            Log($"  → Move HandPoint to the wrist/tip (rope origin).");
            if (cam == null) Log("  ⚠ No camera found — assign gameplayCamera on GrappleHookBehaviour manually.");
        }
    }

    static Transform EnsureWeaponChild(Transform parent, string childName)
    {
        var existing = parent.Find(childName);
        if (existing != null) return existing;
        var go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, $"Create {childName}");
        return go.transform;
    }

    static void WeaponSubHeader(string text)
    {
        var s = new GUIStyle(EditorStyles.boldLabel);
        EditorGUILayout.LabelField(text, s);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. UI THEME EDITOR  (custom images for every UIFactory-built screen)
    // ─────────────────────────────────────────────────────────────────────

    void DrawThemeEditor()
    {
        Section("4.  UI THEME  (custom images)");

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _themeTarget = (SOUITheme)EditorGUILayout.ObjectField("Theme Asset", _themeTarget, typeof(SOUITheme), false);
        if (EditorGUI.EndChangeCheck() && _themeEditor != null) { DestroyImmediate(_themeEditor); _themeEditor = null; }
        if (GUILayout.Button("New", GUILayout.Width(60))) CreateNewTheme();
        EditorGUILayout.EndHorizontal();

        if (_themeTarget != null)
        {
            if (_themeEditor == null) _themeEditor = Editor.CreateEditor(_themeTarget);
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _themeEditor.OnInspectorGUI();
            EditorGUILayout.EndVertical();

            EditorGUILayout.HelpBox("Assign sprites to reskin every UIFactory-built screen (panels, buttons, " +
                "boss bar). A null sprite keeps the current flat look. Functional colors (e.g. the death-screen " +
                "fade) are preserved.", MessageType.None);

            ColorBtn("✔  Set Active  (scene provider + Resources)", ApplyTheme, new Color(0.55f, 1f, 0.7f));
        }
        else
        {
            EditorGUILayout.HelpBox("Create or assign an SOUITheme to customize UI images from here.", MessageType.Info);
        }
        EditorGUILayout.Space(8);
    }

    void CreateNewTheme()
    {
        const string folder = "Assets/Resources";
        EnsureFolder(folder);

        var theme = ScriptableObject.CreateInstance<SOUITheme>();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/UITheme.asset");
        AssetDatabase.CreateAsset(theme, path);
        AssetDatabase.SaveAssets();

        _themeTarget = theme;
        if (_themeEditor != null) { DestroyImmediate(_themeEditor); _themeEditor = null; }
        Selection.activeObject = theme;
        EditorGUIUtility.PingObject(theme);
    }

    void ApplyTheme()
    {
        _log.Clear();
        if (_themeTarget == null) { Log("❌  No theme assigned."); return; }

        // Reliable path: an in-scene UIThemeProvider that assigns UITheme.Current early.
        var provider = FindFirstObjectByType<UIThemeProvider>();
        if (provider == null)
            provider = FindOrCreate("UIThemeProvider").EnsureComp<UIThemeProvider>();
        WriteObj(provider, "theme", _themeTarget);

        Log("✓ UIThemeProvider set in scene → theme active at runtime.");

        string path = AssetDatabase.GetAssetPath(_themeTarget).Replace('\\', '/');
        if (!path.Contains("/Resources/"))
            Log("⚠ Theme is outside a Resources/ folder; the in-scene provider applies it. " +
                "Put it at Assets/Resources/UITheme.asset to auto-load without a provider.");

        Log("\n✅  Enter Play to see the reskinned UI.");
    }

    private void OnEnable()  => LoadPrefs();

    // Editor-owned drafts/editors must be released when the window closes.
    private void OnDisable()
    {
        if (_itemDraftEditor != null) { DestroyImmediate(_itemDraftEditor); _itemDraftEditor = null; }
        if (_itemDraft != null)       { DestroyImmediate(_itemDraft);       _itemDraft = null; }
        if (_themeEditor != null)     { DestroyImmediate(_themeEditor);     _themeEditor = null; }
        SavePrefs();
    }

    // ─────────────────────────────────────────────────────────────────────
    // PREFS  (persist window state across open/close)
    // ─────────────────────────────────────────────────────────────────────

    const string KP = "RWiz_";

    void SavePrefs()
    {
        EditorPrefs.SetString(KP+"GameScene",      _gameplaySceneName);
        SaveObjPref(KP+"MainPool",                 _mainPool);

        EditorPrefs.SetString(KP+"NpcName",        _npcName);
        EditorPrefs.SetString(KP+"NpcId",          _npcId);
        EditorPrefs.SetString(KP+"NpcDialogue",    _npcDialogue);
        EditorPrefs.SetBool  (KP+"NpcIsShop",      _npcIsShop);
        EditorPrefs.SetInt   (KP+"NpcPersonality", (int)_npcPersonality);
        SaveObjPref(KP+"NpcShopPool",              _npcShopPool);
        EditorPrefs.SetInt   (KP+"NpcShopSize",    _npcShopSize);

        SaveObjPref(KP+"ChestModel",               _chestModelPrefab);
        EditorPrefs.SetString(KP+"ChestLid",       _chestLidName);
        EditorPrefs.SetFloat (KP+"ChestEulerX",    _chestOpenEuler.x);
        EditorPrefs.SetFloat (KP+"ChestEulerY",    _chestOpenEuler.y);
        EditorPrefs.SetFloat (KP+"ChestEulerZ",    _chestOpenEuler.z);
        EditorPrefs.SetFloat (KP+"ChestDur",       _chestOpenDuration);
        EditorPrefs.SetFloat (KP+"ChestColSzX",    _chestColliderSize.x);
        EditorPrefs.SetFloat (KP+"ChestColSzY",    _chestColliderSize.y);
        EditorPrefs.SetFloat (KP+"ChestColSzZ",    _chestColliderSize.z);
        EditorPrefs.SetFloat (KP+"ChestColCX",     _chestColliderCenter.x);
        EditorPrefs.SetFloat (KP+"ChestColCY",     _chestColliderCenter.y);
        EditorPrefs.SetFloat (KP+"ChestColCZ",     _chestColliderCenter.z);

        EditorPrefs.SetInt   (KP+"ItemTypeIdx",    _itemTypeIndex);
        EditorPrefs.SetString(KP+"ItemFolder",     _itemSaveFolder);

        SaveObjPref(KP+"Theme",                    _themeTarget);
        SaveObjPref(KP+"BGMTrack",                 _bgmDefaultTrack);
        SaveObjPref(KP+"BGMProfile",               _bgmProfile);

        SaveObjPref(KP+"VfxProfile",               _vfxProfile);
        EditorPrefs.SetFloat(KP+"VfxStepMin",      _vfxStepMinSpeed);
        EditorPrefs.SetFloat(KP+"VfxStepWalk",     _vfxStepWalkInterval);
        EditorPrefs.SetFloat(KP+"VfxStepRun",      _vfxStepRunInterval);
        EditorPrefs.SetFloat(KP+"VfxRunThr",       _vfxRunThreshold);
        if (_bindSfx != null)
        {
            string[] acts = SOVFXProfile.AllActionNames;
            for (int i = 0; i < acts.Length && i < _bindSfx.Length; i++)
            {
                SaveObjPref(KP+"Vfx_"+acts[i]+"_Sfx", _bindSfx[i]);
                SaveObjPref(KP+"Vfx_"+acts[i]+"_Pfb", _bindPrefab[i]);
                EditorPrefs.SetFloat(KP+"Vfx_"+acts[i]+"_Vol", _bindVolume[i]);
                EditorPrefs.SetFloat(KP+"Vfx_"+acts[i]+"_Dur", _bindDuration[i]);
            }
        }

        EditorPrefs.SetBool  (KP+"WpnFoldout",     _weaponFoldout);
        SaveObjPref(KP+"WpnSwordAsset",             _wpnSwordAsset);
        SaveObjPref(KP+"WpnSwordPrefab",            _wpnSwordPrefab);
        EditorPrefs.SetString(KP+"WpnSwordGrip",    _wpnSwordGripName);
        EditorPrefs.SetString(KP+"WpnSwordHitbox",  _wpnSwordHitboxName);
        SaveObjPref(KP+"WpnSlingAsset",             _wpnSlingAsset);
        SaveObjPref(KP+"WpnSlingPrefab",            _wpnSlingPrefab);
        SaveObjPref(KP+"WpnGrappleAsset",           _wpnGrappleAsset);
        SaveObjPref(KP+"WpnGrapplePrefab",          _wpnGrapplePrefab);
    }

    void LoadPrefs()
    {
        _gameplaySceneName = EditorPrefs.GetString(KP+"GameScene",      _gameplaySceneName);
        _mainPool          = LoadObjPref<SOItemPool>(KP+"MainPool");

        _npcName        = EditorPrefs.GetString(KP+"NpcName",        _npcName);
        _npcId          = EditorPrefs.GetString(KP+"NpcId",          _npcId);
        _npcDialogue    = EditorPrefs.GetString(KP+"NpcDialogue",    _npcDialogue);
        _npcIsShop      = EditorPrefs.GetBool  (KP+"NpcIsShop",      _npcIsShop);
        _npcPersonality = (NPCPersonality)EditorPrefs.GetInt(KP+"NpcPersonality", (int)_npcPersonality);
        _npcShopPool    = LoadObjPref<SOItemPool>(KP+"NpcShopPool");
        _npcShopSize    = EditorPrefs.GetInt   (KP+"NpcShopSize",    _npcShopSize);

        _chestModelPrefab  = LoadObjPref<GameObject>(KP+"ChestModel");
        _chestLidName      = EditorPrefs.GetString(KP+"ChestLid",    _chestLidName);
        _chestOpenEuler    = new Vector3(
            EditorPrefs.GetFloat(KP+"ChestEulerX", _chestOpenEuler.x),
            EditorPrefs.GetFloat(KP+"ChestEulerY", _chestOpenEuler.y),
            EditorPrefs.GetFloat(KP+"ChestEulerZ", _chestOpenEuler.z));
        _chestOpenDuration  = EditorPrefs.GetFloat(KP+"ChestDur",    _chestOpenDuration);
        _chestColliderSize  = new Vector3(
            EditorPrefs.GetFloat(KP+"ChestColSzX", _chestColliderSize.x),
            EditorPrefs.GetFloat(KP+"ChestColSzY", _chestColliderSize.y),
            EditorPrefs.GetFloat(KP+"ChestColSzZ", _chestColliderSize.z));
        _chestColliderCenter = new Vector3(
            EditorPrefs.GetFloat(KP+"ChestColCX",  _chestColliderCenter.x),
            EditorPrefs.GetFloat(KP+"ChestColCY",  _chestColliderCenter.y),
            EditorPrefs.GetFloat(KP+"ChestColCZ",  _chestColliderCenter.z));

        _itemTypeIndex  = EditorPrefs.GetInt   (KP+"ItemTypeIdx", _itemTypeIndex);
        _itemSaveFolder = EditorPrefs.GetString(KP+"ItemFolder",  _itemSaveFolder);

        _themeTarget     = LoadObjPref<SOUITheme>  (KP+"Theme");
        _bgmDefaultTrack = LoadObjPref<AudioClip>  (KP+"BGMTrack");
        _bgmProfile      = LoadObjPref<SOBGMProfile>(KP+"BGMProfile");

        _vfxProfile          = LoadObjPref<SOVFXProfile>(KP+"VfxProfile");
        _vfxStepMinSpeed     = EditorPrefs.GetFloat(KP+"VfxStepMin",  _vfxStepMinSpeed);
        _vfxStepWalkInterval = EditorPrefs.GetFloat(KP+"VfxStepWalk", _vfxStepWalkInterval);
        _vfxStepRunInterval  = EditorPrefs.GetFloat(KP+"VfxStepRun",  _vfxStepRunInterval);
        _vfxRunThreshold     = EditorPrefs.GetFloat(KP+"VfxRunThr",   _vfxRunThreshold);
        EnsureBindArrays();
        string[] vfxActs = SOVFXProfile.AllActionNames;
        for (int i = 0; i < vfxActs.Length; i++)
        {
            _bindSfx[i]      = LoadObjPref<AudioClip>  (KP+"Vfx_"+vfxActs[i]+"_Sfx");
            _bindPrefab[i]   = LoadObjPref<GameObject>  (KP+"Vfx_"+vfxActs[i]+"_Pfb");
            _bindVolume[i]   = EditorPrefs.GetFloat(KP+"Vfx_"+vfxActs[i]+"_Vol", _bindVolume[i]);
            _bindDuration[i] = EditorPrefs.GetFloat(KP+"Vfx_"+vfxActs[i]+"_Dur", _bindDuration[i]);
        }

        _weaponFoldout      = EditorPrefs.GetBool  (KP+"WpnFoldout",    _weaponFoldout);
        _wpnSwordAsset      = LoadObjPref<SOItem>   (KP+"WpnSwordAsset");
        _wpnSwordPrefab     = LoadObjPref<GameObject>(KP+"WpnSwordPrefab");
        _wpnSwordGripName   = EditorPrefs.GetString(KP+"WpnSwordGrip",   _wpnSwordGripName);
        _wpnSwordHitboxName = EditorPrefs.GetString(KP+"WpnSwordHitbox", _wpnSwordHitboxName);
        _wpnSlingAsset      = LoadObjPref<SOItem>   (KP+"WpnSlingAsset");
        _wpnSlingPrefab     = LoadObjPref<GameObject>(KP+"WpnSlingPrefab");
        _wpnGrappleAsset    = LoadObjPref<SOItem>   (KP+"WpnGrappleAsset");
        _wpnGrapplePrefab   = LoadObjPref<GameObject>(KP+"WpnGrapplePrefab");
    }

    static void SaveObjPref(string key, Object obj) =>
        EditorPrefs.SetString(key, obj != null ? AssetDatabase.GetAssetPath(obj) : "");

    static T LoadObjPref<T>(string key) where T : Object
    {
        string path = EditorPrefs.GetString(key, "");
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
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
        CheckType<VictoryScreenUI>("VictoryScreen");
        CheckType<RunTracker>("RunTracker");
        CheckType<MinimapManager>("MinimapManager");
        CheckType<VFXPlayer>("VFXPlayer");
        var scanVfx = FindFirstObjectByType<VFXPlayer>();
        if (scanVfx != null)
        {
            var vfxSO = new SerializedObject(scanVfx);
            var profProp = vfxSO.FindProperty("profile");
            if (profProp.objectReferenceValue == null)
                Log("  ⚠ VFXPlayer.profile not assigned — SFX/VFX won't play (use Character SFX/VFX foldout)");
            else
                Log($"  ✓ VFXPlayer.profile → {profProp.objectReferenceValue.name}");
        }
        CheckType<MusicManager>("MusicManager");
        var scanMM = FindFirstObjectByType<MusicManager>();
        if (scanMM != null)
        {
            var mmSO = new SerializedObject(scanMM);
            if (mmSO.FindProperty("defaultTrack").objectReferenceValue == null)
                Log("  ⚠ MusicManager.defaultTrack not assigned — silence outside music zones");
            if (mmSO.FindProperty("bossProfile").objectReferenceValue == null)
                Log("  ⚠ MusicManager.bossProfile not assigned — boss encounters won't auto-switch music");
        }

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

        Log("\n── Weapon visuals ──");
        var scanEq = FindFirstObjectByType<EquipHandler>();
        if (scanEq == null)
            Log("  ❌ EquipHandler not found");
        else if (scanEq.ItemsPivotPoint == null)
            Log("  ❌ EquipHandler.ItemsPivotPoint not assigned");
        else
        {
            Log($"  ✓ ItemsPivotPoint: {scanEq.ItemsPivotPoint.name}");
            var wvs = scanEq.ItemsPivotPoint.GetComponentsInChildren<WeaponVisual>(true);
            if (wvs.Length == 0)
                Log("  ⚠ No WeaponVisual found under ItemsPivotPoint — run the Weapon Visual Builder");
            else
            {
                foreach (var wv in wvs)
                {
                    var wvSO  = new SerializedObject(wv);
                    var wvProp = wvSO.FindProperty("itemAsset");
                    bool hasAsset = wvProp != null && wvProp.objectReferenceValue != null;
                    Log(hasAsset
                        ? $"  ✓ {wv.gameObject.name}  →  {wvProp.objectReferenceValue.name}"
                        : $"  ❌ {wv.gameObject.name}: itemAsset not assigned");
                }
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

    // Adds the component only if it's missing from the WHOLE hierarchy (this object
    // and its children). Checking children matters because singleton managers may live
    // on a dedicated child object (e.g. a "[QuickslotManager]" child): a root-only
    // GetComponent check would add a duplicate, and a duplicate singleton destroys one
    // of the two at runtime — which can delete the whole player.
    void AutoAdd<T>(GameObject go) where T : Component
    {
        if (go.GetComponentInChildren<T>(true) == null)
        {
            Undo.AddComponent<T>(go);
            Log($"  + Added {typeof(T).Name}");
        }
        else Log($"  ✓ {typeof(T).Name}");
    }

    static string UniqueChestId() =>
        "chest_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

    // Depth-first search for a child Transform by name.
    static Transform FindInHierarchy(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindInHierarchy(child, name);
            if (found != null) return found;
        }
        return null;
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
