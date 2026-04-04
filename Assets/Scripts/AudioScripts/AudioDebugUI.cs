using UnityEngine;
using UnityEngine.InputSystem;

public class AudioDebugUI : MonoBehaviour
{
    [SerializeField] private Key toggleKey = Key.F1;
    
    public MinimapController minimap;

    private GUIStyle headerStyle;
    private GUIStyle rowStyle;
    private GUIStyle buttonStyle;
    private GUIStyle boxStyle;
    private bool stylesInitialized = false;

    private const float WINDOW_X     = 10f;
    private const float WINDOW_Y     = 10f;
    private const float WINDOW_WIDTH = 530f;
    private const float ROW_HEIGHT   = 22f;
    private const float COL_NAME     = 160f;
    private const float COL_DIST     = 85f;
    private const float COL_VOL      = 85f;
    private const float COL_BLEND    = 75f;
    private const float COL_MUTE     = 65f;
    private const float PADDING      = 6f;

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame && AudioManager.Instance != null)
        {
            AudioManager.Instance.ToggleDebug();
            if (minimap != null)
                minimap.gameObject.SetActive(!minimap.gameObject.activeSelf);
        }
    }

    private void OnGUI()
    {
        if (AudioManager.Instance == null)
            return;

        if (!stylesInitialized)
            InitializeStyles();

        // Toggle button — always visible
        string buttonLabel = AudioManager.DebugEnabled ? $"Debug: ON [{toggleKey}]" : $"Debug: OFF [{toggleKey}]";
        if (GUI.Button(new Rect(WINDOW_X, WINDOW_Y, 145f, 26f), buttonLabel, buttonStyle))
            AudioManager.Instance.ToggleDebug();

        if (!AudioManager.DebugEnabled)
            return;

        var emitters = AudioManager.Instance.ActiveEmitters;
        int count = emitters.Count;

        float headerHeight = ROW_HEIGHT + PADDING * 2;
        float rowsHeight   = Mathf.Max(count, 1) * ROW_HEIGHT + PADDING;
        float windowHeight = headerHeight + rowsHeight + 4f;
        float windowY      = WINDOW_Y + 34f;

        Rect windowRect = new Rect(WINDOW_X, windowY, WINDOW_WIDTH, windowHeight);
        GUI.Box(windowRect, GUIContent.none, boxStyle);

        float y = windowY + PADDING;
        float x = WINDOW_X + PADDING;
        
        // Minimap mode button
        if (minimap != null)
        {
            string mapLabel = minimap.IsOverview 
                ? $"Map: Overview [{minimap.ToggleOverviewKey}]" 
                : $"Map: Follow [{minimap.ToggleOverviewKey}]";

            if (GUI.Button(new Rect(WINDOW_X + 155f, WINDOW_Y, 180f, 26f), mapLabel, buttonStyle))
                minimap.ToggleOverview();
        }

        // Header
        GUI.Label(new Rect(x, y, COL_NAME,  ROW_HEIGHT), "Emitter",     headerStyle); x += COL_NAME;
        GUI.Label(new Rect(x, y, COL_DIST,  ROW_HEIGHT), "Distance",    headerStyle); x += COL_DIST;
        GUI.Label(new Rect(x, y, COL_VOL,   ROW_HEIGHT), "Eff. Volume", headerStyle); x += COL_VOL;
        GUI.Label(new Rect(x, y, COL_BLEND, ROW_HEIGHT), "Spatial",     headerStyle); x += COL_BLEND;
        GUI.Label(new Rect(x, y, COL_MUTE,  ROW_HEIGHT), "Muted",       headerStyle);
        y += ROW_HEIGHT + PADDING;

        // Separator
        GUI.Box(new Rect(WINDOW_X + PADDING, y - 2f, WINDOW_WIDTH - PADDING * 2f, 1f), GUIContent.none);

        if (count == 0)
        {
            GUI.Label(
                new Rect(WINDOW_X + PADDING, y, WINDOW_WIDTH - PADDING * 2f, ROW_HEIGHT),
                "No DebugEmitters registered. Attach DebugEmitter to an AudioSource GameObject.",
                rowStyle
            );
            return;
        }

        // Emitter rows
        for (int i = 0; i < count; i++)
        {
            DebugEmitter emitter = emitters[i];
            if (emitter == null) continue;

            AudioSource src = emitter.GetComponent<AudioSource>();
            if (src == null) continue;

            x = WINDOW_X + PADDING;
            GUI.Label(new Rect(x, y, COL_NAME,  ROW_HEIGHT), emitter.gameObject.name,                        rowStyle); x += COL_NAME;
            GUI.Label(new Rect(x, y, COL_DIST,  ROW_HEIGHT), emitter.DistanceToListener.ToString("F1") + "m", rowStyle); x += COL_DIST;
            GUI.Label(new Rect(x, y, COL_VOL,   ROW_HEIGHT), emitter.EffectiveVolume.ToString("F2"),           rowStyle); x += COL_VOL;
            GUI.Label(new Rect(x, y, COL_BLEND, ROW_HEIGHT), src.spatialBlend.ToString("F2"),                  rowStyle); x += COL_BLEND;
            GUI.Label(new Rect(x, y, COL_MUTE,  ROW_HEIGHT), src.mute ? "YES" : "-",                           rowStyle);
            y += ROW_HEIGHT;
        }
    }

    private void InitializeStyles()
    {
        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold
        };
        headerStyle.normal.textColor = Color.white;

        rowStyle = new GUIStyle(GUI.skin.label);
        rowStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold
        };

        boxStyle = new GUIStyle(GUI.skin.box);

        stylesInitialized = true;
    }
}
