using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>只管理闪避追击的输入缓存和宽限时间，不播放动画或判定伤害。</summary>
    public sealed class SamuraiDodgeFollowUp
    {
        bool dodgeActive;
        float bufferedUntil = float.NegativeInfinity;
        float graceUntil = float.NegativeInfinity;

        public void BeginDodge()
        {
            Cancel();
            dodgeActive = true;
        }

        public void BufferPress(float clock, float normalized, float inputStart, float bufferSeconds)
        {
            if (!dodgeActive || normalized < Mathf.Clamp01(inputStart)) return;
            bufferedUntil = clock + Mathf.Max(0f, bufferSeconds);
        }

        public bool ConsumeBuffered(float clock, float normalized, float attackStart)
        {
            if (!dodgeActive || normalized < Mathf.Clamp01(attackStart) || clock > bufferedUntil) return false;
            bufferedUntil = float.NegativeInfinity;
            return true;
        }

        public void FinishDodge(float clock, float graceSeconds)
        {
            // CancelInput之后即使动画继续播完，也不能重新获得追击资格。
            graceUntil = dodgeActive ? clock + Mathf.Max(0f, graceSeconds) : float.NegativeInfinity;
            dodgeActive = false;
            bufferedUntil = float.NegativeInfinity;
        }

        public bool ConsumeGrace(float clock)
        {
            bool eligible = clock <= graceUntil;
            graceUntil = float.NegativeInfinity;
            return eligible;
        }

        public void Cancel()
        {
            dodgeActive = false;
            bufferedUntil = graceUntil = float.NegativeInfinity;
        }
    }
}
