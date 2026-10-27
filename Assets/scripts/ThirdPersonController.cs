using UnityEngine;

public class ThirdPersonController : MonoBehaviour
{
    [Tooltip("Speed at which the character moves.")]
    public float velocity = 5f;

    [Tooltip("This value is added to the speed while sprinting.")]
    public float sprintAdittion = 3.5f;

    [Tooltip("The higher the value, the higher the character will jump.")]
    public float jumpForce = 18f;

    [Tooltip("The higher the value, the longer the character stays in the jump.")]
    public float jumpTime = 0.85f;

    [Space]

    [Tooltip("Force that pulls the player down.")]
    public float gravity = 9.8f;

    float jumpElapsedTime = 0f;

    bool isJumping = false;
    bool isSprinting = false;
    bool isCrouching = false;

    float inputHorizontal;
    float inputVertical;
    bool inputJump;
    bool inputCrouch;
    bool inputSprint;

    Animator animator;
    CharacterController cc;

    // Controlled by GameMechanics.
    bool pushModeActive = false;
    float pushMoveSpeed = 0f;
    Vector3 lockedPushDirection = Vector3.forward;

    // GameMechanics supplies a target position; this controller remains the
    // only place that calls CharacterController.Move().
    bool pushTargetActive = false;
    Vector3 pushTargetPosition = Vector3.zero;
    float pushTargetMoveSpeed = 0f;

    public bool IsForwardPressed => inputVertical > 0.01f;

    public void SetPushMode(
        bool enabled,
        float speed,
        Vector3 direction)
    {
        pushModeActive = enabled;
        pushMoveSpeed = Mathf.Max(0f, speed);

        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
            lockedPushDirection = direction.normalized;
    }

    public void SetPushTarget(
        Vector3 worldPosition,
        float moveSpeed,
        bool enabled)
    {
        worldPosition.y = transform.position.y;
        pushTargetPosition = worldPosition;
        pushTargetMoveSpeed = Mathf.Max(0f, moveSpeed);
        pushTargetActive = enabled;
    }

    public void ClearPushTarget()
    {
        pushTargetActive = false;
        pushTargetMoveSpeed = 0f;
    }

    void Start()
    {
        cc = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        if (animator == null)
        {
            Debug.LogWarning(
                "Player has no Animator component. Animations will not work."
            );
        }
    }

    void Update()
    {
        inputHorizontal = Input.GetAxis("Horizontal");
        inputVertical = Input.GetAxis("Vertical");
        inputJump = Input.GetAxis("Jump") == 1f;
        inputSprint = Input.GetAxis("Fire3") == 1f;

        inputCrouch =
            Input.GetKeyDown(KeyCode.LeftControl) ||
            Input.GetKeyDown(KeyCode.JoystickButton1);

        if (inputCrouch)
            isCrouching = !isCrouching;

        if (cc.isGrounded && animator != null)
        {
            animator.SetBool("crouch", isCrouching);

            float minimumSpeed = 0.9f;

            animator.SetBool(
                "run",
                cc.velocity.magnitude > minimumSpeed
            );

            isSprinting =
                cc.velocity.magnitude > minimumSpeed &&
                inputSprint;

            animator.SetBool("sprint", isSprinting);
        }

        if (animator != null)
        {
            animator.SetBool(
                "air",
                cc.isGrounded == false
            );
        }

        if (inputJump && cc.isGrounded)
            isJumping = true;

        HeadHittingDetect();

        // IMPORTANT JITTER FIX:
        // CharacterController movement belongs on rendered frames.
        HandleMovement();
    }

    private void HandleMovement()
    {
        float velocityAdittion = 0f;

        if (isSprinting)
            velocityAdittion = sprintAdittion;

        if (isCrouching)
            velocityAdittion = -(velocity * 0.50f);

        float directionY = 0f;

        if (isJumping)
        {
            directionY =
                Mathf.SmoothStep(
                    jumpForce,
                    jumpForce * 0.30f,
                    jumpElapsedTime / jumpTime
                ) * Time.deltaTime;

            jumpElapsedTime += Time.deltaTime;

            if (jumpElapsedTime >= jumpTime)
            {
                isJumping = false;
                jumpElapsedTime = 0f;
            }
        }

        directionY -= gravity * Time.deltaTime;

        Vector3 horizontalDirection;

        // PUSH MODE
        if (pushModeActive)
        {
            horizontalDirection =
                lockedPushDirection *
                pushMoveSpeed *
                Time.deltaTime;

            // Add the correction to the SAME movement vector.
            // This keeps gravity/grounding and push correction inside one
            // CharacterController.Move() call.
            if (pushTargetActive)
            {
                Vector3 toTarget =
                    pushTargetPosition -
                    transform.position;

                toTarget.y = 0f;

                float distance = toTarget.magnitude;

                if (distance > 0.001f)
                {
                    float moveAmount = Mathf.Min(
                        pushTargetMoveSpeed * Time.deltaTime,
                        distance
                    );

                    horizontalDirection +=
                        toTarget.normalized *
                        moveAmount;
                }
            }

            if (lockedPushDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(
                        lockedPushDirection,
                        Vector3.up
                    );

                transform.rotation =
                    Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        1f - Mathf.Exp(-14f * Time.deltaTime)
                    );
            }
        }
        else
        {
            // NORMAL THIRD-PERSON MOVEMENT
            float currentMoveSpeed =
                velocity + velocityAdittion;

            float directionX =
                inputHorizontal *
                currentMoveSpeed *
                Time.deltaTime;

            float directionZ =
                inputVertical *
                currentMoveSpeed *
                Time.deltaTime;

            Vector3 forward = Camera.main.transform.forward;
            Vector3 right = Camera.main.transform.right;

            forward.y = 0f;
            right.y = 0f;

            forward.Normalize();
            right.Normalize();

            forward *= directionZ;
            right *= directionX;

            horizontalDirection =
                forward + right;

            if (directionX != 0f || directionZ != 0f)
            {
                float angle =
                    Mathf.Atan2(
                        horizontalDirection.x,
                        horizontalDirection.z
                    ) * Mathf.Rad2Deg;

                Quaternion rotation =
                    Quaternion.Euler(
                        0f,
                        angle,
                        0f
                    );

                transform.rotation =
                    Quaternion.Slerp(
                        transform.rotation,
                        rotation,
                        0.15f
                    );
            }
        }

        Vector3 verticalDirection =
            Vector3.up * directionY;

        Vector3 movement =
            verticalDirection +
            horizontalDirection;

        cc.Move(movement);
    }

    void HeadHittingDetect()
    {
        float headHitDistance = 1.1f;

        Vector3 ccCenter =
            transform.TransformPoint(cc.center);

        float hitCalc =
            cc.height / 2f *
            headHitDistance;

        if (Physics.Raycast(
            ccCenter,
            Vector3.up,
            hitCalc))
        {
            jumpElapsedTime = 0f;
            isJumping = false;
        }
    }
}
