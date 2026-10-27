using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws tread marks as thin ribbons lying on the ground. A fixed pool of strips is recycled,
/// so cost stays constant no matter how long you play. No decal system or assets needed.
/// </summary>
public class SkidMarks : MonoBehaviour
{
    [Tooltip("Number of strips kept alive. The oldest is recycled first.")]
    public int maxStrips = 14;
    [Tooltip("Points per strip. A new strip starts when one is full.")]
    public int pointsPerStrip = 60;
    [Tooltip("Minimum distance between points (m).")]
    public float minPointSpacing = 0.10f;
    [Tooltip("Lift above the surface to avoid z-fighting (m).")]
    public float lift = 0.012f;
    public Color color = new Color(0.03f, 0.03f, 0.03f, 1f);
    [Range(0f, 1f)] public float maxAlpha = 0.7f;

    class Strip
    {
        public GameObject go;
        public Mesh mesh;
        public Vector3[] verts;
        public Color[] cols;
        public int count;
        public Vector3 lastCenter;
        public Vector3 lastA, lastB;
        public Color lastColor;
    }

    readonly List<Strip> strips = new List<Strip>();
    Strip current;
    Transform root;
    Material material;

    void Awake()
    {
        root = new GameObject("Skid Marks").transform;
        // Sprites/Default is transparent and vertex coloured. No texture: the ribbons have no UVs.
        Shader sh = Shader.Find("Sprites/Default");
        material = new Material(sh);
        material.name = "SkidMarkMaterial";
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    /// <summary>Ends the current strip (call when the tyre stops slipping).</summary>
    public void Break()
    {
        current = null;
    }

    /// <summary>Adds a point to the current mark.</summary>
    /// <param name="center">Contact point on the ground.</param>
    /// <param name="side">Unit vector across the tread, along the ground plane.</param>
    /// <param name="halfWidth">Half the mark width in metres.</param>
    /// <param name="normal">Ground normal.</param>
    /// <param name="intensity">0..1 darkness.</param>
    public void Add(Vector3 center, Vector3 side, float halfWidth, Vector3 normal, float intensity)
    {
        Vector3 p = center + normal * lift;
        Color c = color;
        c.a = Mathf.Clamp01(intensity) * maxAlpha;

        if (current == null)
        {
            current = NewStrip();
        }
        else if (current.count > 0 && (p - current.lastCenter).sqrMagnitude < minPointSpacing * minPointSpacing)
        {
            return;
        }

        if (current.count >= pointsPerStrip)
        {
            // Strip full: continue seamlessly in a fresh one that starts at the last point.
            Strip full = current;
            current = NewStrip();
            Write(current, full.lastA, full.lastB, p, full.lastColor);
        }

        Vector3 a = p - side * halfWidth;
        Vector3 b = p + side * halfWidth;
        Write(current, a, b, p, c);
    }

    // ------------------------------------------------------------------
    void Write(Strip s, Vector3 a, Vector3 b, Vector3 center, Color c)
    {
        int i = s.count;
        s.verts[2 * i] = a;
        s.verts[2 * i + 1] = b;
        s.cols[2 * i] = c;
        s.cols[2 * i + 1] = c;

        // Unused tail vertices collapse onto the last pair, so their triangles have zero area.
        for (int k = i + 1; k < pointsPerStrip; k++)
        {
            s.verts[2 * k] = a;
            s.verts[2 * k + 1] = b;
            s.cols[2 * k] = c;
            s.cols[2 * k + 1] = c;
        }

        s.count = i + 1;
        s.lastCenter = center;
        s.lastA = a;
        s.lastB = b;
        s.lastColor = c;

        s.mesh.vertices = s.verts;
        s.mesh.colors = s.cols;
        s.mesh.RecalculateBounds();

        if (!s.go.activeSelf) s.go.SetActive(true);
    }

    Strip NewStrip()
    {
        Strip s;

        if (strips.Count >= maxStrips)
        {
            // Recycle the oldest strip.
            s = strips[0];
            strips.RemoveAt(0);
            s.count = 0;
            s.go.SetActive(false);
        }
        else
        {
            s = new Strip();
            s.go = new GameObject("SkidStrip");
            s.go.transform.SetParent(root, false);
            s.go.SetActive(false);

            s.verts = new Vector3[pointsPerStrip * 2];
            s.cols = new Color[pointsPerStrip * 2];

            int[] tris = new int[(pointsPerStrip - 1) * 6];
            int t = 0;
            for (int i = 0; i < pointsPerStrip - 1; i++)
            {
                int a = 2 * i, b = 2 * i + 1, c = 2 * i + 2, d = 2 * i + 3;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }

            s.mesh = new Mesh();
            s.mesh.name = "SkidStrip";
            s.mesh.MarkDynamic();
            s.mesh.vertices = s.verts;
            s.mesh.colors = s.cols;
            s.mesh.triangles = tris;

            MeshFilter mf = s.go.AddComponent<MeshFilter>();
            mf.sharedMesh = s.mesh;
            MeshRenderer mr = s.go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        strips.Add(s);
        return s;
    }
}
