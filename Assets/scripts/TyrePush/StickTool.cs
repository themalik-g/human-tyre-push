using UnityEngine;

/// <summary>
/// The stick. Every force it applies is a real force at a real contact point on the tyre, so a hit
/// gives the Rigidbody BOTH a push and a torque (torque = contact offset x force):
///
///   Hit high on the tread  -> the tyre rolls forward.
///   Hit low                -> it skids more than it rolls.
///   Hit slightly off the centre line -> a small yaw/lean, a small deflection from the straight path.
///   Hit far off the centre line (shoulder, sidewall) -> a large deflection.
///
/// Where the stick lands is where you aim: the camera direction, helped by a partial aim assist
/// towards the tyre. The stick pushes along its own axis, limited by the friction cone, so the
/// angle you approach at matters.
///
///  PUSH  (LMB / RT held)  the tip follows your hand speed. The tyre ends up rolling at your pace.
///  POKE  (LMB / RT tap)   a fast stroke. Each stroke is an impulse; speed comes from the rhythm of strokes.
///  BRACE (RMB / LT held)  presses the upper tread. Progressive friction brake + limited anti-lean reflex.
///
/// Forces are slew-limited (jerk-limited) like a human arm, so a start from rest is a firm kick
/// that builds in a few hundredths of a second, never an instant robotic step.
/// </summary>
public class StickTool : MonoBehaviour
{
    public enum Mode { Idle, Poke, Push, Brace }

    [Header("References (auto-filled when empty)")]
    public TyreGameInput input;
    public PlayerMotor player;
    public TyrePhysics tyre;
    public ThirdPersonCamera cam;

    [Header("Stick")]
    [Tooltip("Stick length in metres.")]
    public float length = 1.5f;
    public GameObject stickModel;
    public Material stickMaterial;
    [Tooltip("Tick if the stick model is held by the wrong end.")]
    public bool flipModel = false;
    [Tooltip("Right hand position relative to the player (right, up, forward).")]
    public Vector3 gripLocalOffset = new Vector3(0.28f, 1.05f, 0.30f);

    [Header("Aiming")]
    [Range(0f, 1f)]
    [Tooltip("0 = the stick goes exactly where the camera points (hard). 1 = it always finds the tyre centre (no steering).")]
    public float aimAssist = 0.65f;

    [Header("Forces (Newtons)")]
    [Tooltip("Highest steady push a person can hold with a stick.")]
    public float holdForce = 150f;
    [Tooltip("Highest force during a poke stroke.")]
    public float pokeForce = 320f;
    [Tooltip("Pressing force while bracing.")]
    public float braceForce = 100f;
    [Tooltip("Servo gain in N per (m/s) of closing speed.")]
    public float servoGain = 220f;
    [Tooltip("How much faster than the hand the stick tries to move the tyre at full push (m/s).")]
    public float pushOverspeed = 0.6f;

    [Header("Start from rest (the initial jerk)")]
    [Tooltip("Tyre speed (m/s) below which a new contact counts as a start from rest.")]
    public float restSpeed = 0.3f;
    [Tooltip("Extra force added at the very start, decaying away. This is the breakaway kick.")]
    public float launchSurge = 120f;
    [Tooltip("How fast the surge decays (s).")]
    public float launchSurgeTime = 0.12f;

    [Header("Force build-up (N per second, jerk limit)")]
    public float pushRiseRate = 1800f;
    public float pokeRiseRate = 9000f;
    [Tooltip("Lower = a more progressive, controllable brake.")]
    public float braceRiseRate = 600f;
    public float releaseRate = 8000f;

    [Header("Poke stroke")]
    public float pokeDuration = 0.28f;
    [Tooltip("How far the hand thrusts forward (m). Peak tip speed = reach * pi / duration.")]
    public float pokeReach = 0.38f;
    public float pokeCooldown = 0.08f;

    [Header("Contact")]
    public float contactRadius = 0.02f;
    [Tooltip("Stick on rubber friction coefficient (push / poke).")]
    public float contactFriction = 0.55f;
    [Tooltip("Stick on rubber friction coefficient while braking (a firm press grips better).")]
    public float braceFriction = 0.6f;
    public float frictionStiffness = 60f;

    [Header("Brace reflex (friction-limited)")]
    public bool braceAssist = true;
    [Tooltip("N per radian of lean.")]
    public float braceKp = 220f;
    [Tooltip("N per (rad/s) of lean rate.")]
    public float braceKd = 22f;

    // ------------------------------------------------------------------
    // Public state (HUD / IK / debugging)
    // ------------------------------------------------------------------
    public Mode CurrentMode { get; private set; }
    public Vector3 GripWorld { get; private set; }
    public Vector3 Direction { get { return dirCurrent; } }
    public bool InContact { get; private set; }
    public float ContactForce { get; private set; }
    public float ContactSlip { get; private set; }
    public Vector3 LastContactPoint { get; private set; }
    public Vector3 LastContactNormal { get; private set; }

    /// <summary>True when the tyre is close enough, and in front, so a stroke can reach it.</summary>
    public bool InRange { get; private set; }

    /// <summary>Signed distance (m) of the last contact from the tyre's centre line, across the tread.</summary>
    public float ContactOffCentre { get; private set; }

    /// <summary>Raised when the stick touches the tyre: point, surface normal, intended force (N).</summary>
    public event System.Action<Vector3, Vector3, float> ContactStarted;

    Transform stickRoot;
    Vector3 dirCurrent = Vector3.forward;
    float ext;
    float extRate;
    float pokeTimer;
    float cooldown;
    bool pushWasDown;
    float contactDistance = -1f;
    float braceLeanPrev;
    bool braceLeanPrevValid;

    float appliedForce;      // slew-limited normal force
    float surgeTime;
    bool startedFromRest;

    // ------------------------------------------------------------------
    void Awake()
    {
        if (player == null) player = GetComponent<PlayerMotor>();
        if (input == null) input = GetComponent<TyreGameInput>();

        pokeTimer = pokeDuration;
        dirCurrent = transform.forward;
        BuildVisual();
    }

    void Start()
    {
        if (tyre == null) tyre = FindFirstObjectByType<TyrePhysics>();
        if (cam == null) cam = FindFirstObjectByType<ThirdPersonCamera>();
    }

    void OnDestroy()
    {
        if (stickRoot != null) Destroy(stickRoot.gameObject);
    }

    void OnDisable()
    {
        if (player != null)
        {
            player.ClearFacing();
            player.SetAnimatorBool("hitTyre", false);
        }
    }

    // ------------------------------------------------------------------
    // Visual
    // ------------------------------------------------------------------
    void BuildVisual()
    {
        stickRoot = new GameObject("Stick (visual)").transform;

        if (stickModel != null)
        {
            GameObject m = Instantiate(stickModel, stickRoot);
            m.name = "StickModel";
            TyreUtil.StripColliders(m);
            FitStick(m.transform);
            TyreUtil.ApplyMaterial(m, stickMaterial);
        }
        else
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "StickMesh";
            Collider c = cyl.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Destroy(c); }

            cyl.transform.SetParent(stickRoot, false);
            cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cyl.transform.localScale = new Vector3(0.035f, length * 0.5f, 0.035f);
            cyl.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);

            Material mat = stickMaterial != null ? stickMaterial : TyreUtil.MakeMaterial(new Color(0.45f, 0.28f, 0.13f), 0.15f);
            cyl.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    // Aligns the model's longest axis to +Z, scales it to `length`, grip end at the origin.
    void FitStick(Transform model)
    {
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one;

        Bounds b;
        if (!TyreUtil.GetLocalBounds(stickRoot, out b)) return;

        Vector3 size = b.size;
        int axis = 0;
        if (size.y > size[axis]) axis = 1;
        if (size.z > size[axis]) axis = 2;

        if (axis != 2)
        {
            Quaternion q = Quaternion.FromToRotation(axis == 0 ? Vector3.right : Vector3.up, Vector3.forward);
            model.localRotation = q;
        }

        if (flipModel)
            model.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.localRotation;

        TyreUtil.GetLocalBounds(stickRoot, out b);
        float k = b.size.z > 1e-4f ? length / b.size.z : 1f;
        model.localScale = Vector3.one * k;
        model.localPosition = Vector3.zero;

        TyreUtil.GetLocalBounds(stickRoot, out b);
        model.localPosition = new Vector3(-b.center.x, -b.center.y, -b.min.z);
    }

    // ------------------------------------------------------------------
    // Frame update: input, mode, aim
    // ------------------------------------------------------------------
    void Update()
    {
        if (player == null || input == null) return;
        if (tyre == null) tyre = FindFirstObjectByType<TyrePhysics>();

        float dt = Time.deltaTime;
        float pushValue = input.Push;
        float braceValue = input.Brace;

        bool pushDown = pushValue > 0.5f && !pushWasDown;
        pushWasDown = pushValue > 0.5f;
        cooldown -= dt;

        // --- mode --------------------------------------------------------
        if (braceValue > 0.3f)
        {
            pokeTimer = pokeDuration;
            CurrentMode = Mode.Brace;
        }
        else if (pokeTimer < pokeDuration)
        {
            pokeTimer += dt;
            CurrentMode = Mode.Poke;
            if (pokeTimer >= pokeDuration) cooldown = pokeCooldown;
        }
        else if (pushDown && cooldown <= 0f)
        {
            pokeTimer = 0f;
            CurrentMode = Mode.Poke;
        }
        else if (pushValue > 0.1f)
        {
            CurrentMode = Mode.Push;
        }
        else
        {
            CurrentMode = Mode.Idle;
        }

        bool engaged = CurrentMode != Mode.Idle;

        // --- poke stroke: e(t) = E sin(pi t / T), tip speed = e'(t) -------------
        if (CurrentMode == Mode.Poke)
        {
            float ph = Mathf.Clamp01(pokeTimer / pokeDuration);
            ext = pokeReach * Mathf.Sin(Mathf.PI * ph);
            extRate = pokeReach * Mathf.PI / pokeDuration * Mathf.Cos(Mathf.PI * ph);
        }
        else
        {
            ext = Mathf.Lerp(ext, 0f, 1f - Mathf.Exp(-20f * dt));
            extRate = 0f;
        }

        // --- aim ---------------------------------------------------------
        Transform pt = player.transform;
        Vector3 grip = pt.TransformPoint(gripLocalOffset);
        Vector3 dirTarget;
        InRange = false;

        if (tyre != null)
        {
            Vector3 c = tyre.transform.position;
            float pitch = cam != null ? cam.Pitch : 15f;
            float ratio = CurrentMode == Mode.Brace
                ? 0.85f
                : Mathf.Lerp(0.9f, 0.1f, Mathf.InverseLerp(0f, 35f, pitch));

            Vector3 aimPoint = c + Vector3.up * (tyre.radius * ratio);
            Vector3 to = aimPoint - grip;
            bool reachable = to.magnitude <= length + pokeReach + tyre.radius;
            InRange = reachable && Vector3.Dot(pt.forward, to.normalized) > 0.2f;

            Vector3 camFlat = cam != null ? Quaternion.Euler(0f, cam.Yaw, 0f) * Vector3.forward : pt.forward;

            if (engaged && reachable)
            {
                // Where the camera points, same horizontal distance and height as the tyre aim point,
                // then pulled part of the way towards the tyre centre (aim assist).
                Vector3 towards = to.normalized;
                Vector3 toFlat = new Vector3(to.x, 0f, to.z);
                Vector3 camDir = (camFlat * toFlat.magnitude + Vector3.up * to.y).normalized;
                if (Vector3.Dot(camDir, towards) < 0.2f) camDir = towards;

                dirTarget = Vector3.Slerp(camDir, towards, aimAssist).normalized;
            }
            else
            {
                dirTarget = Quaternion.AngleAxis(engaged ? 25f : 50f, pt.right) * (engaged ? camFlat : pt.forward);
            }

            if (engaged) player.SetFacing(camFlat);
            else player.ClearFacing();
        }
        else
        {
            dirTarget = Quaternion.AngleAxis(50f, pt.right) * pt.forward;
            player.ClearFacing();
        }

        float k = engaged ? 20f : 8f;
        dirCurrent = Vector3.Slerp(dirCurrent, dirTarget, 1f - Mathf.Exp(-k * dt)).normalized;
        GripWorld = grip + dirCurrent * ext;

        player.SetAnimatorBool("hitTyre", engaged);
    }

    // ------------------------------------------------------------------
    // Physics step: contact and forces
    // ------------------------------------------------------------------
    void ReleaseContact()
    {
        appliedForce = 0f;
        braceLeanPrevValid = false;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        bool wasContact = InContact;
        InContact = false;
        ContactForce = 0f;
        ContactSlip = 0f;
        contactDistance = -1f;

        if (tyre == null || tyre.Body == null || player == null || input == null || CurrentMode == Mode.Idle)
        {
            ReleaseContact();
            return;
        }

        Vector3 g = GripWorld;
        Vector3 dir = dirCurrent;

        Vector3 point, normal;
        float dist;
        if (!FindContact(g, dir, length, out point, out normal, out dist))
        {
            ReleaseContact();
            return;
        }

        if (!wasContact)
        {
            startedFromRest = tyre.Speed < restSpeed;
            surgeTime = 0f;
        }
        surgeTime += dt;

        // Relative velocity of the stick against the tyre surface at the contact point
        Vector3 vStick = player.HorizontalVelocity + dir * extRate;
        Vector3 vTyre = tyre.Body.GetPointVelocity(point);
        Vector3 vRel = vStick - vTyre;
        float closing = Vector3.Dot(vRel, -normal); // > 0 means the stick is moving into the tyre

        float pushValue = input.Push;
        float braceValue = input.Brace;
        float target, rise, mu;

        if (CurrentMode == Mode.Brace)
        {
            target = braceForce * braceValue;
            rise = braceRiseRate;
            mu = braceFriction;
        }
        else
        {
            float cap = CurrentMode == Mode.Poke ? pokeForce : holdForce;
            target = servoGain * Mathf.Max(0f, closing + pushOverspeed * pushValue);

            if (startedFromRest)
            {
                // The breakaway kick: extra force at the very start, decaying away.
                float surge = launchSurge * Mathf.Exp(-surgeTime / Mathf.Max(0.01f, launchSurgeTime));
                target += surge;
                cap += surge;
            }

            target = Mathf.Min(target, cap);
            rise = CurrentMode == Mode.Poke ? pokeRiseRate : pushRiseRate;
            mu = contactFriction;
        }

        // Jerk limit: the arm cannot change force instantly.
        float step = (target > appliedForce ? rise : releaseRate) * dt;
        appliedForce = Mathf.MoveTowards(appliedForce, target, step);
        float fn = appliedForce;

        // The stick pushes along its own axis. The part of that push that is sideways to the
        // surface is limited by the friction cone, so a steep approach angle makes the stick slip.
        Vector3 into = -normal;
        Vector3 intent = Vector3.zero;
        float cosPhi = Vector3.Dot(dir, into);
        if (cosPhi > 0.05f)
            intent = (dir / cosPhi - into) * fn;

        Vector3 vTan = vRel - normal * Vector3.Dot(vRel, normal);
        Vector3 ft = Vector3.ClampMagnitude(intent + vTan * frictionStiffness, mu * fn);

        Vector3 force = into * fn + ft;

        // Brace reflex: lateral force resisting lean, limited by what the contact can transmit
        if (CurrentMode == Mode.Brace && braceAssist)
        {
            Vector3 a = tyre.Body.rotation * Vector3.right;
            Vector3 s = new Vector3(a.x, 0f, a.z);
            float sm = s.magnitude;

            if (sm > 0.3f)
            {
                s /= sm;
                float lean = -a.y; // top displacement along s, in radians (small angles)
                float rate = braceLeanPrevValid ? (lean - braceLeanPrev) / dt : 0f;
                braceLeanPrev = lean;
                braceLeanPrevValid = true;

                float desired = -(braceKp * lean + braceKd * rate);
                float cap = (mu + Mathf.Abs(Vector3.Dot(into, s))) * fn;
                force += s * Mathf.Clamp(desired, -cap, cap);
            }
            else
            {
                braceLeanPrevValid = false;
            }
        }

        // One call gives the Rigidbody both a push and a torque (r x F), exactly like AddForce + AddTorque.
        tyre.Body.AddForceAtPosition(force, point, ForceMode.Force);

        // Newton's third law: the player feels the push
        player.ApplyVelocityChange(-force * dt / Mathf.Max(1f, player.mass));

        InContact = true;
        ContactForce = force.magnitude;
        ContactSlip = vTan.magnitude;
        contactDistance = dist;
        LastContactPoint = point;
        LastContactNormal = normal;
        ContactOffCentre = Vector3.Dot(point - tyre.Body.position, tyre.Body.rotation * Vector3.right);

        if (!wasContact && ContactStarted != null && target > 20f)
            ContactStarted(point, normal, target);
    }

    // Marches along the stick from the hand and returns the first point that touches the tyre.
    bool FindContact(Vector3 g, Vector3 dir, float len, out Vector3 point, out Vector3 normal, out float dist)
    {
        point = g;
        normal = Vector3.up;
        dist = 0f;

        Vector3 tmp;
        float start = tyre.SignedDistance(g, out tmp);
        if (start < contactRadius) return false; // hand is already inside the tyre: ignore

        const int steps = 48;
        float prevS = 0f;

        for (int i = 1; i <= steps; i++)
        {
            float s = len * i / steps;
            float sd = tyre.SignedDistance(g + dir * s, out tmp);

            if (sd < contactRadius)
            {
                float lo = prevS;
                float hi = s;
                for (int k = 0; k < 8; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (tyre.SignedDistance(g + dir * mid, out tmp) < contactRadius) hi = mid;
                    else lo = mid;
                }

                point = g + dir * hi;
                tyre.SignedDistance(point, out normal);
                dist = hi;
                return true;
            }

            prevS = s;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Visual stick follows the hand; it stops at the tyre surface
    // ------------------------------------------------------------------
    void LateUpdate()
    {
        if (stickRoot == null) return;

        float slide = 0f;
        if (InContact && contactDistance >= 0f && contactDistance < length)
            slide = contactDistance - length;

        stickRoot.SetPositionAndRotation(
            GripWorld + dirCurrent * slide,
            Quaternion.LookRotation(dirCurrent, Vector3.up));
    }
}
