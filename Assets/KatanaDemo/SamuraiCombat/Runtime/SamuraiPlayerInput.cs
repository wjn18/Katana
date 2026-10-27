using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;

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
        private bool sheatheLatched;
        private int suppressedFrame = -1;
        private SamuraiInputFrame lastFrame;
        private PointerEventData uiPointer;
        private EventSystem uiEventSystem;
        private readonly List<RaycastResult> uiPointerHits = new List<RaycastResult>(8);
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
            sheatheLatched = false;
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
            sheatheLatched = false;
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
                ResetTrainingOpponents();
                if (orbitCamera != null) orbitCamera.ResetView();
                lastFrame = default;
                return;
            }
            actor.Tick(lastFrame, Time.deltaTime);
        }

        public void ResetTrainingOpponents()
        {
            foreach (var enemy in FindObjectsByType<SamuraiTrainingEnemy>())
                if (enemy.gameObject.scene == gameObject.scene) enemy.ResetTraining();
        }
        public void SetTrainingOpponentsActive(bool active)
        {
            foreach (var enemy in FindObjectsByType<SamuraiTrainingEnemy>())
                if (enemy.gameObject.scene == gameObject.scene) enemy.SetActiveTraining(active);
        }
        public void SetTrainingOpponentsMode(SamuraiTrainingMode mode)
        {
            foreach (var enemy in FindObjectsByType<SamuraiTrainingEnemy>())
                if (enemy.gameObject.scene == gameObject.scene) enemy.SetTrainingMode(mode);
        }

        private SamuraiInputFrame ReadFrame()
        {
            var frame = new SamuraiInputFrame();
            if (!hasFocus || paused || suspended) return frame;
            // 菜单关闭/鼠标捕获的同一帧不能再处理 Esc 或攻击边沿。
            if (suppressedFrame == Time.frameCount) return frame;
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
                if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
                    && !PointerHitsUI(mouse))
                    CaptureCursor();
                return frame;
            }
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
                frame.kiPulsePressed = keys.qKey.wasPressedThisFrame;
                frame.kiPulseHeld = keys.qKey.isPressed;
                frame.resetPressed = keys.rKey.wasPressedThisFrame;
                frame.highStancePressed = keys.digit1Key.wasPressedThisFrame;
                frame.executionPressed = keys.eKey.wasPressedThisFrame;
                frame.middleStancePressed = keys.digit2Key.wasPressedThisFrame;
                bool sheatheChord = keys.qKey.isPressed && keys.spaceKey.isPressed;
                if (!sheatheLatched && sheatheChord && (keys.qKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame))
                {
                    sheatheLatched = true;
                    frame.sheathePressed = true;
                }
                if (sheatheLatched)
                {
                    frame.sheatheHeld = keys.spaceKey.isPressed;
                    frame.sheatheReleased = keys.spaceKey.wasReleasedThisFrame || !keys.spaceKey.isPressed;
                    frame.dodgePressed = frame.kiPulsePressed = frame.kiPulseHeld = false;
                    if (frame.sheatheReleased) sheatheLatched = false;
                }
                if (keys.tKey.wasPressedThisFrame && trainingEnemy != null) SetTrainingOpponentsActive(!trainingEnemy.activeTraining);
                if (keys.gKey.wasPressedThisFrame && trainingEnemy != null && !frame.resetPressed)
                    SetTrainingOpponentsMode((SamuraiTrainingMode)(((int)trainingEnemy.trainingMode + 1) % 3));
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
                Vector2 look = mouse.delta.ReadValue();
                // 刚按锁定键和复位帧丢弃切换手势；捕获/菜单/失焦边界已由上方统一隔离。
                if (targetLock != null && !toggleLock && !frame.resetPressed)
                    targetLock.ReadMouseSwitch(look, Time.unscaledDeltaTime);
                if (orbitCamera != null)
                    orbitCamera.ApplyLook(look, mouse.scroll.ReadValue().y);
            }
            // 记录明确的组合语义，命中停顿恢复时不依赖修饰键是否仍然按住。
            if (frame.sheathePressed || frame.sheatheHeld || frame.sheatheReleased)
                frame.lightPressed = frame.heavyPressed = frame.heavyReleased = frame.parryPressed = false;
            else
            {
                frame.guardLightPressed = frame.guardHeld && frame.lightPressed;
                frame.guardHeavyPressed = frame.guardHeld && frame.heavyPressed;
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

        // 释放鼠标后仍能点击右上设置按钮；用当前指针位置检测，不读取上一帧 UI 悬停状态。
        private bool PointerHitsUI(Mouse mouse)
        {
            var system = EventSystem.current;
            if (system == null) return false;
            if (uiPointer == null || uiEventSystem != system)
            { uiEventSystem = system; uiPointer = new PointerEventData(system); }
            uiPointer.Reset();
            uiPointer.position = mouse.position.ReadValue();
            uiPointerHits.Clear();
            system.RaycastAll(uiPointer, uiPointerHits);
            foreach (var hit in uiPointerHits)
                if (hit.module is UnityEngine.UI.GraphicRaycaster) return true;
            return false;
        }
    }
}
