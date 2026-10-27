using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>One-click setup for the tyre push prototype.</summary>
public static class TyrePushMenu
{
    const string PlayerModelPath = "Assets/models/chracter/X Bot.fbx";
    const string AnimatorPath = "Assets/animations/xbot animtor.controller";
    const string TyreModelPath = "Assets/models/tyre/base.obj";
    const string TyreMaterialPath = "Assets/models/tyre/New Material.mat";
    const string StickModelPath = "Assets/models/stick/base.obj";
    const string StickMaterialPath = "Assets/models/stick/New Material.mat";
    const string DiffusePath = "Assets/models/tyre/texture_diffuse.png";
    const string NormalPath = "Assets/models/tyre/texture_normal.png";
    const string PackedPath = "Assets/models/tyre/tyre_MetalSmooth.png";
    const string RubberMaterialPath = "Assets/models/tyre/Tyre_Rubber.mat";
    internal const string ScenePath = "Assets/Scenes/TyrePushPrototype.unity";

    [MenuItem("Tools/Tyre Push/Create Prototype Scene")]
    static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        PrepareAnimatorInternal();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddBootstrapInternal();

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("Tyre Push: scene saved to " + ScenePath + ". Press Play.");
    }

    [MenuItem("Tools/Tyre Push/Add Bootstrap To Open Scene")]
    static void AddBootstrap()
    {
        AddBootstrapInternal();
    }

    [MenuItem("Tools/Tyre Push/Build Tyre Rubber Material")]
    static void BuildRubber()
    {
        Material m = BuildRubberMaterial();
        if (m != null) Selection.activeObject = m;
    }

    [MenuItem("Tools/Tyre Push/Prepare Player Animator (IK pass + parameters)")]
    static void PrepareAnimator()
    {
        PrepareAnimatorInternal();
    }

    static void AddBootstrapInternal()
    {
        GameObject go = new GameObject("TyrePushBootstrap");
        TyrePushBootstrap b = go.AddComponent<TyrePushBootstrap>();
        FillBootstrap(b);

        Undo.RegisterCreatedObjectUndo(go, "Add Tyre Push Bootstrap");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    // Fills any empty model/material slot from the project's own assets.
    // Returns true when the player, stick and tyre models were all found.
    internal static bool FillBootstrap(TyrePushBootstrap b)
    {
        if (b.playerModel == null) b.playerModel = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
        if (b.playerAnimator == null) b.playerAnimator = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorPath);
        if (b.tyreModel == null) b.tyreModel = AssetDatabase.LoadAssetAtPath<GameObject>(TyreModelPath);
        if (b.stickModel == null) b.stickModel = AssetDatabase.LoadAssetAtPath<GameObject>(StickModelPath);
        if (b.stickMaterial == null) b.stickMaterial = AssetDatabase.LoadAssetAtPath<Material>(StickMaterialPath);

        if (b.tyreMaterial == null)
        {
            Material rubber = BuildRubberMaterial();
            b.tyreMaterial = rubber != null ? rubber : AssetDatabase.LoadAssetAtPath<Material>(TyreMaterialPath);
        }

        return b.playerModel != null && b.stickModel != null && b.tyreModel != null;
    }

    // Opens the prototype scene in the background, fills its bootstrap, saves and closes it.
    internal static bool SetupScene(string scenePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        if (!System.IO.File.Exists(scenePath)) return false;

        PrepareAnimatorInternal();

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        bool complete = false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            TyrePushBootstrap b = root.GetComponentInChildren<TyrePushBootstrap>(true);
            if (b == null) continue;

            complete = FillBootstrap(b);
            EditorUtility.SetDirty(b);
        }

        EditorSceneManager.SaveScene(scene);
        EditorSceneManager.CloseScene(scene, true);
        return complete;
    }

    [MenuItem("Tools/Tyre Push/Run Setup On Prototype Scene")]
    static void RunSetupMenu()
    {
        bool ok = SetupScene(ScenePath);
        Debug.Log(ok ? "Tyre Push: setup complete." : "Tyre Push: setup incomplete (assets still importing or scene missing). Try again in a moment.");
    }

    static void PrepareAnimatorInternal()
    {
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath);
        if (ctrl == null)
        {
            Debug.LogWarning("Tyre Push: animator controller not found at " + AnimatorPath);
            return;
        }

        // IK Pass on every layer, so the hand can be pinned to the stick grip
        AnimatorControllerLayer[] layers = ctrl.layers;
        for (int i = 0; i < layers.Length; i++) layers[i].iKPass = true;
        ctrl.layers = layers;

        string[] names = { "run", "sprint", "air", "crouch", "hitTyre" };
        foreach (string n in names)
        {
            bool has = false;
            foreach (AnimatorControllerParameter p in ctrl.parameters)
                if (p.name == n) { has = true; break; }

            if (!has) ctrl.AddParameter(n, AnimatorControllerParameterType.Bool);
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------
    // Upgraded tyre rubber material. Built from the project's own textures:
    //  - albedo:  texture_diffuse.png (clean). The old material used shaded.png,
    //             which has lighting baked in, so light was applied twice.
    //  - metal:   0. The old material had metallic 0.754, which made rubber look like chrome.
    //  - smooth:  tyre_MetalSmooth.png (from texture_roughness.png, remapped to a real rubber
    //             range: matte tread, slightly satin sidewall).
    //  - normal:  texture_normal.png at scale 1 (old value 3 exaggerated the tread).
    // The old "New Material.mat" is left untouched.
    // ------------------------------------------------------------------
    static Material BuildRubberMaterial()
    {
        Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath);
        if (diffuse == null)
        {
            Debug.LogWarning("Tyre Push: tyre textures not found, keeping the existing material.");
            return null;
        }

        // Data maps must be linear, not sRGB. Higher anisotropy keeps the tread sharp at grazing angles.
        ConfigureTexture(DiffusePath, true, false);
        ConfigureTexture(NormalPath, false, true);
        ConfigureTexture(PackedPath, false, false);

        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        Texture2D packed = AssetDatabase.LoadAssetAtPath<Texture2D>(PackedPath);

        bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
        Shader sh = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Standard");

        Material m = AssetDatabase.LoadAssetAtPath<Material>(RubberMaterialPath);
        bool isNew = m == null;
        if (isNew) m = new Material(sh);
        else m.shader = sh;

        SetTexture(m, "_MainTex", diffuse);
        SetTexture(m, "_BaseMap", diffuse);
        SetTexture(m, "_BumpMap", normal);
        SetTexture(m, "_MetallicGlossMap", packed);

        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 1f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
        if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);
        if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 1f);
        if (m.HasProperty("_GlossyReflections")) m.SetFloat("_GlossyReflections", 1f);

        m.EnableKeyword("_NORMALMAP");
        m.EnableKeyword("_METALLICGLOSSMAP");
        m.EnableKeyword("_METALLICSPECGLOSSMAP");

        if (isNew) AssetDatabase.CreateAsset(m, RubberMaterialPath);
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();

        Debug.Log("Tyre Push: rubber material ready at " + RubberMaterialPath);
        return m;
    }

    static void SetTexture(Material m, string property, Texture tex)
    {
        if (tex != null && m.HasProperty(property)) m.SetTexture(property, tex);
    }

    static void ConfigureTexture(string path, bool srgb, bool normalMap)
    {
        TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;

        bool changed = false;

        if (normalMap)
        {
            if (imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; changed = true; }
        }
        else if (imp.sRGBTexture != srgb)
        {
            imp.sRGBTexture = srgb;
            changed = true;
        }

        if (imp.anisoLevel < 8) { imp.anisoLevel = 8; changed = true; }

        if (changed) imp.SaveAndReimport();
    }
}
