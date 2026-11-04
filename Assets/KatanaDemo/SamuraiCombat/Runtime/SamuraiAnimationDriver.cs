using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>动画选择结果；状态机决定是否切换，驱动器不修改战斗状态。</summary>
    public struct SamuraiLocomotionAnimation
    {
        public string StateName;
        public AnimationClip Clip;
        public float Phase;
    }

    /// <summary>
    /// 封装 Animator 查询、播放及八方向移动片段选择，不负责耐力、伤害或动作许可。
    /// 角色状态机只需传入目标状态和期望播放进度。
    /// </summary>
    public sealed class SamuraiAnimationDriver
    {
        static readonly string[] WalkStateNames =
            { "Walk", "WalkForwardRight", "WalkRight", "WalkBackwardRight", "WalkBackward", "WalkBackwardLeft", "WalkLeft", "WalkForwardLeft" };
        static readonly string[] RunStateNames =
            { "Run", "RunForwardRight", "RunRight", "RunBackwardRight", "RunBackward", "RunBackwardLeft", "RunLeft", "RunForwardLeft" };
        // 只缓存名称对应的哈希；状态是否存在仍向当前 Animator 查询，兼容运行时更换控制器。
        static readonly Dictionary<string, int> StateHashes = new Dictionary<string, int>(64, System.StringComparer.Ordinal);

        readonly Animator animator;
        readonly Transform root;
        readonly SamuraiCombatProfile profile;
        int locomotionDirection = -1;
        string lastMovementState;

        public Animator Animator => animator;

        public SamuraiAnimationDriver(Animator animator, Transform root, SamuraiCombatProfile profile)
        {
            this.animator = animator;
            this.root = root;
            this.profile = profile;
        }

        public void Reset()
        {
            ResetLocomotionDirection();
            if (animator == null) return;
            // 桥接器逐次接管Animator根运动；普通动作仍丢弃根增量，避免切换开关重初始化Animator。
            animator.applyRootMotion = animator.GetComponent<SamuraiRootMotionDriver>() != null;
            animator.speed = 1f;
        }

        public void ResetLocomotionDirection()
        {
            locomotionDirection = -1;
            lastMovementState = null;
        }

        public static float ClipDuration(AnimationClip clip) =>
            clip != null ? Mathf.Max(0.02f, clip.length) : 0.4f;

        public bool HasState(string stateName) => animator != null && animator.runtimeAnimatorController != null
            && !string.IsNullOrEmpty(stateName) && animator.HasState(0, StateHash(stateName));

        public bool CanPlay(SamuraiActionDefinition action) =>
            action != null && action.IsUsable && HasState(action.stateName);
        public void SetActionPlayback(SamuraiActionDefinition action, float normalized)
        {
            if (animator != null) animator.speed = action != null ? action.AttackPlaybackSpeed(normalized) : 1f;
        }

        public bool TryProgress(string stateName, out float progress)
        {
            progress = 0f;
            if (animator == null || !animator.isActiveAndEnabled
                || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName)) return false;
            int hash = StateHash(stateName);
            if (animator.IsInTransition(0))
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                if (next.fullPathHash == hash) { progress = next.normalizedTime; return true; }
            }
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (current.fullPathHash == hash) { progress = current.normalizedTime; return true; }
            return false;
        }

        public void Play(string stateName, float duration, bool immediate = false, float normalizedStart = 0f,
            float transitionSeconds = -1f)
        {
            if (stateName == "Idle") ResetLocomotionDirection();
            if (!Application.isPlaying || !HasState(stateName)) return;
            int hash = StateHash(stateName);
            if (immediate) animator.Play(hash, 0, normalizedStart);
            else
            {
                float blend = transitionSeconds >= 0f ? transitionSeconds : profile != null ? profile.transitionSeconds : 0f;
                // 压臂冲量在反应片段开头；较长的通用混合会把这一拍抹平。
                // 仅缩短入混合，受力速度曲线烘焙在片段中，不占用HitStop控制的Animator.speed。
                if (SamuraiGuardReactionSelector.IsReactionState(stateName) || SamuraiHitReactionSelector.IsReactionState(stateName)
                    || stateName == "Parry" || stateName == "Deflected" || profile!=null&&stateName==profile.deflectedStateName)
                    blend = Mathf.Min(blend, .018f);
                animator.CrossFadeInFixedTime(hash, blend, 0, normalizedStart * Mathf.Max(0.02f, duration));
            }
        }

        public SamuraiLocomotionAnimation SelectLocomotion(Vector3 move, bool running, bool moving,
            string currentState, bool currentlyLocomoting, SamuraiStanceDefinition stance = null)
        {
            if (!moving) ResetLocomotionDirection();
            string next = "Idle";
            AnimationClip clip = profile != null ? profile.idle : null;
            if (moving && profile != null && root != null)
            {
                Vector3 local = root.InverseTransformDirection(move);
                float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                int direction = (Mathf.RoundToInt(angle / 45f) + 8) % 8;
                if (locomotionDirection >= 0 && Mathf.Abs(Mathf.DeltaAngle(locomotionDirection * 45f, angle))
                    < 22.5f + Mathf.Clamp(profile.locomotionDirectionHysteresis, 0f, 20f))
                    direction = locomotionDirection;
                locomotionDirection = direction;
                var entries = stance != null && stance.locomotion != null
                    ? running ? stance.locomotion.run : stance.locomotion.walk : null;
                var entry = entries != null && entries.Length > direction ? entries[direction] : null;
                if (entry != null && entry.clip != null && HasState(entry.stateName))
                {
                    next = entry.stateName;
                    clip = entry.clip;
                }
                else
                {
                    // 每个方向独立回退；缺配置不能把新片段误配给旧状态，或反过来。
                    AnimationClip[] directional = running ? profile.directionalRun : profile.directionalWalk;
                    string baseName = running ? "Run" : "Walk";
                    string directionalName = (running ? RunStateNames : WalkStateNames)[direction];
                    bool hasDirectional = directional != null && directional.Length > direction
                        && directional[direction] != null && HasState(directionalName);
                    next = hasDirectional ? directionalName : baseName;
                    clip = hasDirectional ? directional[direction] : running ? profile.run : profile.walk;
                }
            }

            float phase = 0f;
            // 逻辑架势可先于动画变化；旧架势Idle不能被误认作新架势的步态。
            // 只有上一实际选择的移动状态可以传递脚步相位，跨方向/步速/架势均适用。
            if (currentState != next && moving && currentlyLocomoting && lastMovementState != null && currentState == lastMovementState
                && TryProgress(currentState, out float progress))
                phase = Mathf.Repeat(progress, 1f);
            lastMovementState = moving ? next : null;
            return new SamuraiLocomotionAnimation { StateName = next, Clip = clip, Phase = phase };
        }

        static int StateHash(string stateName)
        {
            if (StateHashes.TryGetValue(stateName, out int hash)) return hash;
            hash = UnityEngine.Animator.StringToHash(stateName.Contains(".") ? stateName : "Base Layer." + stateName);
            StateHashes.Add(stateName, hash);
            return hash;
        }
    }
}
