using UnityEngine;

public class MinimapEmitterRing : MonoBehaviour
{
    [Header("Ring Settings")]
    public float expandSpeed = 5f;
    public float maxRadius = 1f;
    public Color inRangeColor = Color.blue;
    public Color outOfRangeColor = Color.red;

    private LineRenderer ring;
    private float currentRadius = 0f;
    private bool isPlaying = false;
    private AudioSource audioSource;
    private DebugEmitter debugEmitter;
    private Material ringMaterial;
    private const int segments = 64;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        debugEmitter = GetComponent<DebugEmitter>();

        GameObject ringObj = new GameObject("Ring");
        ringObj.transform.SetParent(transform);
        ringObj.transform.localPosition = Vector3.zero;
        ringObj.layer = LayerMask.NameToLayer("Minimap");

        ring = ringObj.AddComponent<LineRenderer>();
        ring.loop = true;
        ring.positionCount = segments + 1;
        ring.useWorldSpace = true;

        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpUnlit != null)
        {
            ringMaterial = new Material(urpUnlit);
            ringMaterial.SetFloat("_Surface", 1f);
            ringMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ringMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ringMaterial.SetInt("_ZWrite", 0);
            ringMaterial.renderQueue = 3000;
            ring.material = ringMaterial;
        }

        ring.startWidth = 0.3f;
        ring.endWidth = 0.3f;
        ring.enabled = false;
    }

    void Update()
    {
        if (audioSource == null || debugEmitter == null) return;

        bool soundPlaying = audioSource.isPlaying;
        bool inRange = debugEmitter.EffectiveVolume > 0f;

        if (soundPlaying && inRange)
        {
            isPlaying = true;
            ring.enabled = true;

            // Determine color
            bool playerInRange = debugEmitter.DistanceToListener <= audioSource.maxDistance;
            Color targetColor = playerInRange ? inRangeColor : outOfRangeColor;
            ringMaterial?.SetColor("_BaseColor", targetColor);
            ring.startColor = targetColor;
            ring.endColor = targetColor;

            // Expand ring
            currentRadius += expandSpeed * Time.deltaTime;
            if (currentRadius > maxRadius)
                currentRadius = 0f;

            DrawRing(currentRadius);
        }
        else
        {
            ring.enabled = false;
            currentRadius = 0f;
            isPlaying = false;
        }
    }

    void DrawRing(float radius)
    {
        float angleStep = 360f / segments;
        Vector3 center = transform.position;

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Deg2Rad * angleStep * i;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            ring.SetPosition(i, new Vector3(center.x + x, center.y + 0.1f, center.z + z));
        }
    }
}