using UnityEngine;

/// <summary>
/// A real rigid-body tyre.
///
/// - Hollow ring of capsule colliders (rounded shoulders, flat crown) - NOT a sphere.
/// - Real mass and real thin-ring inertia tensor (gives genuine gyroscopic stability).
/// - Rolling comes purely from friction. Nothing is frozen, nothing is faked.
/// - Rolling resistance and air drag use real-world formulas.
/// - Exposes true telemetry (speed, lean) for the HUD, and a signed-distance query for the stick.
///
/// Local axes: X = axle, Z = rolling direction when upright. Root scale must stay (1,1,1).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class TyrePhysics : MonoBehaviour
{
    [Header("Real-world size and mass")]
    [Tooltip("Outer radius in metres. 0.52 is a large, highly visible tyre (~1.04m diameter).")]
    public float radius = 0.52f;

    [Tooltip("Mass in kg.")]
    public float mass = 14f;

    [Tooltip("Tyre width in metres. Only used when no model is assigned (a model sets its own width).")]
    public float defaultWidth = 0.32f;

    [Tooltip("Radial thickness of the tread shell. Also sets how rounded the tread shoulders are.")]
    public float treadThickness = 0.08f;

    [Range(0.3f, 0.8f)]
    [Tooltip("Hollow centre radius as a fraction of the outer radius. There is no rim: the centre is empty.")]
    public float rimRadiusRatio = 0.55f;

    [Header("Visual (optional)")]
    [Tooltip("Tyre model. It is auto-rotated, scaled and centred to match the physics. Leave empty for a procedural tyre.")]
    public Transform visual;
    public Material visualMaterial;
    public bool fitVisualToRadius = true;

    [Header("Contact")]
    [Range(24, 64)] public int ringSegments = 40;
    public float friction = 0.9f;
    [Range(0f, 0.6f)] public float bounciness = 0.15f;

    [Header("Resistance (real-world values)")]
    [Tooltip("Rolling resistance coefficient. Asphalt ~0.015, packed dirt ~0.05, loose sand ~0.1. A free tyre keeps rolling on its own momentum and slowly loses speed.")]
    public float rollingResistance = 0.05f;

    [Tooltip("Resistance multiplier at standstill. Gives a real breakaway: a resting tyre needs a firmer push than a rolling one.")]
    public float breakawayFactor = 1.6f;

    [Tooltip("Angular drag (spin loss). Rigidbody angular damping.")]
    public float angularDrag = 0.05f;

    [Tooltip("Safety cap on speed (m/s), prevents runaway physics.")]
    public float maxSpeed = 25f;

    [Tooltip("Drag coefficient of the tyre seen from the front.")]
    public float dragCoefficient = 1.0f;

    [Header("Turning (a leaned rolling tyre curves, like a hoop or a bicycle wheel)")]
    [Range(0f, 1.5f)]
    [Tooltip("How strongly a leaned rolling tyre curves towards its lean. It only fills in the turn the contact physics does not already give. 0 = off.")]
    public float turnAssist = 1.2f;
    [Tooltip("How quickly the turn catches up with the lean (1/s).")]
    public float turnResponse = 8f;
    [Tooltip("Largest turning torque (N m).")]
    public float maxTurnTorque = 35f;

    [Header("Wobble (looks about to fall as it slows)")]
    public bool wobble = true;
    [Tooltip("Wobble starts below this speed (m/s) and grows as the tyre slows.")]
    public float wobbleBelowSpeed = 3f;
    [Tooltip("Peak wobble torque (N m). 0 = off.")]
    public float wobbleStrength = 3.5f;
    [Tooltip("Wobble speed (Hz). It rises a little as the tyre slows.")]
    public float wobbleFrequency = 1.3f;
    [Range(0f, 1.5f)]
    [Tooltip("How strongly a leaning slow tyre leans further. Higher = falls over sooner.")]
    public float wobbleGrowth = 0.5f;

    /// <summary>0..1, set by the stick: a braced or pushed tyre wobbles less.</summary>
    public float WobbleDamping { get; set; }

    // ------------------------------------------------------------------
    public Rigidbody Body { get; private set; }
    public float Width { get; private set; }
    public float ShoulderRadius { get { return Mathf.Min(treadThickness * 0.5f, Width * 0.5f); } }

    /// <summary>Real speed of the tyre in m/s.</summary>
    public float Speed { get { return Body != null ? Body.linearVelocity.magnitude : 0f; } }

    /// <summary>Speed along the ground (m/s), ignoring falling. This is what the SPEED bar shows.</summary>
    public float GroundSpeed
    {
        get
        {
            if (Body == null) return 0f;
            Vector3 v = Body.linearVelocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;

    /// <summary>True while the tyre is standing on its tread (axle roughly horizontal).</summary>
    public bool IsStanding { get { return Mathf.Abs(transform.right.y) < 0.5f; } }

    // --- real contact data for feedback (dust, marks, sound) -------------
    /// <summary>Average ground contact point of the last physics step.</summary>
    public Vector3 GroundPoint { get; private set; }

    /// <summary>
    /// Speed (m/s) of the tread surface relative to the ground at the contact point.
    /// ~0 when rolling cleanly, large when skidding or sliding. Comes straight from the Rigidbody.
    /// </summary>
    public float SlipSpeed { get; private set; }

    /// <summary>World direction the tread slides over the ground (unit, along the ground plane).</summary>
    public Vector3 SlipDirection { get; private set; } = Vector3.forward;

    /// <summary>Colour of the surface under the tyre (used to tint dust).</summary>
    public Color GroundTint { get; private set; } = new Color(0.55f, 0.53f, 0.50f, 1f);

    /// <summary>Raised on a real collision: impulse (N s), contact point.</summary>
    public event System.Action<float, Vector3> Impact;

    bool groundSeen;
    Vector3 groundNormalAccum;
    Vector3 groundPointAccum;
    int groundPointCount;
    Collider groundCollider;
    Collider tintSource;
    float lastLeanSign = 1f;
    float wobblePhase;

    // ------------------------------------------------------------------
    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Build();
    }

    [ContextMenu("Rebuild Tyre")]
    public void Build()
    {
        if (Body == null) Body = GetComponent<Rigidbody>();

        if (transform.localScale != Vector3.one)
        {
            Debug.LogWarning("TyrePhysics: root scale must be (1,1,1). It was reset. Scale the model child instead.", this);
            transform.localScale = Vector3.one;
        }

        SetupVisual();
        BuildColliders();
        ConfigureBody();
    }

    // ------------------------------------------------------------------
    // Visual
    // ------------------------------------------------------------------
    void SetupVisual()
    {
        if (visual == null)
        {
            foreach (Transform child in transform)
            {
                Bounds probe;
                if (TyreUtil.GetLocalBounds(child, out probe))
                {
                    visual = child;
                    break;
                }
            }
        }

        if (visual == null)
        {
            Width = defaultWidth;
            BuildProceduralVisual();
            return;
        }

        FitModel();
    }

    void BuildProceduralVisual()
    {
        GameObject go = new GameObject("TyreMesh");
        go.transform.SetParent(transform, false);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = TyreUtil.BuildTyreMesh(radius, Width, ShoulderRadius, radius * rimRadiusRatio);

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = visualMaterial != null
            ? visualMaterial
            : TyreUtil.MakeHighContrastTyreMaterial();

        visual = go.transform;
    }

    void FitModel()
    {
        if (visual.parent != transform)
            visual.SetParent(transform, true);

        Bounds b;
        if (!TyreUtil.GetLocalBounds(transform, out b))
        {
            Width = defaultWidth;
            return;
        }

        // 1. The thinnest dimension of a tyre is its axle. Rotate the model so that axis is X.
        Vector3 size = b.size;
        int axis = 0;
        if (size.y < size[axis]) axis = 1;
        if (size.z < size[axis]) axis = 2;

        if (axis != 0)
        {
            Quaternion q = Quaternion.FromToRotation(axis == 1 ? Vector3.up : Vector3.forward, Vector3.right);
            visual.localPosition = q * visual.localPosition;
            visual.localRotation = q * visual.localRotation;
            TyreUtil.GetLocalBounds(transform, out b);
        }

        // 2. Scale to the physical radius (or adopt the model's radius).
        float outer = Mathf.Max(b.size.y, b.size.z) * 0.5f;
        if (outer > 1e-4f)
        {
            if (fitVisualToRadius)
            {
                float k = radius / outer;
                visual.localScale = visual.localScale * k;
                visual.localPosition = visual.localPosition * k;
                TyreUtil.GetLocalBounds(transform, out b);
            }
            else
            {
                radius = outer;
            }
        }

        // 3. Centre the model on the rigidbody origin.
        visual.position = visual.position - transform.TransformVector(b.center);
        TyreUtil.GetLocalBounds(transform, out b);

        Width = Mathf.Max(0.05f, b.size.x);
        TyreUtil.ApplyMaterial(visual.gameObject, visualMaterial);
    }

    // ------------------------------------------------------------------
    // Colliders: hollow ring of capsules around the axle
    // ------------------------------------------------------------------
    void BuildColliders()
    {
        foreach (Collider c in GetComponents<Collider>())
        {
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }

        float r = ShoulderRadius;
        float ringRadius = radius - r;

        PhysicsMaterial pm = new PhysicsMaterial("TyreRubber");
        pm.dynamicFriction = friction;
        pm.staticFriction = friction * 1.1f;
        pm.bounciness = bounciness;
        pm.frictionCombine = PhysicsMaterialCombine.Maximum;
        pm.bounceCombine = PhysicsMaterialCombine.Average;

        int n = Mathf.Max(24, ringSegments);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI * 2f * i / n;
            CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 0; // X axis = axle
            cap.radius = r;
            cap.height = Width;
            cap.center = new Vector3(0f, ringRadius * Mathf.Sin(a), ringRadius * Mathf.Cos(a));
            cap.sharedMaterial = pm;
        }
    }

    // ------------------------------------------------------------------
    // Rigidbody: real mass, real inertia, no cheating
    // ------------------------------------------------------------------
    void ConfigureBody()
    {
        Body.mass = mass;
        Body.useGravity = true;
        Body.isKinematic = false;
        Body.constraints = RigidbodyConstraints.None;

        // Drag is modelled explicitly in FixedUpdate with real formulas.
        Body.linearDamping = 0f;
        Body.angularDamping = angularDrag;
        Body.maxLinearVelocity = maxSpeed;

        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // A rolling tyre spins fast (5 m/s on 0.36 m radius = 14 rad/s).
        // Unity's default cap is 7 rad/s, which silently stops natural rolling.
        Body.maxAngularVelocity = 80f;
        Body.maxDepenetrationVelocity = 4f;
        Body.solverIterations = 24;
        Body.solverVelocityIterations = 8;

        // Thin ring: I_axle = m R^2, I_diameter = m R^2 / 2 + m w^2 / 12
        float rm = radius - ShoulderRadius;
        float iAxle = mass * rm * rm;
        float iDiam = 0.5f * mass * rm * rm + mass * Width * Width / 12f;

        Body.centerOfMass = Vector3.zero;
        Body.inertiaTensor = new Vector3(iAxle, iDiam, iDiam);
        Body.inertiaTensorRotation = Quaternion.identity;
    }

    // ------------------------------------------------------------------
    // Resistance forces
    // ------------------------------------------------------------------
    void FixedUpdate()
    {
        IsGrounded = groundSeen;
        GroundNormal = (groundSeen && groundNormalAccum.sqrMagnitude > 1e-6f) ? groundNormalAccum.normalized : Vector3.up;

        if (groundSeen && groundPointCount > 0)
        {
            GroundPoint = groundPointAccum / groundPointCount;

            // Velocity of the tread material at the contact point, along the ground plane.
            // Pure rolling gives ~0; skidding and sliding give a large value.
            Vector3 vc = Vector3.ProjectOnPlane(Body.GetPointVelocity(GroundPoint), GroundNormal);
            SlipSpeed = vc.magnitude;
            if (SlipSpeed > 0.05f) SlipDirection = vc / SlipSpeed;
        }
        else
        {
            SlipSpeed = 0f;
        }

        if (groundSeen && groundCollider != null) UpdateTint(groundCollider);

        groundSeen = false;
        groundNormalAccum = Vector3.zero;
        groundPointAccum = Vector3.zero;
        groundPointCount = 0;
        groundCollider = null;

        Vector3 v = Body.linearVelocity;
        float speed = v.magnitude;

        // Aerodynamic drag: F = 1/2 rho Cd A v^2
        if (speed > 0.01f)
        {
            float area = Width * 2f * radius;
            Body.AddForce(-0.5f * 1.2f * dragCoefficient * area * speed * v, ForceMode.Force);
        }

        // Rolling resistance: F = Crr * N, only while rolling on the tread.
        // Higher at standstill (breakaway), settling to the rolling value above ~0.8 m/s.
        if (IsGrounded && Mathf.Abs((Body.rotation * Vector3.right).y) < 0.5f)
        {
            Vector3 vAlong = Vector3.ProjectOnPlane(v, GroundNormal);
            float s = vAlong.magnitude;
            if (s > 0.001f)
            {
                float crr = Mathf.Lerp(rollingResistance * breakawayFactor, rollingResistance, Mathf.InverseLerp(0.05f, 0.8f, s));
                float normalForce = Body.mass * Physics.gravity.magnitude * Mathf.Max(0.1f, GroundNormal.y);
                float ramp = Mathf.Min(1f, s / 0.03f); // avoids chatter around zero speed
                Body.AddForce(-vAlong / s * (crr * normalForce * ramp), ForceMode.Force);
            }
        }

        ApplyRollingDynamics(v);
    }


    // ------------------------------------------------------------------
    // Rolling dynamics: lean steering and slow-speed wobble
    // ------------------------------------------------------------------
    void ApplyRollingDynamics(Vector3 v)
    {
        if (!IsGrounded || !IsStanding) return;

        Vector3 vH = new Vector3(v.x, 0f, v.z);
        float speed = vH.magnitude;
        if (speed < 0.3f) return;

        Vector3 axle = Body.rotation * Vector3.right;
        Vector3 heading = Vector3.Cross(axle, Vector3.up);
        if (heading.sqrMagnitude < 1e-4f) return;
        heading.Normalize();
        if (Vector3.Dot(heading, vH) < 0f) heading = -heading;
        Vector3 right = Vector3.Cross(Vector3.up, heading);

        Vector3 sH = new Vector3(axle.x, 0f, axle.z);
        float sm = sH.magnitude;
        if (sm < 0.3f) return;
        sH /= sm;

        // + = the top of the tyre leans to the right of its direction of travel (radians, small angles)
        float leanRight = -axle.y * Vector3.Dot(sH, right);
        float dt = Time.fixedDeltaTime;

        // --- Turning ------------------------------------------------------
        // A rolling wheel that leans curves towards its lean: yaw rate = g tan(lean) / speed.
        // Positive yaw (about +Y) is a right turn. We only add the turn that is missing, so hits that
        // already yaw the tyre (off-centre pushes) are never fought.
        if (turnAssist > 0f)
        {
            Vector3 w = Body.angularVelocity;
            float spin = Vector3.Dot(w, axle);
            float yawRate = Vector3.Dot(w, Vector3.up) - spin * axle.y; // heading change, not axle tilt

            float target = Physics.gravity.magnitude * Mathf.Tan(Mathf.Clamp(leanRight, -0.7f, 0.7f)) / Mathf.Max(speed, 1f);
            float deficit = target - yawRate;

            if (deficit * target > 0f)
            {
                float torque = Mathf.Clamp(Body.inertiaTensor.y * turnResponse * deficit * turnAssist, -maxTurnTorque, maxTurnTorque);
                Body.AddTorque(Vector3.up * torque, ForceMode.Force);
            }
        }

        // --- Wobble -------------------------------------------------------
        // Below a critical speed the tyre loses its self-stability: it snakes and leans in turn, and the
        // lean feeds itself (inverted pendulum). Stronger the slower it goes. A braced tyre calms down.
        if (wobble && wobbleStrength > 0f && speed < wobbleBelowSpeed)
        {
            float k = Mathf.Clamp01((wobbleBelowSpeed - speed) / Mathf.Max(0.1f, wobbleBelowSpeed - 0.3f));
            k = k * k * (3f - 2f * k);
            float calm = 1f - Mathf.Clamp01(WobbleDamping);

            wobblePhase += Mathf.PI * 2f * wobbleFrequency * (1f + 0.6f * k) * dt;
            float osc = Mathf.Sin(wobblePhase) + 0.7f * (Mathf.PerlinNoise(Time.time * 2.3f, 0.7f) - 0.5f);

            float gravityTorque = Body.mass * Physics.gravity.magnitude * radius;
            float torque = (wobbleStrength * osc - wobbleGrowth * gravityTorque * leanRight) * k * calm;
            Body.AddTorque(heading * torque, ForceMode.Force);
        }
    }

    void OnCollisionEnter(Collision c)
    {
        CollectGround(c);

        if (Impact != null && c.contactCount > 0)
            Impact(c.impulse.magnitude, c.GetContact(0).point);
    }

    void OnCollisionStay(Collision c) { CollectGround(c); }

    void CollectGround(Collision c)
    {
        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint cp = c.GetContact(i);
            if (cp.normal.y > 0.35f)
            {
                groundSeen = true;
                groundNormalAccum += cp.normal;
                groundPointAccum += cp.point;
                groundPointCount++;
                groundCollider = c.collider;
            }
        }
    }

    // Reads the colour of the surface once per surface change (cheap), for dust tint.
    void UpdateTint(Collider col)
    {
        if (col == tintSource) return;
        tintSource = col;

        Renderer r = col.GetComponent<Renderer>();
        if (r == null) return;

        Material m = r.sharedMaterial;
        if (m == null) return;

        Color c;
        if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
        else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
        else return;

        GroundTint = new Color(c.r, c.g, c.b, 1f);
    }

    // ------------------------------------------------------------------
    // Telemetry (for the HUD)
    // ------------------------------------------------------------------
    /// <summary>
    /// Lean of the wheel plane from vertical in degrees. Positive = leaning towards viewRight.
    /// </summary>
    public float LeanDegrees(Vector3 viewRight)
    {
        Vector3 a = transform.right;
        float ay = Mathf.Clamp(a.y, -1f, 1f);
        float mag = Mathf.Asin(Mathf.Abs(ay)) * Mathf.Rad2Deg;

        Vector3 lean = new Vector3(-a.x * ay, 0f, -a.z * ay);
        float lm = lean.magnitude;
        if (lm > 1e-4f)
        {
            float d = Vector3.Dot(lean / lm, viewRight);
            if (Mathf.Abs(d) > 0.15f)
                lastLeanSign = d >= 0f ? 1f : -1f;
        }

        return mag * lastLeanSign;
    }

    // ------------------------------------------------------------------
    // Signed distance from a world point to the tyre surface (negative = inside).
    // Uses the physics pose, so call it from FixedUpdate.
    // The shape is the exact tread (rounded-box profile revolved around the axle)
    // plus two thin sidewalls. normal points away from the tyre.
    // ------------------------------------------------------------------
    public float SignedDistance(Vector3 worldPoint, out Vector3 worldNormal)
    {
        Quaternion rot = Body.rotation;
        Vector3 p = Quaternion.Inverse(rot) * (worldPoint - Body.position);

        float rho = Mathf.Sqrt(p.y * p.y + p.z * p.z);
        Vector2 radialDir = rho > 1e-5f ? new Vector2(p.y / rho, p.z / rho) : new Vector2(0f, 1f);
        Vector2 q = new Vector2(p.x, rho);

        float r = ShoulderRadius;
        float h = Mathf.Max(0f, Width * 0.5f - r);
        float rc = radius - r;
        float rim = radius * rimRadiusRatio;
        const float sw = 0.012f;
        float uSide = Width * 0.5f - sw;

        Vector2 n2;
        float best = SegDist(q, new Vector2(-h, rc), new Vector2(h, rc), out n2) - r;

        Vector2 nl;
        float dl = SegDist(q, new Vector2(-uSide, rim), new Vector2(-uSide, rc), out nl) - sw;
        if (dl < best) { best = dl; n2 = nl; }

        Vector2 nr;
        float dr = SegDist(q, new Vector2(uSide, rim), new Vector2(uSide, rc), out nr) - sw;
        if (dr < best) { best = dr; n2 = nr; }

        worldNormal = rot * new Vector3(n2.x, n2.y * radialDir.x, n2.y * radialDir.y);
        return best;
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b, out Vector2 normal)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
        Vector2 closest = a + ab * t;
        Vector2 d = p - closest;
        float m = d.magnitude;
        normal = m > 1e-6f ? d / m : new Vector2(0f, 1f);
        return m;
    }

    // ------------------------------------------------------------------
    /// <summary>Stand the tyre upright at a position, heading along rot's forward.</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.position = position;
        Body.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        Body.WakeUp();
    }
}
