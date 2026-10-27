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
/// Where the stick lands is where you aim: how far you stand or look to the side of the tyre centre. The stick pushes along its own axis, limited by the friction cone, so the
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
    public float length = 1.8f;
    [Tooltip("Stick thickness (diameter, m) for the generated stick. A broom handle is about 0.03, this is a chunky club.")]
    public float thickness = 0.08f;
    [Tooltip("How much thicker than the original the stick MODEL is drawn (1 = as modelled).")]
    public float modelThickness = 2.2f;
    public GameObject stickModel;
    public Material stickMaterial;
    [Tooltip("Tick if the stick model is held by the wrong end.")]
    public bool flipModel = false;
    [Tooltip("Right hand position relative to the player (right, up, forward).")]
    public Vector3 gripLocalOffset = new Vector3(0.28f, 1.05f, 0.30f);

    [Header("Aiming (fine control)")]
    [Range(0f, 1.5f)]
    [Tooltip("How far the contact point moves across the tyre per metre you move or aim sideways. 0.8 means 10 cm of side movement moves the hit by 8 cm.")]
    public float lateralAimGain = 1.0f;
    [Tooltip("Furthest the stick can land from the tyre centre line (m). Tread half-width is about 0.12, so 0.3 reaches past the shoulder.")]
    public float maxLateralAim = 0.3f;
    [Tooltip("Within this distance (m) of the tyre the character holds still facing the camera, sideways steps get small and mouse turning slows down.")]
    public float nearTyreEnter = 3.6f;
    public float nearTyreExit = 4.2f;
    [Range(0.1f, 1f)]
    [Tooltip("Mouse/stick look speed multiplier while near the tyre. Lower = finer aim.")]
    public float nearTyreLookScale = 0.4f;

    [Header("Steering (hold Push, then A / D or left stick)")]
    [Tooltip("Largest angle (deg) the push can be steered to the left or right of where the camera looks. D = tyre goes right, A = tyre goes left.")]
    public float steerMaxAngle = 40f;
    [Tooltip("How quickly the steering builds up and returns to centre (1/s).")]
    public float steerResponse = 6f;
    [Tooltip("While you push, the tyre's heading is turned toward the push direction. Gain of that turn (1/s). 0 = off (pure physics).")]
    public float headingGain = 3f;
    [Tooltip("Fastest heading change (rad/s) the stick can make.")]
    public float headingMaxRate = 2.2f;
    [Range(0f, 0.9f)]
    [Tooltip("How far across the tread (fraction of half-width) the hit moves at full steer. Hitting the left side sends the tyre right.")]
    public float steerContactShift = 0.5f;
    [Tooltip("Draw an arrow on the ground showing where the push will send the tyre.")]
    public bool showGuide = true;

    [Header("Forces (Newtons)")]
    [Tooltip("Highest steady push a person can hold with a stick.")]
    public float holdForce = 130f;
    [Tooltip("Highest force during a poke stroke.")]
    public float pokeForce = 240f;
    [Tooltip("Pressing force while bracing.")]
    public float braceForce = 100f;
    [Tooltip("Servo gain in N per (m/s) of closing speed.")]
    public float servoGain = 140f;
    [Tooltip("How much faster than the hand the stick tries to move the tyre at full push (m/s).")]
    public float pushOverspeed = 0.5f;

    [Header("Start from rest (the initial jerk)")]
    [Tooltip("Tyre speed (m/s) below which a new contact counts as a start from rest.")]
    public float restSpeed = 0.3f;
    [Tooltip("Extra force added at the very start, decaying away. This is the breakaway kick.")]
    public float launchSurge = 45f;
    [Tooltip("How fast the surge decays (s).")]
    public float launchSurgeTime = 0.22f;

    [Header("Force build-up (N per second, jerk limit)")]
    public float pushRiseRate = 700f;
    public float pokeRiseRate = 2600f;
    [Tooltip("Lower = a more progressive, controllable brake.")]
    public float braceRiseRate = 450f;
    public float releaseRate = 2500f;

    [Header("Poke stroke")]
    public float pokeDuration = 0.28f;
    [Tooltip("How far the hand thrusts forward (m). Peak tip speed = reach * pi / duration.")]
    public float pokeReach = 0.38f;
    public float pokeCooldown = 0.08f;

    [Header("Contact")]
    public float contactRadius = 0.04f;
    [Tooltip("Stick on rubber friction coefficient (push / poke).")]
    public float contactFriction = 0.9f;
    [Tooltip("Stick on rubber friction coefficient while braking (a firm press grips better).")]
    public float braceFriction = 0.9f;
    public float frictionStiffness = 40f;
    [Tooltip("Time (s) over which a new touch fades in, so contact never jerks. 0 = instant.")]
    public float touchSoftening = 0.09f;

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

    /// <summary>Steering input, -1 (left) .. +1 (right), smoothed.</summary>
    public float Steer { get { return steer; } }

    /// <summary>Flat world direction the push is aimed along (camera direction turned by the steering).</summary>
    public Vector3 PushDirection { get; private set; } = Vector3.forward;
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

    bool wasNear;
    float steer;
    Vector3 physGrip;
    LineRenderer guide;
    float appliedForce;      // slew-limited normal force
    float contactTime;
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
        BuildGuide();
    }

    void Start()
    {
        if (tyre == null) tyre = FindFirstObjectByType<TyrePhysics>();
        if (cam == null) cam = FindFirstObjectByType<ThirdPersonCamera>();
    }

    void OnDestroy()
    {
        if (stickRoot != null) Destroy(stickRoot.gameObject);
        if (guide != null) Destroy(guide.gameObject);
    }

    void OnDisable()
    {
        if (player != null)
        {
            player.ClearFacing();
            player.SetPrecisionLateral(false);
            player.SetLateralLock(false);
            player.SetAnimatorBool("hitTyre", false);
        }
        if (cam != null) cam.lookScale = 1f;
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
            stickRoot.localScale = new Vector3(modelThickness, modelThickness, 1f); // thicker, same length
        }
        else
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "StickMesh";
            Collider c = cyl.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Destroy(c); }

            cyl.transform.SetParent(stickRoot, false);
            cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cyl.transform.localScale = new Vector3(thickness, length * 0.5f, thickness);
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
        Vector3 hand = pt.TransformPoint(gripLocalOffset);
        Vector3 camFlat = cam != null ? Quaternion.Euler(0f, cam.Yaw, 0f) * Vector3.forward : pt.forward;
        Vector3 camRight = Vector3.Cross(Vector3.up, camFlat);

        // The PHYSICS origin of the stick sits on the camera's centre line, not on the right hand.
        // (The old version started 0.28 m to the right, so every "straight" push slanted to the left.)
        // Where the crosshair line meets the tyre is where it is hit, and a tyre in the middle of the
        // screen is hit in the middle and pushed straight. The visible stick bends from the hand to the
        // same contact point.
        float shoulder = cam != null ? cam.shoulderOffset : 0f;
        Vector3 grip = new Vector3(pt.position.x, hand.y, pt.position.z) + camRight * shoulder + camFlat * gripLocalOffset.z;

        Vector3 dirTarget;
        InRange = false;
        bool near = false;
        Vector3 steerDir = camFlat;

        if (tyre != null)
        {
            Vector3 c = tyre.transform.position;
            float pitch = cam != null ? cam.Pitch : 15f;
            float ratio = CurrentMode == Mode.Brace
                ? 0.85f
                : Mathf.Lerp(0.9f, 0.1f, Mathf.InverseLerp(0f, 35f, pitch));

            Vector3 toFlat = c - hand;
            toFlat.y = 0f;
            near = toFlat.magnitude < (wasNear ? nearTyreExit : nearTyreEnter);

            // --- steering: A / D (or left stick) while pushing ---------------
            bool steering = near && (CurrentMode == Mode.Push || CurrentMode == Mode.Poke);
            float steerTarget = 0f;
            if (steering)
            {
                steerTarget = input.Move.x;
                if (Mathf.Abs(steerTarget) < 0.15f) steerTarget = 0f;
            }
            steer = Mathf.MoveTowards(steer, Mathf.Clamp(steerTarget, -1f, 1f), steerResponse * dt);
            steerDir = Quaternion.AngleAxis(steer * steerMaxAngle, Vector3.up) * camFlat;

            // --- where the stick lands across the tread ----------------------
            float halfW = tyre.Width * 0.5f;
            float latMax = Mathf.Min(maxLateralAim, halfW * 0.85f);
            float hoff = Vector3.Dot(c - grip, camRight);   // tyre centre to the right of the crosshair line
            float lateral = Mathf.Clamp(-hoff * lateralAimGain - steer * steerContactShift * halfW, -latMax, latMax);

            Vector3 aimPoint = c + Vector3.up * (tyre.radius * ratio) + camRight * lateral;
            Vector3 to = aimPoint - grip;
            bool reachable = to.magnitude <= length + pokeReach + tyre.radius;
            InRange = reachable && Vector3.Dot(pt.forward, to.normalized) > 0.2f;

            if (engaged && reachable)
                dirTarget = to.normalized;
            else
                dirTarget = Quaternion.AngleAxis(engaged ? 25f : 50f, pt.right) * ((engaged || near) ? camFlat : pt.forward);

            // Near the tyre the body always faces where the camera looks, so a sideways step
            // does not swing the body (and the stick) round before the stroke.
            if (engaged || near) player.SetFacing(camFlat);
            else player.ClearFacing();
        }
        else
        {
            steer = Mathf.MoveTowards(steer, 0f, steerResponse * dt);
            dirTarget = Quaternion.AngleAxis(50f, pt.right) * pt.forward;
            player.ClearFacing();
        }

        PushDirection = steerDir;
        wasNear = near;
        player.SetPrecisionLateral(near);
        // While pushing near the tyre, A / D steer instead of strafing.
        player.SetLateralLock(near && (CurrentMode == Mode.Push || CurrentMode == Mode.Poke));
        if (cam != null) cam.lookScale = near ? nearTyreLookScale : 1f;

        float k = engaged ? 20f : 8f;
        dirCurrent = Vector3.Slerp(dirCurrent, dirTarget, 1f - Mathf.Exp(-k * dt)).normalized;
        physGrip = grip + dirCurrent * ext;     // physics origin (camera line)
        GripWorld = hand + dirCurrent * ext;    // the visible hand (IK, stick visual)

        player.SetAnimatorBool("hitTyre", engaged);
    }

    // ------------------------------------------------------------------
    // Physics step: contact and forces
    // ------------------------------------------------------------------
    void ReleaseContact()
    {
        appliedForce = 0f;
        braceLeanPrevValid = false;
        if (tyre != null) tyre.WobbleDamping = 0f;
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

        Vector3 g = physGrip;
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
            contactTime = 0f;
        }
        surgeTime += dt;
        contactTime += dt;

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

        // A new touch fades in smoothly (no instant step) ...
        if (touchSoftening > 0f)
            target *= Mathf.SmoothStep(0f, 1f, contactTime / touchSoftening);

        // ... and the arm cannot change force instantly (jerk limit).
        float step = (target > appliedForce ? rise : releaseRate) * dt;
        appliedForce = Mathf.MoveTowards(appliedForce, target, step);
        float fn = appliedForce;

        // The stick pushes along its own axis. The part of that push that is sideways to the
        // surface is limited by the friction cone, so a steep approach angle makes the stick slip.
        Vector3 into = -normal;
        Vector3 intent = Vector3.zero;

        // The push goes where you steer (camera direction turned by A / D), keeping the stick's pitch.
        Vector3 pushDir = dir;
        if (CurrentMode != Mode.Brace)
        {
            Vector3 flat = PushDirection;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-4f)
            {
                float hl = Mathf.Sqrt(Mathf.Max(0f, 1f - dir.y * dir.y));
                pushDir = (flat.normalized * hl + Vector3.up * dir.y).normalized;
            }
        }

        float cosPhi = Vector3.Dot(pushDir, into);
        if (cosPhi > 0.05f)
            intent = (pushDir / cosPhi - into) * fn;

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

        // A firmly braced tyre wobbles much less; a tyre being pushed wobbles a little less.
        tyre.WobbleDamping = CurrentMode == Mode.Brace ? Mathf.Clamp01(braceValue) * 0.85f : 0.3f;

        // One call gives the Rigidbody both a push and a torque (r x F), exactly like AddForce + AddTorque.
        tyre.Body.AddForceAtPosition(force, point, ForceMode.Force);

        // Easy direction change: while you push, the tyre's heading follows the push direction.
        // The contact still gives the real push and torque; this only keeps the turn predictable.
        if (CurrentMode != Mode.Brace && headingGain > 0f)
        {
            float w = Mathf.Clamp01(fn / Mathf.Max(1f, holdForce * 0.35f));
            tyre.AlignHeading(PushDirection, headingGain, headingMaxRate, dt, w);
        }

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
        if (stickRoot != null)
        {
            // Tip of the stick: the contact point while touching the tyre, otherwise the full length.
            float tipDist = (InContact && contactDistance >= 0f && contactDistance < length) ? contactDistance + contactRadius * 0.8f : length;
            Vector3 tip = physGrip + dirCurrent * tipDist;

            // The visible stick runs from the hand to that tip.
            Vector3 vis = tip - GripWorld;
            Vector3 dirVis = vis.sqrMagnitude > 0.01f ? vis.normalized : dirCurrent;

            stickRoot.SetPositionAndRotation(
                tip - dirVis * length,
                Quaternion.LookRotation(dirVis, Vector3.up));
        }

        UpdateGuide();
    }

    // ------------------------------------------------------------------
    // Ground arrow: where the push will send the tyre
    // ------------------------------------------------------------------
    void BuildGuide()
    {
        GameObject go = new GameObject("Push Guide");
        guide = go.AddComponent<LineRenderer>();
        guide.positionCount = 2;
        guide.widthMultiplier = 0.09f;
        guide.numCapVertices = 6;
        guide.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        guide.receiveShadows = false;

        Shader sh = Shader.Find("Sprites/Default");
        guide.sharedMaterial = sh != null ? new Material(sh) : TyreUtil.MakeMaterial(Color.white, 0f);
        guide.enabled = false;
    }

    void UpdateGuide()
    {
        if (guide == null) return;

        bool show = showGuide && tyre != null && wasNear;
        guide.enabled = show;
        if (!show) return;

        Vector3 d = PushDirection;
        d.y = 0f;
        d.Normalize();

        float y = tyre.IsGrounded ? tyre.GroundPoint.y + 0.05f : tyre.transform.position.y - tyre.radius + 0.05f;
        Vector3 start = tyre.transform.position + d * (tyre.radius + 0.1f);
        start.y = y;
        Vector3 end = start + d * 1.8f;

        guide.SetPosition(0, start);
        guide.SetPosition(1, end);

        Color col = Color.Lerp(new Color(1f, 1f, 1f, 0.55f), new Color(1f, 0.85f, 0.1f, 0.95f), Mathf.Abs(steer));
        guide.startColor = col;
        guide.endColor = new Color(col.r, col.g, col.b, 0.1f);
    }
}
