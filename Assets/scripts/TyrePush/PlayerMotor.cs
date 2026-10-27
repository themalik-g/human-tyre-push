using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Character movement. Replaces ThirdPersonController.
/// Real gravity from Physics.gravity, jump height specified in metres,
/// acceleration based movement (no instant speed changes), slope following,
/// coyote time and jump buffering. Drives the Animator when one is present.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    [Header("References (auto-filled when empty)")]
    public TyreGameInput input;
    public ThirdPersonCamera cam;
    public Animator animator;

    [Header("Speed (m/s)")]
    public float walkSpeed = 3f;
    public float runSpeed = 6.2f;

    [Header("Acceleration (m/s^2)")]
    public float groundAcceleration = 28f;
    public float groundDeceleration = 34f;
    public float airAcceleration = 6f;

    [Header("Jump")]
    [Tooltip("Jump height in metres. Take-off speed = sqrt(2 g h).")]
    public float jumpHeight = 1f;
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.12f;

    [Header("Precision near the tyre")]
    [Tooltip("Sideways speed limit (m/s) while close to the tyre. One tap of A or D then moves about 5 to 9 cm.")]
    public float precisionLateralSpeed = 0.9f;
    public float precisionLateralAccel = 8f;
    public float precisionLateralDecel = 12f;

    [Header("Knockback (tyre hitting the player)")]
    public TyrePhysics tyre;
    [Tooltip("Bounciness of the hit. 0 = dead stop, 1 = perfect rebound.")]
    [Range(0f, 1f)] public float knockbackRestitution = 0.35f;
    [Tooltip("Tyre speed (m/s) below which it cannot knock the player back.")]
    public float knockbackMinTyreSpeed = 1f;
    [Tooltip("Closing speed (m/s) needed to count as a hit.")]
    public float knockbackMinClosing = 0.8f;
    [Tooltip("Largest speed change (m/s) a single hit may cause.")]
    public float knockbackMaxSpeed = 7f;

    [Header("Body")]
    [Tooltip("Used for the stick reaction force and body pushes.")]
    public float mass = 75f;
    [Tooltip("Maximum force with which the body can bump the tyre.")]
    public float bodyPushForce = 250f;
    public float turnSharpness = 12f;

    CharacterController cc;
    Vector3 horizontalVelocity;
    float verticalVelocity;
    float coyoteTimer;
    float jumpBufferTimer;
    Vector3 groundNormal = Vector3.up;
    bool hasFacing;
    Vector3 facing;
    float staggerTimer;
    float knockbackCooldown;
    bool precisionLateral;

    readonly HashSet<string> boolParams = new HashSet<string>();
    readonly RaycastHit[] probeHits = new RaycastHit[8];

    public Vector3 HorizontalVelocity { get { return horizontalVelocity; } }
    public bool Grounded { get; private set; }

    // ------------------------------------------------------------------
    void Awake()
    {
        cc = GetComponent<CharacterController>();

        if (input == null) input = GetComponent<TyreGameInput>();
        if (input == null) input = gameObject.AddComponent<TyreGameInput>();

        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        CacheAnimatorParameters();
    }

    void Start()
    {
        if (cam == null) cam = FindFirstObjectByType<ThirdPersonCamera>();
        if (tyre == null) tyre = FindFirstObjectByType<TyrePhysics>();
    }

    void CacheAnimatorParameters()
    {
        boolParams.Clear();
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Bool)
                boolParams.Add(p.name);
    }

    public void SetAnimatorBool(string name, bool value)
    {
        if (animator != null && boolParams.Contains(name))
            animator.SetBool(name, value);
    }

    // ------------------------------------------------------------------
    // API used by StickTool
    // ------------------------------------------------------------------
    public void SetFacing(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-4f) return;
        facing = direction.normalized;
        hasFacing = true;
    }

    public void ClearFacing() { hasFacing = false; }

    /// <summary>Near the tyre: sideways movement becomes small and gentle (forward movement is unchanged).</summary>
    public void SetPrecisionLateral(bool on) { precisionLateral = on; }

    /// <summary>Instant change of horizontal velocity (reaction force * dt / mass).</summary>
    public void ApplyVelocityChange(Vector3 deltaV)
    {
        horizontalVelocity += new Vector3(deltaV.x, 0f, deltaV.z);
    }

    public void Teleport(Vector3 position)
    {
        cc.enabled = false;
        transform.position = position;
        cc.enabled = true;
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        staggerTimer = 0f;
    }

    /// <summary>True while the player is thrown off stride by a hit.</summary>
    public bool Staggered { get { return staggerTimer > 0f; } }

    /// <summary>Knocks the player back and interrupts the stride for a moment.</summary>
    public void ApplyKnockback(Vector3 deltaV, float staggerSeconds)
    {
        deltaV.y = 0f;
        horizontalVelocity += deltaV;
        staggerTimer = Mathf.Max(staggerTimer, staggerSeconds);
    }

    // ------------------------------------------------------------------
    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || input == null) return;

        // --- ground state ------------------------------------------------
        Vector3 probeNormal;
        bool probe = ProbeGround(out probeNormal);
        Grounded = cc.isGrounded || probe;
        groundNormal = probe ? probeNormal : Vector3.up;

        if (Grounded) coyoteTimer = coyoteTime; else coyoteTimer -= dt;
        if (input.JumpPressed) jumpBufferTimer = jumpBufferTime; else jumpBufferTimer -= dt;

        // --- horizontal --------------------------------------------------
        Vector2 mv = input.Move;
        Vector3 camForward, camRight;

        if (cam != null)
        {
            Quaternion yaw = Quaternion.Euler(0f, cam.Yaw, 0f);
            camForward = yaw * Vector3.forward;
            camRight = yaw * Vector3.right;
        }
        else
        {
            Transform ct = Camera.main != null ? Camera.main.transform : transform;
            camForward = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized;
            camRight = Vector3.ProjectOnPlane(ct.right, Vector3.up).normalized;
        }

        Vector3 wish = camForward * mv.y + camRight * mv.x;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        if (staggerTimer > 0f) staggerTimer -= dt;
        bool staggered = staggerTimer > 0f;

        float topSpeed = (input.Run && !staggered) ? runSpeed : walkSpeed;
        Vector3 desired = wish * topSpeed;

        float rate;
        if (!Grounded) rate = airAcceleration;
        else rate = desired.sqrMagnitude > 0.0001f ? groundAcceleration : groundDeceleration;
        if (staggered) rate *= 0.2f; // stride interrupted: the legs cannot respond quickly

        if (precisionLateral && Grounded)
        {
            // Forward/back keeps the normal feel. Sideways is slow and gentle, so a tap of A or D
            // shifts the stick across the tyre in small, predictable steps.
            float vF = Vector3.Dot(horizontalVelocity, camForward);
            float vR = Vector3.Dot(horizontalVelocity, camRight);
            float dF = Vector3.Dot(desired, camForward);
            float dR = Mathf.Clamp(Vector3.Dot(desired, camRight), -precisionLateralSpeed, precisionLateralSpeed);

            vF = Mathf.MoveTowards(vF, dF, rate * dt);
            vR = Mathf.MoveTowards(vR, dR, (Mathf.Abs(dR) > 0.01f ? precisionLateralAccel : precisionLateralDecel) * dt);

            horizontalVelocity = camForward * vF + camRight * vR;
        }
        else
        {
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, rate * dt);
        }

        // --- vertical ----------------------------------------------------
        if (Grounded && verticalVelocity < 0f) verticalVelocity = -2f;

        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            Grounded = false;
        }

        verticalVelocity += Physics.gravity.y * dt;
        verticalVelocity = Mathf.Max(verticalVelocity, -50f);

        // --- move --------------------------------------------------------
        Vector3 move = horizontalVelocity;
        if (Grounded && verticalVelocity <= 0.1f)
            move = Vector3.ProjectOnPlane(horizontalVelocity, groundNormal); // follow the slope, keep speed

        move += Vector3.up * verticalVelocity;

        CollisionFlags flags = cc.Move(move * dt);
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;

        // --- facing ------------------------------------------------------
        Vector3 faceDir = hasFacing ? facing : horizontalVelocity;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.04f)
        {
            Quaternion target = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSharpness * dt));
        }

        // --- animator ----------------------------------------------------
        float speed = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
        bool moving = speed > 0.9f;
        SetAnimatorBool("run", moving);
        SetAnimatorBool("sprint", moving && input.Run && !staggered);
        SetAnimatorBool("air", !Grounded);
        SetAnimatorBool("crouch", false);
    }

    // The tyre can hit the player. If it comes at the body quickly (for example after bouncing
    // off a barrier) it exchanges momentum with the player: the player is knocked back and
    // stumbles, and the tyre loses the same impulse.
    void FixedUpdate()
    {
        if (knockbackCooldown > 0f) knockbackCooldown -= Time.fixedDeltaTime;
        if (tyre == null || tyre.Body == null || knockbackCooldown > 0f) return;
        if (tyre.Speed < knockbackMinTyreSpeed) return;

        Vector3 playerVel = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);

        for (int i = 0; i < 3; i++)
        {
            Vector3 sample = transform.position + Vector3.up * (0.3f + 0.6f * i); // feet, waist, chest
            Vector3 n;
            float sd = tyre.SignedDistance(sample, out n); // n points from the tyre towards the player
            if (sd > cc.radius) continue;

            Vector3 contact = sample - n * Mathf.Max(0f, sd);
            Vector3 relative = tyre.Body.GetPointVelocity(contact) - playerVel;
            float closing = Vector3.Dot(relative, n); // > 0: the tyre is moving into the player
            if (closing < knockbackMinClosing) continue;

            float tyreMass = tyre.Body.mass;
            float impulse = (1f + knockbackRestitution) * closing / (1f / tyreMass + 1f / mass);
            Vector3 deltaV = n * (impulse / mass);
            if (deltaV.magnitude > knockbackMaxSpeed) deltaV = deltaV.normalized * knockbackMaxSpeed;

            ApplyKnockback(deltaV, Mathf.Clamp(0.2f + closing * 0.06f, 0.2f, 0.7f));
            tyre.Body.AddForceAtPosition(-n * impulse, contact, ForceMode.Impulse);

            knockbackCooldown = 0.3f;
            break;
        }
    }

    bool ProbeGround(out Vector3 normal)
    {
        normal = Vector3.up;

        float radius = cc.radius * 0.95f;
        Vector3 origin = transform.position + Vector3.up * (cc.radius + 0.1f);

        int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, probeHits,
            0.15f + cc.skinWidth + 0.2f, ~0, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = probeHits[i];
            if (h.collider.transform.IsChildOf(transform)) continue;

            Rigidbody rb = h.collider.attachedRigidbody;
            if (rb != null && !rb.isKinematic) continue; // the tyre is not ground

            if (h.distance > 0f && h.distance < best && h.normal.y > 0.3f)
            {
                best = h.distance;
                normal = h.normal;
                found = true;
            }
        }

        return found;
    }

    // ------------------------------------------------------------------
    // Walking into the tyre bumps it with a force-limited push
    // ------------------------------------------------------------------
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody rb = hit.collider.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;
        if (hit.moveDirection.y < -0.3f) return;

        Vector3 dir = hit.moveDirection;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();

        Vector3 rbHoriz = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float closing = Vector3.Dot(horizontalVelocity - rbHoriz, dir);
        if (closing <= 0f) return;

        float force = Mathf.Min(bodyPushForce, mass * closing * 4f);
        rb.AddForceAtPosition(dir * (force * Time.deltaTime), hit.point, ForceMode.Impulse);
    }
}
