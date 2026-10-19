using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>
    /// 唯一 Animator 写入入口。攻击按动画实际归一化进度结束，避免帧率、播放倍率与计时器失步。
    /// 普通攻击按 01→02→04→05→06 衔接，最多缓存下一段一次请求；重击和单招测试不接受续招。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatAnimationDriver : MonoBehaviour
    {
        public const string LocomotionStatePath = "Base Layer.Locomotion";
        public const string MoveParameter = "MoveSpeed";
        public const string AttackSpeedParameter = "AttackSpeed";
        private static readonly int LocomotionHash = Animator.StringToHash(LocomotionStatePath);
        private static readonly int MoveHash = Animator.StringToHash(MoveParameter);
        private static readonly int AttackSpeedHash = Animator.StringToHash(AttackSpeedParameter);
        private static readonly int[] NormalCombo = { 0, 1, 3, 4, 5 };
        private const int HeavyAttackIndex = 2;

        [SerializeField] private Animator target;
        [SerializeField] private CombatAttackDefinition[] attacks;
        [SerializeField, Min(0f)] private float returnBlendSeconds = 0.15f;
        private CombatAttackDefinition currentAttack;
        private bool enteredAttackState;
        private int requestFrame;
        private bool resetWhenAnimatorReturns;
        private int comboStep = -1;
        private bool bufferedCombo;
        private int nextDamageWindow;

        /// <summary>伤害系统接入点：参数为攻击配置、本次出招序号、窗口开关；不做命中或伤害计算。</summary>
        public event System.Action<CombatAttackDefinition, uint, bool> DamageWindowChanged;
        public bool IsDamageWindowActive { get; private set; }
        public int ActiveDamageWindowIndex { get; private set; } = -1;

        public Animator Target => target;
        public bool IsAttacking => currentAttack != null;
        public CombatAttackDefinition CurrentAttack => currentAttack;
        public int CurrentAttackIndex { get; private set; } = -1;
        public int AttackCount => attacks == null ? 0 : attacks.Length;
        public int ComboStep => comboStep;
        public bool HasBufferedCombo => bufferedCombo;
        public uint AttackSerial { get; private set; }

        /// <summary>以 Animator 正在评估的当前/目标状态为时钟；不另计时驱动空中动作。</summary>
        public float CurrentAttackNormalizedTime
        {
            get
            {
                if (!isActiveAndEnabled || !IsAttacking || !target || !target.isActiveAndEnabled) return 0f;
                // 同一重击在结束帧重新起手时，Animator 尚可能返回上一遍同名状态的末尾进度。
                if (!enteredAttackState && Time.frameCount == requestFrame) return 0f;
                if (target.IsInTransition(0))
                {
                    AnimatorStateInfo next = target.GetNextAnimatorStateInfo(0);
                    if (next.fullPathHash == currentAttack.StateHash) return Mathf.Max(0f, next.normalizedTime);
                }
                AnimatorStateInfo state = target.GetCurrentAnimatorStateInfo(0);
                return state.fullPathHash == currentAttack.StateHash ? Mathf.Max(0f, state.normalizedTime) : 0f;
            }
        }

        public void Configure(Animator animator, CombatAttackDefinition[] definitions)
        {
            target = animator;
            attacks = definitions;
        }

        public bool ValidateConfiguration(out string error)
        {
            if (!target || !target.runtimeAnimatorController)
            { error = "缺少 Animator 或控制器。"; return false; }
            if (!target.HasState(0, LocomotionHash))
            { error = "Animator 缺少 " + LocomotionStatePath; return false; }
            bool moveFound = false, attackSpeedFound = false;
            foreach (AnimatorControllerParameter parameter in target.parameters)
            {
                if (parameter.nameHash == MoveHash && parameter.type == AnimatorControllerParameterType.Float) moveFound = true;
                if (parameter.nameHash == AttackSpeedHash && parameter.type == AnimatorControllerParameterType.Float) attackSpeedFound = true;
            }
            if (!moveFound || !attackSpeedFound)
            { error = "Animator 缺少 MoveSpeed / AttackSpeed 浮点参数。"; return false; }
            if (AttackCount != 6) { error = "薙刀配置必须按 01、02、重击03、04、05、06 排列六招。"; return false; }
            foreach (CombatAttackDefinition attack in attacks)
                if (!attack || !target.HasState(0, attack.StateHash))
                { error = "攻击配置为空或其 Animator 状态不存在。"; return false; }
            error = null;
            return true;
        }

        public bool TryStartAttack(int index)
        {
            if (IsAttacking) return false;
            // 数字键/工具触发的单招不隐式加入普通连段。
            return StartAttack(index, -1);
        }

        public bool TryPrimaryAttack()
        {
            if (!isActiveAndEnabled || !target || !target.isActiveAndEnabled) return false;
            if (!IsAttacking) return StartAttack(NormalCombo[0], 0);
            if (comboStep < 0 || comboStep >= NormalCombo.Length - 1 || bufferedCombo
                || CurrentAttackNormalizedTime > currentAttack.ComboInputCloseNormalized) return false;
            bufferedCombo = true;
            TryAdvanceCombo();
            return true;
        }

        /// <summary>重击仅在空闲时接受；忙时拒绝，既不打断普通招式，也不排在其后。</summary>
        public bool TryHeavyAttack() => !IsAttacking && StartAttack(HeavyAttackIndex, -1);

        private bool StartAttack(int index, int nextComboStep)
        {
            if (!isActiveAndEnabled || index < 0 || index >= AttackCount || !target || !target.isActiveAndEnabled) return false;
            CombatAttackDefinition attack = attacks[index];
            if (!attack || !target.HasState(0, attack.StateHash)) return false;
            uint previousSerial = AttackSerial;
            CloseDamageWindow();
            if (AttackSerial != previousSerial) return false;
            currentAttack = attack;
            nextDamageWindow = 0;
            ActiveDamageWindowIndex = -1;
            CurrentAttackIndex = index;
            comboStep = nextComboStep;
            bufferedCombo = false;
            AttackSerial++;
            enteredAttackState = false;
            requestFrame = Time.frameCount;
            target.SetFloat(AttackSpeedHash, attack.PlaybackSpeed);
            target.CrossFadeInFixedTime(attack.StateHash, attack.BlendInSeconds, 0, 0f);
            TickDamageWindows(0f);
            return true;
        }

        /// <summary>在控制器读取新请求之前调用，使刚结束的攻击立即释放移动锁。</summary>
        public void TickAttack()
        {
            if (!isActiveAndEnabled) { ClearAttack(); return; }
            if (!target || !target.isActiveAndEnabled)
            {
                // Animator 临时禁用时不能保留运动锁；恢复后从移动状态重新开始。
                ClearAttack();
                resetWhenAnimatorReturns = true;
                return;
            }
            if (resetWhenAnimatorReturns) ResetState();
            if (!IsAttacking) return;
            AnimatorStateInfo state = target.GetCurrentAnimatorStateInfo(0);
            bool transitioning = target.IsInTransition(0);
            AnimatorStateInfo next = transitioning ? target.GetNextAnimatorStateInfo(0) : default;
            bool isCurrent = state.fullPathHash == currentAttack.StateHash;
            bool isNext = transitioning && next.fullPathHash == currentAttack.StateHash;
            if (isCurrent || isNext) enteredAttackState = true;

            if (transitioning && isCurrent && !isNext)
            {
                // 外部状态切换视作打断，立即关闭窗口；保留外部指定的目标动画。
                ClearAttack();
                return;
            }

            CombatAttackDefinition evaluatedAttack = currentAttack;
            uint evaluatedSerial = AttackSerial;
            if (isCurrent || isNext) TickDamageWindows(CurrentAttackNormalizedTime);
            if (currentAttack != evaluatedAttack || AttackSerial != evaluatedSerial) return;
            if (TryAdvanceCombo()) return;
            if (isCurrent && !isNext && state.normalizedTime >= 1f)
                ReturnToLocomotion();
            else if (!isCurrent && !isNext && (enteredAttackState || Time.frameCount > requestFrame + 2))
                ReturnToLocomotion(); // 外部换状态/重绑时也释放锁，避免永久卡在攻击中。
        }

        private bool TryAdvanceCombo()
        {
            if (!bufferedCombo || comboStep < 0 || comboStep >= NormalCombo.Length - 1
                || CurrentAttackNormalizedTime < currentAttack.ComboLinkNormalized) return false;
            int next = comboStep + 1;
            // 先消费唯一缓存，不能把这一点击重复带到再下一段。
            bufferedCombo = false;
            return StartAttack(NormalCombo[next], next);
        }

        public void SetLocomotion(float speed, CombatMovementSettings movement, float deltaTime)
        {
            if (!isActiveAndEnabled || !target || !movement) return;
            float blend = speed <= movement.WalkSpeed
                ? speed / movement.WalkSpeed
                : 1f + (speed - movement.WalkSpeed) / Mathf.Max(0.01f, movement.RunSpeed - movement.WalkSpeed);
            target.SetFloat(MoveHash, Mathf.Clamp(blend, 0f, 2f), 0.1f, deltaTime);
        }

        private void ReturnToLocomotion()
        {
            uint serial = AttackSerial;
            ClearAttack();
            if (AttackSerial != serial) return;
            target.CrossFadeInFixedTime(LocomotionHash, returnBlendSeconds, 0, 0f);
        }

        private void ClearAttack()
        {
            uint serial = AttackSerial;
            CloseDamageWindow();
            if (AttackSerial != serial) return;
            nextDamageWindow = 0;
            ActiveDamageWindowIndex = -1;
            currentAttack = null;
            CurrentAttackIndex = -1;
            enteredAttackState = false;
            comboStep = -1;
            bufferedCombo = false;
        }

        private void TickDamageWindows(float progress)
        {
            CombatAttackDefinition evaluatedAttack = currentAttack;
            uint serial = AttackSerial;
            // 先处理被本帧跨过的窗口，再换连段；低帧率也会成对通知开启/关闭。
            while (currentAttack && nextDamageWindow < currentAttack.DamageWindowCount)
            {
                Vector2 window = currentAttack.GetDamageWindow(nextDamageWindow);
                if (progress < window.x) break;
                if (!IsDamageWindowActive)
                {
                    IsDamageWindowActive = true;
                    ActiveDamageWindowIndex = nextDamageWindow;
                    DamageWindowChanged?.Invoke(currentAttack, AttackSerial, true);
                    if (currentAttack != evaluatedAttack || AttackSerial != serial) return;
                }
                if (progress < window.y) break;
                CloseDamageWindow();
                if (currentAttack != evaluatedAttack || AttackSerial != serial) return;
                nextDamageWindow++;
            }
        }

        private void CloseDamageWindow()
        {
            if (!IsDamageWindowActive) return;
            CombatAttackDefinition closingAttack = currentAttack;
            uint serial = AttackSerial;
            IsDamageWindowActive = false;
            // 关闭回调中仍可读取当前窗口索引；回调结束后恢复 -1。
            DamageWindowChanged?.Invoke(currentAttack, AttackSerial, false);
            if (currentAttack == closingAttack && AttackSerial == serial && !IsDamageWindowActive)
                ActiveDamageWindowIndex = -1;
        }

        public void ResetState()
        {
            uint serial = AttackSerial;
            ClearAttack();
            if (AttackSerial != serial) return;
            resetWhenAnimatorReturns = !target || !target.isActiveAndEnabled;
            if (resetWhenAnimatorReturns || !target.runtimeAnimatorController) return;
            target.SetFloat(MoveHash, 0f);
            target.SetFloat(AttackSpeedHash, 1f);
            target.Play(LocomotionHash, 0, 0f);
        }

        private void OnDisable() => ResetState();
    }
}
