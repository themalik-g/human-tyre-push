using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All game input in one place. Keyboard + mouse and gamepad are bound in code,
/// so no .inputactions asset has to be configured.
///
///   Move   WASD / Left stick          Run    Shift / L3
///   Look   Mouse / Right stick        Jump   Space / A (Cross)
///   Push   Left mouse or E / RT (R2)  Brace  Right mouse or Q / LT (L2)
///   Reset tyre  R / Y (Triangle)
/// </summary>
public class TyreGameInput : MonoBehaviour
{
    InputAction move, lookMouse, lookStick, run, jump, push, brace, reset;

    public Vector2 Move { get { return move.ReadValue<Vector2>(); } }
    public Vector2 MouseLook { get { return lookMouse.ReadValue<Vector2>(); } }
    public Vector2 StickLook { get { return lookStick.ReadValue<Vector2>(); } }
    public bool Run { get { return run.IsPressed(); } }
    public bool JumpPressed { get { return jump.WasPressedThisFrame(); } }
    public float Push { get { return Mathf.Clamp01(push.ReadValue<float>()); } }
    public float Brace { get { return Mathf.Clamp01(brace.ReadValue<float>()); } }
    public bool ResetPressed { get { return reset.WasPressedThisFrame(); } }

    void Awake()
    {
        move = new InputAction("Move", InputActionType.Value, null, null, null, "Vector2");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        move.AddBinding("<Gamepad>/leftStick");

        lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta", null, null, "Vector2");
        lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", null, null, "Vector2");

        run = new InputAction("Run", InputActionType.Button);
        run.AddBinding("<Keyboard>/leftShift");
        run.AddBinding("<Gamepad>/leftStickPress");

        jump = new InputAction("Jump", InputActionType.Button);
        jump.AddBinding("<Keyboard>/space");
        jump.AddBinding("<Gamepad>/buttonSouth");

        push = new InputAction("Push", InputActionType.Value, null, null, null, "Axis");
        push.AddBinding("<Mouse>/leftButton");
        push.AddBinding("<Keyboard>/e");            // for laptops without mouse buttons
        push.AddBinding("<Gamepad>/rightTrigger");

        brace = new InputAction("Brace", InputActionType.Value, null, null, null, "Axis");
        brace.AddBinding("<Mouse>/rightButton");
        brace.AddBinding("<Keyboard>/q");           // for laptops without mouse buttons
        brace.AddBinding("<Gamepad>/leftTrigger");

        reset = new InputAction("ResetTyre", InputActionType.Button);
        reset.AddBinding("<Keyboard>/r");
        reset.AddBinding("<Gamepad>/buttonNorth");
    }

    void OnEnable()
    {
        move.Enable(); lookMouse.Enable(); lookStick.Enable(); run.Enable();
        jump.Enable(); push.Enable(); brace.Enable(); reset.Enable();
    }

    void OnDisable()
    {
        move.Disable(); lookMouse.Disable(); lookStick.Disable(); run.Disable();
        jump.Disable(); push.Disable(); brace.Disable(); reset.Disable();
    }

    void OnDestroy()
    {
        if (move != null) move.Dispose();
        if (lookMouse != null) lookMouse.Dispose();
        if (lookStick != null) lookStick.Dispose();
        if (run != null) run.Dispose();
        if (jump != null) jump.Dispose();
        if (push != null) push.Dispose();
        if (brace != null) brace.Dispose();
        if (reset != null) reset.Dispose();
    }
}
