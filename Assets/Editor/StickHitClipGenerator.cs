using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class StickHitClipGenerator
{
    private const string PlayerPrefabPath = "Assets/EasyStart Third Person Controller/Prefabs/ThirdPersonController.prefab";
    private const string IdleClipPath = "Assets/EasyStart Third Person Controller/Prefabs/Source/Animations/Unarmed Idle.anim";
    private const string OutputPath = "Assets/Animations/StickHit.anim";

    static StickHitClipGenerator()
    {
        EditorApplication.delayCall += GenerateIfMissing;
    }

    private static void GenerateIfMissing()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputPath) == null)
            Generate();
    }

    public static void Generate()
    {
        GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            AnimationClip idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
            if (idle == null)
                throw new InvalidOperationException("The existing idle clip could not be loaded.");

            idle.SampleAnimation(player, 0f);

            var bones = new (string path, Vector3[] offsets)[]
            {
                ("Armature/Hips/Spine/Spine1/Spine2", new[]
                {
                    Vector3.zero, new Vector3(0f, -8f, 0f), new Vector3(0f, 11f, 0f), new Vector3(0f, 4f, 0f), Vector3.zero
                }),
                ("Armature/Hips/Spine/Spine1/Spine2/RightShoulder", new[]
                {
                    Vector3.zero, new Vector3(-8f, -20f, 10f), new Vector3(8f, 28f, -12f), new Vector3(4f, 12f, -6f), Vector3.zero
                }),
                ("Armature/Hips/Spine/Spine1/Spine2/RightShoulder/RightArm", new[]
                {
                    Vector3.zero, new Vector3(-35f, -42f, -30f), new Vector3(70f, 20f, 32f), new Vector3(34f, 10f, 12f), Vector3.zero
                }),
                ("Armature/Hips/Spine/Spine1/Spine2/RightShoulder/RightArm/RightForeArm", new[]
                {
                    Vector3.zero, new Vector3(-55f, 0f, 0f), new Vector3(64f, 0f, 0f), new Vector3(28f, 0f, 0f), Vector3.zero
                }),
                ("Armature/Hips/Spine/Spine1/Spine2/RightShoulder/RightArm/RightForeArm/RightHand", new[]
                {
                    Vector3.zero, new Vector3(-20f, 0f, 15f), new Vector3(30f, 0f, -12f), new Vector3(12f, 0f, -5f), Vector3.zero
                })
            };

            float[] times = { 0f, 0.18f, 0.42f, 0.54f, 0.75f };
            var clip = new AnimationClip { name = "StickHit", frameRate = 60f, wrapMode = WrapMode.Once };

            foreach ((string path, Vector3[] offsets) in bones)
            {
                Transform bone = player.transform.Find(path);
                if (bone == null)
                    throw new InvalidOperationException($"Expected Generic bone was not found: {path}");

                Quaternion baseRotation = bone.localRotation;
                SetRotationCurves(clip, path, baseRotation, offsets, times);
            }

            AssetDatabase.DeleteAsset(OutputPath);
            AssetDatabase.CreateAsset(clip, OutputPath);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(player);
        }
    }

    private static void SetRotationCurves(AnimationClip clip, string path, Quaternion baseRotation, IReadOnlyList<Vector3> offsets, IReadOnlyList<float> times)
    {
        var x = new AnimationCurve();
        var y = new AnimationCurve();
        var z = new AnimationCurve();
        var w = new AnimationCurve();

        for (int index = 0; index < times.Count; index++)
        {
            Quaternion rotation = baseRotation * Quaternion.Euler(offsets[index]);
            x.AddKey(times[index], rotation.x);
            y.AddKey(times[index], rotation.y);
            z.AddKey(times[index], rotation.z);
            w.AddKey(times[index], rotation.w);
        }

        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.x"), x);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.y"), y);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.z"), z);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.w"), w);
    }
}
