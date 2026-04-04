using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MinimapController : MonoBehaviour
{
    [Header("References")]
    public Camera minimapCamera;
    public Transform player;

    [Header("Zoom")]
    public float defaultZoom = 30f;
    public float minZoom = 10f;
    public float maxZoom = 100f;
    public float zoomStep = 5f;

    [Header("Toggle Key")]
    public Key toggleOverviewKey = Key.M;

    private bool isOverview = false;
    private Vector3 overviewPosition;
    
    public bool IsOverview => isOverview;
    public Key ToggleOverviewKey => toggleOverviewKey;

    void Start()
    {
        minimapCamera.orthographicSize = defaultZoom;
        overviewPosition = new Vector3(0, 100, 0);
    }

    void Update()
    {
        if (!isOverview)
        {
            Vector3 pos = player.position;
            pos.y = minimapCamera.transform.position.y;
            minimapCamera.transform.position = pos;
        }

        if (Keyboard.current[toggleOverviewKey].wasPressedThisFrame)
            ToggleOverview();

        if (Keyboard.current.equalsKey.wasPressedThisFrame)
            ZoomIn();

        if (Keyboard.current.minusKey.wasPressedThisFrame)
            ZoomOut();
    }

    public void ZoomIn()
    {
        minimapCamera.orthographicSize = Mathf.Max(minimapCamera.orthographicSize - zoomStep, minZoom);
    }

    public void ZoomOut()
    {
        minimapCamera.orthographicSize = Mathf.Min(minimapCamera.orthographicSize + zoomStep, maxZoom);
    }

    public void ToggleOverview()
    {
        isOverview = !isOverview;

        if (isOverview)
        {
            minimapCamera.transform.SetParent(null);
            minimapCamera.transform.position = overviewPosition;
            minimapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
            minimapCamera.orthographicSize = CalculateOverviewZoom();
        }
        else
        {
            minimapCamera.transform.SetParent(player);
            minimapCamera.transform.localPosition = new Vector3(0, 50, 0);
            minimapCamera.transform.localRotation = Quaternion.Euler(90, 0, 0);
            minimapCamera.orthographicSize = defaultZoom;
        }
    }

    private float CalculateOverviewZoom()
    {
        if (AudioManager.Instance == null || AudioManager.Instance.ActiveEmitters.Count == 0)
            return maxZoom;

        float furthest = 0f;
        foreach (DebugEmitter emitter in AudioManager.Instance.ActiveEmitters)
        {
            if (emitter == null) continue;
            float dist = Vector2.Distance(
                new Vector2(overviewPosition.x, overviewPosition.z),
                new Vector2(emitter.transform.position.x, emitter.transform.position.z)
            );
            if (dist > furthest) furthest = dist;
        }

        return furthest + 10f; 
    }
}