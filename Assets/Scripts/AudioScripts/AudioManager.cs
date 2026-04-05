using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct SoundEntry
{
    [Tooltip("Unique name used to look up this clip at runtime (e.g. \"Ambient Hum\")")]
    public string label;
    public AudioClip clip;
}

[DefaultExecutionOrder(-100)]
public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;
    public static AudioManager Instance => _instance;

    [SerializeField] private bool debugEnabled = false;
    public static bool DebugEnabled => _instance != null && _instance.debugEnabled;

    private readonly List<DebugEmitter>      activeEmitters = new List<DebugEmitter>();
    public IReadOnlyList<DebugEmitter>      ActiveEmitters => activeEmitters;

    private readonly List<AmbientZoneSpline> activeZones    = new List<AmbientZoneSpline>();
    public IReadOnlyList<AmbientZoneSpline>  ActiveZones    => activeZones;

    private AudioListener listener;
    public AudioListener Listener => listener;

    [Header("Doppler Pitch (Global)")]
    [Tooltip("Apply a Doppler pitch effect to all DebugEmitters — pitch rises as the listener " +
             "moves toward a source and drops when moving away.")]
    [SerializeField] private bool dopplerPitchEnabled = true;
    [Tooltip("Pitch-shift strength multiplier. 1 = physically-scaled by pitchSoundSpeed; " +
             ">1 = exaggerated. 0 = no shift.")]
    [SerializeField][Range(0f, 3f)] private float dopplerPitchStrength = 1f;
    [Tooltip("Virtual speed of sound used for pitch Doppler (m/s). 343 = realistic but barely " +
             "perceptible at walking speeds. 30 gives a noticeable ~7-10% shift at a brisk walk.")]
    [SerializeField] private float pitchSoundSpeed = 30f;

    // Listener velocity — smoothed each Update, consumed by DebugEmitter.LateUpdate
    private Vector3 _prevListenerPos;
    public  Vector3 ListenerVelocity { get; private set; }

    public static bool  DopplerPitchEnabled => _instance != null && _instance.dopplerPitchEnabled;
    public static float DopplerPitchStrength => _instance != null ? _instance.dopplerPitchStrength : 1f;
    public static float PitchSoundSpeed     => _instance != null
                                               ? Mathf.Max(_instance.pitchSoundSpeed, 0.001f) : 30f;

    [Header("Sound Library")]
    [SerializeField] private List<SoundEntry> soundLibrary = new List<SoundEntry>();
    public IReadOnlyList<SoundEntry> SoundLibrary => soundLibrary;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        listener = FindFirstObjectByType<AudioListener>();
        if (listener != null)
            _prevListenerPos = listener.transform.position;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Update()
    {
        if (listener == null)
        {
            listener = FindFirstObjectByType<AudioListener>();
            if (listener != null)
                _prevListenerPos = listener.transform.position;
        }

        if (listener != null)
        {
            Vector3 rawVelocity = (listener.transform.position - _prevListenerPos)
                                  / Mathf.Max(Time.deltaTime, 0.0001f);
            // Smooth to suppress single-frame jitter (10 Hz smoothing)
            ListenerVelocity = Vector3.Lerp(ListenerVelocity, rawVelocity, 10f * Time.deltaTime);
            _prevListenerPos = listener.transform.position;
        }
        else
        {
            ListenerVelocity = Vector3.zero;
        }
    }

    public void Register(DebugEmitter emitter)
    {
        if (emitter != null && !activeEmitters.Contains(emitter))
            activeEmitters.Add(emitter);
    }

    public void Unregister(DebugEmitter emitter)
    {
        activeEmitters.Remove(emitter);
    }

    public void RegisterZone(AmbientZoneSpline zone)
    {
        if (zone != null && !activeZones.Contains(zone))
            activeZones.Add(zone);
    }

    public void UnregisterZone(AmbientZoneSpline zone)
    {
        activeZones.Remove(zone);
    }

    public AudioClip GetClip(string label)
    {
        foreach (SoundEntry entry in soundLibrary)
        {
            if (entry.label == label)
                return entry.clip;
        }
        Debug.LogWarning($"[AudioManager] No clip found with label \"{label}\"", this);
        return null;
    }

    public void ToggleDebug()
    {
        debugEnabled = !debugEnabled;

        if (!debugEnabled)
        {
            foreach (DebugEmitter emitter in activeEmitters)
            {
                if (emitter != null)
                    emitter.SetLineVisible(false);
            }
        }
    }
}
