#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

/// <summary>
/// Tools → Audio Debug System → Setup Wizard
///
/// One-click scene setup:
///   1. Creates required layers
///   2. Instantiates AudioSystem and MinimapRig prefabs
///   3. Wires MinimapController.player and AudioDebugUI.minimap at scene level
///   4. Shows a live validation checklist
/// </summary>
public class AudioDebugSetupWizard : EditorWindow
{
    private static readonly string[] RequiredLayers = { "Minimap", "Ground", "MinimapOnly" };

    private const string PrefabSearchFolder = "Packages/com.audiodebug/Prefabs";

    private Vector2 _scroll;
    private bool    _showHelp = false;

    [MenuItem("Tools/Audio Debug System/Setup Wizard")]
    public static void Open()
    {
        var w = GetWindow<AudioDebugSetupWizard>(title: "Audio Debug Setup");
        w.minSize = new Vector2(420, 520);
        w.Show();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // Title
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        EditorGUILayout.LabelField("Audio Debug System", titleStyle, GUILayout.Height(28));
        EditorGUILayout.LabelField("Setup Wizard — Unity 6",
            new GUIStyle(EditorStyles.centeredGreyMiniLabel));
        EditorGUILayout.Space(8);

        // ── Checklist ──────────────────────────────────────────────────────
        DrawSection("① Layers",       DrawLayerChecks);
        EditorGUILayout.Space(4);
        DrawSection("② Prefabs",      DrawPrefabChecks);
        EditorGUILayout.Space(4);
        DrawSection("③ Wiring",       DrawWiringChecks);
        EditorGUILayout.Space(8);

        // ── Run button ─────────────────────────────────────────────────────
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
        if (GUILayout.Button("▶  Run Full Setup", GUILayout.Height(34)))
            RunFullSetup();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(8);

        // ── Help foldout ───────────────────────────────────────────────────
        _showHelp = EditorGUILayout.Foldout(_showHelp, "Per-emitter setup instructions", true);
        if (_showHelp)
            DrawHelp();

        EditorGUILayout.EndScrollView();
    }

    // ── Section wrapper ───────────────────────────────────────────────────────

    private void DrawSection(string title, Action content)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        content();
        EditorGUI.indentLevel--;
    }

    // ── Checklist sections ────────────────────────────────────────────────────

    private void DrawLayerChecks()
    {
        foreach (string layer in RequiredLayers)
        {
            bool ok = LayerMask.NameToLayer(layer) != -1;
            DrawRow(layer, ok, ok ? "exists" : "missing — will be created");
        }
    }

    private void DrawPrefabChecks()
    {
        bool audioSystemPrefab = FindPackagePrefab("AudioSystem") != null;
        bool minimapRigPrefab  = FindPackagePrefab("MinimapRig")  != null;
        bool audioSystemInScene = FindFirstObjectOfType<AudioManager>() != null;
        bool minimapRigInScene  = FindFirstObjectOfType<MinimapController>() != null;

        DrawRow("AudioSystem prefab in package",  audioSystemPrefab,
            audioSystemPrefab ? "found" : "missing — place AudioSystem.prefab in Packages/com.audiodebug/Prefabs/");
        DrawRow("MinimapRig prefab in package",   minimapRigPrefab,
            minimapRigPrefab  ? "found" : "missing — place MinimapRig.prefab in Packages/com.audiodebug/Prefabs/");
        DrawRow("AudioSystem in scene",           audioSystemInScene,
            audioSystemInScene ? "already present" : "will be instantiated");
        DrawRow("MinimapRig in scene",            minimapRigInScene,
            minimapRigInScene  ? "already present" : "will be instantiated");
    }

    private void DrawWiringChecks()
    {
        AudioDebugUI      ui = FindFirstObjectOfType<AudioDebugUI>();
        MinimapController mc = FindFirstObjectOfType<MinimapController>();

        bool listenerExists = FindFirstObjectOfType<AudioListener>() != null;
        bool playerTagged   = GameObject.FindWithTag("Player") != null;
        bool mcHasPlayer    = mc != null && mc.player != null;
        bool mcHasCamera    = mc != null && mc.minimapCamera != null;
        bool uiHasMinimap   = ui != null && ui.minimap != null;
        bool uiHasViz       = ui != null && ui.visualizer != null;

        DrawRow("AudioListener in scene",       listenerExists, listenerExists ? "found" : "add one to your camera");
        DrawRow("Player tagged \"Player\"",     playerTagged,   playerTagged   ? "found" : "tag your player GameObject");
        DrawRow("MinimapController → player",   mcHasPlayer,    mcHasPlayer    ? "wired" : "will be wired on setup");
        DrawRow("MinimapController → camera",   mcHasCamera,    mcHasCamera    ? "wired" : "set in prefab");
        DrawRow("AudioDebugUI → minimap",       uiHasMinimap,   uiHasMinimap   ? "wired" : "will be wired on setup");
        DrawRow("AudioDebugUI → visualizer",    uiHasViz,       uiHasViz       ? "wired" : "will be wired on setup");
    }

    // ── Full setup ────────────────────────────────────────────────────────────

    private void RunFullSetup()
    {
        Undo.SetCurrentGroupName("Audio Debug System Setup");
        int group = Undo.GetCurrentGroup();

        // 1. Layers
        CreateMissingLayers();

        // 2. AudioSystem prefab
        AudioManager mgr = FindFirstObjectOfType<AudioManager>();
        if (mgr == null)
        {
            GameObject prefab = FindPackagePrefab("AudioSystem");
            if (prefab != null)
            {
                GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(go, "Instantiate AudioSystem");
                mgr = go.GetComponent<AudioManager>();
            }
            else
            {
                Debug.LogError("[AudioDebugSetup] AudioSystem.prefab not found in " +
                               PrefabSearchFolder + ". Place it there and run setup again.");
            }
        }

        // 3. MinimapRig prefab
        MinimapController mc = FindFirstObjectOfType<MinimapController>();
        if (mc == null)
        {
            GameObject prefab = FindPackagePrefab("MinimapRig");
            if (prefab != null)
            {
                GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(go, "Instantiate MinimapRig");
                mc = go.GetComponentInChildren<MinimapController>();
            }
            else
            {
                Debug.LogError("[AudioDebugSetup] MinimapRig.prefab not found in " +
                               PrefabSearchFolder + ". Place it there and run setup again.");
            }
        }

        // 4. Wire player
        if (mc != null && mc.player == null)
        {
            GameObject playerGO = GameObject.FindWithTag("Player");
            if (playerGO != null)
            {
                Undo.RecordObject(mc, "Wire Player");
                mc.player = playerGO.transform;
                EditorUtility.SetDirty(mc);
            }
            else
            {
                Debug.LogWarning("[AudioDebugSetup] No GameObject tagged 'Player' found. " +
                                 "Assign MinimapController.player manually.");
            }
        }

        // 5. Wire AudioDebugUI
        AudioDebugUI ui = FindFirstObjectOfType<AudioDebugUI>();
        if (ui != null)
        {
            Undo.RecordObject(ui, "Wire AudioDebugUI");
            if (ui.minimap == null && mc != null)
                ui.minimap = mc;
            if (ui.visualizer == null)
                ui.visualizer = ui.GetComponent<AudioDirectionVisualizer>();
            EditorUtility.SetDirty(ui);
        }

        Undo.CollapseUndoOperations(group);

        Debug.Log("[AudioDebugSetup] Setup complete. Check the checklist above for any remaining steps.");
        Repaint();
    }

    // ── Layer creation ────────────────────────────────────────────────────────

    private static void CreateMissingLayers()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        foreach (string layerName in RequiredLayers)
        {
            if (LayerMask.NameToLayer(layerName) != -1) continue;

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[AudioDebugSetup] Created layer '{layerName}' at index {i}.");
                break;
            }
        }
    }

    // ── Prefab lookup ─────────────────────────────────────────────────────────

    private static GameObject FindPackagePrefab(string prefabName)
    {
        string[] guids = AssetDatabase.FindAssets(
            $"t:Prefab {prefabName}",
            new[] { PrefabSearchFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // Exact name match so "AudioSystem" doesn't match "AudioSystemExtra" etc.
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (fileName == prefabName)
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    // ── Help text ─────────────────────────────────────────────────────────────

    private void DrawHelp()
    {
        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox(
            "Add these components to each sound object:\n" +
            "  • AudioSource       — Unity built-in\n" +
            "  • AudioEmitter      — clip label, interval, randomization\n" +
            "  • DebugEmitter      — occlusion, Doppler, gizmos, HUD data\n" +
            "  • MinimapEmitterRing — (optional) minimap pulse ring\n\n" +
#if UNITY_SPLINES
            "Ambient zone setup:\n" +
            "  1. Empty GO + SplineContainer → draw path in Scene view\n" +
            "  2. Another empty GO + AmbientZoneSpline + AudioSource\n" +
            "  3. Assign Spline Container and Player in inspector\n\n" +
#endif
            "Runtime keys:\n" +
            "  F1 — toggle debug HUD\n" +
            "  V  — toggle direction visualizer\n" +
            "  G  — toggle minimap overview\n" +
            "  = / - — zoom minimap",
            MessageType.None);
        EditorGUI.indentLevel--;
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private static void DrawRow(string label, bool ok, string detail)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(
            ok ? EditorGUIUtility.IconContent("d_greenLight")
               : EditorGUIUtility.IconContent("d_redLight"),
            GUILayout.Width(20), GUILayout.Height(18));
        EditorGUILayout.LabelField(label, GUILayout.Width(230));
        EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private static T FindFirstObjectOfType<T>() where T : UnityEngine.Object
        => UnityEngine.Object.FindFirstObjectByType<T>();
}
#endif
