using UnityEngine;

public class MinimapEmitterRing : MonoBehaviour
{
    [Header("Ring Settings")]
    public Color outOfRangeColor = Color.red;
    public float staticRingWidth = 0.5f;
    public float pulseRingWidth = 0.3f;
    public float staticRingOpacity = 1.0f;

    private LineRenderer pulseRing;
    private LineRenderer staticRing;
    private Material pulseMaterial;
    private Material staticMaterial;
    private const int segments = 64;

    private float maxRadius;
    private float currentRadius = 0f;
    private float expansionSpeed = 0f;
    private int remainingPulses = 0;
    private bool isPulsing = false;
    private const float secondsPerPulse = 1.0f;

    private AudioSource audioSource;
    private DebugEmitter debugEmitter;
    
    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        debugEmitter = GetComponent<DebugEmitter>();
        maxRadius = audioSource.maxDistance;

        pulseRing = CreateRing("PulseRing", pulseRingWidth);
        staticRing = CreateRing("StaticRing", staticRingWidth);

        pulseMaterial = CreateMaterial();
        staticMaterial = CreateMaterial();

        pulseRing.material = pulseMaterial;
        staticRing.material = staticMaterial;

        pulseRing.enabled = false;
        staticRing.enabled = false;
    }
    
    void Update()
    {
        if (debugEmitter == null || audioSource == null) return;

        float dy = AudioManager.Instance?.Listener != null
            ? AudioManager.Instance.Listener.transform.position.y - transform.position.y
            : 0f;

        float horizontalRadius = 0f;
        if (Mathf.Abs(dy) < maxRadius)
            horizontalRadius = Mathf.Sqrt(maxRadius * maxRadius - dy * dy);

        bool playerInRange = debugEmitter.DistanceToListener <= audioSource.maxDistance;
        Color emitterColor = DebugEmitter.GetEmitterColor(gameObject.GetInstanceID());
        Color pulseColor = playerInRange ? emitterColor : outOfRangeColor;

        if (isPulsing && horizontalRadius > 0f)
        {
            staticRing.enabled = true;
            if (staticMaterial != null) staticMaterial.color = new Color(emitterColor.r, emitterColor.g, emitterColor.b, staticRingOpacity);
            staticRing.startColor = new Color(emitterColor.r, emitterColor.g, emitterColor.b, staticRingOpacity);
            staticRing.endColor = new Color(emitterColor.r, emitterColor.g, emitterColor.b, staticRingOpacity);
            DrawRing(staticRing, horizontalRadius);

            pulseRing.enabled = true;
            if (pulseMaterial != null) pulseMaterial.color = pulseColor;
            pulseRing.startColor = pulseColor;
            pulseRing.endColor = pulseColor;

            float scaledRadius = (currentRadius / maxRadius) * horizontalRadius;
            currentRadius += expansionSpeed * Time.deltaTime;

            if (currentRadius >= maxRadius)
            {
                remainingPulses--;
                if (remainingPulses <= 0)
                {
                    isPulsing = false;
                    pulseRing.enabled = false;
                    staticRing.enabled = false;
                }
                else
                {
                    currentRadius = 0f;
                }
            }

            DrawRing(pulseRing, scaledRadius);
        }
        else
        {
            pulseRing.enabled = false;
            staticRing.enabled = false;
        }
    }

    public void TriggerPulse(float clipLength)
    {
        int pulseCount = Mathf.Max(1, Mathf.RoundToInt(clipLength / secondsPerPulse));
        expansionSpeed = maxRadius / secondsPerPulse;
        remainingPulses = pulseCount;
        currentRadius = 0f;
        isPulsing = true;
    }

    LineRenderer CreateRing(string name, float width)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(transform);
        obj.transform.localPosition = Vector3.zero;
        obj.layer = LayerMask.NameToLayer("Minimap");

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.positionCount = segments + 1;
        lr.useWorldSpace = true;
        lr.startWidth = width;
        lr.endWidth = width;
        return lr;
    }

    Material CreateMaterial()
    {
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpUnlit == null) return null;

        Material mat = new Material(urpUnlit);
        mat.SetFloat("_Surface", 1f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        return mat;
    }

    void DrawRing(LineRenderer lr, float radius)
    {
        float angleStep = 360f / segments;
        Vector3 center = transform.position;

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Deg2Rad * angleStep * i;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            lr.SetPosition(i, new Vector3(center.x + x, center.y + 0.1f, center.z + z));
        }
    }
}