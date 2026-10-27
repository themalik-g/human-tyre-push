using UnityEngine;

/// <summary>
/// Puts the character's right hand on the stick grip so the arm visibly pushes,
/// pokes and braces. Lives on the same GameObject as the Animator.
/// Requires "IK Pass" enabled on the Animator layer
/// (Tools > Tyre Push > Prepare Player Animator does this).
/// </summary>
[RequireComponent(typeof(Animator))]
public class StickHandIK : MonoBehaviour
{
    public StickTool tool;

    [Range(0f, 1f)] public float idleWeight = 0.85f;
    [Range(0f, 1f)] public float rotationWeight = 0f;
    [Tooltip("Rotate the hand so the grip fits your model (only used when Rotation Weight > 0).")]
    public Vector3 handRotationOffset = Vector3.zero;

    Animator anim;
    float weight;

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (tool == null || anim == null) return;

        float target = tool.CurrentMode == StickTool.Mode.Idle ? idleWeight : 1f;
        weight = Mathf.MoveTowards(weight, target, Time.deltaTime * 6f);

        anim.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        anim.SetIKPosition(AvatarIKGoal.RightHand, tool.GripWorld);

        anim.SetIKRotationWeight(AvatarIKGoal.RightHand, rotationWeight * weight);
        if (rotationWeight > 0f)
        {
            anim.SetIKRotation(AvatarIKGoal.RightHand,
                Quaternion.LookRotation(tool.Direction, Vector3.up) * Quaternion.Euler(handRotationOffset));
        }

        // Keep the elbow out to the side instead of folding through the body
        Transform body = tool.player != null ? tool.player.transform : transform;
        anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0.6f * weight);
        anim.SetIKHintPosition(AvatarIKHint.RightElbow, body.TransformPoint(new Vector3(0.65f, 1.2f, -0.1f)));
    }
}
