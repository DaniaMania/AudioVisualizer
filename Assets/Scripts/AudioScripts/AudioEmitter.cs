using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioEmitter : MonoBehaviour
{
    [Header("Sound")]
    public string clipLabel;
    public bool playOnStart = true;

    [Header("Playback")]
    public bool loop = false;
    public float minInterval = 2f;
    public float maxInterval = 5f;

    [Header("Randomization")]
    public float minVolume = 0.9f;
    public float maxVolume = 1f;
    public float minPitch = 0.9f;
    public float maxPitch = 1.1f;

    private AudioSource audioSource;
    private float timer;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = loop;
        audioSource.playOnAwake = false;

        if (loop)
        {
            AudioClip clip = AudioManager.Instance.GetClip(clipLabel);
            if (clip != null)
            {
                audioSource.clip = clip;
                ApplyRandomization();
                if (playOnStart)
                    audioSource.Play();
            }
        }
        else
        {
            timer = playOnStart ? 0f : Random.Range(minInterval, maxInterval);
        }
    }

    void Update()
    {
        if (loop) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Play();
            timer = Random.Range(minInterval, maxInterval);
        }
    }

    void Play()
    {
        AudioClip clip = AudioManager.Instance.GetClip(clipLabel);
        if (clip == null) return;

        ApplyRandomization();
        audioSource.PlayOneShot(clip);
    }

    void ApplyRandomization()
    {
        audioSource.volume = Random.Range(minVolume, maxVolume);
        audioSource.pitch = Random.Range(minPitch, maxPitch);
    }
}