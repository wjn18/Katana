using UnityEngine;
using UnityEngine.InputSystem;

namespace OCDesign
{
    /// <summary>Camera-relative locomotion and a single, in-place sword attack.</summary>
    [DisallowMultipleComponent]
    public sealed class UchigatanaMovement : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform movementCamera;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 1f;
        [SerializeField, Min(0.001f)] private float authoredMoveSpeed = 0.21875f;
        [Tooltip("Walk cadence relative to the speed-matched animation; does not change movement speed.")]
        [SerializeField, Min(0f)] private float cadenceMultiplier = 0.4f;
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [Tooltip("The model's walking forward direction, in degrees from this root's local +Z.")]
        [SerializeField] private float modelForwardYawOffset;

        [Header("Fast Walk (hold Shift)")]
        [SerializeField, Min(0f)] private float fastMoveSpeed = 1.6f;
        [SerializeField, Min(0.001f)] private float fastAuthoredMoveSpeed = 0.5f;
        [SerializeField, Min(0f)] private float fastCadenceMultiplier = 1f;

        [Header("Run (hold Ctrl)")]
        [SerializeField, Min(0f)] private float runMoveSpeed = 2.8f;
        [Tooltip("Run clip playback at the configured run speed; turning slows it with actual movement.")]
        [SerializeField, Min(0f)] private float runCadenceMultiplier = 1f;

        [Header("Preview")]
        [SerializeField] private bool followCamera = true;
        [SerializeField] private bool showControls = true;

        private static readonly int IsMovingId = Animator.StringToHash("IsMoving");
        private static readonly int WalkSpeedId = Animator.StringToHash("WalkSpeed");
        private static readonly int IsFastWalkingId = Animator.StringToHash("IsFastWalking");
        private static readonly int FastWalkSpeedId = Animator.StringToHash("FastWalkSpeed");
        private static readonly int IsRunningId = Animator.StringToHash("IsRunning");
        private static readonly int RunSpeedId = Animator.StringToHash("RunSpeed");
        private static readonly int AttackStateId = Animator.StringToHash("Base Layer.UchigatanaAttack");
        private static readonly int IdleStateId = Animator.StringToHash("Base Layer.UchigatanaIdle");
        private static readonly int SlowStateId = Animator.StringToHash("Base Layer.UchigatanaSlowWalk");
        private static readonly int FastStateId = Animator.StringToHash("Base Layer.UchigatanaFastWalk");
        private static readonly int RunStateId = Animator.StringToHash("Base Layer.UchigatanaRun");
        private const float AttackBlendInSeconds = 0.08f;
        private const float AttackBlendOutSeconds = 0.12f;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Vector3 previousPosition;
        private bool isAttacking;
        private bool resumeLocomotion;

        public bool IsAttacking => isAttacking;

        public void Configure(Animator targetAnimator, float yawOffset, float speed, float requestedMoveSpeed = 1f)
        {
            animator = targetAnimator;
            modelForwardYawOffset = yawOffset;
            moveSpeed = Mathf.Max(0f, requestedMoveSpeed);
            authoredMoveSpeed = Mathf.Max(0.001f, speed);
        }

        public void ConfigureFastWalk(float authoredSpeed, float requestedMoveSpeed, float cadence = 1f)
        {
            fastAuthoredMoveSpeed = Mathf.Max(0.001f, authoredSpeed);
            fastMoveSpeed = Mathf.Max(0f, requestedMoveSpeed);
            fastCadenceMultiplier = Mathf.Max(0f, cadence);
        }

        public void ConfigureRun(float requestedMoveSpeed = 2.8f, float cadence = 1f)
        {
            runMoveSpeed = Mathf.Max(0f, requestedMoveSpeed);
            runCadenceMultiplier = Mathf.Max(0f, cadence);
        }

        private void Reset()
        {
            animator = GetComponentInChildren<Animator>();
            if (Camera.main != null)
                movementCamera = Camera.main.transform;
        }

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (movementCamera == null && Camera.main != null)
                movementCamera = Camera.main.transform;

            startPosition = transform.position;
            startRotation = transform.rotation;
            previousPosition = startPosition;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                ResetToStart();
                return;
            }

            if (UpdateAttack())
                return;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && TryAttack())
                return;

            Vector2 input = ReadMovement(keyboard);
            bool running = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool fast = !running && keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            float requestedSpeed = running ? runMoveSpeed : fast ? fastMoveSpeed : moveSpeed;
            if (input.sqrMagnitude < 0.001f || requestedSpeed <= 0f)
            {
                SetAnimation(0f);
                return;
            }

            Vector3 cameraForward = movementCamera != null
                ? Vector3.ProjectOnPlane(movementCamera.forward, Vector3.up)
                : Vector3.forward;
            if (cameraForward.sqrMagnitude < 0.001f)
                cameraForward = movementCamera != null
                    ? Vector3.ProjectOnPlane(movementCamera.up, Vector3.up)
                    : Vector3.forward;
            cameraForward.Normalize();
            Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward);
            Vector3 desiredDirection = (cameraRight * input.x + cameraForward * input.y).normalized;

            Quaternion targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up)
                * Quaternion.Euler(0f, -modelForwardYawOffset, 0f);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation, turnSpeed * Time.deltaTime);

            // The baked pose faces away from the container's +Z. Move along the
            // corrected visible heading, including while the root is turning.
            Vector3 facing = transform.rotation
                * Quaternion.Euler(0f, modelForwardYawOffset, 0f) * Vector3.forward;
            facing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
            float alignment = Mathf.Max(0f, Vector3.Dot(facing, desiredDirection));
            float actualSpeed = requestedSpeed * alignment;
            transform.position += facing * (actualSpeed * Time.deltaTime);
            SetAnimation(actualSpeed, fast, running);
        }

        /// <summary>Play one slash; repeated presses during the attack are ignored.</summary>
        public bool TryAttack()
        {
            if (isAttacking || animator == null || !animator.isActiveAndEnabled
                || animator.runtimeAnimatorController == null || !animator.HasState(0, AttackStateId))
                return false;

            resumeLocomotion = false;
            SetAnimation(0f);
            isAttacking = true;
            animator.CrossFadeInFixedTime(AttackStateId, AttackBlendInSeconds, 0, 0f);
            return true;
        }

        private bool UpdateAttack()
        {
            if (!isAttacking)
                return false;
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                isAttacking = false;
                resumeLocomotion = true;
                return false;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            bool blendingToAttack = animator.IsInTransition(0)
                && animator.GetNextAnimatorStateInfo(0).fullPathHash == AttackStateId;
            if (blendingToAttack || (state.fullPathHash == AttackStateId && state.normalizedTime < 1f))
                return true;

            isAttacking = false;
            resumeLocomotion = true;
            return false;
        }

        private static Vector2 ReadMovement(Keyboard keyboard)
        {
            if (keyboard == null)
                return Vector2.zero;

            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private void LateUpdate()
        {
            if (followCamera && movementCamera != null)
                movementCamera.position += transform.position - previousPosition;
            previousPosition = transform.position;
        }

        public void ResetToStart()
        {
            transform.SetPositionAndRotation(startPosition, startRotation);
            CancelAttack();
            SetAnimation(0f);
        }

        private void CancelAttack()
        {
            bool wasAttacking = isAttacking || resumeLocomotion;
            bool animatorReady = animator != null && animator.isActiveAndEnabled
                && animator.runtimeAnimatorController != null;
            if (animatorReady)
                wasAttacking |= animator.GetCurrentAnimatorStateInfo(0).fullPathHash == AttackStateId
                    || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == AttackStateId);
            isAttacking = false;
            // An inactive Animator must consume the recovery after it is enabled;
            // otherwise its non-looping attack state could resume and stay at the end.
            resumeLocomotion = wasAttacking;
            if (wasAttacking && animatorReady && animator.HasState(0, IdleStateId))
            {
                animator.Play(IdleStateId, 0, 0f);
                resumeLocomotion = false;
            }
        }

        private void SetAnimation(float speed, bool fast = false, bool running = false)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                return;
            animator.SetBool(IsMovingId, speed > 0.001f);
            animator.SetBool(IsFastWalkingId, fast && !running && speed > 0.001f);
            animator.SetBool(IsRunningId, running && speed > 0.001f);
            animator.SetFloat(WalkSpeedId, speed / Mathf.Max(0.001f, authoredMoveSpeed) * cadenceMultiplier);
            animator.SetFloat(FastWalkSpeedId, speed / Mathf.Max(0.001f, fastAuthoredMoveSpeed) * fastCadenceMultiplier);
            animator.SetFloat(RunSpeedId, speed / Mathf.Max(0.001f, runMoveSpeed) * runCadenceMultiplier);
            if (resumeLocomotion)
            {
                int state = speed <= 0.001f ? IdleStateId : running ? RunStateId : fast ? FastStateId : SlowStateId;
                animator.CrossFadeInFixedTime(state, AttackBlendOutSeconds, 0, 0f);
                resumeLocomotion = false;
            }
        }

        private void OnDisable()
        {
            CancelAttack();
            SetAnimation(0f);
        }

        private void OnGUI()
        {
            if (showControls)
                GUI.Box(new Rect(12f, 12f, 800f, 30f), "WASD / Arrows: slow walk    |    Shift: fast walk    |    Ctrl: run    |    Left click: attack    |    R: reset");
        }
    }
}
