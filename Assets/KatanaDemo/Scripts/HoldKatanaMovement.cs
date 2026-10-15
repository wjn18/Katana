using UnityEngine;
using UnityEngine.InputSystem;

namespace OCDesign
{
    /// <summary>Simple camera-relative, in-place walk test on a flat plane.</summary>
    [DisallowMultipleComponent]
    public sealed class HoldKatanaMovement : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform movementCamera;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 0.21875f;
        [SerializeField, Min(0.001f)] private float authoredMoveSpeed = 0.21875f;
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [Tooltip("The model's walking forward direction, in degrees from this root's local +Z.")]
        [SerializeField] private float modelForwardYawOffset;

        [Header("Preview")]
        [SerializeField] private bool followCamera = true;
        [SerializeField] private bool showControls = true;

        private static readonly int IsMovingId = Animator.StringToHash("IsMoving");
        private static readonly int WalkSpeedId = Animator.StringToHash("WalkSpeed");
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Vector3 previousPosition;

        public void Configure(Animator targetAnimator, float yawOffset, float speed)
        {
            animator = targetAnimator;
            modelForwardYawOffset = yawOffset;
            moveSpeed = Mathf.Max(0f, speed);
            authoredMoveSpeed = Mathf.Max(0.001f, speed);
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

            Vector2 input = ReadMovement(keyboard);
            if (input.sqrMagnitude < 0.001f || moveSpeed <= 0f)
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
            float actualSpeed = moveSpeed * alignment;
            transform.position += facing * (actualSpeed * Time.deltaTime);
            SetAnimation(actualSpeed);
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
            SetAnimation(0f);
        }

        private void SetAnimation(float speed)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return;
            animator.SetBool(IsMovingId, speed > 0.001f);
            animator.SetFloat(WalkSpeedId, speed / Mathf.Max(0.001f, authoredMoveSpeed));
        }

        private void OnDisable()
        {
            SetAnimation(0f);
        }

        private void OnGUI()
        {
            if (showControls)
                GUI.Box(new Rect(12f, 12f, 365f, 30f), "WASD / Arrow keys: move    |    R: reset");
        }
    }
}
