namespace OCDesign.Samurai
{
    /// <summary>残心/流转之后的一次冬月资格。只处理战斗时间与架势，不持有动画或输入设备。</summary>
    public sealed class SamuraiFrostMoon
    {
        const float BoundaryTolerance = .00001f;
        float clock, openedAt, deadline;
        bool hasPulse, hasWindow, consumed;
        int stance = -1;

        public bool IsWindowOpen => WindowOpenAt(clock);
        public int Stance => stance;

        public void Open(float now, int pulseStance, float seconds)
        {
            Clear();
            if (pulseStance < 0 || seconds <= 0f) return;
            hasPulse = hasWindow = true;
            clock = openedAt = now;
            deadline = now + seconds;
            stance = pulseStance;
        }

        /// <summary>合法流转后改为目标架势并开放衔接窗口；已消费的冬月指令不会重新发放。</summary>
        public void AfterFlux(float now, int targetStance, float seconds)
        {
            if (!hasPulse || consumed || targetStance < 0 || seconds <= 0f) return;
            hasWindow = true;
            clock = openedAt = now;
            deadline = now + seconds;
            stance = targetStance;
        }

        public void Advance(float now)
        {
            clock = now;
            if (now > deadline + BoundaryTolerance) hasWindow = false;
        }

        public bool WindowOpenAt(float timestamp) => hasWindow && !consumed
            && timestamp >= openedAt && timestamp <= deadline + BoundaryTolerance;

        /// <summary>空动画槽也消费本次有效指令，避免同一轮按住或补配置后延迟出招。</summary>
        public bool TryConsume(float timestamp, int requestedStance)
        {
            if (!WindowOpenAt(timestamp) || requestedStance != stance) return false;
            consumed = true;
            hasWindow = false;
            return true;
        }

        public void Clear()
        {
            hasPulse = hasWindow = consumed = false;
            stance = -1;
            openedAt = deadline = 0f;
        }
    }
}
