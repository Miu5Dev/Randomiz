#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor window for authoring <see cref="SOVFXProfile"/> assets.
/// Open via Randomiz > VFX &amp; SFX Binder.
///
/// Features:
///   - Asset selector at the top.
///   - Table showing every binding: action name | VFX prefab | SFX clip | volume | preview.
///   - Per-row Remove button.
///   - Add Binding button with a dropdown populated from the predefined constants.
///   - Full undo/redo support via SerializedObject.
/// </summary>
public class VFXProfileEditor : EditorWindow
{
    // ── Window state ──────────────────────────────────────────────────────────
    private SOVFXProfile    _profile;
    private SerializedObject _so;
    private SerializedProperty _bindingsProp;

    // Action name dropdown state (per-row).
    private int[] _actionNameIndices; // cached dropdown selection per binding row

    // Scroll position for the binding table.
    private Vector2 _scrollPos;

    // ── Column widths ─────────────────────────────────────────────────────────
    private const float ColAction  = 160f;
    private const float ColVFX     = 180f;
    private const float ColSFX     = 180f;
    private const float ColVolume  = 80f;
    private const float ColPreview = 80f;
    private const float ColRemove  = 60f;

    // ── Menu item ─────────────────────────────────────────────────────────────
    [MenuItem("Randomiz/VFX & SFX Binder")]
    public static void Open()
    {
        var window = GetWindow<VFXProfileEditor>(false, "VFX & SFX Binder");
        window.minSize = new Vector2(820f, 400f);
        window.Show();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void OnEnable()
    {
        // If a SOVFXProfile is currently selected in the Project window, open it.
        if (Selection.activeObject is SOVFXProfile p)
            SelectProfile(p);
    }

    private void OnSelectionChange()
    {
        if (Selection.activeObject is SOVFXProfile p)
        {
            SelectProfile(p);
            Repaint();
        }
    }

    private void SelectProfile(SOVFXProfile profile)
    {
        _profile = profile;
        _so      = profile != null ? new SerializedObject(profile) : null;
        _bindingsProp = _so?.FindProperty("bindings");
        RebuildActionIndices();
    }

    // ── GUI ───────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        DrawHeader();

        if (_profile == null || _so == null)
        {
            EditorGUILayout.HelpBox(
                "Select or create a VFX Profile asset to begin editing.",
                MessageType.Info);
            return;
        }

        _so.Update();

        DrawTable();
        GUILayout.Space(6f);
        DrawAddButton();

        _so.ApplyModifiedProperties();
    }

    // ── Header ────────────────────────────────────────────────────────────────
    private void DrawHeader()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("VFX & SFX Binder", EditorStyles.boldLabel);
        EditorGUILayout.Space(2f);

        EditorGUI.BeginChangeCheck();
        var selected = (SOVFXProfile)EditorGUILayout.ObjectField(
            "Profile Asset", _profile, typeof(SOVFXProfile), false);
        if (EditorGUI.EndChangeCheck())
            SelectProfile(selected);

        EditorGUILayout.Space(8f);
    }

    // ── Table ─────────────────────────────────────────────────────────────────
    private void DrawTable()
    {
        // Column header row.
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Action Name",  GUILayout.Width(ColAction));
        GUILayout.Label("VFX Prefab",   GUILayout.Width(ColVFX));
        GUILayout.Label("SFX Clip",     GUILayout.Width(ColSFX));
        GUILayout.Label("Volume",       GUILayout.Width(ColVolume));
        GUILayout.Label("Preview",      GUILayout.Width(ColPreview));
        GUILayout.Label("",             GUILayout.Width(ColRemove));
        EditorGUILayout.EndHorizontal();

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        int removeIndex = -1; // Deferred removal to avoid modifying during iteration.

        for (int i = 0; i < _bindingsProp.arraySize; i++)
        {
            SerializedProperty binding    = _bindingsProp.GetArrayElementAtIndex(i);
            SerializedProperty actionProp = binding.FindPropertyRelative("actionName");
            SerializedProperty vfxProp    = binding.FindPropertyRelative("vfxPrefab");
            SerializedProperty sfxProp    = binding.FindPropertyRelative("sfxClip");
            SerializedProperty volProp    = binding.FindPropertyRelative("sfxVolume");
            SerializedProperty durProp    = binding.FindPropertyRelative("duration");
            SerializedProperty attachProp = binding.FindPropertyRelative("attachToSource");

            EditorGUILayout.BeginVertical("box");

            // ── Row 1: main fields ────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            // Action name dropdown + freeform fallback text field.
            DrawActionNameField(actionProp, i);

            // VFX prefab.
            vfxProp.objectReferenceValue = EditorGUILayout.ObjectField(
                vfxProp.objectReferenceValue,
                typeof(GameObject), false,
                GUILayout.Width(ColVFX));

            // SFX clip.
            sfxProp.objectReferenceValue = EditorGUILayout.ObjectField(
                sfxProp.objectReferenceValue,
                typeof(AudioClip), false,
                GUILayout.Width(ColSFX));

            // Volume slider.
            volProp.floatValue = EditorGUILayout.Slider(
                volProp.floatValue, 0f, 1f,
                GUILayout.Width(ColVolume));

            // Preview SFX button.
            GUI.enabled = sfxProp.objectReferenceValue != null;
            if (GUILayout.Button("Play", GUILayout.Width(ColPreview)))
                PreviewClip((AudioClip)sfxProp.objectReferenceValue, volProp.floatValue);
            GUI.enabled = true;

            // Remove row button.
            if (GUILayout.Button("Remove", GUILayout.Width(ColRemove)))
                removeIndex = i;

            EditorGUILayout.EndHorizontal();

            // ── Row 2: secondary options (duration, attach) ───────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(ColAction + 4f);

            GUILayout.Label("Duration:", GUILayout.Width(60f));
            durProp.floatValue = EditorGUILayout.FloatField(
                durProp.floatValue, GUILayout.Width(50f));

            GUILayout.Space(10f);
            attachProp.boolValue = EditorGUILayout.ToggleLeft(
                "Attach to source", attachProp.boolValue, GUILayout.Width(120f));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        // Apply deferred removal.
        if (removeIndex >= 0)
        {
            Undo.RecordObject(_profile, "Remove VFX Binding");
            _bindingsProp.DeleteArrayElementAtIndex(removeIndex);
            RebuildActionIndices();
        }
    }

    // ── Action name field ─────────────────────────────────────────────────────

    /// <summary>
    /// Draws a popup with all predefined action names plus a freeform text field
    /// so designers can either pick a known name or type a custom one.
    /// </summary>
    private void DrawActionNameField(SerializedProperty actionProp, int rowIndex)
    {
        EnsureActionIndicesCapacity();

        string[] names    = SOVFXProfile.AllActionNames;
        string   current  = actionProp.stringValue;

        // Find matching index in names; -1 means custom / not found.
        int matchIndex = System.Array.IndexOf(names, current);

        // Build display options: all predefined + "Custom…"
        string[] options = new string[names.Length + 1];
        System.Array.Copy(names, options, names.Length);
        options[names.Length] = "Custom…";

        int selectedDisplay = matchIndex >= 0 ? matchIndex : names.Length;

        EditorGUILayout.BeginVertical(GUILayout.Width(ColAction));

        int newSelection = EditorGUILayout.Popup(selectedDisplay, options,
            GUILayout.Width(ColAction));

        if (newSelection != names.Length)
        {
            // Predefined name selected.
            actionProp.stringValue = names[newSelection];
        }
        else
        {
            // Custom name — show editable text field.
            string typed = EditorGUILayout.DelayedTextField(
                current == string.Empty ? "custom_action" : current,
                GUILayout.Width(ColAction));
            actionProp.stringValue = typed;
        }

        EditorGUILayout.EndVertical();
    }

    // ── Add binding button ────────────────────────────────────────────────────
    private void DrawAddButton()
    {
        if (GUILayout.Button("+ Add Binding", GUILayout.Height(26f)))
        {
            Undo.RecordObject(_profile, "Add VFX Binding");
            int newIndex = _bindingsProp.arraySize;
            _bindingsProp.InsertArrayElementAtIndex(newIndex);

            // Set sensible defaults for the newly added element.
            SerializedProperty newBinding    = _bindingsProp.GetArrayElementAtIndex(newIndex);
            SerializedProperty actionProp    = newBinding.FindPropertyRelative("actionName");
            SerializedProperty vfxProp       = newBinding.FindPropertyRelative("vfxPrefab");
            SerializedProperty sfxProp       = newBinding.FindPropertyRelative("sfxClip");
            SerializedProperty volProp       = newBinding.FindPropertyRelative("sfxVolume");
            SerializedProperty durProp       = newBinding.FindPropertyRelative("duration");
            SerializedProperty attachProp    = newBinding.FindPropertyRelative("attachToSource");

            actionProp.stringValue            = string.Empty;
            vfxProp.objectReferenceValue      = null;
            sfxProp.objectReferenceValue      = null;
            volProp.floatValue                = 1f;
            durProp.floatValue                = 2f;
            attachProp.boolValue              = false;

            RebuildActionIndices();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RebuildActionIndices()
    {
        int count = _bindingsProp != null ? _bindingsProp.arraySize : 0;
        _actionNameIndices = new int[count];
    }

    private void EnsureActionIndicesCapacity()
    {
        int needed = _bindingsProp != null ? _bindingsProp.arraySize : 0;
        if (_actionNameIndices == null || _actionNameIndices.Length != needed)
            _actionNameIndices = new int[needed];
    }

    // ── SFX Preview via internal AudioUtility ─────────────────────────────────

    /// <summary>
    /// Plays <paramref name="clip"/> in the editor without entering Play Mode,
    /// using Unity's internal AudioUtility via reflection.
    /// Falls back to a harmless no-op if the internal API changes.
    /// </summary>
    private static void PreviewClip(AudioClip clip, float volume)
    {
        if (clip == null) return;

        try
        {
            System.Type audioUtil = System.Type.GetType(
                "UnityEditor.AudioUtil,UnityEditor");

            if (audioUtil == null)
            {
                Debug.LogWarning("[VFXProfileEditor] AudioUtil type not found — " +
                                 "cannot preview clip in editor.");
                return;
            }

            MethodInfo playClip = audioUtil.GetMethod(
                "PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { typeof(AudioClip), typeof(int), typeof(bool) },
                null);

            if (playClip != null)
            {
                playClip.Invoke(null, new object[] { clip, 0, false });
            }
            else
            {
                // Older Unity signature: just the clip.
                MethodInfo fallback = audioUtil.GetMethod(
                    "PlayClip",
                    BindingFlags.Static | BindingFlags.Public);
                fallback?.Invoke(null, new object[] { clip });
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[VFXProfileEditor] Could not preview SFX: {ex.Message}");
        }
    }
}
#endif
