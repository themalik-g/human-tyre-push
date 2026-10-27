using UnityEngine;

/// <summary>
/// Drop this on an empty GameObject in any scene. It builds everything the prototype needs:
/// player, stick, tyre, camera, HUD, and (optionally) a test track.
///
/// Run rules: if the tyre or the player falls off the track the run ends. R restarts the run
/// (or stands the tyre back up in front of you while the run is still going).
///
/// For the client's own level: untick "Build Test Arena", make sure the level meshes have
/// colliders, and set "Fall Y" a little below the lowest part of the track.
/// </summary>
[DefaultExecutionOrder(-200)]
public class TyrePushBootstrap : MonoBehaviour
{
    [Header("Models (optional - temporary models are generated when empty)")]
    public GameObject playerModel;
    public RuntimeAnimatorController playerAnimator;
    public GameObject stickModel;
    public Material stickMaterial;
    [Tooltip("Only used when 'Use Project Tyre Model' is ticked.")]
    public GameObject tyreModel;
    public Material tyreMaterial;
    [Tooltip("Off = generated tyre: perfectly round, hollow centre, real tread, no rim. On = the tyre mesh from the project.")]
    public bool useProjectTyreModel = false;

    [Header("Spawn")]
    public Vector3 playerSpawn = new Vector3(0f, 0.1f, 0f);
    public float tyreDistance = 2.2f;

    [Header("Fall hazard")]
    [Tooltip("If the tyre or the player drops below this height the run ends.")]
    public float fallY = -4f;

    [Header("Scene")]
    public bool buildTestArena = true;
    [Tooltip("100 Hz physics for a stable rolling tyre.")]
    public bool setFixedTimestep = true;

    [Header("Feedback")]
    [Tooltip("Dust, tread marks, rolling/impact sound, stick hit puff, controller rumble and camera kick.")]
    public bool contactFeedback = true;

    [Header("HUD")]
    public bool showObjective = true;
    public string objectiveText = "Reach the top of the hill.";

    public PlayerMotor Player { get; private set; }
    public TyrePhysics Tyre { get; private set; }
    public bool RunEnded { get; private set; }

    TyreHUD hud;

    // ------------------------------------------------------------------
    void Awake()
    {
        if (setFixedTimestep) Time.fixedDeltaTime = 0.01f;

        DisableLegacyScripts();
        if (buildTestArena) BuildArena();

        BuildPlayer();
        BuildTyre();
        BuildCamera();
        BuildHud();
    }

    void Update()
    {
        if (Player == null || Tyre == null) return;

        if (!RunEnded)
        {
            if (Tyre.transform.position.y < fallY) EndRun("The tyre fell off the track");
            else if (Player.transform.position.y < fallY) EndRun("You fell off the track");
        }

        if (Player.input != null && Player.input.ResetPressed)
        {
            if (RunEnded) RestartRun();
            else ResetTyre();
        }
    }

    void EndRun(string reason)
    {
        RunEnded = true;
        if (hud != null) hud.SetRunEnded(true, reason);
    }

    void RestartRun()
    {
        RunEnded = false;
        if (hud != null) hud.SetRunEnded(false, "");

        Player.Teleport(playerSpawn);
        Player.transform.rotation = Quaternion.identity;
        if (Player.cam != null) Player.cam.Init(Player.transform);

        ResetTyre();
    }

    void ResetTyre()
    {
        Vector3 f = Player.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        f.Normalize();

        Vector3 pos = Player.transform.position + f * tyreDistance + Vector3.up * (Tyre.radius + 0.05f);
        Tyre.Teleport(pos, Quaternion.LookRotation(f, Vector3.up));
    }

    // Old prototype scripts would fight the new ones, so switch them off if found.
    void DisableLegacyScripts()
    {
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string n = mb.GetType().Name;
            if (n == "GameMechanics" || n == "ThirdPersonController" || n == "CameraController")
            {
                Debug.LogWarning("TyrePushBootstrap: disabled legacy script " + n + " on " + mb.gameObject.name);
                mb.enabled = false;
            }
        }
    }

    // ------------------------------------------------------------------
    // Test arena: an elevated narrow track over a void (like the client's sample), with barriers,
    // a climb and a summit with a flag. No rails: falling off ends the run.
    // ------------------------------------------------------------------
    void BuildArena()
    {
        Material sand = TyreUtil.MakeMaterial(new Color(0.62f, 0.57f, 0.48f), 0.1f);
        Material hazard = TyreUtil.MakeMaterial(new Color(0.90f, 0.70f, 0.10f), 0.2f);
        Material dark = TyreUtil.MakeMaterial(new Color(0.12f, 0.12f, 0.13f), 0.1f);
        Material orange = TyreUtil.MakeMaterial(new Color(0.95f, 0.35f, 0.10f), 0.2f);

        const float width = 3.5f;

        // start platform and the long narrow straight
        Box("StartPlatform", new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(8f, 1f, 10f), sand);
        Box("Track", new Vector3(0f, -0.5f, 25f), Quaternion.identity, new Vector3(width, 1f, 40f), sand);

        // barriers the tyre can hit (it bounces, loses energy, or stops)
        Barrier("Barrier A", new Vector3(-0.7f, 0.35f, 16f), hazard, dark);
        Barrier("Barrier B", new Vector3(0.7f, 0.35f, 26f), hazard, dark);
        Barrier("Barrier C", new Vector3(-0.6f, 0.35f, 36f), hazard, dark);

        // a gentle climb
        const float rampLen = 30f;
        Quaternion rot = Quaternion.Euler(-8f, 0f, 0f);
        Vector3 start = new Vector3(0f, 0f, 45f);
        Box("Ramp", start + rot * new Vector3(0f, -0.5f, rampLen * 0.5f), rot, new Vector3(width, 1f, rampLen), sand);

        // upper straight and summit platform
        Vector3 top = start + rot * new Vector3(0f, 0f, rampLen);
        Box("UpperTrack", top + new Vector3(0f, -0.5f, 10f), Quaternion.identity, new Vector3(width, 1f, 20f), sand);
        Box("Summit", top + new Vector3(0f, -0.5f, 24f), Quaternion.identity, new Vector3(8f, 1f, 8f), sand);

        // flag at the top
        Vector3 pole = top + new Vector3(0f, 0f, 24f);
        GameObject poleGo = Box("FlagPole", pole + Vector3.up * 1.5f, Quaternion.identity, new Vector3(0.08f, 3f, 0.08f), hazard);
        Collider pc = poleGo.GetComponent<Collider>();
        if (pc != null) pc.enabled = false;
        GameObject flag = Box("Flag", pole + new Vector3(0.5f, 2.6f, 0f), Quaternion.identity, new Vector3(1f, 0.6f, 0.04f), orange);
        Collider fc = flag.GetComponent<Collider>();
        if (fc != null) fc.enabled = false;

        if (FindFirstObjectByType<Light>() == null)
        {
            GameObject lg = new GameObject("Sun");
            Light l = lg.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            lg.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    static GameObject Box(string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // Hazard-striped block: a dark body with yellow bands on top.
    static void Barrier(string name, Vector3 pos, Material hazard, Material dark)
    {
        GameObject body = Box(name, pos, Quaternion.identity, new Vector3(1.6f, 0.7f, 0.5f), dark);
        for (int i = 0; i < 4; i++)
        {
            GameObject band = Box(name + " band", pos + new Vector3(-0.6f + i * 0.4f, 0f, 0f), Quaternion.identity,
                new Vector3(0.18f, 0.71f, 0.51f), hazard);
            Collider c = band.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Object.Destroy(c); }
            band.transform.SetParent(body.transform, true);
        }
    }

    // ------------------------------------------------------------------
    void BuildPlayer()
    {
        PlayerMotor existing = FindFirstObjectByType<PlayerMotor>();
        if (existing != null)
        {
            Player = existing;
            if (contactFeedback && existing.GetComponent<StickFeedback>() == null && existing.GetComponent<StickTool>() != null)
                existing.gameObject.AddComponent<StickFeedback>();
            return;
        }

        // Inactive while configuring, so Awake runs after every field is set.
        GameObject go = new GameObject("Player");
        go.SetActive(false);
        go.transform.position = playerSpawn;
        go.tag = "Player";

        CharacterController cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 50f;
        cc.skinWidth = 0.03f;
        cc.minMoveDistance = 0f;

        TyreGameInput inp = go.AddComponent<TyreGameInput>();

        Animator anim = null;
        if (playerModel != null)
        {
            GameObject m = Instantiate(playerModel, go.transform);
            m.name = "Model";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            TyreUtil.StripColliders(m);

            anim = m.GetComponentInChildren<Animator>(true);
            if (anim == null) anim = m.AddComponent<Animator>();
            if (playerAnimator != null) anim.runtimeAnimatorController = playerAnimator;
            anim.applyRootMotion = false;
        }
        else
        {
            Debug.LogWarning("TyrePushBootstrap: no Player Model assigned. Using a plain placeholder body.");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlaceholderBody";
            Collider bc = body.GetComponent<Collider>();
            if (bc != null) { bc.enabled = false; Destroy(bc); }
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body.GetComponent<Renderer>().sharedMaterial = TyreUtil.MakeMaterial(new Color(0.85f, 0.85f, 0.88f), 0.3f);
        }

        PlayerMotor motor = go.AddComponent<PlayerMotor>();
        motor.input = inp;
        motor.animator = anim;

        StickTool stick = go.AddComponent<StickTool>();
        stick.player = motor;
        stick.input = inp;
        stick.stickModel = stickModel;
        stick.stickMaterial = stickMaterial;

        if (contactFeedback) go.AddComponent<StickFeedback>();

        if (anim != null)
        {
            StickHandIK ik = anim.gameObject.AddComponent<StickHandIK>();
            ik.tool = stick;
        }

        go.SetActive(true);
        Player = motor;
    }

    void BuildTyre()
    {
        TyrePhysics existing = FindFirstObjectByType<TyrePhysics>();
        if (existing != null)
        {
            Tyre = existing;
            if (contactFeedback && existing.GetComponent<TyreContactFeedback>() == null)
                existing.gameObject.AddComponent<TyreContactFeedback>();
            return;
        }

        GameObject go = new GameObject("Tyre");
        go.SetActive(false);

        Vector3 f = Player.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        f.Normalize();

        go.AddComponent<Rigidbody>();
        TyrePhysics tp = go.AddComponent<TyrePhysics>();

        if (useProjectTyreModel && tyreModel != null)
        {
            tp.visualMaterial = tyreMaterial;
            GameObject m = Instantiate(tyreModel, go.transform);
            m.name = "TyreModel";
            TyreUtil.StripColliders(m);
            tp.visual = m.transform;
        }

        go.transform.SetPositionAndRotation(
            Player.transform.position + f * tyreDistance + Vector3.up * (tp.radius + 0.02f),
            Quaternion.LookRotation(f, Vector3.up));

        if (contactFeedback) go.AddComponent<TyreContactFeedback>();

        go.SetActive(true);
        Tyre = tp;
    }

    void BuildCamera()
    {
        Camera c = Camera.main;
        if (c == null)
        {
            GameObject cg = new GameObject("Main Camera");
            cg.tag = "MainCamera";
            c = cg.AddComponent<Camera>();
            cg.AddComponent<AudioListener>();
        }

        c.fieldOfView = 60f;
        c.nearClipPlane = 0.1f;

        ThirdPersonCamera tpc = c.GetComponent<ThirdPersonCamera>();
        if (tpc == null) tpc = c.gameObject.AddComponent<ThirdPersonCamera>();

        tpc.input = Player.input;
        tpc.Init(Player.transform);
        Player.cam = tpc;
        Player.tyre = Tyre;

        StickTool stick = Player.GetComponent<StickTool>();
        if (stick != null)
        {
            stick.tyre = Tyre;
            stick.cam = tpc;
        }
    }

    void BuildHud()
    {
        GameObject go = new GameObject("TyreHUD");
        go.SetActive(false);
        hud = go.AddComponent<TyreHUD>();
        hud.tyre = Tyre;
        hud.cam = Player.cam;
        hud.stick = Player.GetComponent<StickTool>();
        hud.showObjective = showObjective;
        hud.objectiveText = objectiveText;
        go.SetActive(true);
    }
}
