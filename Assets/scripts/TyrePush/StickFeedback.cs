using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Feedback when the stick meets the tyre. Strength comes from the real contact force
/// computed by StickTool: a light touch gives a tiny puff and a soft knock, a hard poke
/// gives a bigger puff, a sharp knock, a controller pulse and a small camera kick.
/// While you keep pushing, a faint continuous rumble follows the real contact force.
/// </summary>
[RequireComponent(typeof(StickTool))]
public class StickFeedback : MonoBehaviour
{
    public bool puff = true;
    public bool sound = true;
    public bool rumble = true;
    public bool cameraShake = true;

    [Range(0f, 1f)] public float volume = 0.8f;
    [Tooltip("Contact force (N) that counts as a full-strength hit.")]
    public float forceForFullEffect = 600f;
    [Tooltip("Optional real recording. When empty, a generated knock is used.")]
    public AudioClip hitClip;

    [Tooltip("Rubber squeal while braking (follows the real slip speed under the stick).")]
    public bool brakeSound = true;

    StickTool stick;
    ParticleSystem puffSystem;
    AudioSource source;
    AudioSource brakeSource;

    float pulse;
    float lastLow, lastHigh;

    void Awake()
    {
        stick = GetComponent<StickTool>();

        if (puff) puffSystem = TyreUtil.CreateDustSystem(transform, "Stick Puff", 120);

        if (sound)
        {
            GameObject go = new GameObject("Audio StickHit");
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 30f;
        }

        if (brakeSound)
        {
            GameObject bg = new GameObject("Audio StickBrake");
            bg.transform.SetParent(transform, false);
            brakeSource = bg.AddComponent<AudioSource>();
            brakeSource.clip = TyreSfx.ScrapeLoop();
            brakeSource.loop = true;
            brakeSource.playOnAwake = false;
            brakeSource.spatialBlend = 1f;
            brakeSource.rolloffMode = AudioRolloffMode.Linear;
            brakeSource.minDistance = 2f;
            brakeSource.maxDistance = 25f;
            brakeSource.volume = 0f;
            brakeSource.Play();
        }
    }

    void OnEnable()
    {
        stick = GetComponent<StickTool>();
        stick.ContactStarted += OnContact;
    }

    void OnDisable()
    {
        if (stick != null) stick.ContactStarted -= OnContact;
        SetMotors(0f, 0f);
    }

    void OnContact(Vector3 point, Vector3 normal, float force)
    {
        float s = Mathf.Clamp01(force / Mathf.Max(1f, forceForFullEffect));

        if (puffSystem != null)
        {
            int n = 2 + Mathf.RoundToInt(s * 8f);
            for (int i = 0; i < n; i++)
            {
                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
                ep.position = point + normal * 0.03f + Random.insideUnitSphere * 0.02f;
                ep.velocity = normal * (0.4f + s * 1.2f) + Random.insideUnitSphere * 0.4f;
                ep.startSize = Random.Range(0.05f, 0.12f);
                ep.startLifetime = Random.Range(0.35f, 0.7f);
                ep.startColor = new Color(0.45f, 0.43f, 0.40f, 0.35f + 0.3f * s);
                puffSystem.Emit(ep, 1);
            }
        }

        if (source != null)
        {
            source.transform.position = point;
            source.pitch = Random.Range(0.92f, 1.08f);
            source.PlayOneShot(hitClip != null ? hitClip : TyreSfx.Thwack(), (0.25f + 0.75f * s) * volume);
        }

        if (rumble) pulse = Mathf.Max(pulse, s);

        if (cameraShake && stick.cam != null)
            stick.cam.AddShake(0.05f + s * 0.35f);
    }

    void Update()
    {
        UpdateBrakeSound();

        if (!rumble) return;

        pulse *= Mathf.Exp(-14f * Time.deltaTime);

        float hold = stick.InContact
            ? Mathf.Clamp01(stick.ContactForce / Mathf.Max(1f, forceForFullEffect)) * 0.25f
            : 0f;

        float low = Mathf.Max(pulse * 0.8f, hold);
        float high = pulse * 0.6f;

        if (Mathf.Abs(low - lastLow) > 0.01f || Mathf.Abs(high - lastHigh) > 0.01f)
            SetMotors(low, high);
    }

    void UpdateBrakeSound()
    {
        if (brakeSource == null) return;

        float target = 0f;
        if (stick.InContact && stick.CurrentMode == StickTool.Mode.Brace)
        {
            float slip = Mathf.Clamp01((stick.ContactSlip - 0.3f) / 4f);
            float press = Mathf.Clamp01(stick.ContactForce / Mathf.Max(1f, stick.braceForce));
            target = slip * press * 0.5f * volume;
            brakeSource.transform.position = stick.LastContactPoint;
            brakeSource.pitch = Mathf.Lerp(0.6f, 1.0f, Mathf.Clamp01(stick.ContactSlip / 8f));
        }

        brakeSource.volume = Mathf.MoveTowards(brakeSource.volume, target, Time.deltaTime * 5f);
    }

    void SetMotors(float low, float high)
    {
        lastLow = low;
        lastHigh = high;

        Gamepad pad = Gamepad.current;
        if (pad != null) pad.SetMotorSpeeds(low, high);
    }
}
