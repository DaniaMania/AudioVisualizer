using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(AudioSource))]
public class DebugEmitter : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private Gradient volumeGradient;
    [SerializeField] private float minWidth = 0.01f;
    [SerializeField] private float maxWidth = 0.15f;

    private AudioSource audioSource;
    private LineRenderer lineRenderer;
    private Material lineMaterial;
    private bool registrationPending = false;

    public float EffectiveVolume { get; private set; }
    public float DistanceToListener { get; private set; }

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.allowOcclusionWhenDynamic = false;

        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpUnlit != null)
        {
            Material mat = new Material(urpUnlit);
            mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            lineRenderer.material = mat;
            lineMaterial = mat;
        }
        else
        {
            Debug.LogWarning("[DebugEmitter] URP Unlit shader not found. LineRenderer will render with default material.", this);
        }

        if (volumeGradient == null || volumeGradient.colorKeys.Length == 0)
            InitializeDefaultGradient();

        lineRenderer.enabled = false;
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

        if (lineRenderer != null)
            lineRenderer.enabled = false;
    }

    private void LateUpdate()
    {
        if (!AudioManager.DebugEnabled)
        {
            if (lineRenderer.enabled) lineRenderer.enabled = false;
            return;
        }

        AudioListener listener = AudioManager.Instance?.Listener;
        if (listener == null)
        {
            if (lineRenderer.enabled) lineRenderer.enabled = false;
            return;
        }

        Vector3 emitterPos  = transform.position;
        Vector3 listenerPos = listener.transform.position;

        DistanceToListener = Vector3.Distance(emitterPos, listenerPos);
        EffectiveVolume    = ComputeEffectiveVolume(DistanceToListener);

        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, emitterPos);
        lineRenderer.SetPosition(1, listenerPos);

        Color lineColor = volumeGradient.Evaluate(EffectiveVolume);
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor   = lineColor;
        lineMaterial?.SetColor("_BaseColor", lineColor);

        float lineWidth = Mathf.Lerp(minWidth, maxWidth, EffectiveVolume);
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth   = lineWidth;
    }

    private float ComputeEffectiveVolume(float distance)
    {
        float baseVolume  = audioSource.volume;
        float attenuation;

        switch (audioSource.rolloffMode)
        {
            case AudioRolloffMode.Logarithmic:
                attenuation = Mathf.Clamp01(audioSource.minDistance / Mathf.Max(distance, 0.0001f));
                break;

            case AudioRolloffMode.Linear:
                float minDist = audioSource.minDistance;
                float maxDist = audioSource.maxDistance;
                if (maxDist <= minDist)
                {
                    attenuation = 1f;
                    break;
                }
                attenuation = Mathf.Clamp01(1f - (distance - minDist) / (maxDist - minDist));
                break;

            case AudioRolloffMode.Custom:
                attenuation = 1f - Mathf.Clamp01(distance / Mathf.Max(audioSource.maxDistance, 0.0001f));
                break;

            default:
                attenuation = 1f;
                break;
        }

        return Mathf.Clamp01(baseVolume * attenuation);
    }

    public void SetLineVisible(bool visible)
    {
        if (lineRenderer != null)
            lineRenderer.enabled = visible;
    }
}
