using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small shared helpers: runtime materials, mesh-bounds fitting and a procedural
/// tyre mesh (hollow centre + tread) used when no tyre model is assigned.
/// </summary>
public static class TyreUtil
{
    // ------------------------------------------------------------------
    // Materials
    // ------------------------------------------------------------------
    public static Material MakeMaterial(Color color, float smoothness = 0.3f, float metallic = 0f)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material m = new Material(sh);
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        return m;
    }

    public static void ApplyMaterial(GameObject go, Material material)
    {
        if (go == null || material == null) return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
    }

    public static void StripColliders(GameObject go)
    {
        if (go == null) return;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
        {
            c.enabled = false;
            Object.Destroy(c);
        }
    }

    // ------------------------------------------------------------------
    // Bounds of all meshes below `root`, expressed in root's local space
    // ------------------------------------------------------------------
    public static bool GetLocalBounds(Transform root, out Bounds bounds)
    {
        bool has = false;
        bounds = new Bounds(Vector3.zero, Vector3.zero);

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Encapsulate(root, mf.transform, mf.sharedMesh.bounds, ref bounds, ref has);
        }

        foreach (SkinnedMeshRenderer sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (sk.sharedMesh == null) continue;
            Encapsulate(root, sk.transform, sk.sharedMesh.bounds, ref bounds, ref has);
        }

        return has;
    }

    static void Encapsulate(Transform root, Transform t, Bounds b, ref Bounds acc, ref bool has)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);

            Vector3 p = root.InverseTransformPoint(t.TransformPoint(corner));

            if (!has)
            {
                acc = new Bounds(p, Vector3.zero);
                has = true;
            }
            else
            {
                acc.Encapsulate(p);
            }
        }
    }

    // ------------------------------------------------------------------
    // Procedural tyre mesh (hollow centre, rounded shoulders, real tread pattern).
    // Axle = local X, tyre centre = origin. Profile is revolved around X.
    // Tread: 2 wide + 2 narrow circumferential grooves, slanted lateral grooves.
    // ------------------------------------------------------------------
    public static Mesh BuildTyreMesh(float outerRadius, float width, float shoulderRadius, float rimRadius, int segments = 144)
    {
        float r = Mathf.Min(shoulderRadius, width * 0.5f);
        float h = Mathf.Max(0f, width * 0.5f - r);   // half length of the flat tread
        float rc = outerRadius - r;                  // radius of the shoulder-arc centres

        var pts = new List<Vector2>();    // (u along axle, rho from axle)
        var treadU = new List<float>();   // -1..1 on the tread, 99 elsewhere

        System.Action<float, float, float> add = (u, rho, tu) =>
        {
            pts.Add(new Vector2(u, rho));
            treadU.Add(tu);
        };

        const int SW = 6, ARC = 6, T = 40, LINER = 6;

        // left sidewall, bead -> shoulder
        for (int k = 0; k < SW; k++)
        {
            float t = k / (float)SW;
            add(-(0.40f * width + 0.10f * width * Mathf.Sin(t * Mathf.PI * 0.5f)),
                Mathf.Lerp(rimRadius, rc, t), 99f);
        }

        // left shoulder arc, 180 deg -> 90 deg
        for (int k = 0; k <= ARC; k++)
        {
            float a = Mathf.PI - Mathf.PI * 0.5f * (k / (float)ARC);
            add(-h + r * Mathf.Cos(a), rc + r * Mathf.Sin(a), k == ARC ? -1f : 99f);
        }

        // tread
        for (int k = 1; k < T; k++)
        {
            float u = -h + 2f * h * k / T;
            add(u, outerRadius, h > 1e-4f ? u / h : 0f);
        }

        // right shoulder arc, 90 deg -> 0 deg
        for (int k = 0; k <= ARC; k++)
        {
            float a = Mathf.PI * 0.5f * (1f - k / (float)ARC);
            add(h + r * Mathf.Cos(a), rc + r * Mathf.Sin(a), k == 0 ? 1f : 99f);
        }

        // right sidewall, shoulder -> bead
        for (int k = 1; k <= SW; k++)
        {
            float t = k / (float)SW;
            add(0.40f * width + 0.10f * width * Mathf.Sin((1f - t) * Mathf.PI * 0.5f),
                Mathf.Lerp(rc, rimRadius, t), 99f);
        }

        // inner liner, right -> left (hollow centre)
        for (int k = 1; k < LINER; k++)
        {
            float t = k / (float)LINER;
            add(Mathf.Lerp(0.40f * width, -0.40f * width, t), rimRadius, 99f);
        }

        int P = pts.Count;

        // No duplicated seam column: columns wrap, so RecalculateNormals shades the seam smoothly.
        var verts = new Vector3[P * segments];
        var uvs = new Vector2[P * segments];

        for (int j = 0; j < segments; j++)
        {
            float th = Mathf.PI * 2f * j / segments;
            float s = Mathf.Sin(th);
            float c = Mathf.Cos(th);

            for (int i = 0; i < P; i++)
            {
                float rho = pts[i].y - TreadDepth(treadU[i], j);
                int idx = j * P + i;
                verts[idx] = new Vector3(pts[i].x, rho * s, rho * c);
                uvs[idx] = new Vector2(j / (float)segments * 10f, i / (float)P);
            }
        }

        var tris = new int[segments * P * 6];
        int ti = 0;
        for (int j = 0; j < segments; j++)
        {
            int j1 = (j + 1) % segments;
            for (int i = 0; i < P; i++)
            {
                int i1 = (i + 1) % P;
                int A = j * P + i;
                int B = j * P + i1;
                int C = j1 * P + i1;
                int D = j1 * P + i;
                tris[ti++] = A; tris[ti++] = B; tris[ti++] = C;
                tris[ti++] = A; tris[ti++] = C; tris[ti++] = D;
            }
        }

        Mesh m = new Mesh();
        m.name = "ProceduralTyre";
        if (verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = verts;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Depth (m) the tread surface is cut at (tu = -1..1 across the tread, j = column around the tyre).
    static float TreadDepth(float tu, int j)
    {
        if (tu > 1.5f) return 0f;

        float a = Mathf.Abs(tu);
        float d = 0f;

        // circumferential grooves: wide pair and narrow pair
        d = Mathf.Max(d, 0.0085f * Groove(a, 0.30f, 0.075f));
        d = Mathf.Max(d, 0.0065f * Groove(a, 0.68f, 0.055f));

        // slanted lateral grooves, skewed across the tread and mirrored on both halves; centre rib stays solid
        if (a > 0.12f)
        {
            int shift = Mathf.RoundToInt(a * 3f) * (tu < 0f ? 1 : -1);
            int m = ((j + shift) % 6 + 6) % 6;
            float k = m == 0 ? 1f : ((m == 1 || m == 5) ? 0.45f : 0f);
            d = Mathf.Max(d, 0.0070f * k);
        }

        return d;
    }

    static float Groove(float u, float centre, float halfWidth)
    {
        float x = 1f - Mathf.Abs(u - centre) / halfWidth;
        return Mathf.Clamp01(x * 2f);
    }

    // ------------------------------------------------------------------
    // Particles (dust, rubber puffs) - generated in code, no assets needed
    // ------------------------------------------------------------------
    static Texture2D softDot;
    static Material particleMaterial;

    public static Texture2D SoftDotTexture()
    {
        if (softDot != null) return softDot;

        const int n = 64;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false);
        softDot.name = "SoftDot";
        softDot.wrapMode = TextureWrapMode.Clamp;

        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f;
                float dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smoothstep falloff
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }

        softDot.SetPixels32(px);
        softDot.Apply();
        return softDot;
    }

    public static Material ParticleMaterial()
    {
        if (particleMaterial != null) return particleMaterial;

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
        particleMaterial = new Material(sh);
        particleMaterial.name = "TyrePushParticle";
        particleMaterial.mainTexture = SoftDotTexture();
        return particleMaterial;
    }

    /// <summary>
    /// A world-space particle system that is only driven by ps.Emit(...) calls.
    /// Particles fade in/out and grow like real dust.
    /// </summary>
    public static ParticleSystem CreateDustSystem(Transform parent, string name, int maxParticles)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startSize = 0.25f;
        main.gravityModifier = -0.02f; // dust drifts up very slightly

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = g;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 1.8f)));

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = ParticleMaterial();
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;

        ps.Play();
        return ps;
    }
}
