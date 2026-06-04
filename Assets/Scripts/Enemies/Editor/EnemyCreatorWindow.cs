using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-stop authoring window for the modular enemy/boss system.
/// (Tools → Enemy Creator). Configure perception, parts, phases and weighted
/// decision states, then press Create to generate:
///   • a single SOEnemy asset with every pattern/condition/modifier embedded as
///     a sub-asset, and
///   • a ready-to-drop prefab with all components wired (multi-part bosses get a
///     BossGroup root with one child per part).
/// </summary>
public class EnemyCreatorWindow : EditorWindow
{
    private SOEnemy _enemy;
    private string  _saveFolder = "Assets/Enemies";
    private Vector2 _scroll;

    // When true, _enemy is an existing on-disk asset being edited in place.
    private bool    _editingExisting;
    private SOEnemy _loadTarget;

    private readonly Dictionary<ScriptableObject, Editor> _editorCache = new();

    [MenuItem("Tools/Enemy Creator")]
    public static void Open()
    {
        var w = GetWindow<EnemyCreatorWindow>("Enemy Creator");
        w.minSize = new Vector2(420, 500);
        w.EnsureEnemy();
    }

    /// <summary>Opens the window already editing the given enemy asset.</summary>
    public static void OpenFor(SOEnemy enemy)
    {
        var w = GetWindow<EnemyCreatorWindow>("Enemy Creator");
        w.minSize = new Vector2(420, 500);
        w.LoadExisting(enemy);
    }

    private void OnEnable() => EnsureEnemy();

    private void EnsureEnemy()
    {
        if (_enemy != null) return;
        NewEnemy();
    }

    private void NewEnemy()
    {
        _editorCache.Clear();
        _editingExisting = false;
        _loadTarget = null;
        _enemy = CreateInstance<SOEnemy>();
        _enemy.hideFlags = HideFlags.DontSave;
        if (_enemy.parts.Count == 0) _enemy.parts.Add(new EnemyPartData());
    }

    private void LoadExisting(SOEnemy enemy)
    {
        if (enemy == null) return;
        _editorCache.Clear();
        _enemy = enemy;                 // edit the real asset + its real sub-assets
        _editingExisting = true;
        _loadTarget = enemy;
        _saveFolder = System.IO.Path.GetDirectoryName(
            System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(enemy)))?.Replace('\\', '/') ?? _saveFolder;
        if (_enemy.parts.Count == 0) _enemy.parts.Add(new EnemyPartData());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        EnsureEnemy();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(_editingExisting ? "ENEMY EDITOR" : "ENEMY CREATOR", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        // ── Load / New bar ──────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _loadTarget = (SOEnemy)EditorGUILayout.ObjectField("Edit Existing", _loadTarget, typeof(SOEnemy), false);
        if (EditorGUI.EndChangeCheck() && _loadTarget != null && _loadTarget != _enemy)
            LoadExisting(_loadTarget);
        if (GUILayout.Button("New", GUILayout.Width(60)))
            NewEnemy();
        EditorGUILayout.EndHorizontal();

        if (_editingExisting)
            EditorGUILayout.HelpBox("Editing the existing asset in place. Adding/removing modules updates its " +
                                    "sub-assets on Save; the prefab keeps working (it references this asset).",
                                    MessageType.None);
        EditorGUILayout.Space(4);

        _enemy.enemyName = EditorGUILayout.TextField("Name", _enemy.enemyName);
        EditorGUILayout.BeginHorizontal();
        _saveFolder = EditorGUILayout.TextField("Save Folder", _saveFolder);
        if (GUILayout.Button("…", GUILayout.Width(28)))
        {
            string picked = EditorUtility.OpenFolderPanel("Save Folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(picked) && picked.StartsWith(Application.dataPath))
                _saveFolder = "Assets" + picked.Substring(Application.dataPath.Length);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        for (int i = 0; i < _enemy.parts.Count; i++)
        {
            DrawPart(_enemy.parts[i], i);
            EditorGUILayout.Space(6);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Part"))
            _enemy.parts.Add(new EnemyPartData { partName = "Part " + _enemy.parts.Count });
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(_enemy.parts.Count == 0 || string.IsNullOrWhiteSpace(_enemy.enemyName)))
        {
            if (_editingExisting)
            {
                if (GUILayout.Button("SAVE CHANGES", GUILayout.Height(34)))
                    SaveExisting();
            }
            else
            {
                if (GUILayout.Button("CREATE ENEMY PREFAB", GUILayout.Height(34)))
                    CreateEnemy();
            }
        }
    }

    // ─── Part ────────────────────────────────────────────────────────────────

    private void DrawPart(EnemyPartData part, int index)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        part.partName = EditorGUILayout.TextField("Part", part.partName);
        if (_enemy.parts.Count > 1 && GUILayout.Button("✕", GUILayout.Width(24)))
        {
            _enemy.parts.RemoveAt(index);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        part.maxHearts = EditorGUILayout.IntField("Max Hearts", Mathf.Max(1, part.maxHearts));
        part.moveSpeed = EditorGUILayout.FloatField("Move Speed", part.moveSpeed);
        part.turnSpeed = EditorGUILayout.FloatField("Turn Speed", part.turnSpeed);

        EditorGUILayout.BeginHorizontal();
        part.canFly    = EditorGUILayout.ToggleLeft("Can Fly", part.canFly, GUILayout.Width(110));
        part.canClimb  = EditorGUILayout.ToggleLeft("Can Climb", part.canClimb, GUILayout.Width(110));
        part.wallSteer = EditorGUILayout.ToggleLeft("Wall Steer", part.wallSteer, GUILayout.Width(110));
        EditorGUILayout.EndHorizontal();

        part.weapon = (SOItem)EditorGUILayout.ObjectField("Weapon", part.weapon, typeof(SOItem), false);

        // Vision
        EditorGUILayout.LabelField("Vision", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        part.vision.range          = EditorGUILayout.FloatField("Range", part.vision.range);
        part.vision.angle          = EditorGUILayout.Slider("Cone Angle", part.vision.angle, 0f, 360f);
        part.vision.alertRadius    = EditorGUILayout.FloatField("Alert Radius", part.vision.alertRadius);
        part.vision.loseSightDelay = EditorGUILayout.FloatField("Lose Sight Delay", part.vision.loseSightDelay);
        part.vision.blockMask      = LayerMaskField("Block Mask", part.vision.blockMask);
        EditorGUI.indentLevel--;

        // Idle movement
        EditorGUILayout.Space(2);
        part.idleMovement = (SOMovementPattern)DrawPatternSlot(
            "Idle Movement", part.idleMovement, typeof(SOMovementPattern));

        // Dodge reaction (reflex)
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Dodge Reaction", EditorStyles.boldLabel);
        part.dodgeReaction = (SOMovementPattern)DrawPatternSlot(
            "Reflex Move", part.dodgeReaction, typeof(SOMovementPattern));
        if (part.dodgeReaction != null)
        {
            part.dodgeChance       = EditorGUILayout.Slider("Dodge Chance", part.dodgeChance, 0f, 1f);
            part.dodgeReactionTime = EditorGUILayout.FloatField("Reaction Time", part.dodgeReactionTime);
        }

        // Phases
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Phases", EditorStyles.boldLabel);
        for (int p = 0; p < part.phases.Count; p++)
        {
            if (part.phases[p] == null)
            {
                part.phases[p] = NewSO<SOEnemyPhase>("Phase " + p);
            }
            DrawPhase(part, p);
        }
        if (GUILayout.Button("+ Add Phase"))
            part.phases.Add(NewSO<SOEnemyPhase>("Phase " + part.phases.Count));

        EditorGUILayout.EndVertical();
    }

    // ─── Phase ───────────────────────────────────────────────────────────────

    private void DrawPhase(EnemyPartData part, int index)
    {
        SOEnemyPhase phase = part.phases[index];
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        phase.label = EditorGUILayout.TextField("Phase", phase.label);
        if (GUILayout.Button("✕", GUILayout.Width(24)))
        {
            part.phases.RemoveAt(index);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        phase.decisionInterval = EditorGUILayout.Vector2Field("Decision Interval (min,max)", phase.decisionInterval);
        phase.noise            = EditorGUILayout.FloatField("Noise", phase.noise);

        EditorGUILayout.LabelField("States", EditorStyles.miniBoldLabel);
        for (int st = 0; st < phase.states.Count; st++)
            DrawState(phase, st);

        if (GUILayout.Button("+ Add State"))
            phase.states.Add(new EnemyStateEntry { label = "State " + phase.states.Count });

        EditorGUILayout.Space(2);
        phase.exitCondition = (SOPhaseCondition)DrawPatternSlot(
            "Exit Condition", phase.exitCondition, typeof(SOPhaseCondition));

        EditorGUILayout.EndVertical();
    }

    // ─── State ───────────────────────────────────────────────────────────────

    private void DrawState(SOEnemyPhase phase, int index)
    {
        EnemyStateEntry s = phase.states[index];
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        s.label = EditorGUILayout.TextField("State", s.label);
        if (GUILayout.Button("✕", GUILayout.Width(24)))
        {
            phase.states.RemoveAt(index);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        s.baseWeight = EditorGUILayout.FloatField("Base Weight", s.baseWeight);
        s.duration   = EditorGUILayout.Vector2Field("Duration (min,max)", s.duration);

        s.movement = (SOMovementPattern)DrawPatternSlot("Movement", s.movement, typeof(SOMovementPattern));
        s.attack   = (SOAttackPattern)  DrawPatternSlot("Attack",   s.attack,   typeof(SOAttackPattern));

        // Modifiers
        EditorGUILayout.LabelField("Weight Modifiers", EditorStyles.miniLabel);
        for (int m = 0; m < s.modifiers.Count; m++)
        {
            EditorGUILayout.BeginHorizontal();
            s.modifiers[m] = (SOWeightModifier)DrawPatternSlot("", s.modifiers[m], typeof(SOWeightModifier));
            if (GUILayout.Button("✕", GUILayout.Width(24))) { s.modifiers.RemoveAt(m); EditorGUILayout.EndHorizontal(); break; }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("+ Add Modifier", GUILayout.Width(140)))
            s.modifiers.Add(null);

        EditorGUILayout.EndVertical();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reusable SO-slot drawer: type popup + inline field editor
    // ─────────────────────────────────────────────────────────────────────────

    private ScriptableObject DrawPatternSlot(string label, ScriptableObject current, Type baseType)
    {
        List<Type> types = TypeCache.GetTypesDerivedFrom(baseType)
                                    .Where(t => !t.IsAbstract)
                                    .OrderBy(t => t.Name)
                                    .ToList();

        string[] options = new string[types.Count + 1];
        options[0] = "None";
        for (int i = 0; i < types.Count; i++) options[i + 1] = Nicify(types[i].Name);

        int currentIdx = current == null ? 0 : types.IndexOf(current.GetType()) + 1;

        EditorGUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(label)) EditorGUILayout.LabelField(label, GUILayout.Width(110));
        int newIdx = EditorGUILayout.Popup(currentIdx, options);
        EditorGUILayout.EndHorizontal();

        if (newIdx != currentIdx)
        {
            if (current != null) _editorCache.Remove(current);
            current = newIdx == 0 ? null : NewSO(types[newIdx - 1]);
        }

        if (current != null)
        {
            EditorGUI.indentLevel++;
            Editor ed = GetCachedEditor(current);
            ed.OnInspectorGUI();
            EditorGUI.indentLevel--;
        }

        return current;
    }

    private Editor GetCachedEditor(ScriptableObject so)
    {
        if (!_editorCache.TryGetValue(so, out Editor ed) || ed == null)
        {
            ed = Editor.CreateEditor(so);
            _editorCache[so] = ed;
        }
        return ed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Creation / persistence
    // ─────────────────────────────────────────────────────────────────────────

    private void CreateEnemy()
    {
        var result = EnemyAssetBuilder.Build(_enemy, _saveFolder);

        EditorUtility.DisplayDialog("Enemy Creator",
            $"Created:\n• {result.assetPath}\n• {result.prefabPath}", "Nice");

        Selection.activeObject = result.prefab;
        EditorGUIUtility.PingObject(result.prefab);

        // Reset window for the next enemy (the old _enemy now lives on disk).
        _editorCache.Clear();
        _enemy = null;
        EnsureEnemy();
    }

    private void SaveExisting()
    {
        ReconcileSubAssets(_enemy);
        EditorUtility.SetDirty(_enemy);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(_enemy));
        EditorUtility.DisplayDialog("Enemy Editor", $"Saved changes to {_enemy.enemyName}.", "OK");
    }

    /// <summary>
    /// Brings the asset's embedded sub-assets in sync with what the in-memory graph
    /// now references: newly created modules get added, orphaned ones get deleted.
    /// External shared assets (weapons) are never touched.
    /// </summary>
    private void ReconcileSubAssets(SOEnemy enemy)
    {
        string path = AssetDatabase.GetAssetPath(enemy);
        if (string.IsNullOrEmpty(path)) return;

        // 1) Everything the graph currently references (recursively).
        var wanted = new HashSet<ScriptableObject>();
        CollectReferenced(enemy, wanted, path);

        // 2) Add in-memory modules that aren't sub-assets yet.
        foreach (var so in wanted)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(so)))
            {
                so.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(so, enemy);
            }
        }

        // 3) Delete sub-assets that are no longer referenced.
        foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
        {
            if (rep is ScriptableObject sub && sub != enemy && !wanted.Contains(sub))
            {
                AssetDatabase.RemoveObjectFromAsset(sub);
                DestroyImmediate(sub, true);
            }
        }
    }

    private void CollectReferenced(ScriptableObject so, HashSet<ScriptableObject> wanted, string enemyPath)
    {
        if (so == null) return;

        var sob = new SerializedObject(so);
        var it  = sob.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
            if (it.objectReferenceValue is ScriptableObject child)
            {
                string childPath = AssetDatabase.GetAssetPath(child);
                bool external = !string.IsNullOrEmpty(childPath) && childPath != enemyPath;
                if (external) continue;            // shared asset (weapon, reused module)
                if (!wanted.Add(child)) continue;  // already collected
                CollectReferenced(child, wanted, enemyPath);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static T NewSO<T>(string name) where T : ScriptableObject
    {
        var so = CreateInstance<T>();
        so.name = name;
        so.hideFlags = HideFlags.DontSave;
        return so;
    }

    private static ScriptableObject NewSO(Type t)
    {
        var so = CreateInstance(t);
        so.name = Nicify(t.Name);
        so.hideFlags = HideFlags.DontSave;
        return so;
    }

    private static string Nicify(string typeName)
    {
        // SOMove_Chase → "Chase", SOAttack_Melee → "Melee", SOCondition_Never → "Never"
        int us = typeName.IndexOf('_');
        string s = us >= 0 ? typeName.Substring(us + 1) : typeName;
        return ObjectNames.NicifyVariableName(s);
    }

    private static LayerMask LayerMaskField(string label, LayerMask layerMask)
    {
        string[] layers = UnityEditorInternal.InternalEditorUtility.layers;

        // Real bitmask → concatenated index (MaskField uses positions in 'layers').
        int concat = 0;
        for (int i = 0; i < layers.Length; i++)
            if ((layerMask.value & (1 << LayerMask.NameToLayer(layers[i]))) != 0)
                concat |= 1 << i;

        concat = EditorGUILayout.MaskField(label, concat, layers);

        // Concatenated index → real bitmask.
        int result = 0;
        for (int i = 0; i < layers.Length; i++)
            if ((concat & (1 << i)) != 0)
                result |= 1 << LayerMask.NameToLayer(layers[i]);

        return result;
    }
}
