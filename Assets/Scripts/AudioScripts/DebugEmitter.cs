using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(AudioSource))]
public class DebugEmitter : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private Gradient volumeGradient;
    [SerializeField] private float minLineThickness = 2f;
    [SerializeField] private float maxLineThickness = 8f;
    [SerializeField] private float waveSpeed = 6f;

    private AudioSource audioSource;
    private bool registrationPending = false;

#if UNITY_EDITOR
    // Used to keep rings visible briefly after a clip stops so they fade rather
    // than disappearing in a single frame.
    private float _lastPlayingWallTime = -1f;
    private float _lastSrcTime         = 0f;
    // Tracks the wall-clock moment this source most recently started playing so
    // the PlayOneShot ring starts at radius 0 and expands exactly once.
    private float _playStartWallTime   = -1f;
    private bool  _wasPlayingLastFrame = false;
#endif

    public float EffectiveVolume { get; private set; }
    public float DistanceToListener { get; private set; }

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        if (volumeGradient == null || volumeGradient.colorKeys.Length == 0)
            InitializeDefaultGradient();
    }

    private void InitializeDefaultGradient()
    {
        volumeGradient = new Gradient();
        volumeGradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.blue,  0.0f),
                new GradientColorKey(Color.green, 0.5f),
                new GradientColorKey(Color.red,   1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.8f, 0.0f),
                new GradientAlphaKey(1.0f, 1.0f)
            }
        );
    }

    // Called by Unity when the component is first added in the editor — sets the
    // gradient immediately so it never starts as the default white-to-white gradient.
    private void Reset() => InitializeDefaultGradient();

    private void OnEnable()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.Register(this);
            registrationPending = false;
        }
        else
        {
            registrationPending = true;
        }

#if UNITY_EDITOR
        EditorApplication.update += SceneView.RepaintAll;
#endif
    }

    private void Start()
    {
        if (registrationPending && AudioManager.Instance != null)
        {
            AudioManager.Instance.Register(this);
            registrationPending = false;
        }
    }

    private void OnDisable()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.Unregister(this);

#if UNITY_EDITOR
        EditorApplication.update -= SceneView.RepaintAll;
#endif
    }

    private void LateUpdate()
    {
        AudioListener listener = AudioManager.Instance?.Listener;
        if (listener == null) return;

        DistanceToListener = Vector3.Distance(transform.position, listener.transform.position);
        EffectiveVolume    = ComputeEffectiveVolume(audioSource, DistanceToListener);
    }

    private static float ComputeEffectiveVolume(AudioSource src, float distance)
    {
        if (src == null) return 0f;

        float baseVolume = src.volume;
        float attenuation;

        switch (src.rolloffMode)
        {
            case AudioRolloffMode.Logarithmic:
                attenuation = Mathf.Clamp01(src.minDistance / Mathf.Max(distance, 0.0001f));
                break;

            case AudioRolloffMode.Linear:
                float minDist = src.minDistance;
                float maxDist = src.maxDistance;
                if (maxDist <= minDist)
                {
                    attenuation = 1f;
                    break;
                }
                attenuation = Mathf.Clamp01(1f - (distance - minDist) / (maxDist - minDist));
                break;

            case AudioRolloffMode.Custom:
                attenuation = 1f - Mathf.Clamp01(distance / Mathf.Max(src.maxDistance, 0.0001f));
                break;

            default:
                attenuation = 1f;
                break;
        }

        return Mathf.Clamp01(baseVolume * attenuation);
    }

    // No-op: visualization moved to editor gizmos; AudioManager still calls this on toggle
    public void SetLineVisible(bool visible) { }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!AudioManager.DebugEnabled) return;

        AudioSource src = GetComponent<AudioSource>();
        if (src == null) return;

        Vector3 emitterPos = transform.position;
        float   minDist    = src.minDistance;
        float   maxDist    = Mathf.Max(src.maxDistance, minDist + 0.1f);

        // Resolve listener — runtime path first, then edit-mode fallback
        Vector3 listenerPos = emitterPos;
        bool    hasListener = false;

        if (AudioManager.Instance != null && AudioManager.Instance.Listener != null)
        {
            listenerPos = AudioManager.Instance.Listener.transform.position;
            hasListener = true;
        }
        else
        {
            AudioListener found = FindFirstObjectByType<AudioListener>();
            if (found != null)
            {
                listenerPos = found.transform.position;
                hasListener = true;
            }
        }

        // Effective volume drives ray thickness
        float dist   = hasListener ? Vector3.Distance(emitterPos, listenerPos) : 0f;
        float effVol = ComputeEffectiveVolume(src, dist);

        // Unique color per emitter — hue derived from instance ID so each emitter
        // gets its own consistent color across all its gizmos.
        float hue          = (Mathf.Abs(gameObject.GetInstanceID()) % 100) / 100f;
        Color emitterColor = Color.HSVToRGB(hue, 0.9f, 1f);

        // ── Ray to listener (purple = emitter–player hearing connection) ────────
        if (hasListener)
        {
            Handles.color = new Color(0.72f, 0.22f, 1f, 1f);
            float thickness = Mathf.Lerp(minLineThickness, maxLineThickness, effVol);
            Handles.DrawAAPolyLine(thickness, emitterPos, listenerPos);
        }

        // ── Min distance sphere + label ──────────────────────────────────────
        Gizmos.color  = new Color(emitterColor.r, emitterColor.g, emitterColor.b, 1f);
        Gizmos.DrawWireSphere(emitterPos, minDist);
        Handles.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, 1f);
        Handles.Label(emitterPos + Vector3.right * minDist, $"Min Range  {minDist:F1}m");

        // ── Max distance sphere + label (same hue, desaturated so it reads as
        //    "outer boundary" rather than a competing solid object) ────────────
        Color maxColor = Color.HSVToRGB(hue, 0.4f, 0.9f);
        Gizmos.color  = new Color(maxColor.r, maxColor.g, maxColor.b, 0.45f);
        Gizmos.DrawWireSphere(emitterPos, maxDist);
        Handles.color = new Color(maxColor.r, maxColor.g, maxColor.b, 1f);
        Handles.Label(emitterPos + Vector3.right * maxDist, $"Max Range  {maxDist:F1}m");

        // ── Pulsing sound-wave ring ──────────────────────────────────────────
        //    One ring per emitter. radius tracks src.time so it is always 0 at
        //    clip start and maxDist at clip end; alpha fades in the same window
        //    so the ring is never visible past its fade point.
        //    A 0.5s grace period after the clip stops fades the ring out instead
        //    of cutting it off in a single frame.
        if (Application.isPlaying)
        {
            // Detect the rising edge so the PlayOneShot ring always starts at 0
            if (src.isPlaying && !_wasPlayingLastFrame)
                _playStartWallTime = Time.time;

            if (src.isPlaying)
            {
                _lastPlayingWallTime = Time.time;
                // src.time is only valid when a clip is assigned; PlayOneShot
                // leaves src.clip null and reading .time produces a Unity warning
                if (src.clip != null)
                    _lastSrcTime = src.time;
            }

            _wasPlayingLastFrame = src.isPlaying;

            float timeSinceStop = _lastPlayingWallTime < 0f
                ? float.MaxValue
                : Time.time - _lastPlayingWallTime;

            // graceFade = 1 while playing, then lerps to 0 over 0.5s after stop
            float graceFade = src.isPlaying
                ? 1f
                : Mathf.Clamp01(1f - timeSinceStop / 0.5f);

            if (graceFade > 0f)
            {
                if (src.clip != null)
                {
                    // Single ring: progress directly mirrors playback position so
                    // radius=0 at clip start, radius=maxDist at clip end, and the
                    // ring can never grow past the fade-out point.
                    float effectiveTime = src.isPlaying ? src.time : _lastSrcTime;
                    float progress      = Mathf.Clamp01(effectiveTime / src.clip.length);
                    float radius        = progress * maxDist;
                    float alpha         = (1f - progress) * graceFade * 0.85f;

                    if (alpha > 0.01f)
                    {
                        Gizmos.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, alpha);
                        Gizmos.DrawWireSphere(emitterPos, radius);
                    }
                }
                else
                {
                    // No clip assigned (PlayOneShot) — single ring expands once from
                    // the moment the source started playing.  Clamp01 stops it at
                    // maxDist so it can never "grow" past the fade-out point.
                    float cycleDuration = maxDist / Mathf.Max(waveSpeed, 0.01f);
                    float elapsed       = _playStartWallTime < 0f
                        ? cycleDuration                           // no record yet → fully expanded / invisible
                        : Time.time - _playStartWallTime;
                    float progress      = Mathf.Clamp01(elapsed / cycleDuration);
                    float radius        = progress * maxDist;
                    float alpha         = (1f - progress) * graceFade * 0.85f;

                    if (alpha > 0.01f)
                    {
                        Gizmos.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, alpha);
                        Gizmos.DrawWireSphere(emitterPos, radius);
                    }
                }
            }
        }

        // ── Emitter label (warm yellow — readable on any background) ─────────
        Handles.color = new Color(1f, 0.92f, 0.3f, 1f);
        Handles.Label(emitterPos + Vector3.up * (minDist + 0.25f),
            $"{gameObject.name}\nVol: {effVol:F2}");
    }
#endif
}
