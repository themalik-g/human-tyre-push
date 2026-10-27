using UnityEngine;

/// <summary>
/// Visual and audio feedback for the tyre touching the world. Everything is driven by real
/// physics values from TyrePhysics (slip speed, speed, impact impulse), never by timers:
///
///   Dust    kicked up when the tread slips on the ground, and a little at high rolling speed.
///           Tinted with the colour of the surface under the tyre.
///   Marks   dark tread marks laid along the real contact path while slipping.
///   Sound   rolling rumble (volume and pitch follow speed), scrape (follows slip),
///           thud on impacts (follows impulse).
/// </summary>
[RequireComponent(typeof(TyrePhysics))]
public class TyreContactFeedback : MonoBehaviour
{
    [Header("Dust")]
    public bool dust = true;
    [Range(0f, 3f)] public float dustAmount = 1f;
    [Tooltip("Slip speed (m/s) below which no dust is kicked up.")]
    public float slipDustThreshold = 0.4f;
    [Tooltip("Particles per second per m/s of slip.")]
    public float dustPerSlip = 28f;
    [Tooltip("Rolling speed (m/s) above which a light dust trail appears.")]
    public float rollingDustSpeed = 3f;
    public float rollingDust = 3f;

    [Header("Marks")]
    public bool marks = true;
    [Tooltip("Slip speed (m/s) above which tread marks are drawn.")]
    public float slipMarkThreshold = 0.8f;

    [Header("Sound")]
    public bool sound = true;
    [Range(0f, 1f)] public float volume = 0.7f;
    [Tooltip("Optional real recordings. When empty, generated sounds are used.")]
    public AudioClip rollingClip;
    public AudioClip scrapeClip;
    public AudioClip impactClip;

    TyrePhysics tyre;
    ParticleSystem dustSystem;
    SkidMarks skid;
    AudioSource rollSource, scrapeSource, hitSource;
    float emitRemainder;

    void Awake()
    {
        tyre = GetComponent<TyrePhysics>();

        if (dust) dustSystem = TyreUtil.CreateDustSystem(transform, "Tyre Dust", 400);
        if (marks) skid = gameObject.AddComponent<SkidMarks>();
        if (sound) BuildAudio();
    }

    void OnEnable()
    {
        tyre = GetComponent<TyrePhysics>();
        tyre.Impact += OnImpact;
    }

    void OnDisable()
    {
        if (tyre != null) tyre.Impact -= OnImpact;
    }

    // ------------------------------------------------------------------
    // Physics-rate: dust and marks follow the real contact patch
    // ------------------------------------------------------------------
    void FixedUpdate()
    {
        if (tyre == null || tyre.Body == null) return;

        if (!tyre.IsGrounded)
        {
            if (skid != null) skid.Break();
            return;
        }

        float slip = tyre.SlipSpeed;
        float speed = tyre.Speed;
        Vector3 point = tyre.GroundPoint;
        Vector3 normal = tyre.GroundNormal;

        // --- dust ---------------------------------------------------------
        if (dustSystem != null && dustAmount > 0f)
        {
            float rate = 0f;
            if (slip > slipDustThreshold) rate += dustPerSlip * (slip - slipDustThreshold);
            if (speed > rollingDustSpeed) rate += rollingDust * (speed - rollingDustSpeed);
            rate *= dustAmount;

            emitRemainder += rate * Time.fixedDeltaTime;
            int count = Mathf.Min(Mathf.FloorToInt(emitRemainder), 12);
            emitRemainder -= Mathf.Floor(emitRemainder);

            for (int i = 0; i < count; i++)
                EmitDust(point, normal, slip, 1f);
        }

        // --- marks --------------------------------------------------------
        if (skid != null)
        {
            Vector3 side = Vector3.ProjectOnPlane(transform.right, normal);
            bool wide = side.sqrMagnitude > 0.09f; // axle roughly along the ground (tyre not lying flat)

            if (slip > slipMarkThreshold && wide)
            {
                side.Normalize();
                float intensity = Mathf.Clamp01((slip - slipMarkThreshold) / 4f + 0.25f);
                skid.Add(point, side, tyre.Width * 0.4f, normal, intensity);
            }
            else
            {
                skid.Break();
            }
        }
    }

    void EmitDust(Vector3 point, Vector3 normal, float slip, float sizeScale)
    {
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        ep.position = point + normal * 0.03f + Random.insideUnitSphere * 0.05f;

        // thrown backwards against the slide direction, and up
        Vector3 kick = -tyre.SlipDirection * Mathf.Min(slip * 0.25f, 2f)
                       + normal * Random.Range(0.1f, 0.5f)
                       + Random.insideUnitSphere * 0.2f;
        ep.velocity = kick;

        ep.startSize = Random.Range(0.12f, 0.28f) * (1f + Mathf.Clamp01(slip / 6f)) * sizeScale;
        ep.startLifetime = Random.Range(0.6f, 1.2f);

        // dust is lighter than the surface it comes from
        Color c = Color.Lerp(tyre.GroundTint, new Color(0.78f, 0.75f, 0.71f), 0.45f);
        c.a = Mathf.Clamp01(0.10f + slip * 0.04f) * 0.6f;
        ep.startColor = c;

        dustSystem.Emit(ep, 1);
    }

    // ------------------------------------------------------------------
    // Sound
    // ------------------------------------------------------------------
    void BuildAudio()
    {
        rollSource = NewSource("Roll", rollingClip != null ? rollingClip : TyreSfx.RollingLoop(), true);
        scrapeSource = NewSource("Scrape", scrapeClip != null ? scrapeClip : TyreSfx.ScrapeLoop(), true);
        hitSource = NewSource("Hit", null, false);
    }

    AudioSource NewSource(string name, AudioClip clip, bool loop)
    {
        GameObject go = new GameObject("Audio " + name);
        go.transform.SetParent(transform, false);

        AudioSource a = go.AddComponent<AudioSource>();
        a.clip = clip;
        a.loop = loop;
        a.playOnAwake = false;
        a.spatialBlend = 1f;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = 2f;
        a.maxDistance = 40f;
        a.volume = 0f;

        if (loop && clip != null) a.Play();
        return a;
    }

    void Update()
    {
        if (!sound || tyre == null || tyre.Body == null || rollSource == null) return;

        float speed = tyre.Speed;
        float slip = tyre.SlipSpeed;
        bool grounded = tyre.IsGrounded;

        float rollTarget = grounded ? Mathf.Clamp01((speed - 0.2f) / 4f) * 0.45f * volume : 0f;
        float scrapeTarget = grounded ? Mathf.Clamp01((slip - 0.4f) / 4f) * 0.55f * volume : 0f;

        float step = Time.deltaTime * 4f;
        rollSource.volume = Mathf.MoveTowards(rollSource.volume, rollTarget, step);
        rollSource.pitch = Mathf.Lerp(0.7f, 1.5f, Mathf.Clamp01(speed / 10f));

        scrapeSource.volume = Mathf.MoveTowards(scrapeSource.volume, scrapeTarget, step);
        scrapeSource.pitch = Mathf.Lerp(0.8f, 1.3f, Mathf.Clamp01(slip / 6f));
    }

    void OnImpact(float impulse, Vector3 point)
    {
        if (tyre == null) return;

        // impulse of a 1.5 m/s bump is about mass * 1.5; ignore anything smaller than that
        float strength = impulse / (tyre.Body.mass * 3f);
        if (strength < 0.15f) return;
        strength = Mathf.Clamp01(strength);

        if (hitSource != null)
        {
            hitSource.transform.position = point;
            hitSource.pitch = Random.Range(0.9f, 1.1f);
            hitSource.PlayOneShot(impactClip != null ? impactClip : TyreSfx.Thud(), strength * volume);
        }

        if (dustSystem != null && dustAmount > 0f)
        {
            int n = Mathf.RoundToInt(strength * 10f * dustAmount);
            for (int i = 0; i < n; i++)
                EmitDust(point, Vector3.up, 2f + strength * 3f, 1.2f);
        }
    }
}
