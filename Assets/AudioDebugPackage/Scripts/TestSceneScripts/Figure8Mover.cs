using UnityEngine;

public class Figure8Mover : MonoBehaviour
{
    [Header("Figure 8 Settings")]
    public float speed = 1f;
    public float width = 5f;
    public float height = 5f;

    private float t = 0f;
    
    private Vector3 origin;

    void Start()
    {
        origin = transform.position;
    }

    void Update()
    {
        t += Time.deltaTime * speed;

        float x = width * Mathf.Sin(t);
        float z = height * Mathf.Sin(t) * Mathf.Cos(t);

        transform.position = new Vector3(origin.x + x, origin.y, origin.z + z);
    }
}