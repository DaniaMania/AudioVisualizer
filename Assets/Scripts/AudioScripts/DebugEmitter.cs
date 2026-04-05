using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(AudioSource))]
public class DebugEmitter : MonoBehaviour
{
    // ── Visual Settings ───────────────────────────────────────────────────────
    [Header("Visual Settings")]
    [SerializeField] private Gradient volumeGradient;
    [SerializeField] private float minLineThickness = 2f;
    [SerializeField] private float maxLineThickness = 8f;
    [SerializeField] private float waveSpeed = 6f;
    
    [Header("UI Settings")]
    public bool excludeFromUI = false;

    // ── Occlusion ─────────────────────────────────────────────────────────────
    [Header("Occlusion")]
    [Tooltip("Casts a ray from the listener to the emitter to detect intervening walls " +
             "(drives volume + LPF), and 10 outward probe rays from the emitter to classify " +
             "the environment as indoors or outdoors (boosts filter character when indoors).")]
    [SerializeField] private bool occlusionEnabled = false;
    [Tooltip("Volume multiplier applied per wall between listener and emitter " +
             "(0 = silence per wall, 1 = no effect). 0.7 = each wall reduces to 70%.")]
    [SerializeField][Range(0f, 1f)] private float occlusionVolumeMultiplier = 0.7f;
    [Tooltip("Minimum volume fraction regardless of wall count " +
             "(0 = can silence completely, 0.25 = always at least 25% audible).")]
    [SerializeField][Range(0f, 1f)] private float occlusionVolumeFloor = 0.25f;
    [Tooltip("How far each of the 10 environment probe rays travels (m). " +
             "Should match your room/area scale — rays that reach this distance count as 'open'.")]
    [SerializeField] private float occlusionProbeDistance = 15f;
    [Tooltip("Low-pass filter cutoff (Hz) when fully indoors — lower = more muffled (100–1500 Hz). " +
             "22000 disables filtering. 600 Hz is a moderate indoor muffle; 300 Hz is heavy.")]
    [SerializeField] private float occlusionLowPassCutoff = 600f;
    [Tooltip("Resonance Q at the cutoff frequency (1 = flat / natural muffle, >1 = adds a tonal peak " +
             "at the cutoff that can sound like 'pshhh' during transitions — keep at 1 for clean walls).")]
    [SerializeField][Range(1f, 3f)] private float occlusionLowPassResonance = 1f;
    [Tooltip("Which layers count as enclosing geometry. Exclude Player, triggers, water, etc.")]
    [SerializeField] private LayerMask occlusionLayerMask = ~0;
    [Tooltip("How fast volume and filter blend when moving in/out of occlusion. " +
             "Lower = slower crossfade (1 = ~1 s), higher = snappier (8 = ~0.1 s).")]
    [SerializeField][Range(0.5f, 15f)] private float occlusionSmoothSpeed = 4f;
    [Tooltip("Wall thickness in meters that equals one full 'wall' of occlusion. " +
             "0.5 = a 0.5 m wall gives full single-wall attenuation; a 0.1 m corner gives 20% of that. (Set to the max thickness of your wall)")]
    [SerializeField] private float occlusionThicknessReference = 2.0f;

    // ── Doppler Displacement ──────────────────────────────────────────────────
    [Header("Doppler Displacement")]
    [Tooltip("Offset the audio source behind the moving object to simulate sound-lag.")]
    [SerializeField] private bool  dopplerEnabled          = false;
    [Tooltip("Speed of sound in m/s (343 = realistic). Lower = more noticeable lag.")]
    [SerializeField] private float soundSpeed              = 343f;
    [Tooltip("Maximum world-unit offset so fast objects don't displace across the map.")]
    [SerializeField] private float maxDisplacementDistance = 15f;
    [Tooltip("Artistic multiplier — >1 exaggerates the lag for dramatic effect.")]
    [SerializeField] private float dopplerExaggeration     = 1f;

    [Tooltip("Shift pitch based on how fast the listener and emitter are closing in or moving apart " +
             "(classic Doppler). Only active while Doppler Displacement is enabled.")]
    [SerializeField] private bool  dopplerPitchEnabled    = true;
    [Tooltip("Virtual speed of sound for the pitch calculation (m/s). " +
             "30 gives a noticeable ~7-10 % shift at a brisk walk; 343 is physically accurate " +
             "but nearly imperceptible at typical game speeds.")]
    [SerializeField] private float dopplerPitchSoundSpeed = 30f;
    [Tooltip("Pitch-shift strength multiplier. 1 = physically-scaled by Pitch Sound Speed; " +
             ">1 = exaggerated; 0 = no shift.")]
    [SerializeField][Range(0f, 3f)] private float dopplerPitchStrength = 1f;
    [Tooltip("Also shift pitch based on distance — higher pitch when the listener is close, " +
             "lower when far. Independent of movement direction.")]
    [SerializeField] private bool  distancePitchEnabled   = false;
    [Tooltip("Pitch multiplier applied when the listener is at or inside Min Distance.")]
    [SerializeField][Range(0.5f, 2f)] private float pitchAtMinDistance = 1.1f;
    [Tooltip("Pitch multiplier applied when the listener is at Max Distance.")]
    [SerializeField][Range(0.5f, 2f)] private float pitchAtMaxDistance = 0.9f;

    // ── Private runtime state ─────────────────────────────────────────────────
    private AudioSource   audioSource;
    private AudioListener _listener;
    private bool          registrationPending = false;

    // Occlusion — listener-to-emitter wall ray
    private RaycastHit[] _wallHitBuffer;   // forward ray hits (deduplicated)
    private RaycastHit[] _reverseHitBuffer; // reverse ray hits (emitter→listener, for exit points)
    private int          _wallHitCount;    // unique walls this frame (for gizmo display)
    private float        _totalWallThickness; // sum of wall thicknesses in meters this frame

    // Occlusion — environment probe (10 rays outward from emitter)
    private RaycastHit[] _probeHitBuffer; // scratch buffer shared across all probe rays
    private int          _probeHitCount;  // how many of the 10 rays hit geometry this frame

    // Occlusion — smoothed values applied to audio (targets set by raycasts each frame)
    private float _smoothedOcclusionFactor = 1f;
    private float _smoothedFilterT         = 0f;
    private AudioLowPassFilter _lowPassFilter;
    private AudioLowPassFilter _proxyLowPassFilter;

    // Fixed world-space probe directions: 8 horizontal (45° steps) + up + down
    private static readonly Vector3[] ProbeDirs = BuildProbeDirs();
    private static Vector3[] BuildProbeDirs()
    {
        var d = new Vector3[10];
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            d[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }
        d[8] = Vector3.up;
        d[9] = Vector3.down;
        return d;
    }

    // Doppler
    private Rigidbody   _rb;
    private Vector3     _prevPosition;
    private Vector3     _currentVelocity;
    private GameObject  _proxyGO;
    private Transform   _proxyTransform;
    private AudioSource _proxySource;
    private bool        _proxyWasPlaying;       // runtime rising/falling edge for SyncProxyPlayback
    private bool        _dopplerWarnedOnce;     // suppress repeated "no AudioEmitter" warnings
    private float       _prevDistanceToListener = -1f; // -1 = not yet initialized (skips first-frame spike)

    // Volume / pitch tracking — prevents occlusion and Doppler feedback loops
    private float _naturalVolume   = 1f;   // volume AudioEmitter intended; captured once per rising edge
    private float _naturalPitch    = 1f;   // pitch  AudioEmitter intended; captured once per rising edge
    private bool  _srcWasPlayingLU = false; // rising-edge tracker for LateUpdate

#if UNITY_EDITOR
    // Ring animation state (editor-only — only read inside OnDrawGizmos)
    private float _lastPlayingWallTime   = -1f;
    private float _lastSrcTime           = 0f;
    private float _playStartWallTime     = -1f;
    private bool  _wasPlayingLastFrame   = false;
    private float _playOneShotClipLength = -1f;

    // Probe-ray gizmo state — written by ComputeEnvironmentFactor in LateUpdate,
    // read by OnDrawGizmos. One entry per probe ray (10 total).
    private readonly bool[]    _probeRayDidHit    = new bool[10];
    private readonly Vector3[] _probeRayHitPoints = new Vector3[10];
#endif

    // ── Public properties (read by AudioDebugUI) ──────────────────────────────
    public float   EffectiveVolume        { get; private set; }
    public float   DistanceToListener     { get; private set; }
    public float   OcclusionFactor        { get; private set; } = 1f;
    public float   IndoorRatio            { get; private set; }       // 0 = outdoor, 1 = indoor
    public float   DisplacementDistance   { get; private set; }
    public Vector3 DisplacedAudioPosition { get; private set; }
    public float   CurrentPitch           { get; private set; } = 1f;
    public float   CurrentLowPassCutoff   { get; private set; } = 22000f;
    public float   ReverbDecayTime        => 0f; // not yet implemented

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        audioSource         = GetComponent<AudioSource>();
        _rb                 = GetComponent<Rigidbody>();
        _prevPosition       = transform.position;

        // Reset mute in case a previous play-mode session left it serialized as true
        // (SyncProxyPlayback mutes the main source while Doppler is active; if the scene
        // was saved in that state the AudioSource retains mute=true across sessions).
        audioSource.mute = false;
        _wallHitBuffer    = new RaycastHit[16]; // listener→emitter forward ray
        _reverseHitBuffer = new RaycastHit[16]; // emitter→listener reverse ray (exit points)
        _probeHitBuffer   = new RaycastHit[8];  // per-probe-ray scratch

        // Always destroy any AudioLowPassFilter present at startup.
        // A previous version added it eagerly in Awake(), saving it into scene files.
        // Unity's internal playOnAwake path fires Play() before LateUpdate runs, so
        // any filter component that exists at that moment — even with occlusionEnabled=true
        // — triggers "Only custom filters can be played" on sources with no clip.
        // Destroying it here and re-adding lazily (EnsureLowPassFilter, called from
        // LateUpdate once audio is already running) sidesteps the timing entirely.
        if (TryGetComponent<AudioLowPassFilter>(out var staleFilter))
            Destroy(staleFilter);
        _lowPassFilter = null;

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
    
    public static Color GetEmitterColor(int instanceID)
    {
        uint uid = (uint)Mathf.Abs(instanceID);
        uid ^= uid >> 16; uid *= 0x45d9f3bu; uid ^= uid >> 16;
        float hue = (uid % 360u) / 360f;
        return Color.HSVToRGB(hue, 0.9f, 1f);
    }

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

        if (audioSource != null)
        {
            audioSource.mute   = false;
            audioSource.volume = _naturalVolume; // undo any occluded write-back
            audioSource.pitch  = _naturalPitch;  // undo any Doppler pitch write-back
        }

        if (_lowPassFilter != null)
        {
            _lowPassFilter.cutoffFrequency   = 22000f;
            _lowPassFilter.lowpassResonanceQ = 1f;
        }

#if UNITY_EDITOR
        EditorApplication.update -= SceneView.RepaintAll;
#endif
    }

    private void OnDestroy()
    {
        if (_proxyGO != null)
            Destroy(_proxyGO);
    }

    private void LateUpdate()
    {
        _listener = AudioManager.Instance?.Listener;
        if (_listener == null) return;

        // ── 1. Distance ──────────────────────────────────────────────────────
        DistanceToListener = Vector3.Distance(transform.position, _listener.transform.position);

        // ── 2. Doppler displacement ──────────────────────────────────────────
        Vector3 audioPos;
        if (dopplerEnabled)
        {
            UpdateVelocity();
            if (_proxyGO == null)
                InitializeProxy();
            UpdateDopplerDisplacement();
            SyncProxyPlayback();
            audioPos = DisplacedAudioPosition;
        }
        else
        {
            DisplacementDistance   = 0f;
            DisplacedAudioPosition = transform.position;
            if (_proxyGO != null)
                audioSource.mute = false;
            audioPos = transform.position;
        }

        // ── 3. Natural volume + effective volume ──────────────────────────────
        // Capture the volume AudioEmitter intended exactly once — on the first frame
        // isPlaying becomes true (before we ever write to audioSource.volume).
        // Never re-read audioSource.volume after we've written the occluded value:
        // doing so compounding OcclusionFactor each frame → exponential decay → silence.
        bool srcIsPlaying = audioSource.isPlaying;
        if (srcIsPlaying && !_srcWasPlayingLU)
        {
            _naturalVolume = audioSource.volume;
            _naturalPitch  = audioSource.pitch;
        }
        _srcWasPlayingLU = srcIsPlaying;

        EffectiveVolume = ComputeEffectiveVolume(audioSource, DistanceToListener, _naturalVolume);

        // ── 4. Occlusion ─────────────────────────────────────────────────────
        bool inRange = occlusionEnabled && DistanceToListener <= audioSource.maxDistance;

        if (occlusionEnabled)
        {
            float targetOcclusionFactor;
            float targetFilterT;

            if (inRange)
            {
                // ── Raycasts: compute this frame's targets ────────────────────
                EnsureLowPassFilter();
                _wallHitCount = CastWallRay(_listener.transform.position, audioPos);

                // wallEquivalents is continuous: total thickness / reference thickness.
                // A thin corner (0.1 m) with reference 0.5 m = 0.2 equivalents → mild occlusion.
                // A full wall (0.5 m+) = 1.0+ equivalents → full single-wall occlusion.
                float wallEquivalents = _totalWallThickness / Mathf.Max(occlusionThicknessReference, 0.001f);

                targetOcclusionFactor = Mathf.Max(
                    Mathf.Pow(occlusionVolumeMultiplier, wallEquivalents),
                    occlusionVolumeFloor);

                IndoorRatio = ComputeEnvironmentProbe(audioPos);
                float wallFilterT = 1f - Mathf.Exp(-3f * wallEquivalents);
                targetFilterT = Mathf.Clamp01(
                    wallFilterT + IndoorRatio * 0.15f * (1f - wallFilterT));

                // ── Smooth toward targets ────────────────────────────────────
                float t = occlusionSmoothSpeed * Time.deltaTime;
                _smoothedOcclusionFactor = Mathf.Lerp(_smoothedOcclusionFactor, targetOcclusionFactor, t);
                _smoothedFilterT         = Mathf.Lerp(_smoothedFilterT, targetFilterT, t);
            }
            else
            {
                // Outside max range — snap smoothed state clean so re-entry
                // always starts from an unoccluded baseline (no pop on entry).
                _wallHitCount            = 0;
                _probeHitCount           = 0;
                IndoorRatio              = 0f;
                _smoothedOcclusionFactor = 1f;
                _smoothedFilterT         = 0f;
            }

            // ── Apply smoothed values to audio ───────────────────────────────
            OcclusionFactor  = _smoothedOcclusionFactor;
            EffectiveVolume *= OcclusionFactor;

            float cutoff    = Mathf.Lerp(22000f, occlusionLowPassCutoff,  _smoothedFilterT);
            float resonance = Mathf.Lerp(1f,     occlusionLowPassResonance, _smoothedFilterT);
            CurrentLowPassCutoff = cutoff;

            if (dopplerEnabled && _proxySource != null)
            {
                _proxySource.volume = _naturalVolume * OcclusionFactor;
                if (_proxyLowPassFilter != null)
                {
                    _proxyLowPassFilter.cutoffFrequency   = cutoff;
                    _proxyLowPassFilter.lowpassResonanceQ = resonance;
                }
            }
            else
            {
                audioSource.volume = _naturalVolume * OcclusionFactor;
                if (_lowPassFilter != null)
                {
                    _lowPassFilter.cutoffFrequency   = cutoff;
                    _lowPassFilter.lowpassResonanceQ = resonance;
                }
            }
        }
        else
        {
            // Occlusion disabled — snap everything clean immediately
            OcclusionFactor          = 1f;
            IndoorRatio              = 0f;
            _smoothedOcclusionFactor = 1f;
            _smoothedFilterT         = 0f;
            _wallHitCount            = 0;
            _probeHitCount           = 0;
            CurrentLowPassCutoff     = 22000f;
            if (srcIsPlaying)
                audioSource.volume = _naturalVolume;
            if (_lowPassFilter != null)
            {
                _lowPassFilter.cutoffFrequency   = 22000f;
                _lowPassFilter.lowpassResonanceQ = 1f;
            }
            if (_proxyLowPassFilter != null)
            {
                _proxyLowPassFilter.cutoffFrequency   = 22000f;
                _proxyLowPassFilter.lowpassResonanceQ = 1f;
            }
        }

        // ── 5. Doppler pitch (per-emitter, gated on dopplerEnabled) ──────────
        // Both velocity-based and distance-based contributions are computed
        // independently and multiplied together so each can be toggled separately.
        if (dopplerEnabled)
        {
            float combinedShift = 1f;

            // 5a. Velocity-based Doppler — closing rate of the listener↔emitter gap.
            //     Positive closingSpeed = distance shrinking = approaching = pitch up.
            //     Using the scalar closing rate (rather than a listener velocity vector)
            //     naturally captures movement of BOTH listener and emitter.
            if (dopplerPitchEnabled && _prevDistanceToListener >= 0f)
            {
                float closingSpeed = (_prevDistanceToListener - DistanceToListener)
                                     / Mathf.Max(Time.deltaTime, 0.0001f);
                float velocityShift = 1f + (closingSpeed / Mathf.Max(dopplerPitchSoundSpeed, 0.001f))
                                         * dopplerPitchStrength;
                combinedShift *= Mathf.Clamp(velocityShift, 0.5f, 2f);
            }

            // 5b. Distance-based pitch — higher when close, lower when far.
            if (distancePitchEnabled)
            {
                float minD = audioSource.minDistance;
                float maxD = audioSource.maxDistance;
                float t    = (maxD > minD)
                    ? Mathf.Clamp01((DistanceToListener - minD) / (maxD - minD))
                    : 0f;
                combinedShift *= Mathf.Lerp(pitchAtMinDistance, pitchAtMaxDistance, t);
            }

            float finalPitch = _naturalPitch * Mathf.Clamp(combinedShift, 0.5f, 2f);
            CurrentPitch = finalPitch;
            if (_proxySource != null)
                _proxySource.pitch = finalPitch;
            audioSource.pitch = finalPitch; // keep in sync even while muted via proxy
        }
        else
        {
            // Doppler disabled — restore natural pitch immediately
            CurrentPitch      = _naturalPitch;
            audioSource.pitch = _naturalPitch;
        }

        // Record distance for next frame's closing-speed calculation
        _prevDistanceToListener = DistanceToListener;
    }

    // ── Occlusion ─────────────────────────────────────────────────────────────

    // Lazy init: only add AudioLowPassFilter when occlusion is actually enabled.
    // Adding it in Awake (before AudioEmitter.Start assigns the clip) triggers
    // Unity's "Only custom filters can be played" warning on the first PlayOneShot.
    private void EnsureLowPassFilter()
    {
        if (_lowPassFilter != null) return;
        if (!TryGetComponent(out _lowPassFilter))
            _lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
        _lowPassFilter.cutoffFrequency = 22000f;
    }

    // Single ray from listener to emitter — counts unique walls in between.
    // Forward ray listener→emitter gives entry distances; reverse ray emitter→listener
    // gives exit distances. Thickness per wall = exitDistFromListener - entryDist.
    // Summed into _totalWallThickness for use as a continuous occlusion driver.
    private int CastWallRay(Vector3 from, Vector3 to)
    {
        Vector3 dir  = to - from;
        float   dist = dir.magnitude;
        if (dist < 0.001f) { _totalWallThickness = 0f; return 0; }
        Vector3 normDir = dir / dist;

        // ── Forward ray: listener → emitter (entry points) ───────────────────
        int rawFwd = Physics.RaycastNonAlloc(
            from, normDir, _wallHitBuffer, dist,
            occlusionLayerMask, QueryTriggerInteraction.Ignore);

        // Deduplicate forward hits
        int hitCount = 0;
        for (int i = 0; i < rawFwd; i++)
        {
            RaycastHit hit = _wallHitBuffer[i];
            if (IsInHierarchy(hit.transform, transform))           continue;
            if (IsInHierarchy(hit.transform, _listener.transform)) continue;
            bool dup = false;
            for (int j = 0; j < hitCount; j++)
                if (_wallHitBuffer[j].collider == hit.collider) { dup = true; break; }
            if (dup) continue;
            _wallHitBuffer[hitCount++] = hit;
        }

        // ── Reverse ray: emitter → listener (exit points) ────────────────────
        int rawBack = Physics.RaycastNonAlloc(
            to, -normDir, _reverseHitBuffer, dist,
            occlusionLayerMask, QueryTriggerInteraction.Ignore);

        // ── Compute thickness per wall ────────────────────────────────────────
        // Entry distance from listener = _wallHitBuffer[i].distance
        // Exit distance from listener  = dist - _reverseHitBuffer[j].distance
        // Thickness = exit - entry (clamped to 0 to handle near-zero cases)
        float totalThickness = 0f;
        for (int i = 0; i < hitCount; i++)
        {
            Collider c         = _wallHitBuffer[i].collider;
            float    entryDist = _wallHitBuffer[i].distance;
            float    exitDist  = dist; // fallback: treat exit at emitter position

            for (int j = 0; j < rawBack; j++)
            {
                if (_reverseHitBuffer[j].collider != c) continue;
                exitDist = dist - _reverseHitBuffer[j].distance;
                break;
            }

            totalThickness += Mathf.Max(0f, exitDist - entryDist);
        }

        _totalWallThickness = totalThickness;
        return hitCount;
    }

    // 10 probe rays outward from the emitter (8 horizontal 45° + up + down).
    // Returns IndoorRatio (0 = fully outdoor, 1 = fully indoor).
    // Does NOT affect volume — only used to modify filter character.
    private float ComputeEnvironmentProbe(Vector3 emitterPos)
    {
        int hitCount = 0;
        for (int i = 0; i < 10; i++)
        {
            int n = Physics.RaycastNonAlloc(
                emitterPos, ProbeDirs[i], _probeHitBuffer,
                occlusionProbeDistance, occlusionLayerMask, QueryTriggerInteraction.Ignore);

            bool    didHit   = false;
            Vector3 hitPoint = emitterPos + ProbeDirs[i] * occlusionProbeDistance;
            float   closest  = float.MaxValue;
            for (int j = 0; j < n; j++)
            {
                RaycastHit h = _probeHitBuffer[j];
                if (IsInHierarchy(h.transform, transform))           continue;
                if (_listener != null &&
                    IsInHierarchy(h.transform, _listener.transform)) continue;
                if (h.distance < closest)
                {
                    closest  = h.distance;
                    hitPoint = h.point;
                    didHit   = true;
                }
            }

            if (didHit) hitCount++;
#if UNITY_EDITOR
            _probeRayDidHit[i]    = didHit;
            _probeRayHitPoints[i] = hitPoint;
#endif
        }

        _probeHitCount = hitCount;
        return hitCount / 10f;
    }

    // Walk up the transform hierarchy — needed because the listener is often on a
    // camera child while the player's collider is on the root Rigidbody object.
    private static bool IsInHierarchy(Transform child, Transform ancestor)
    {
        for (Transform t = child; t != null; t = t.parent)
            if (t == ancestor) return true;
        return false;
    }

    // ── Doppler displacement ──────────────────────────────────────────────────

    private void InitializeProxy()
    {
        _proxyGO = new GameObject("_DopplerProxy");
        _proxyGO.transform.SetParent(transform, worldPositionStays: true);
        _proxyGO.AddComponent<DopplerProxy>();

        _proxySource                     = _proxyGO.AddComponent<AudioSource>();
        _proxySource.spatialBlend        = audioSource.spatialBlend;
        _proxySource.rolloffMode         = audioSource.rolloffMode;
        _proxySource.minDistance         = audioSource.minDistance;
        _proxySource.maxDistance         = audioSource.maxDistance;
        _proxySource.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
        _proxySource.reverbZoneMix       = audioSource.reverbZoneMix;
        _proxySource.playOnAwake         = false;
        _proxySource.loop                = audioSource.loop;

        _proxyTransform = _proxyGO.transform;

        _proxyLowPassFilter = _proxyGO.AddComponent<AudioLowPassFilter>();
        _proxyLowPassFilter.cutoffFrequency = 22000f;
    }

    private void UpdateVelocity()
    {
        _currentVelocity = _rb != null
            ? _rb.linearVelocity
            : (transform.position - _prevPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        _prevPosition = transform.position;
    }

    private void UpdateDopplerDisplacement()
    {
        float speed       = _currentVelocity.magnitude;
        float displaceAmt = Mathf.Clamp(
            speed * (DistanceToListener / Mathf.Max(soundSpeed, 0.001f)) * dopplerExaggeration,
            0f, maxDisplacementDistance);

        Vector3 displacedPos = speed > 0.01f
            ? transform.position - _currentVelocity.normalized * displaceAmt
            : transform.position;

        DisplacedAudioPosition   = displacedPos;
        DisplacementDistance     = displaceAmt;
        _proxyTransform.position = displacedPos;
    }

    private void SyncProxyPlayback()
    {
        bool isPlaying = audioSource.isPlaying;

        // Rising edge — mirror playback to proxy at displaced position
        if (isPlaying && !_proxyWasPlaying)
        {
            _proxySource.volume = audioSource.volume;
            _proxySource.pitch  = audioSource.pitch;
            _proxySource.loop   = audioSource.loop;

            if (audioSource.clip != null)
            {
                _proxySource.clip = audioSource.clip;
                _proxySource.Play();
            }
            else
            {
                AudioEmitter ae = GetComponent<AudioEmitter>();
                if (ae != null && AudioManager.Instance != null)
                {
                    AudioClip c = AudioManager.Instance.GetClip(ae.clipLabel);
                    if (c != null)
                        _proxySource.PlayOneShot(c);
                    else if (!_dopplerWarnedOnce)
                    {
                        Debug.LogWarning($"[DebugEmitter] Doppler: could not resolve clip '{ae.clipLabel}' — displacement skipped.", this);
                        _dopplerWarnedOnce = true;
                    }
                }
                else if (!_dopplerWarnedOnce)
                {
                    Debug.LogWarning("[DebugEmitter] Doppler: no AudioEmitter on this GameObject — displacement skipped for one-shot sources.", this);
                    _dopplerWarnedOnce = true;
                }
            }
        }

        // Falling edge — stop looping proxy if the main source stopped
        if (!isPlaying && _proxyWasPlaying && audioSource.loop)
            _proxySource.Stop();

        // Mute main source every frame while doppler is active so only proxy is heard
        audioSource.mute = true;
        _proxyWasPlaying = isPlaying;
    }

    // ── Volume rolloff (shared, static) ──────────────────────────────────────

    // baseVolume: pass _naturalVolume from LateUpdate to avoid the feedback loop;
    // omit (pass -1) from OnDrawGizmos where we just want a display estimate.
    private static float ComputeEffectiveVolume(AudioSource src, float distance, float baseVolume = -1f)
    {
        if (src == null) return 0f;

        float vol = baseVolume >= 0f ? baseVolume : src.volume;
        float attenuation;

        switch (src.rolloffMode)
        {
            case AudioRolloffMode.Logarithmic:
                attenuation = Mathf.Clamp01(src.minDistance / Mathf.Max(distance, 0.0001f));
                break;

            case AudioRolloffMode.Linear:
                float minD = src.minDistance;
                float maxD = src.maxDistance;
                if (maxD <= minD) { attenuation = 1f; break; }
                attenuation = Mathf.Clamp01(1f - (distance - minD) / (maxD - minD));
                break;

            case AudioRolloffMode.Custom:
                attenuation = 1f - Mathf.Clamp01(distance / Mathf.Max(src.maxDistance, 0.0001f));
                break;

            default:
                attenuation = 1f;
                break;
        }

        return Mathf.Clamp01(vol * attenuation);
    }

    // No-op: AudioManager still calls this on toggle; visualization is gizmo-only
    public void SetLineVisible(bool visible) { }

    // ── Editor gizmos ─────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        AudioSource src = GetComponent<AudioSource>();
        if (src == null) return;

        Vector3 emitterPos = transform.position;
        float   minDist    = src.minDistance;
        float   maxDist    = Mathf.Max(src.maxDistance, minDist + 0.1f);

        // Black bold label style — used for all text in this gizmo
        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            normal    = { textColor = Color.black },
            fontStyle = FontStyle.Bold
        };

        // Per-emitter unique color (Wang hash on instance ID)
        uint uid = (uint)Mathf.Abs(gameObject.GetInstanceID());
        uid ^= uid >> 16; uid *= 0x45d9f3bu; uid ^= uid >> 16;
        float hue          = (uid % 360u) / 360f;
        Color emitterColor = Color.HSVToRGB(hue, 0.9f, 1f);
        Color maxColor     = Color.HSVToRGB(hue, 0.4f, 0.9f);

        // ── Min / Max distance spheres (always visible — no debug gate) ────────
        // These appear as soon as the component is placed so designers can see range.
        Gizmos.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, 1f);
        Gizmos.DrawWireSphere(emitterPos, minDist);
        Handles.Label(emitterPos + Vector3.right * minDist, $"Min Range  {minDist:F1}m", labelStyle);

        Gizmos.color = new Color(maxColor.r, maxColor.g, maxColor.b, 0.45f);
        Gizmos.DrawWireSphere(emitterPos, maxDist);
        Handles.Label(emitterPos + Vector3.right * maxDist, $"Max Range  {maxDist:F1}m", labelStyle);

        // ── Pulsing sound-wave ring (always visible) ──────────────────────────
        if (Application.isPlaying)
        {
            // Rising edge
            if (src.isPlaying && !_wasPlayingLastFrame)
            {
                _playStartWallTime = Time.time;
                if (src.clip == null)
                {
                    AudioEmitter ae = GetComponent<AudioEmitter>();
                    if (ae != null && AudioManager.Instance != null)
                    {
                        AudioClip oneShotClip = AudioManager.Instance.GetClip(ae.clipLabel);
                        _playOneShotClipLength = oneShotClip != null
                            ? oneShotClip.length : _playOneShotClipLength;
                    }
                }
            }

            // Falling edge: measure actual PlayOneShot duration for next play
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
                if (src.clip != null)
                    _lastSrcTime = src.time;
            }

            _wasPlayingLastFrame = src.isPlaying;

            float timeSinceStop = _lastPlayingWallTime < 0f
                ? float.MaxValue : Time.time - _lastPlayingWallTime;
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
                    float elapsed = _playStartWallTime < 0f ? 0f
                                  : Time.time - _playStartWallTime;
                    if (_playOneShotClipLength > 0f)
                        progress = Mathf.Clamp01(elapsed / _playOneShotClipLength);
                    else
                    {
                        float travelDist = Mathf.Max(maxDist - minDist, 0.001f);
                        float r0 = Mathf.Clamp(minDist + waveSpeed * elapsed, minDist, maxDist);
                        progress = (r0 - minDist) / travelDist;
                    }
                }

                float ringRadius = Mathf.Lerp(minDist, maxDist, progress);
                float ringAlpha  = (1f - progress) * graceFade * 0.85f;
                if (ringAlpha > 0.01f)
                {
                    Gizmos.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, ringAlpha);
                    Gizmos.DrawWireSphere(emitterPos, ringRadius);
                }
            }
        }

        // ── Everything below requires debug mode on ───────────────────────────
        if (!AudioManager.DebugEnabled) return;

        // Resolve listener (runtime → edit-mode fallback)
        Vector3   listenerPos       = emitterPos;
        Transform listenerTransform = null;
        bool      hasListener       = false;

        if (AudioManager.Instance != null && AudioManager.Instance.Listener != null)
        {
            listenerTransform = AudioManager.Instance.Listener.transform;
            listenerPos       = listenerTransform.position;
            hasListener       = true;
        }
        else
        {
            AudioListener found = FindFirstObjectByType<AudioListener>();
            if (found != null)
            {
                listenerTransform = found.transform;
                listenerPos       = listenerTransform.position;
                hasListener       = true;
            }
        }

        float dist   = hasListener ? Vector3.Distance(emitterPos, listenerPos) : 0f;
        float effVol = ComputeEffectiveVolume(src, dist);

        // Audio position: displaced proxy pos when doppler active, else emitter pos
        Vector3 audioGizmoPos = (Application.isPlaying && dopplerEnabled && _proxyTransform != null)
            ? _proxyTransform.position : emitterPos;

        float lineThickness = Mathf.Lerp(minLineThickness, maxLineThickness, effVol);

        bool listenerInRange = hasListener && dist <= maxDist;

        // ── Listener-to-emitter wall ray ─────────────────────────────────────
        if (hasListener)
        {
            if (occlusionEnabled && Application.isPlaying && listenerInRange)
            {
                // Color by wall count: green = clear, orange = 1-2 walls, red = 3+
                Color wallColor = _wallHitCount == 0 ? Color.green
                                : _wallHitCount <= 2 ? new Color(1f, 0.55f, 0f)
                                :                      Color.red;
                Handles.color = wallColor;
                Handles.DrawAAPolyLine(lineThickness, listenerPos, audioGizmoPos);

                // Sphere at each wall hit point
                Gizmos.color = wallColor;
                for (int i = 0; i < _wallHitCount; i++)
                    Gizmos.DrawSphere(_wallHitBuffer[i].point, 0.15f);
            }
            else
            {
                Handles.color = new Color(0.72f, 0.22f, 1f, 1f);
                Handles.DrawAAPolyLine(lineThickness, emitterPos, listenerPos);
            }
        }

        // ── Environment probe rays (10 outward from emitter) ────────────────
        if (occlusionEnabled)
        {
            if (Application.isPlaying && listenerInRange)
            {
                for (int i = 0; i < 10; i++)
                {
                    bool    hit      = _probeRayDidHit[i];
                    Vector3 endpoint = _probeRayHitPoints[i];
                    Color   rayColor = hit
                        ? new Color(1f, 0.35f, 0.1f, 0.85f)
                        : new Color(0.2f, 1f, 0.25f, 0.55f);

                    Handles.color = rayColor;
                    Handles.DrawAAPolyLine(2f, emitterPos, endpoint);
                    if (hit)
                    {
                        Gizmos.color = rayColor;
                        Gizmos.DrawSphere(endpoint, 0.08f);
                    }
                }

                string envTag = _probeHitCount >= 7 ? "Indoors"
                              : _probeHitCount >= 4 ? "Partial"
                              :                       "Outdoors";
                string wallLabel = $"{_wallHitCount} wall{(_wallHitCount == 1 ? "" : "s")}";
                string labelText = $"Occ: {OcclusionFactor:F2} ({wallLabel})\n" +
                                   $"Status: {envTag} ({_probeHitCount}/10)";
                Handles.Label(audioGizmoPos + Vector3.up * 0.3f, labelText, labelStyle);
            }
            else if (!Application.isPlaying)
            {
                // Edit-mode preview: faint dotted probe directions + normal listener ray
                Handles.color = new Color(1f, 1f, 1f, 0.25f);
                foreach (Vector3 d in ProbeDirs)
                    Handles.DrawDottedLine(emitterPos, emitterPos + d * occlusionProbeDistance, 4f);
            }
        }

        // ── Doppler displacement arrow ────────────────────────────────────────
        if (Application.isPlaying && dopplerEnabled && DisplacementDistance > 0.01f)
        {
            float speedT  = Mathf.Clamp01(_currentVelocity.magnitude / 10f);
            Color spColor = Color.Lerp(Color.green, Color.red, speedT);

            Handles.color = spColor;
            Handles.DrawAAPolyLine(3f, emitterPos, DisplacedAudioPosition);

            Gizmos.color = spColor;
            Gizmos.DrawSphere(DisplacedAudioPosition, 0.2f);

            Handles.Label(
                (emitterPos + DisplacedAudioPosition) * 0.5f,
                $"Disp: {DisplacementDistance:F1}m  {_currentVelocity.magnitude:F1}m/s",
                labelStyle);
        }

        // ── Emitter label ─────────────────────────────────────────────────────
        Handles.Label(emitterPos + Vector3.up * (minDist + 0.25f),
            $"{gameObject.name}\nVol: {effVol:F2}", labelStyle);
    }
#endif
}