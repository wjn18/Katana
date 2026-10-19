using UnityEngine;
using UnityEngine.InputSystem;

namespace OCDesign.Combat
{
    /// <summary>
    /// 键鼠输入适配器。仅生成操作意图和管理鼠标捕获，不直接操纵角色、相机或动画。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatPlayerInput : CombatInputSource
    {
        private const string GameplayMapName = "Gameplay";
        private const int DirectAttackCount = 6;

        [SerializeField, Tooltip("包含 Gameplay 操作表的输入资产；运行时克隆，避免改变共享资产的启用状态。")]
        private InputActionAsset inputActions;

        private InputActionAsset runtimeActions;
        private InputActionMap gameplay;
        private InputAction move;
        private InputAction look;
        private InputAction sprint;
        private InputAction primaryAttack;
        private InputAction secondaryAttack;
        private readonly InputAction[] directAttacks = new InputAction[DirectAttackCount];
        private InputAction releaseCursor;
        private InputAction captureCursor;

        private bool applicationHasFocus;
        private bool cursorCaptured;
        private bool suspended;
        private int sampledFrame = -1;
        private CombatInputFrame sampledInput = CombatInputFrame.None;

        public bool HasCursorCapture => applicationHasFocus && cursorCaptured &&
                                        Cursor.lockState == CursorLockMode.Locked;

        /// <summary>由场景组装代码注入输入资产；运行中更换时销毁旧的私有实例。</summary>
        public void Configure(InputActionAsset asset)
        {
            inputActions = asset;
            if (!Application.isPlaying)
                return;

            ReleaseCursor();
            DisposeRuntimeActions();
            if (isActiveAndEnabled)
                EnableInput();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
                EnableInput();
        }

        private void OnDisable()
        {
            gameplay?.Disable();
            ReleaseCursor();
            SuppressCurrentFrame();
        }

        private void OnDestroy()
        {
            ReleaseCursor();
            DisposeRuntimeActions();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            applicationHasFocus = hasFocus;
            SuppressCurrentFrame();
            gameplay?.Disable();

            if (!hasFocus)
            {
                ReleaseCursor();
                return;
            }

            // 切回应用后需点击重新捕获，避免切换窗口时的点击变成攻击。
            if (isActiveAndEnabled && !suspended)
                gameplay?.Enable();
        }

        public override CombatInputFrame ReadFrame()
        {
            // 一帧内返回同一份快照，避免不同消费者读到不一致的按下边沿。
            if (sampledFrame == Time.frameCount)
                return sampledInput;

            sampledFrame = Time.frameCount;
            sampledInput = CombatInputFrame.None;
            if (!isActiveAndEnabled || suspended || !applicationHasFocus || gameplay == null || !gameplay.enabled)
                return sampledInput;

            if (releaseCursor.WasPressedThisFrame())
            {
                ReleaseCursor();
                return sampledInput;
            }

            if (!HasCursorCapture)
            {
                if (captureCursor.WasPressedThisFrame())
                    CaptureCursor();

                // 重新捕获鼠标的这一帧完全不输出操作，包括绑定同一按键的主攻击。
                return sampledInput;
            }

            // 鼠标输出普通续招/重击意图；仅按下边沿触发，持键不会自动完成连段。
            // 数字键仍为单招测试；同帧按左键、右键、数字从小到大取一个请求。
            bool primaryPressed = primaryAttack.WasPressedThisFrame();
            bool heavyPressed = !primaryPressed && secondaryAttack.WasPressedThisFrame();
            int attackIndex = -1;
            if (!primaryPressed && !heavyPressed)
                for (int i = 0; i < directAttacks.Length; i++)
                    if (directAttacks[i].WasPressedThisFrame())
                    { attackIndex = i; break; }

            sampledInput = new CombatInputFrame(
                Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f),
                look.ReadValue<Vector2>(),
                sprint.IsPressed(),
                attackIndex, primaryPressed, heavyPressed);
            return sampledInput;
        }

        public override void ResetInput()
        {
            // Disable/Enable 清理动作的进行中状态；按钮使用按下边沿，不缓存未消费的攻击。
            gameplay?.Disable();
            if (isActiveAndEnabled && !suspended && applicationHasFocus)
                gameplay?.Enable();
            SuppressCurrentFrame();
        }

        public override void SuspendInput()
        {
            // 输入组件可能仍启用，但控制器已停止 ReadFrame；此时必须主动释放鼠标。
            suspended = true;
            gameplay?.Disable();
            ReleaseCursor();
            SuppressCurrentFrame();
        }

        public override void ResumeInput()
        {
            suspended = false;
            if (Application.isPlaying && isActiveAndEnabled)
                EnableInput();
        }

        private void EnableInput()
        {
            applicationHasFocus = Application.isFocused;
            if (suspended)
            {
                SuppressCurrentFrame();
                return;
            }
            if (runtimeActions == null && !CreateRuntimeActions())
                return;

            if (applicationHasFocus)
            {
                gameplay.Enable();
                CaptureCursor();
            }
            SuppressCurrentFrame();
        }

        private bool CreateRuntimeActions()
        {
            if (inputActions == null)
            {
                Debug.LogError("CombatPlayerInput 缺少输入资产，请配置 Gameplay 操作表。", this);
                return false;
            }

            runtimeActions = Instantiate(inputActions);
            runtimeActions.name = inputActions.name + " (Runtime)";
            gameplay = runtimeActions.FindActionMap(GameplayMapName, false);
            if (gameplay == null)
                return ReportInvalidActions("找不到 Gameplay 操作表。");

            move = gameplay.FindAction("Move", false);
            look = gameplay.FindAction("Look", false);
            sprint = gameplay.FindAction("Sprint", false);
            primaryAttack = gameplay.FindAction("PrimaryAttack", false);
            secondaryAttack = gameplay.FindAction("SecondaryAttack", false);
            releaseCursor = gameplay.FindAction("ReleaseCursor", false);
            captureCursor = gameplay.FindAction("CaptureCursor", false);
            if (move == null || look == null || sprint == null || primaryAttack == null ||
                secondaryAttack == null || releaseCursor == null || captureCursor == null)
                return ReportInvalidActions("Gameplay 必须包含 Move、Look、Sprint、PrimaryAttack、SecondaryAttack、ReleaseCursor 和 CaptureCursor。");

            if (move.expectedControlType != "Vector2" || look.expectedControlType != "Vector2" ||
                sprint.type != InputActionType.Button || primaryAttack.type != InputActionType.Button ||
                secondaryAttack.type != InputActionType.Button || releaseCursor.type != InputActionType.Button ||
                captureCursor.type != InputActionType.Button)
                return ReportInvalidActions("Move/Look 必须为 Vector2，其余操作必须为 Button。");

            for (int i = 0; i < directAttacks.Length; i++)
            {
                string actionName = "Attack" + (i + 1).ToString("00");
                directAttacks[i] = gameplay.FindAction(actionName, false);
                if (directAttacks[i] == null || directAttacks[i].type != InputActionType.Button)
                    return ReportInvalidActions("Gameplay 必须包含 Button 操作 " + actionName + "。");
            }

            return true;
        }

        private bool ReportInvalidActions(string reason)
        {
            Debug.LogError("CombatPlayerInput 输入资产配置无效：" + reason, this);
            DisposeRuntimeActions();
            return false;
        }

        private void DisposeRuntimeActions()
        {
            if (runtimeActions != null)
            {
                runtimeActions.Disable();
                // InputActionAsset 的 OnDestroy 会 Dispose 共享的原生动作状态。
                Destroy(runtimeActions);
            }

            runtimeActions = null;
            gameplay = null;
            move = look = sprint = primaryAttack = secondaryAttack = releaseCursor = captureCursor = null;
            for (int i = 0; i < directAttacks.Length; i++) directAttacks[i] = null;
            SuppressCurrentFrame();
        }

        private void CaptureCursor()
        {
            if (!applicationHasFocus)
                return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cursorCaptured = true;
        }

        private void ReleaseCursor()
        {
            if (!cursorCaptured)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorCaptured = false;
        }

        private void SuppressCurrentFrame()
        {
            sampledFrame = Time.frameCount;
            sampledInput = CombatInputFrame.None;
        }
    }
}
