#if UNITY_SPLINES
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ============== INSTRUCTION ==============
// Create empty game object and add "Spline Container" component
// Add knots and draw line using the "Spline Edit Mode" in the Scene Tools
// Create another empty game object and add this script
// Select "Spline" as well as "Player" in the inspector
// Add sound to the object

[RequireComponent(typeof(AudioSource))]
public class AmbientZoneSpline : MonoBehaviour
{
    [Tooltip("Spline Path to follow")]
    public SplineContainer Spline;

    [Tooltip("Character to track")]
    public Transform Player;

    [Tooltip("How far outside the spline the player can be before the audio is silenced entirely. " +
             "Set this to match (or be slightly less than) the AudioSource's Max Distance.")]
    [SerializeField] private float activationDistance = 20f;

    [Tooltip("Speed (m/s) at which the pulsing gizmo ring travels outward from min to max range.")]
    [SerializeField] private float waveSpeed = 6f;

    // ── Runtime state (read by AudioDebugUI + OnDrawGizmos) ──────────────────
    private AudioSource _audioSource;
    private bool        _isInside            = false;
    private bool        _isActive            = false;
    private float       _distToSpline        = float.MaxValue;
    private bool        _registrationPending = false;

    // Sampled polygon used for point-in-polygon test — reused every frame
    private const int PolygonSamples = 64;
    private readonly Vector3[] _polygon = new Vector3[PolygonSamples];

    public bool  IsInside     => _isInside;
    public bool  IsActive     => _isActive;
    public float DistToSpline => _distToSpline;

#if UNITY_EDITOR
    // Ring animation state — matches DebugEmitter pattern
    private float _lastPlayingTime       = -1f;
    private float _lastSrcTime           = 0f;
    private float _playStartTime         = -1f;
    private bool  _wasPlayingLastFrame   = false;
    private float _playOneShotClipLength = -1f;
#endif

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.RegisterZone(this);
            _registrationPending = false;
        }
        else
        {
            _registrationPending = true;
        }

#if UNITY_EDITOR
        EditorApplication.update += SceneView.RepaintAll;
#endif
    }

    private void Start()
    {
        if (_registrationPending && AudioManager.Instance != null)
        {
            AudioManager.Instance.RegisterZone(this);
            _registrationPending = false;
        }

        // Set the correct initial mute state before the first Update so there
        // is no single-frame blip of audio when the scene loads.
        if (_audioSource != null && Player != null && Spline != null)
        {
            bool startActive = IsInsideClosedSpline(Player.position) ||
                               DistanceToSpline(Player.position) <= activationDistance;
            _audioSource.mute = !startActive;
        }
    }

    private void OnDisable()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.UnregisterZone(this);

        if (_audioSource != null)
            _audioSource.mute = false;

#if UNITY_EDITOR
        EditorApplication.update -= SceneView.RepaintAll;
#endif
    }

    // ── Main loop ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (Spline == null || Player == null) return;

        // ── Nearest point on spline ───────────────────────────────────────────
        Vector3 localPlayerPos = Spline.transform.InverseTransformPoint(Player.position);
        SplineUtility.GetNearestPoint(Spline.Spline, localPlayerPos,
            out float3 nearestPointLocal, out float normalizedT);

        Vector3 nearestWorldPos = Spline.transform.TransformPoint(nearestPointLocal);
        _distToSpline = Vector3.Distance(Player.position, nearestWorldPos);

        // AudioSource follows the nearest spline point so distance-based rolloff
        // works correctly when the player is outside the zone.
        transform.position = nearestWorldPos;
        Vector3 tangent = Spline.transform.TransformDirection(
            Spline.Spline.EvaluateTangent(normalizedT));
        if (tangent != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(tangent);

        // ── Inside/outside detection (2D ray-casting, XZ plane) ──────────────
        // The old dot-product approach only tested one tangent point and failed
        // for any non-trivial spline shape.  Ray-casting is O(N) but correct
        // regardless of winding direction or convexity.
        _isInside = Spline.Spline.Closed && IsInsideClosedSpline(Player.position);

        if (_isInside)
        {
            _distToSpline      = 0f;
            transform.position = Player.position + new Vector3(0f, 1f, 0f);
            transform.rotation = Player.rotation;
        }

        // ── Mute when out of range ────────────────────────────────────────────
        // Use mute rather than writing to audioSource.volume so this script
        // does not conflict with DebugEmitter, which also owns audioSource.volume.
        _isActive = _isInside || _distToSpline <= activationDistance;
        if (_audioSource != null)
            _audioSource.mute = !_isActive;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Builds a sampled polygon from the spline and runs a standard 2D
    // point-in-polygon ray-cast test in the XZ plane.  Winding-order agnostic.
    private bool IsInsideClosedSpline(Vector3 point)
    {
        for (int i = 0; i < PolygonSamples; i++)
        {
            float t = (float)i / PolygonSamples;
            _polygon[i] = Spline.transform.TransformPoint(
                (Vector3)Spline.Spline.EvaluatePosition(t));
        }

        float  px     = point.x;
        float  pz     = point.z;
        bool   inside = false;
        int    j      = PolygonSamples - 1;

        for (int i = 0; i < PolygonSamples; j = i++)
        {
            float xi = _polygon[i].x, zi = _polygon[i].z;
            float xj = _polygon[j].x, zj = _polygon[j].z;

            if ((zi > pz) != (zj > pz) &&
                px < (xj - xi) * (pz - zi) / (zj - zi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    // Quick nearest-point distance used only in Start() for initial state.
    private float DistanceToSpline(Vector3 worldPos)
    {
        Vector3 local = Spline.transform.InverseTransformPoint(worldPos);
        SplineUtility.GetNearestPoint(Spline.Spline, local,
            out float3 nearestLocal, out float _);
        return Vector3.Distance(worldPos,
            Spline.transform.TransformPoint((Vector3)nearestLocal));
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (Spline == null) return;

        // Black bold label style — matches DebugEmitter
        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            normal    = { textColor = Color.black },
            fontStyle = FontStyle.Bold
        };

        bool isPlaying = Application.isPlaying;

        // Per-zone unique color (Wang hash on instance ID — same algorithm as DebugEmitter)
        uint uid = (uint)Mathf.Abs(gameObject.GetInstanceID());
        uid ^= uid >> 16; uid *= 0x45d9f3bu; uid ^= uid >> 16;
        float hue         = (uid % 360u) / 360f;
        Color uniqueColor = Color.HSVToRGB(hue, 0.9f, 1f);
        Color maxColor    = Color.HSVToRGB(hue, 0.4f, 0.9f);

        // Anchor: current audio position in play, spline midpoint in edit
        Vector3 anchor = isPlaying
            ? transform.position
            : Spline.transform.TransformPoint((Vector3)Spline.Spline.EvaluatePosition(0.5f));

        // ── Min / Max distance spheres (always visible) ───────────────────────
        AudioSource src = GetComponent<AudioSource>();
        float minDist = 1f;
        float maxDist = 10f;
        if (src != null)
        {
            minDist = src.minDistance;
            maxDist = Mathf.Max(src.maxDistance, minDist + 0.1f);

            Gizmos.color = new Color(uniqueColor.r, uniqueColor.g, uniqueColor.b, 1f);
            Gizmos.DrawWireSphere(anchor, minDist);
            Handles.Label(anchor + Vector3.right * minDist,
                $"Min Range  {minDist:F1}m", labelStyle);

            Gizmos.color = new Color(maxColor.r, maxColor.g, maxColor.b, 0.45f);
            Gizmos.DrawWireSphere(anchor, maxDist);
            Handles.Label(anchor + Vector3.right * maxDist,
                $"Max Range  {maxDist:F1}m", labelStyle);
        }

        // ── Pulsing sound-wave ring (always visible) ──────────────────────────
        if (isPlaying && src != null)
        {
            if (src.isPlaying && !_wasPlayingLastFrame)
                _playStartTime = Time.time;

            if (!src.isPlaying && _wasPlayingLastFrame && src.clip == null
                && _playStartTime >= 0f)
            {
                float measured = Time.time - _playStartTime;
                if (measured > 0.05f)
                    _playOneShotClipLength = measured;
            }

            if (src.isPlaying)
            {
                _lastPlayingTime = Time.time;
                if (src.clip != null) _lastSrcTime = src.time;
            }

            _wasPlayingLastFrame = src.isPlaying;

            float timeSinceStop = _lastPlayingTime < 0f
                ? float.MaxValue : Time.time - _lastPlayingTime;
            float graceFade = src.isPlaying
                ? 1f : Mathf.Clamp01(1f - timeSinceStop / 0.5f);

            if (graceFade > 0f)
            {
                float progress;
                if (src.clip != null)
                {
                    float effectiveTime = src.isPlaying ? src.time : _lastSrcTime;
                    progress = Mathf.Clamp01(effectiveTime / src.clip.length);
                }
                else
                {
                    float elapsed = _playStartTime < 0f ? 0f : Time.time - _playStartTime;
                    progress = _playOneShotClipLength > 0f
                        ? Mathf.Clamp01(elapsed / _playOneShotClipLength)
                        : Mathf.Clamp01((minDist + waveSpeed * elapsed - minDist)
                            / Mathf.Max(maxDist - minDist, 0.001f));
                }

                float ringRadius = Mathf.Lerp(minDist, maxDist, progress);
                float ringAlpha  = (1f - progress) * graceFade * 0.85f;
                if (ringAlpha > 0.01f)
                {
                    Gizmos.color = new Color(uniqueColor.r, uniqueColor.g, uniqueColor.b, ringAlpha);
                    Gizmos.DrawWireSphere(anchor, ringRadius);
                }
            }
        }

        // ── Everything below requires debug mode on ───────────────────────────
        if (!AudioManager.DebugEnabled) return;

        // State color: green = inside, orange = active-outside, grey = out of range
        Color zoneColor;
        if (!isPlaying)
            zoneColor = new Color(1f, 0.6f, 0f);
        else if (_isInside)
            zoneColor = Color.green;
        else if (_isActive)
            zoneColor = new Color(1f, 0.6f, 0f);
        else
            zoneColor = new Color(0.5f, 0.5f, 0.5f);

        // ── Spline path ───────────────────────────────────────────────────────
        const int Steps = 64;
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= Steps; i++)
        {
            float   t       = (float)i / Steps;
            Vector3 worldPt = Spline.transform.TransformPoint(
                (Vector3)Spline.Spline.EvaluatePosition(t));
            if (i > 0)
            {
                Handles.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.85f);
                Handles.DrawAAPolyLine(3f, prev, worldPt);
            }
            prev = worldPt;
        }

        // ── Activation radius ring around the nearest anchor ──────────────────
        if (!isPlaying || !_isInside)
        {
            Vector3 ringCenter = isPlaying
                ? transform.position
                : Spline.transform.TransformPoint((Vector3)Spline.Spline.EvaluatePosition(0f));
            Handles.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.25f);
            Handles.DrawWireDisc(ringCenter, Vector3.up, activationDistance);
        }

        // ── Play-mode runtime visuals ─────────────────────────────────────────
        if (isPlaying)
        {
            Gizmos.color = zoneColor;
            Gizmos.DrawSphere(transform.position, 0.2f);

            if (Player != null && !_isInside && _isActive)
            {
                Handles.color = new Color(1f, 0.6f, 0f, 0.55f);
                Handles.DrawDottedLine(Player.position, transform.position, 4f);
            }

            string distStr   = _isInside ? "0.0m" : $"{_distToSpline:F1}m";
            string statusStr = _isInside ? "Inside"
                             : _isActive ? "Outside"
                             :             "Out of range";
            Handles.Label(transform.position + Vector3.up * 0.5f,
                $"{gameObject.name}\nStatus: {statusStr} ({distStr})", labelStyle);
        }
        else
        {
            Handles.Label(anchor + Vector3.up * 0.5f,
                $"{gameObject.name}\nActivation: {activationDistance:F0}m", labelStyle);
        }
    }
#endif
}
#endif // UNITY_SPLINES
