using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds a Unity Canvas HUD at runtime (no prefab wiring needed):
///   bottom left  BALANCE  lean of the tyre left/right (centre = upright)
///   bottom right SPEED    real speed of the tyre
/// Both values are read straight from TyrePhysics (the Rigidbody), nothing is estimated.
/// </summary>
public class TyreHUD : MonoBehaviour
{
    public TyrePhysics tyre;
    public ThirdPersonCamera cam;
    [Tooltip("Used for the IN RANGE indicator.")]
    public StickTool stick;

    [Header("Scales")]
    [Tooltip("Lean angle shown at either end of the BALANCE bar.")]
    public float maxLeanDisplay = 30f;
    [Tooltip("Speed (m/s) shown at the right end of the SPEED bar.")]
    public float maxSpeedDisplay = 8f;

    [Header("Extras")]
    public bool showObjective = true;
    public string objectiveText = "Reach the top of the hill.";
    public bool showControlsHint = true;

    GameObject runEndedRoot;
    TextMeshProUGUI runEndedReason;
    Image rangeDot;
    TextMeshProUGUI rangeText;
    bool pendingEnded;
    string pendingReason = "";

    RectTransform balanceFill, balanceMarker, speedFill;
    Image balanceMarkerImage;
    TextMeshProUGUI balanceValue, speedValue;

    float balanceShown = 0.5f, balanceVel;
    float speedShown, speedVel;

    static readonly Color Orange = new Color(0.98f, 0.58f, 0.12f, 1f);
    static readonly Color Red = new Color(0.92f, 0.22f, 0.15f, 1f);
    static readonly Color White = new Color(0.93f, 0.93f, 0.93f, 1f);
    static readonly Color Green = new Color(0.62f, 0.80f, 0.20f, 1f);

    void Start()
    {
        Build();
    }

    // ------------------------------------------------------------------
    void Build()
    {
        GameObject canvasGo = new GameObject("HUD Canvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform root = canvasGo.transform;

        // BALANCE: white fill from the left up to an orange marker, centre = upright
        BuildBar(root, "BALANCE", true, White, true,
                 out balanceFill, out balanceMarker, out balanceValue);
        balanceMarkerImage = balanceMarker.GetComponent<Image>();

        // SPEED: green fill from the left
        RectTransform noMarker;
        BuildBar(root, "SPEED", false, Green, false,
                 out speedFill, out noMarker, out speedValue);

        if (showObjective && !string.IsNullOrEmpty(objectiveText))
        {
            TextMeshProUGUI head = NewText("Objective Head", root, "OBJECTIVE:", 34f, Orange, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            SetRect(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -40f), new Vector2(700f, 44f));

            TextMeshProUGUI body = NewText("Objective Body", root, objectiveText, 34f, White, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            SetRect(body.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -84f), new Vector2(900f, 44f));
        }

        BuildRangeBadge(root);
        BuildRunEnded(root);

        if (showControlsHint)
        {
            TextMeshProUGUI hint = NewText("Controls", root,
                "WASD move   Shift run   Space jump   Mouse look   LMB push / poke   RMB brace   R reset tyre / restart",
                22f, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Bottom, FontStyles.Normal);
            SetRect(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1200f, 34f));
        }
    }

    void BuildBar(Transform root, string label, bool left, Color fillColor, bool withMarker,
                  out RectTransform fill, out RectTransform marker, out TextMeshProUGUI valueText)
    {
        const float W = 460f;
        const float H = 26f;

        Vector2 anchor = left ? new Vector2(0f, 0f) : new Vector2(1f, 0f);

        RectTransform holder = NewRect(label + " Bar", root);
        SetRect(holder, anchor, anchor, anchor, new Vector2(left ? 60f : -60f, 64f), new Vector2(W, H));

        Image frame = NewImage("Frame", holder, new Color(0.72f, 0.72f, 0.72f, 0.9f));
        Stretch(frame.rectTransform, 0f);

        Image track = NewImage("Track", holder, new Color(0.13f, 0.13f, 0.14f, 0.92f));
        Stretch(track.rectTransform, 2f);

        Image fillImg = NewImage("Fill", track.rectTransform, fillColor);
        fill = fillImg.rectTransform;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(withMarker ? 0.5f : 0f, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;

        marker = null;
        if (withMarker)
        {
            Image m = NewImage("Marker", holder, Orange);
            marker = m.rectTransform;
            marker.anchorMin = new Vector2(0.5f, 0.5f);
            marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.anchoredPosition = Vector2.zero;
            marker.sizeDelta = new Vector2(14f, H + 12f);
        }

        // ticks under the bar
        for (int k = 0; k <= 6; k++)
        {
            float f = k / 6f;
            bool major = (k == 3 && withMarker) || k == 0 || k == 6;
            Image tick = NewImage("Tick" + k, holder, new Color(0.85f, 0.85f, 0.85f, 0.8f));
            RectTransform tr = tick.rectTransform;
            tr.anchorMin = new Vector2(f, 0f);
            tr.anchorMax = new Vector2(f, 0f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -4f);
            tr.sizeDelta = new Vector2(2f, major ? 14f : 8f);
        }

        TextMeshProUGUI title = NewText(label, holder, label, 30f, White, TextAlignmentOptions.BottomLeft, FontStyles.Normal);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 8f), new Vector2(260f, 38f));

        valueText = NewText(label + " Value", holder, "", 26f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.BottomRight, FontStyles.Normal);
        SetRect(valueText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 8f), new Vector2(200f, 38f));
    }

    // Small badge: grey when the tyre is out of reach, green when a stroke can reach it.
    void BuildRangeBadge(Transform root)
    {
        RectTransform holder = NewRect("Range Badge", root);
        SetRect(holder, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(60f, 150f), new Vector2(250f, 38f));

        Image bg = NewImage("Bg", holder, new Color(0.13f, 0.13f, 0.14f, 0.85f));
        Stretch(bg.rectTransform, 0f);

        rangeDot = NewImage("Dot", holder, new Color(0.5f, 0.5f, 0.5f, 1f));
        SetRect(rangeDot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(18f, 18f));

        rangeText = NewText("Text", holder, "OUT OF RANGE", 24f, White, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
        SetRect(rangeText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(-48f, 0f));
        rangeText.rectTransform.offsetMin = new Vector2(40f, 0f);
        rangeText.rectTransform.offsetMax = new Vector2(-8f, 0f);
    }

    void BuildRunEnded(Transform root)
    {
        RectTransform panel = NewRect("Run Ended", root);
        Stretch(panel, 0f);
        runEndedRoot = panel.gameObject;

        Image dim = NewImage("Dim", panel, new Color(0f, 0f, 0f, 0.6f));
        Stretch(dim.rectTransform, 0f);

        TextMeshProUGUI title = NewText("Title", panel, "RUN ENDED", 90f, Red, TextAlignmentOptions.Center, FontStyles.Bold);
        SetRect(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1200f, 120f));

        runEndedReason = NewText("Reason", panel, pendingReason, 40f, White, TextAlignmentOptions.Center, FontStyles.Normal);
        SetRect(runEndedReason.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1200f, 60f));

        TextMeshProUGUI hint = NewText("Hint", panel, "Press R (or Y on a controller) to restart", 32f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center, FontStyles.Normal);
        SetRect(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(1200f, 50f));

        runEndedRoot.SetActive(pendingEnded);
    }

    /// <summary>Shows or hides the RUN ENDED screen (tyre or player fell off the track).</summary>
    public void SetRunEnded(bool ended, string reason)
    {
        pendingEnded = ended;
        pendingReason = reason ?? "";

        if (runEndedRoot != null)
        {
            runEndedRoot.SetActive(ended);
            if (runEndedReason != null) runEndedReason.text = pendingReason;
        }
    }

    // ------------------------------------------------------------------
    void Update()
    {
        if (tyre == null || balanceFill == null) return;

        if (rangeDot != null)
        {
            bool inRange = stick != null && stick.InRange;
            rangeDot.color = inRange ? Green : new Color(0.5f, 0.5f, 0.5f, 1f);
            rangeText.text = inRange ? "IN RANGE" : "OUT OF RANGE";
            rangeText.color = inRange ? Green : White;
        }

        Vector3 viewRight = Vector3.right;
        if (cam != null) viewRight = cam.transform.right;
        else if (Camera.main != null) viewRight = Camera.main.transform.right;
        viewRight.y = 0f;
        if (viewRight.sqrMagnitude < 1e-4f) viewRight = Vector3.right;
        viewRight.Normalize();

        // --- BALANCE: real lean of the wheel plane ------------------------
        float lean = tyre.LeanDegrees(viewRight);
        float balanceTarget = Mathf.Clamp(lean / Mathf.Max(1f, maxLeanDisplay), -1f, 1f) * 0.5f + 0.5f;
        balanceShown = Mathf.SmoothDamp(balanceShown, balanceTarget, ref balanceVel, 0.05f);

        balanceFill.anchorMax = new Vector2(balanceShown, 1f);
        balanceMarker.anchorMin = new Vector2(balanceShown, 0.5f);
        balanceMarker.anchorMax = new Vector2(balanceShown, 0.5f);
        balanceMarker.anchoredPosition = Vector2.zero;

        float a = Mathf.Abs(lean);
        balanceMarkerImage.color = Color.Lerp(Orange, Red, Mathf.InverseLerp(10f, 22f, a));

        string side = a < 1.5f ? "" : (lean > 0f ? "  R" : "  L");
        balanceValue.text = a.ToString("0") + "\u00B0" + side;

        // --- SPEED: real speed of the Rigidbody ---------------------------
        float speed = tyre.GroundSpeed; // forward movement along the ground
        float speedTarget = Mathf.Clamp01(speed / Mathf.Max(0.1f, maxSpeedDisplay));
        speedShown = Mathf.SmoothDamp(speedShown, speedTarget, ref speedVel, 0.05f);

        speedFill.anchorMax = new Vector2(speedShown, 1f);
        speedValue.text = (speed * 3.6f).ToString("0.0") + " km/h";
    }

    // ------------------------------------------------------------------
    // uGUI helpers
    // ------------------------------------------------------------------
    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color,
                                   TextAlignmentOptions align, FontStyles style)
    {
        RectTransform rt = NewRect(name, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = style;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
