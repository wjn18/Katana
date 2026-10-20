using UnityEngine;
using UnityEngine.InputSystem;

namespace OCDesign.Samurai
{
    /// <summary>场景键鼠适配器；每帧只向玩家战斗状态机提交一份输入。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class SamuraiPlayerInput : MonoBehaviour
    {
        [Tooltip("独立玩家根对象上的战斗组件。")]
        public SamuraiCombatant actor;
        [Tooltip("计算镜头相对移动方向的相机。")]
        public Transform cameraTransform;
        public SamuraiOrbitCamera orbitCamera;
        public SamuraiTrainingEnemy trainingEnemy;
        public SamuraiTargetLock targetLock;
        [Tooltip("首次进入场景时自动捕获鼠标；失焦后仍须点击重新捕获。")]
        public bool captureOnStart = true;

        private bool hasFocus;
        private bool paused;
        private bool suspended;
        private bool ownsCursor;
        private bool waitForMouseRelease;
        private int suppressedFrame = -1;
        private SamuraiInputFrame lastFrame;
        private readonly SamuraiPlayerPresentation presentation = new SamuraiPlayerPresentation();

        public bool HasCursorCapture => isActiveAndEnabled && !suspended && !paused &&
            hasFocus && ownsCursor && Cursor.lockState == CursorLockMode.Locked;
        public SamuraiInputFrame LastFrame => lastFrame;

        /// <summary>由装配工具注入依赖；不在每帧搜索场景对象。</summary>
        public void Configure(SamuraiCombatant combatant, Transform view,
            SamuraiOrbitCamera camera = null, SamuraiTrainingEnemy enemy = null,
            SamuraiTargetLock lockComponent = null)
        {
            actor = combatant;
            cameraTransform = view;
            orbitCamera = camera;
            trainingEnemy = enemy;
            targetLock = lockComponent != null ? lockComponent : GetComponent<SamuraiTargetLock>();
            if (targetLock != null) targetLock.Configure(actor, cameraTransform);
            if (orbitCamera != null) orbitCamera.targetLock = targetLock;
            lastFrame = default;
        }

        private void OnEnable()
        {
            hasFocus = Application.isFocused;
            suppressedFrame = Time.frameCount;
        }

        private void Start()
        {
            if (actor == null) actor = GetComponent<SamuraiCombatant>();
            presentation.EnsureView(actor, this, trainingEnemy, ref cameraTransform, ref orbitCamera);
            EnsureTargetLock();
            if (orbitCamera == null && cameraTransform != null)
                orbitCamera = cameraTransform.GetComponent<SamuraiOrbitCamera>();
            if (orbitCamera != null)
            {
                var feedback = orbitCamera.GetComponent<SamuraiCombatCameraFeedback>();
                if (feedback == null) feedback = orbitCamera.gameObject.AddComponent<SamuraiCombatCameraFeedback>();
                feedback.Configure(orbitCamera, actor);
            }
            if (captureOnStart && hasFocus) CaptureCursor();
        }

        private void EnsureTargetLock()
        {
            if (actor == null) return;
            if (targetLock == null) targetLock = GetComponent<SamuraiTargetLock>();
            if (targetLock == null) targetLock = gameObject.AddComponent<SamuraiTargetLock>();
            targetLock.Configure(actor, cameraTransform);
            if (orbitCamera != null) orbitCamera.targetLock = targetLock;
        }

        private void OnDisable() => ReleaseCursor();
        private void OnDestroy()
        {
            ReleaseCursor();
            presentation.Dispose();
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            ReleaseCursor();
        }

        private void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            ReleaseCursor();
        }

        /// <summary>菜单等调用；释放鼠标并取消尚未发出的蓄力、格挡和缓存。</summary>
        public void SuspendInput()
        {
            suspended = true;
            ReleaseCursor();
        }

        public void ResumeInput()
        {
            suspended = false;
            hasFocus = Application.isFocused;
            suppressedFrame = Time.frameCount;
        }

        public void CaptureCursor()
        {
            if (!isActiveAndEnabled || !hasFocus || paused || suspended) return;
            ownsCursor = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            // 捕获点击必须先完全松开；同一次点击不允许变成重击或蓄力。
            waitForMouseRelease = true;
            suppressedFrame = Time.frameCount;
            lastFrame = default;
        }

        public void ReleaseCursor()
        {
            if (ownsCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            ownsCursor = false;
            waitForMouseRelease = true;
            suppressedFrame = Time.frameCount;
            lastFrame = default;
            if (actor != null && Application.isPlaying) actor.CancelInput();
            if (targetLock != null) targetLock.ClearLock();
        }

        private void Update()
        {
            lastFrame = ReadFrame();
            if (actor == null) return;
            if (lastFrame.resetPressed)
            {
                if (targetLock != null) targetLock.ClearLock();
                actor.ResetCombat();
                if (trainingEnemy != null) trainingEnemy.ResetTraining();
                if (orbitCamera != null) orbitCamera.ResetView();
                lastFrame = default;
                return;
            }
            actor.Tick(lastFrame, Time.deltaTime);
        }

        private SamuraiInputFrame ReadFrame()
        {
            var frame = new SamuraiInputFrame();
            if (!hasFocus || paused || suspended) return frame;
            if (Time.timeScale <= 0f)
            {
                if (ownsCursor) ReleaseCursor();
                return frame;
            }
            Keyboard keys = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame)
            {
                ReleaseCursor();
                return frame;
            }
            if (!HasCursorCapture)
            {
                if (ownsCursor) ReleaseCursor();
                if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame))
                    CaptureCursor();
                return frame;
            }
            if (suppressedFrame == Time.frameCount) return frame;
            if (waitForMouseRelease)
            {
                if (mouse == null || (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed))
                    waitForMouseRelease = false;
                return frame;
            }
            if (keys != null)
            {
                float x = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1f : 0f)
                    - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1f : 0f);
                float y = (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1f : 0f)
                    - (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1f : 0f);
                frame.move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
                frame.sprint = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed;
                frame.dodgePressed = keys.spaceKey.wasPressedThisFrame;
                frame.guardHeld = keys.fKey.isPressed;
                frame.parryPressed = keys.fKey.wasPressedThisFrame;
                frame.resetPressed = keys.rKey.wasPressedThisFrame;
                if (keys.tKey.wasPressedThisFrame && trainingEnemy != null) trainingEnemy.ToggleActive();
            }
            bool toggleLock = (keys != null && keys.tabKey.wasPressedThisFrame) ||
                (mouse != null && mouse.middleButton.wasPressedThisFrame);
            if (toggleLock && targetLock != null && !frame.resetPressed) targetLock.ToggleLock();
            if (mouse != null)
            {
                frame.lightPressed = mouse.leftButton.wasPressedThisFrame;
                frame.heavyPressed = mouse.rightButton.wasPressedThisFrame;
                frame.heavyHeld = mouse.rightButton.isPressed;
                frame.heavyReleased = mouse.rightButton.wasReleasedThisFrame;
                if (orbitCamera != null)
                    orbitCamera.ApplyLook(mouse.delta.ReadValue(), mouse.scroll.ReadValue().y);
            }
            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            frame.moveWorld = forward * frame.move.y + right * frame.move.x;
            if (targetLock != null && targetLock.CurrentTarget != null)
            {
                Vector3 facing = targetLock.CurrentTarget.transform.position - actor.transform.position;
                facing.y = 0f;
                if (facing.sqrMagnitude > 0.0001f) frame.facingWorld = facing.normalized;
            }
            return frame;
        }
    }
}
