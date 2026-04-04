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
    private float _lastPlayingWallTime    = -1f;
    private float _lastSrcTime            = 0f;
    // Tracks the wall-clock moment this source most recently started playing so
    // the PlayOneShot ring starts at radius 0 and expands exactly once.
    private float _playStartWallTime      = -1f;
    private bool  _wasPlayingLastFrame    = false;
    // Cached clip length for PlayOneShot sources (src.clip stays null for those).
    private float _playOneShotClipLength  = -1f;
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

        // Unique color per emitter — Wang hash on instance ID scatters bits so
        // objects with similar IDs still land on very different hues, and every
        // visual element (spheres, ring, label) shares the same color.
        uint uid = (uint)Mathf.Abs(gameObject.GetInstanceID());
        uid ^= uid >> 16; uid *= 0x45d9f3bu; uid ^= uid >> 16;
        float hue          = (uid % 360u) / 360f;
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
            // Rising edge: record when this source started playing.
            // Prefer AudioEmitter's clip label for an immediate duration estimate.
            // If no AudioEmitter exists, keep any duration measured from a previous
            // play so sources like the player jump ring improve after their first use.
            if (src.isPlaying && !_wasPlayingLastFrame)
            {
                _playStartWallTime = Time.time;

                if (src.clip == null)
                {
                    AudioEmitter ae = GetComponent<AudioEmitter>();
                    if (ae != null && AudioManager.Instance != null)
                    {
                        AudioClip oneShotClip = AudioManager.Instance.GetClip(ae.clipLabel);
                        _playOneShotClipLength = oneShotClip != null ? oneShotClip.length : _playOneShotClipLength;
                    }
                    // else: no AudioEmitter — keep last measured value (or -1 on first play)
                }
            }

            // Falling edge: measure the actual playback duration for sources where
            // src.clip is null (PlayOneShot).  This updates _playOneShotClipLength
            // so ANY component that calls PlayOneShot gets an accurate ring from its
            // second play onward — no AudioEmitter needed.
            if (!src.isPlaying && _wasPlayingLastFrame && src.clip == null
                && _playStartWallTime >= 0f)
            {
                float measured = Time.time - _playStartWallTime;
                if (measured > 0.05f)
                    _playOneShotClipLength = measured;
            }

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
                    // progress=0 → ring sits on the min-range sphere (clip start)
                    // progress=1 → ring sits on the max-range sphere (clip end, fully faded)
                    // The clip length is the sole timer, so the journey always fills
                    // exactly one playback cycle regardless of distance or waveSpeed.
                    float effectiveTime = src.isPlaying ? src.time : _lastSrcTime;
                    float progress      = Mathf.Clamp01(effectiveTime / src.clip.length);
                    float radius        = Mathf.Lerp(minDist, maxDist, progress);
                    float alpha         = (1f - progress) * graceFade * 0.85f;

                    if (alpha > 0.01f)
                    {
                        Gizmos.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, alpha);
                        Gizmos.DrawWireSphere(emitterPos, radius);
                    }
                }
                else
                {
                    // src.clip is null (PlayOneShot path in AudioEmitter).
                    // If we cached the clip length on the rising edge, use the same
                    // clip-length lerp as the branch above so the ring always spans
                    // exactly minDist → maxDist over one clip cycle.
                    // If no length is available (e.g. player jump with no AudioEmitter),
                    // fall back to waveSpeed so something still shows.
                    float elapsed = _playStartWallTime < 0f ? 0f
                                  : Time.time - _playStartWallTime;

                    float progress;
                    if (_playOneShotClipLength > 0f)
                    {
                        progress = Mathf.Clamp01(elapsed / _playOneShotClipLength);
                    }
                    else
                    {
                        float travelDist = Mathf.Max(maxDist - minDist, 0.001f);
                        float radius0    = Mathf.Clamp(minDist + waveSpeed * elapsed, minDist, maxDist);
                        progress         = (radius0 - minDist) / travelDist;
                    }

                    float radius = Mathf.Lerp(minDist, maxDist, progress);
                    float alpha  = (1f - progress) * graceFade * 0.85f;

                    if (alpha > 0.01f)
                    {
                        Gizmos.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, alpha);
                        Gizmos.DrawWireSphere(emitterPos, radius);
                    }
                }
            }
        }

        // ── Emitter label — same color as the emitter's spheres/ring so the
        //    name is unambiguously tied to its boundaries at a glance.
        Handles.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, 1f);
        Handles.Label(emitterPos + Vector3.up * (minDist + 0.25f),
            $"{gameObject.name}\nVol: {effVol:F2}");
    }
#endif
}
