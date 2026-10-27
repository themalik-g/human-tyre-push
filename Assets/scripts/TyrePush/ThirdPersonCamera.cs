using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Over-the-shoulder orbit camera. Put this directly on the Camera object.
/// Mouse / right stick look. Esc unlocks the cursor, a click locks it again.
/// </summary>
[DefaultExecutionOrder(100)]
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public TyreGameInput input;

    [Header("Framing")]
    public float distance = 4.2f;
    public float pivotHeight = 1.6f;
    public float shoulderOffset = 0.55f;
    public float startPitch = 14f;

    [Header("Look")]
    public float minPitch = -20f;
    public float maxPitch = 60f;
    [Tooltip("Degrees per mouse pixel.")]
    public float mouseSensitivity = 0.12f;
    [Tooltip("Degrees per second at full stick deflection.")]
    public float stickSensitivity = 150f;

    [Header("Collision")]
    public float collisionRadius = 0.25f;
    public bool lockCursor = true;

    /// <summary>Camera heading in degrees.</summary>
    public float Yaw { get; private set; }

    /// <summary>Camera pitch in degrees. Positive = looking down.</summary>
    public float Pitch { get; private set; }

    readonly RaycastHit[] hits = new RaycastHit[16];
    bool initialised;
    float shake;

    /// <summary>Small camera kick in degrees (hits, impacts). Decays by itself.</summary>
    public void AddShake(float degrees)
    {
        shake = Mathf.Min(shake + degrees, 2f);
    }

    public void Init(Transform followTarget)
    {
        target = followTarget;
        Yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
        Pitch = startPitch;
        initialised = true;
        SetCursor(lockCursor);
    }

    void Start()
    {
        if (!initialised && target != null) Init(target);
        if (input == null) input = FindFirstObjectByType<TyreGameInput>();
    }

    void LateUpdate()
    {
        if (target == null) return;
        if (!initialised) Init(target);

        HandleCursor();

        Vector2 look = Vector2.zero;
        if (input != null)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                look += input.MouseLook * mouseSensitivity;

            look += input.StickLook * (stickSensitivity * Time.deltaTime);
        }

        Yaw += look.x;
        Pitch = Mathf.Clamp(Pitch - look.y, minPitch, maxPitch);

        Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
        Quaternion yawRot = Quaternion.Euler(0f, Yaw, 0f);

        Vector3 pivot = target.position + Vector3.up * pivotHeight + yawRot * Vector3.right * shoulderOffset;
        Vector3 dir = rot * Vector3.back;

        float dist = distance;
        int n = Physics.SphereCastNonAlloc(pivot, collisionRadius, dir, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider col = hits[i].collider;
            if (col.transform.IsChildOf(target)) continue;

            Rigidbody rb = col.attachedRigidbody;
            if (rb != null && !rb.isKinematic) continue; // never zoom in because of the tyre

            if (hits[i].distance < dist) dist = hits[i].distance;
        }

        Quaternion finalRot = rot;
        if (shake > 0.002f)
        {
            float t = Time.time * 45f;
            float nx = Mathf.PerlinNoise(t, 0.3f) - 0.5f;
            float ny = Mathf.PerlinNoise(0.7f, t) - 0.5f;
            finalRot = rot * Quaternion.Euler(ny * 2f * shake, nx * 2f * shake, 0f);
            shake *= Mathf.Exp(-12f * Time.deltaTime);
        }
        else
        {
            shake = 0f;
        }

        transform.SetPositionAndRotation(pivot + dir * Mathf.Max(0.35f, dist), finalRot);
    }

    void HandleCursor()
    {
        Keyboard kb = Keyboard.current;
        Mouse ms = Mouse.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            SetCursor(false);
        else if (lockCursor && ms != null && ms.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            SetCursor(true);
    }

    static void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
