using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
public class GameMechanics : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private ThirdPersonController playerController;

    [Header("Forward Push")]
    [SerializeField] private float pushForce = 15f;
    [SerializeField] private float maxForwardSpeed = 5f;

    [Header("Push Detection")]
    [Tooltip("Maximum horizontal distance from the tyre at which the player can start pushing.")]
    [Min(0.1f)]
    [SerializeField] private float pushRange = 2.5f;

    [Tooltip("How far around the rear of the tyre the player is allowed to stand. 90 = entire rear half.")]
    [Range(5f, 90f)]
    [SerializeField] private float rearConeAngle = 70f;

    [Tooltip("How accurately the player must look toward the tyre. 90 = very forgiving, smaller = stricter.")]
    [Range(5f, 90f)]
    [SerializeField] private float facingAngle = 60f;

    [Header("Push Adjustment")]
    [Tooltip("Enable soft assistance that moves the player into the ideal push position when LMB is held.")]
    [SerializeField] private bool enablePushAdjustment = true;

    [Tooltip("Maximum distance from the tyre at which adjustment can begin.")]
    [Min(0.1f)]
    [SerializeField] private float adjustmentRange = 3.25f;

    [Tooltip("Desired horizontal distance between the player and the tyre while pushing.")]
    [Min(0.1f)]
    [SerializeField] private float idealPushDistance = 1.25f;

    [Tooltip("How quickly the player is softly moved toward the ideal push position.")]
    [Min(0.01f)]
    [SerializeField] private float adjustmentMoveSpeed = 3f;

    [Tooltip("How quickly the player rotates toward the tyre/push direction.")]
    [Min(0.01f)]
    [SerializeField] private float adjustmentRotationSpeed = 8f;

    [Tooltip("Player is considered aligned when this close to the ideal push position.")]
    [Min(0.01f)]
    [SerializeField] private float adjustmentPositionTolerance = 0.12f;

    [Tooltip("Player is considered aligned when this close to facing the tyre.")]
    [Range(1f, 45f)]
    [SerializeField] private float adjustmentFacingTolerance = 12f;

    [Tooltip("Maximum sideways correction from the tyre's rear direction. Smaller values keep the adjustment more conservative.")]
    [Range(0f, 90f)]
    [SerializeField] private float adjustmentSideAngle = 75f;

    [Header("Push Availability UI")]
    [Tooltip("Optional UI Image that turns green when the player is in a valid push position, otherwise grey.")]
    [SerializeField] private Image pushAvailabilityIcon;
    [SerializeField] private Color unavailableColor = Color.gray;
    [SerializeField] private Color availableColor = Color.green;

    [Header("Turning")]
    [SerializeField] private float turnSpeed = 60f;

    [Header("Turn Player Follow")]
    [Min(0.01f)]
    [SerializeField] private float turnPlayerFollowSpeed = 8f;

    [Header("Turn Threshold")]
    [SerializeField] private float leftTurnThreshold = 45f;
    [SerializeField] private float rightTurnThreshold = 45f;

    [Header("Collider Switch")]
    [SerializeField] private Collider currentPushCollider;
    [SerializeField] private GameObject normalColliderObject;

    [Tooltip("Delay only after normal forward-push LMB release. Turn-threshold switching stays immediate.")]
    [Min(0f)]
    [SerializeField] private float forwardPushColliderSwitchDelay = 0.15f;

    [Header("Visual Tyre")]
    [SerializeField] private Transform tyreVisual;
    [SerializeField] private float tyreRadius = 0.58f;
    [SerializeField] private Vector3 rollAxis = Vector3.right;

    [Header("Tyre Speed UI")]
    [Tooltip("TextMeshPro text used to display the tyre's current physical speed.")]
    [SerializeField] private TMP_Text tyreSpeedText;

    [Tooltip("Display speed in kilometres per hour instead of metres per second.")]
    [SerializeField] private bool displaySpeedInKmh = true;

    [Tooltip("Number of decimal places shown in the speed text.")]
    [Range(0, 2)]
    [SerializeField] private int speedDecimalPlaces = 1;

    [Header("Tyre Turn / Tilt UI")]
    [Tooltip("UI Image used as the left-to-right turn/tilt bar background.")]
    [SerializeField] private Image tyreTiltBar;

    [Tooltip("Small UI line/cross that moves left and right across the bar.")]
    [SerializeField] private RectTransform tyreTiltMarker;

    [Tooltip("TextMeshPro text that displays the current tyre turn angle.")]
    [SerializeField] private TMP_Text tyreTiltDegreeText;

    [Tooltip("Angle represented by each end of the bar. 45 means -45 to +45 degrees.")]
    [Min(0.1f)]
    [SerializeField] private float tiltDisplayMaxAngle = 45f;

    [Tooltip("Show a minus/plus sign, for example -25° or +25°.")]
    [SerializeField] private bool showSignedTiltAngle = true;

    [Header("Free Roll / Coast")]
    [Tooltip("Drag used after push input is released. Lower = tyre travels farther. Try 0.02 - 0.15.")]
    [Min(0f)]
    [SerializeField] private float freeRollLinearDamping = 0.05f;

    [Tooltip("Rotational drag used after push input is released. Lower = tyre keeps rolling longer. Try 0.02 - 0.15.")]
    [Min(0f)]
    [SerializeField] private float freeRollAngularDamping = 0.05f;

    [Tooltip("When push is released, convert the current forward speed into matching physical wheel spin so the tyre does not suddenly skid before it starts rolling.")]
    [SerializeField] private bool matchPhysicalSpinOnRelease = true;

    private Rigidbody tyreRigidbody;
    private Animator playerAnimator;

    private bool pushActive;
    private float currentTurnAngle = 0f;
    private bool turnThresholdTriggered = false;
    private bool pushAdjustmentActive = false;

    // Input is read once per rendered frame, then consumed by physics.
    private float turnInput = 0f;

    // Pending collider switch used only after normal LMB release.
    private Coroutine delayedColliderSwitchRoutine;

    // Preserve the Rigidbody damping you already configured for controlled pushing.
    private float controlledLinearDamping;
    private float controlledAngularDamping;

    // Tracks real Rigidbody movement so the visual tyre keeps rolling even
    // when forward velocity is temporarily small during a turn.
    private Vector3 lastVisualRollPosition;
    private bool visualRollInitialized;

    private void Awake()
    {
        tyreRigidbody = GetComponent<Rigidbody>();

        // Keep your current Rigidbody damping for controlled push mode.
        controlledLinearDamping = tyreRigidbody.linearDamping;
        controlledAngularDamping = tyreRigidbody.angularDamping;

        // Smooth Rigidbody motion between physics steps.
        tyreRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        if (currentPushCollider == null)
            currentPushCollider = GetComponent<Collider>();

        // DEFAULT COLLIDER STATE:
        // Sphere/current push collider is active at startup.
        // Normal collider object is inactive until push stops.
        if (currentPushCollider != null)
            currentPushCollider.enabled = true;

        if (normalColliderObject != null)
            normalColliderObject.SetActive(false);

        if (tyreVisual == null && transform.childCount > 0)
            tyreVisual = transform.GetChild(0);

        if (playerController == null)
            playerController = FindFirstObjectByType<ThirdPersonController>();

        if (playerController != null)
        {
            playerAnimator = playerController.GetComponent<Animator>();

            if (playerAnimator == null)
                playerAnimator = playerController.GetComponentInChildren<Animator>();
        }

        SetHitTyreAnimation(false);
    }

    private void Update()
    {
        if (playerController == null || tyreRigidbody == null)
            return;

        UpdateTyreSpeedText();
        UpdateTyreTiltUI();

        bool lmbHeld = Input.GetMouseButton(0);
        bool conditionsValid = CheckPushConditions();

        // UI always reflects whether the player is currently in a valid
        // push position, even when LMB is not being held.
        UpdatePushAvailabilityIcon(conditionsValid);

        // =====================================================
        // RELEASE INPUT -> REAL FREE PHYSICS / COAST
        // =====================================================
        // Previously pushActive was only set to false here.
        // That left X/Z rotation frozen and left the controlled
        // collider active, which made the tyre look like it braked.
        if (!lmbHeld)
        {
            if (pushActive)
                ExitControlledPush(false);

            // Allows a new push after the mouse is released.
            turnThresholdTriggered = false;
        }

        // =====================================================
        // START CONTROLLED PUSH OR POSITION ADJUSTMENT
        // =====================================================
        // IMPORTANT: LMB always gets a chance to adjust the player to the
        // fixed push point first. We no longer immediately start pushing
        // just because the player happens to be inside the valid detection
        // zone. This is what makes the final push position predictable.
        if (!pushActive &&
            !turnThresholdTriggered &&
            lmbHeld)
        {
            if (enablePushAdjustment)
            {
                if (!pushAdjustmentActive)
                    TryStartPushAdjustment();
            }
            else if (conditionsValid)
            {
                EnterControlledPush();
            }
        }
        else if (!lmbHeld || pushActive)
        {
            StopPushAdjustment();
        }

        // IMPORTANT FIX:
        // TryStartPushAdjustment() only activates the adjustment state.
        // UpdatePushAdjustment() must run every frame afterward so the
        // CharacterController receives the target position and can move
        // the player into the push position. Without this call, the player
        // enters push mode with speed 0 and simply stops near the tyre.
        if (pushAdjustmentActive)
        {
            UpdatePushAdjustment();
        }

        // Cache turn input in Update.
        turnInput = 0f;

        if (pushActive)
        {
            if (Input.GetKey(KeyCode.Q))
                turnInput = -1f;

            if (Input.GetKey(KeyCode.E))
                turnInput = 1f;
        }

        // Player follows only the tyre's forward speed while pushing.
        if (pushActive)
        {
            Vector3 pushDirection = transform.forward;
            pushDirection.y = 0f;

            if (pushDirection.sqrMagnitude > 0.001f)
                pushDirection.Normalize();

            float tyreSpeed = Vector3.Dot(
                tyreRigidbody.linearVelocity,
                pushDirection
            );

            tyreSpeed = Mathf.Max(0f, tyreSpeed);

            playerController.SetPushMode(
                true,
                tyreSpeed,
                pushDirection
            );
        }
        else if (!pushAdjustmentActive)
        {
            playerController.ClearPushTarget();
            playerController.SetPushMode(
                false,
                0f,
                transform.forward
            );
        }

        // Keep the player target updated every frame while pushing so the
        // fixed point follows the tyre during turning.
        if (pushActive)
        {
            Vector3 targetForward = transform.forward;
            targetForward.y = 0f;

            if (targetForward.sqrMagnitude > 0.001f)
            {
                targetForward.Normalize();

                Vector3 targetPosition =
                    transform.position -
                    targetForward * idealPushDistance;

                targetPosition.y =
                    playerController.transform.position.y;

                playerController.SetPushTarget(
                    targetPosition,
                    turnPlayerFollowSpeed,
                    true
                );
            }
        }

        SetHitTyreAnimation(pushActive);
    }

    private void FixedUpdate()
    {
        if (!pushActive || tyreRigidbody == null)
            return;

        // Calculate the direction AFTER this frame's turn input.
        // The push force therefore follows the new tyre heading immediately.
        Quaternion nextRotation = tyreRigidbody.rotation;

        if (turnInput != 0f)
        {
            float turnAmount =
                turnInput *
                turnSpeed *
                Time.fixedDeltaTime;

            nextRotation =
                tyreRigidbody.rotation *
                Quaternion.Euler(0f, turnAmount, 0f);

            tyreRigidbody.MoveRotation(nextRotation);

            currentTurnAngle += turnAmount;

            bool exceededLeft =
                currentTurnAngle <= -Mathf.Abs(leftTurnThreshold);

            bool exceededRight =
                currentTurnAngle >= Mathf.Abs(rightTurnThreshold);

            if (exceededLeft || exceededRight)
            {
                StopPushFromTurnThreshold();
                return;
            }
        }

        Vector3 pushDirection =
            nextRotation * Vector3.forward;

        pushDirection.y = 0f;

        if (pushDirection.sqrMagnitude <= 0.001f)
            return;

        pushDirection.Normalize();

        float forwardSpeed = Vector3.Dot(
            tyreRigidbody.linearVelocity,
            pushDirection
        );

        if (forwardSpeed < maxForwardSpeed)
        {
            tyreRigidbody.AddForce(
                pushDirection * pushForce,
                ForceMode.Force
            );
        }
    }

    private void LateUpdate()
    {
        if (tyreRigidbody == null ||
            tyreVisual == null ||
            tyreRadius <= 0.01f)
            return;

        // During controlled push the Rigidbody's X/Z rotation is frozen.
        // Roll the visual from the tyre's REAL position change instead of
        // estimating rotation from forward velocity. This keeps the visual
        // rolling during turns and prevents it from appearing to stop.
        if (pushActive)
        {
            if (!visualRollInitialized)
            {
                lastVisualRollPosition = transform.position;
                visualRollInitialized = true;
                return;
            }

            Vector3 displacement =
                transform.position -
                lastVisualRollPosition;

            lastVisualRollPosition =
                transform.position;

            Vector3 forward = transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.001f)
            {
                forward.Normalize();

                float travelledDistance =
                    Vector3.Dot(displacement, forward);

                float rotationDegrees =
                    (travelledDistance / tyreRadius) *
                    Mathf.Rad2Deg;

                tyreVisual.Rotate(
                    rollAxis.normalized,
                    -rotationDegrees,
                    Space.Self
                );
            }
        }
        else
        {
            visualRollInitialized = false;
        }
    }

    // =========================================================
    // SOFT PUSH ADJUSTMENT
    // =========================================================
    private void TryStartPushAdjustment()
    {
        if (playerController == null)
            return;

        Transform player = playerController.transform;

        Vector3 tyreToPlayer = player.position - transform.position;
        tyreToPlayer.y = 0f;

        float distance = tyreToPlayer.magnitude;

        // Adjustment is allowed from the larger assistance range, not just
        // the final push range. This lets the system pull the player closer
        // OR move them backward when they are too close.
        if (distance <= 0.001f || distance > adjustmentRange)
        {
            StopPushAdjustment();
            return;
        }

        Vector3 tyreForward = transform.forward;
        tyreForward.y = 0f;

        if (tyreForward.sqrMagnitude <= 0.001f)
            return;

        tyreForward.Normalize();

        // Only start adjustment when the player is somewhere in the
        // rear/rear-side interaction area. The player's current facing is
        // intentionally NOT checked here because rotation is one of the
        // things this system fixes.
        Vector3 playerFromTyre = tyreToPlayer.normalized;
        float rearDot = Vector3.Dot(-tyreForward, playerFromTyre);
        float requiredRearDot = Mathf.Cos(
            adjustmentSideAngle * Mathf.Deg2Rad
        );

        if (rearDot < requiredRearDot)
        {
            StopPushAdjustment();
            return;
        }

        pushAdjustmentActive = true;

        // Lock normal player input while GameMechanics moves the CharacterController
        // to the exact target point. Speed is zero here because the movement
        // itself is supplied by UpdatePushAdjustment().
        playerController.SetPushMode(
            true,
            0f,
            lookDirectionSafe(player, transform)
        );
    }

    private void UpdatePushAdjustment()
    {
        if (!pushAdjustmentActive || playerController == null)
            return;

        if (!Input.GetMouseButton(0) || pushActive)
        {
            StopPushAdjustment();
            return;
        }

        Transform player = playerController.transform;

        Vector3 tyreForward = transform.forward;
        tyreForward.y = 0f;

        if (tyreForward.sqrMagnitude <= 0.001f)
        {
            StopPushAdjustment();
            return;
        }

        tyreForward.Normalize();

        Vector3 desiredPosition =
            transform.position -
            tyreForward * idealPushDistance;

        desiredPosition.y = player.position.y;

        // IMPORTANT: GameMechanics does NOT move the CharacterController.
        // It only supplies the target. ThirdPersonController performs the
        // single cc.Move() together with gravity and normal movement.
        playerController.SetPushTarget(
            desiredPosition,
            adjustmentMoveSpeed,
            true
        );

        Vector3 lookDirection =
            transform.position - player.position;

        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude > 0.001f)
            lookDirection.Normalize();
        else
            lookDirection = transform.forward;

        playerController.SetPushMode(
            true,
            0f,
            lookDirection
        );

        float finalDistance =
            HorizontalDistance(
                player.position,
                desiredPosition
            );

        Vector3 finalLookDirection =
            transform.position - player.position;

        finalLookDirection.y = 0f;

        float finalFacingAngle = 999f;

        if (finalLookDirection.sqrMagnitude > 0.001f)
        {
            finalLookDirection.Normalize();

            finalFacingAngle = Vector3.Angle(
                FlattenY(player.forward),
                finalLookDirection
            );
        }

        if (finalDistance <= adjustmentPositionTolerance &&
            finalFacingAngle <= adjustmentFacingTolerance)
        {
            pushAdjustmentActive = false;
            playerController.ClearPushTarget();
            EnterControlledPush();
        }
    }

    private static Vector3 lookDirectionSafe(Transform player, Transform tyre)
    {
        Vector3 direction = tyre.position - player.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            return player.forward;

        return direction.normalized;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static Vector3 FlattenY(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private void StopPushAdjustment()
    {
        if (!pushAdjustmentActive)
            return;

        pushAdjustmentActive = false;

        if (playerController != null && !pushActive)
        {
            playerController.ClearPushTarget();
            playerController.SetPushMode(
                false,
                0f,
                transform.forward
            );
        }
    }

    // =========================================================
    // CONTROLLED PUSH MODE
    // =========================================================
    private void EnterControlledPush()
    {
        // Cancel a pending release-delay if the player starts pushing again.
        if (delayedColliderSwitchRoutine != null)
        {
            StopCoroutine(delayedColliderSwitchRoutine);
            delayedColliderSwitchRoutine = null;
        }

        // Controlled push always uses the Sphere/current push collider.
        if (currentPushCollider != null)
            currentPushCollider.enabled = true;

        if (normalColliderObject != null)
            normalColliderObject.SetActive(false);

        pushActive = true;
        currentTurnAngle = 0f;
        lastVisualRollPosition = transform.position;
        visualRollInitialized = true;

        // Restore the damping values the Rigidbody had before free-roll mode.
        tyreRigidbody.linearDamping = controlledLinearDamping;
        tyreRigidbody.angularDamping = controlledAngularDamping;

        // Your existing controlled-push behaviour.
        tyreRigidbody.constraints |=
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        if (currentPushCollider != null)
            currentPushCollider.enabled = true;

        if (normalColliderObject != null)
            normalColliderObject.SetActive(false);
    }

    // =========================================================
    // FREE ROLL MODE
    // =========================================================
    private void ExitControlledPush(bool thresholdTriggered)
    {
        pushActive = false;
        turnInput = 0f;
        turnThresholdTriggered = thresholdTriggered;

        // CRITICAL FIX:
        // Give the tyre its physical rolling axes back.
        tyreRigidbody.constraints &=
            ~(RigidbodyConstraints.FreezeRotationX |
              RigidbodyConstraints.FreezeRotationZ);

        // Keep the existing LINEAR momentum exactly as it is.
        // Give the now-free Rigidbody the physical wheel spin that matches
        // its current forward velocity. This makes the handoff from the
        // controlled visual roll to real physics much more natural.
        if (matchPhysicalSpinOnRelease && tyreRadius > 0.01f)
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.001f)
            {
                forward.Normalize();

                float forwardSpeed = Vector3.Dot(
                    tyreRigidbody.linearVelocity,
                    forward
                );

                Vector3 worldRollAxis = transform.TransformDirection(
                    rollAxis.normalized
                );

                float targetRollSpeed =
                    -forwardSpeed / tyreRadius; // radians / second

                Vector3 angularVelocity = tyreRigidbody.angularVelocity;

                float currentRollSpeed = Vector3.Dot(
                    angularVelocity,
                    worldRollAxis
                );

                angularVelocity +=
                    worldRollAxis *
                    (targetRollSpeed - currentRollSpeed);

                tyreRigidbody.angularVelocity = angularVelocity;
            }
        }

        // These two values are your coast controls.
        tyreRigidbody.linearDamping = freeRollLinearDamping;
        tyreRigidbody.angularDamping = freeRollAngularDamping;
        tyreRigidbody.WakeUp();

        // COLLIDER HANDOFF:
        // Normal forward-push release gets a small delay.
        // Turn-threshold exit remains immediate.
        if (thresholdTriggered || forwardPushColliderSwitchDelay <= 0f)
        {
            ApplyNormalColliderImmediately();
        }
        else
        {
            if (delayedColliderSwitchRoutine != null)
                StopCoroutine(delayedColliderSwitchRoutine);

            delayedColliderSwitchRoutine =
                StartCoroutine(DelayedNormalColliderSwitch());
        }

        SetHitTyreAnimation(false);

        if (playerController != null)
        {
            playerController.ClearPushTarget();
            playerController.SetPushMode(
                false,
                0f,
                transform.forward
            );
        }
    }

    private System.Collections.IEnumerator DelayedNormalColliderSwitch()
    {
        yield return new WaitForSeconds(forwardPushColliderSwitchDelay);

        ApplyNormalColliderImmediately();
        delayedColliderSwitchRoutine = null;
    }

    private void ApplyNormalColliderImmediately()
    {
        if (currentPushCollider != null)
            currentPushCollider.enabled = false;

        if (normalColliderObject != null)
            normalColliderObject.SetActive(true);
    }

    private void SetHitTyreAnimation(bool active)
    {
        if (playerAnimator != null)
            playerAnimator.SetBool("hitTyre", active);
    }

    private void StopPushFromTurnThreshold()
    {
        // Same free-physics transition as releasing LMB,
        // but block immediate restart until LMB is released.
        ExitControlledPush(true);
    }

    private bool CheckPushConditions()
    {
        if (playerController == null)
            return false;

        Transform player = playerController.transform;

        // ---------------------------------------------------------
        // 1. HORIZONTAL DISTANCE
        // ---------------------------------------------------------
        // Ignore height so a small difference in ground level does not
        // make the interaction fail.
        Vector3 tyreToPlayer = player.position - transform.position;
        tyreToPlayer.y = 0f;

        float distance = tyreToPlayer.magnitude;

        if (distance <= 0.001f || distance > pushRange)
        {
            Debug.DrawLine(
                player.position,
                transform.position,
                Color.red
            );

            return false;
        }

        Vector3 tyreToPlayerDirection = tyreToPlayer / distance;

        // ---------------------------------------------------------
        // 2. PLAYER MUST BE IN THE REAR / SLIGHT-SIDE ZONE
        // ---------------------------------------------------------
        Vector3 tyreForward = transform.forward;
        tyreForward.y = 0f;

        if (tyreForward.sqrMagnitude <= 0.001f)
            return false;

        tyreForward.Normalize();

        // -tyreForward points toward the rear of the tyre.
        // A larger rearConeAngle allows the player to stand farther toward
        // either side while still being considered a valid push position.
        float rearDot = Vector3.Dot(
            -tyreForward,
            tyreToPlayerDirection
        );

        float requiredRearDot = Mathf.Cos(
            rearConeAngle * Mathf.Deg2Rad
        );

        bool behindTyre = rearDot >= requiredRearDot;

        // ---------------------------------------------------------
        // 3. PLAYER MUST ACTUALLY LOOK AT THE TYRE
        // ---------------------------------------------------------
        Vector3 playerForward = player.forward;
        playerForward.y = 0f;

        if (playerForward.sqrMagnitude <= 0.001f)
            return false;

        playerForward.Normalize();

        // This is the important facing test:
        // compare the player's forward direction with the direction FROM
        // the player TO the tyre. We do NOT compare playerForward directly
        // with tyreForward.
        Vector3 playerToTyreDirection = -tyreToPlayerDirection;

        float facingDot = Vector3.Dot(
            playerForward,
            playerToTyreDirection
        );

        float requiredFacingDot = Mathf.Cos(
            facingAngle * Mathf.Deg2Rad
        );

        bool facingTyre = facingDot >= requiredFacingDot;

        // Green debug line = every condition passed.
        // Yellow = player is in the rear zone but is not looking at tyre.
        // Red = player is outside the rear zone.
        Color debugColor =
            behindTyre && facingTyre ? Color.green :
            behindTyre ? Color.yellow :
            Color.red;

        Debug.DrawLine(
            player.position,
            transform.position,
            debugColor
        );

        Debug.DrawRay(
            player.position + Vector3.up * 0.05f,
            playerForward * pushRange,
            facingTyre ? Color.green : Color.yellow
        );

        return behindTyre && facingTyre;
    }

    private void UpdatePushAvailabilityIcon(bool canPush)
    {
        if (pushAvailabilityIcon == null)
            return;

        pushAvailabilityIcon.color =
            canPush ? availableColor : unavailableColor;
    }

    private void UpdateTyreSpeedText()
    {
        if (tyreSpeedText == null || tyreRigidbody == null)
            return;

        // Use the tyre Rigidbody's real horizontal velocity.
        // Vertical velocity is ignored so falling/jumping does not affect
        // the displayed tyre speed.
        Vector3 horizontalVelocity = tyreRigidbody.linearVelocity;
        horizontalVelocity.y = 0f;

        float speed = horizontalVelocity.magnitude;

        if (displaySpeedInKmh)
            speed *= 3.6f;

        string unit = displaySpeedInKmh ? " km/h" : " m/s";
        tyreSpeedText.text =
            "SPEED: " + speed.ToString("F" + speedDecimalPlaces) + unit;
    }

    private void UpdateTyreTiltUI()
    {
        float maxAngle = Mathf.Max(0.1f, tiltDisplayMaxAngle);

        // Uses the same turn angle already used by the tyre's Q/E turning
        // system, so the UI does not create a second turn calculation.
        float displayAngle = Mathf.Clamp(currentTurnAngle, -maxAngle, maxAngle);

        if (tyreTiltDegreeText != null)
        {
            float angleForText = showSignedTiltAngle
                ? displayAngle
                : Mathf.Abs(displayAngle);

            tyreTiltDegreeText.text = angleForText.ToString("0") + "°";
        }

        if (tyreTiltBar == null || tyreTiltMarker == null)
            return;

        RectTransform barRect = tyreTiltBar.rectTransform;
        float halfWidth = barRect.rect.width * 0.5f;
        float usableHalfWidth = Mathf.Max(0f,
            halfWidth - tyreTiltMarker.rect.width * 0.5f);

        float normalized = displayAngle / maxAngle;

        Vector2 markerPosition = tyreTiltMarker.anchoredPosition;
        markerPosition.x = normalized * usableHalfWidth;
        tyreTiltMarker.anchoredPosition = markerPosition;
    }

    private void OnDisable()
    {
        if (delayedColliderSwitchRoutine != null)
        {
            StopCoroutine(delayedColliderSwitchRoutine);
            delayedColliderSwitchRoutine = null;
        }

        if (tyreRigidbody != null && pushActive)
        {
            // Never leave the Rigidbody frozen if this component is disabled.
            tyreRigidbody.constraints &=
                ~(RigidbodyConstraints.FreezeRotationX |
                  RigidbodyConstraints.FreezeRotationZ);
        }

        pushActive = false;
        pushAdjustmentActive = false;
        turnInput = 0f;

        // Leave the tyre in normal/free-physics collider state.
        if (currentPushCollider != null)
            currentPushCollider.enabled = false;

        if (normalColliderObject != null)
            normalColliderObject.SetActive(true);

        SetHitTyreAnimation(false);
        UpdatePushAvailabilityIcon(false);

        if (playerController != null)
        {
            playerController.ClearPushTarget();
            playerController.SetPushMode(
                false,
                0f,
                Vector3.forward
            );
        }
    }
}