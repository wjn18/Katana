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
        static readonly string[] DirectionNames =
            { "", "ForwardRight", "Right", "BackwardRight", "Backward", "BackwardLeft", "Left", "ForwardLeft" };

        readonly Animator animator;
        readonly Transform root;
        readonly SamuraiCombatProfile profile;
        int locomotionDirection = -1;

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
            animator.applyRootMotion = false;
            animator.speed = 1f;
        }

        public void ResetLocomotionDirection() => locomotionDirection = -1;

        public static float ClipDuration(AnimationClip clip) =>
            clip != null ? Mathf.Max(0.02f, clip.length) : 0.4f;

        public bool HasState(string stateName) => animator != null && animator.runtimeAnimatorController != null
            && !string.IsNullOrEmpty(stateName) && animator.HasState(0, StateHash(stateName));

        public bool CanPlay(SamuraiActionDefinition action) =>
            action != null && action.IsUsable && HasState(action.stateName);

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

        public void Play(string stateName, float duration, bool immediate = false, float normalizedStart = 0f)
        {
            if (stateName == "Idle") ResetLocomotionDirection();
            if (!Application.isPlaying || !HasState(stateName)) return;
            int hash = StateHash(stateName);
            if (immediate) animator.Play(hash, 0, normalizedStart);
            else animator.CrossFadeInFixedTime(hash, profile != null ? profile.transitionSeconds : 0f,
                0, normalizedStart * Mathf.Max(0.02f, duration));
        }

        public SamuraiLocomotionAnimation SelectLocomotion(Vector3 move, bool running, bool moving,
            string currentState, bool currentlyLocomoting)
        {
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
                AnimationClip[] directional = running ? profile.directionalRun : profile.directionalWalk;
                string baseName = running ? "Run" : "Walk";
                string directionalName = baseName + DirectionNames[direction];
                bool hasDirectional = directional != null && directional.Length > direction
                    && directional[direction] != null && HasState(directionalName);
                next = hasDirectional ? directionalName : baseName;
                clip = hasDirectional ? directional[direction] : running ? profile.run : profile.walk;
            }

            float phase = 0f;
            if (currentState != next && moving && currentlyLocomoting && currentState != "Idle"
                && TryProgress(currentState, out float progress))
                phase = Mathf.Repeat(progress, 1f);
            return new SamuraiLocomotionAnimation { StateName = next, Clip = clip, Phase = phase };
        }

        static int StateHash(string stateName) => UnityEngine.Animator.StringToHash(
            stateName.Contains(".") ? stateName : "Base Layer." + stateName);
    }
}
