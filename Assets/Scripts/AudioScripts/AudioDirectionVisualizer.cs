using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class AudioDirectionVisualizer : MonoBehaviour
{
    [Header("Ring Settings")]
    public float ringRadius = 150f;
    public float arcLength = 30f;
    public float arcThickness = 8f;

    [Header("Arrow Settings")]
    public float arrowSize = 10f;
    
    [Header("Fade")]
    public float fadeOutDuration = 0.5f;

    private Material glMaterial;

    private class EmitterState
    {
        public DebugEmitter emitter;
        public float alpha = 0f;
        public float fadeTimer = 0f;
        public bool wasPlaying = false;
        public float[] samples = new float[64];
        public float smoothedAmplitude = 0f;
    }

    private List<EmitterState> states = new List<EmitterState>();
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        glMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
        glMaterial.hideFlags = HideFlags.HideAndDontSave;
        glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glMaterial.SetInt("_Cull", 0);
        glMaterial.SetInt("_ZWrite", 0);
    }

    void Update()
    {
        if (AudioManager.Instance == null) return;

        var activeEmitters = AudioManager.Instance.ActiveEmitters;
        if (activeEmitters == null) return;

        foreach (DebugEmitter e in activeEmitters)
        {
            if (e == null || e.excludeFromUI) continue;
            if (e.GetComponent<AmbientZoneSpline>() != null) continue; // zones have their own gizmo
            if (!states.Exists(s => s.emitter == e))
                states.Add(new EmitterState { emitter = e });
        }

        states.RemoveAll(s => s.emitter == null || !activeEmitters.Contains(s.emitter));

        foreach (EmitterState state in states)
        {
            if (state.emitter == null || state.emitter.excludeFromUI) continue;
            if (state.emitter.GetComponent<AmbientZoneSpline>() != null) continue;

            AudioSource src = state.emitter.GetComponent<AudioSource>();
            if (src == null) continue;

            float dist = state.emitter.DistanceToListener;
            float distanceAlpha = Mathf.Clamp01(1f - (dist / src.maxDistance));

            src.GetOutputData(state.samples, 0);
            float rms = 0f;
            foreach (float s in state.samples)
                rms += s * s;
            rms = Mathf.Sqrt(rms / state.samples.Length);

            float amplitude = Mathf.Clamp01(rms * 200f);

            state.smoothedAmplitude = amplitude > state.smoothedAmplitude
                ? Mathf.Lerp(state.smoothedAmplitude, amplitude, 0.8f)
                : Mathf.Lerp(state.smoothedAmplitude, amplitude, Time.deltaTime / fadeOutDuration);

            bool inRange = state.emitter.DistanceToListener <= src.maxDistance;

            if (src.isPlaying && state.smoothedAmplitude > 0.001f && inRange)
            {
                state.fadeTimer = 1f;
                state.alpha = state.smoothedAmplitude;
            }
            else
            {
                state.fadeTimer = Mathf.MoveTowards(state.fadeTimer, 0f, Time.deltaTime / fadeOutDuration);
                state.alpha = inRange ? state.fadeTimer : 0f;
            }

            state.wasPlaying = src.isPlaying;
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!AudioManager.DebugEnabled) return;
        if (AudioManager.Instance == null) return;

        glMaterial.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix();

        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        foreach (EmitterState state in states)
        {
            if (state.alpha <= 0f) continue;
            if (state.emitter == null || state.emitter.excludeFromUI) continue;
            if (state.emitter.GetComponent<AmbientZoneSpline>() != null) continue;

            AudioSource src = state.emitter.GetComponent<AudioSource>();
            if (src == null) continue;

            Transform listener = AudioManager.Instance.Listener?.transform;
            if (listener == null) continue;

            Vector3 toEmitter = state.emitter.transform.position - listener.position;
            Vector3 localDir = listener.InverseTransformDirection(toEmitter);
            float angle = Mathf.Atan2(localDir.x, -localDir.z) * Mathf.Rad2Deg;

            Color c = DebugEmitter.GetEmitterColor(state.emitter.gameObject.GetInstanceID());
            c.a = state.alpha;

            DrawCrescent(center, angle, c);
            DrawArrowGL(center, angle, c);
        }

        GL.PopMatrix();
    }

    void DrawCrescent(Vector2 center, float angleDeg, Color color)
    {
        int steps = 30;
        float halfArc = arcLength * 0.5f;
        float innerRadius = ringRadius - arcThickness * 0.5f;
        float outerRadius = ringRadius + arcThickness * 0.5f;

        GL.Begin(GL.TRIANGLES);
        GL.Color(color);

        for (int i = 0; i < steps; i++)
        {
            float a0 = (angleDeg - halfArc + (arcLength / steps) * i) * Mathf.Deg2Rad;
            float a1 = (angleDeg - halfArc + (arcLength / steps) * (i + 1)) * Mathf.Deg2Rad;

            Vector2 outerP0 = center + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * outerRadius;
            Vector2 outerP1 = center + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * outerRadius;
            Vector2 innerP0 = center + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * innerRadius;
            Vector2 innerP1 = center + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * innerRadius;

            GL.Vertex3(outerP0.x, outerP0.y, 0);
            GL.Vertex3(outerP1.x, outerP1.y, 0);
            GL.Vertex3(innerP0.x, innerP0.y, 0);

            GL.Vertex3(outerP1.x, outerP1.y, 0);
            GL.Vertex3(innerP1.x, innerP1.y, 0);
            GL.Vertex3(innerP0.x, innerP0.y, 0);
        }

        GL.End();
    }

    void DrawArrowGL(Vector2 center, float angleDeg, Color color)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        Vector2 perp = new Vector2(-dir.y, dir.x);

        Vector2 tip = center + dir * (ringRadius + arcThickness * 0.5f + arrowSize);
        Vector2 baseLeft = center + dir * (ringRadius + arcThickness * 0.5f) + perp * arrowSize * 0.5f;
        Vector2 baseRight = center + dir * (ringRadius + arcThickness * 0.5f) - perp * arrowSize * 0.5f;

        GL.Begin(GL.TRIANGLES);
        GL.Color(color);
        GL.Vertex3(tip.x, tip.y, 0);
        GL.Vertex3(baseLeft.x, baseLeft.y, 0);
        GL.Vertex3(baseRight.x, baseRight.y, 0);
        GL.End();
    }
}