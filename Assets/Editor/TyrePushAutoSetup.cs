using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// First-open setup. Unity can only store references to model files when they are assigned in the
/// editor, so this fills them into TyrePushPrototype.unity automatically (once per machine),
/// enables the animator IK pass and builds the tyre rubber material.
/// It also runs before every player build, so builds always contain the references.
/// </summary>
[InitializeOnLoad]
static class TyrePushAutoSetup
{
    static TyrePushAutoSetup()
    {
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Run;
            return;
        }

        string key = "TyrePush.AutoSetup.v1." + Application.dataPath;
        if (EditorPrefs.GetBool(key, false)) return;
        if (!File.Exists(TyrePushMenu.ScenePath)) return;

        // Only mark as done when every model was found. Otherwise try again next time.
        if (TyrePushMenu.SetupScene(TyrePushMenu.ScenePath))
        {
            EditorPrefs.SetBool(key, true);
            Debug.Log("Tyre Push: auto-setup complete. Open Assets/Scenes/TyrePushPrototype.unity and press Play.");
        }
    }
}

class TyrePushBuildHook : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }

    public void OnPreprocessBuild(BuildReport report)
    {
        TyrePushMenu.SetupScene(TyrePushMenu.ScenePath);
    }
}
