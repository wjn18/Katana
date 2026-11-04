using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>F持防、按下边沿、完美格挡窗口与冷却；不控制动画或生命。</summary>
    public sealed class SamuraiDefense
    {
        readonly SamuraiCombatProfile profile;
        float raisedAt = float.NegativeInfinity, readyAt;
        bool consumed;
        public bool GuardHeld { get; private set; }
        public bool FreshPress { get; private set; }
        public SamuraiDefense(SamuraiCombatProfile profile) { this.profile = profile; }
        public void Reset()
        {
            GuardHeld = FreshPress = consumed = false;
            raisedAt = float.NegativeInfinity; readyAt = 0f;
        }
        public void ReadInput(bool held, bool pressed)
        {
            FreshPress = held && (profile.deflectEnabled ? pressed : pressed || !GuardHeld);
            GuardHeld = held;
            // 松开即结束当前窗口；再次持防必须由新边沿争取下一次机会。
            if (profile.deflectEnabled && !held) CloseWindow();
        }
        public void TryRaise(float now, bool fresh)
        {
            if (!fresh || !GuardHeld || now < readyAt) return;
            raisedAt = now; consumed = false;
            readyAt = now + (profile.deflectEnabled
                ? Mathf.Max(profile.deflectActiveSeconds, profile.deflectMissCooldownSeconds)
                : Mathf.Max(profile.parryActiveSeconds, profile.parryCooldownSeconds));
        }
        public bool WindowOpen(float now, bool guardState)
        {
            float activeSeconds = profile.deflectEnabled ? profile.deflectActiveSeconds : profile.parryActiveSeconds;
            if (!(activeSeconds > 0f)) return false;
            return GuardHeld && (guardState || profile.parryOnlyDefense) && !consumed && now >= raisedAt
                && now - raisedAt < Mathf.Max(.01f, activeSeconds);
        }
        public float CooldownRemaining(float now) => Mathf.Max(0f, readyAt - now);
        public void ConsumeWindow()
        {
            consumed = true;
            if (profile.deflectEnabled) readyAt = 0f;
        }
        public void CloseWindow() => raisedAt = float.NegativeInfinity;
        public void CancelInput()
        {
            GuardHeld = FreshPress = false; consumed = true;
            CloseWindow(); // 冷却不会因松键、失焦或动作切换清零。
        }
    }
}
