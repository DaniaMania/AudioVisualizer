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

    private readonly List<DebugEmitter> activeEmitters = new List<DebugEmitter>();
    public IReadOnlyList<DebugEmitter> ActiveEmitters => activeEmitters;

    private AudioListener listener;
    public AudioListener Listener => listener;

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
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Update()
    {
        if (listener == null)
            listener = FindFirstObjectByType<AudioListener>();
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
