using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 单轮攻击的残心账本与时机。只使用角色战斗Clock，不读取键鼠或Time；
    /// 动作系统决定合法开窗、连段及中断，耐力系统负责最终返还。
    /// </summary>
    public sealed class SamuraiKiPulse
    {
        // 只吸收十微秒级浮点舍入，不扩大可感知的按键窗口。
        const float BoundaryTolerance = .00001f;
        float clock, attackStarted, attackDuration, normalizedStart;
        float gatherSeconds, perfectSeconds, openedAt;
        bool awaitingWindow, hasWindow;
        bool awaitingFlux, fluxPulseWasPerfect;
        int pulseStance = -1;
        float stanceOpenedAt, stanceDeadline;

        public float PendingAmount { get; private set; }
        public bool IsWindowOpen => WindowOpenAt(clock);
        public bool IsPerfectWindow => IsWindowOpen && clock - openedAt + BoundaryTolerance >= gatherSeconds;
        public float Progress => IsWindowOpen ? Mathf.Clamp01((clock - openedAt) / gatherSeconds) : 0f;
        public float RecoverableAmount => PendingAmount * Progress;
        public float FluxRecoveryAmount { get; private set; }
        public bool IsFluxWindowOpen => FluxWindowAt(clock);

        /// <summary>合法连段累加旧额度；独立起手开启新一轮。仅传入实际已支付的攻击耗耐。</summary>
        public void BeginAttack(float paidStamina, float recoveryFraction, bool continuation, float now,
            float duration, float startNormalized, float gather, float perfect)
        {
            if (!continuation) Clear();
            else ClearFlux();
            clock = now;
            PendingAmount += Mathf.Max(0f, paidStamina) * Mathf.Clamp01(recoveryFraction);
            attackStarted = now;
            attackDuration = Mathf.Max(.02f, duration);
            normalizedStart = Mathf.Clamp01(startNormalized);
            gatherSeconds = Mathf.Max(.01f, gather);
            perfectSeconds = Mathf.Max(0f, perfect);
            awaitingWindow = PendingAmount > 0f;
            hasWindow = false;
        }

        /// <summary>以实际动画进度开窗；补偿跨过阈值的帧，不因帧率不同缩短聚集时间。</summary>
        public void ObserveAttackProgress(float normalized, float now)
        {
            if (!awaitingWindow || normalized < normalizedStart) return;
            openedAt = Mathf.Clamp(now - (normalized - normalizedStart) * attackDuration, attackStarted, now);
            awaitingWindow = false;
            hasWindow = true;
        }

        /// <summary>在本帧输入处理之后更新展示时钟并到期；输入始终用按下时钟结算。</summary>
        public void Advance(float now)
        {
            clock = now;
            if (hasWindow && now > openedAt + gatherSeconds + perfectSeconds + BoundaryTolerance) ClearRecovery();
            if (awaitingFlux && now > stanceDeadline + BoundaryTolerance) ClearFlux();
        }

        public bool WindowOpenAt(float timestamp) => hasWindow && PendingAmount > 0f
            && timestamp >= openedAt && timestamp <= openedAt + gatherSeconds + perfectSeconds + BoundaryTolerance;

        /// <summary>未启用流转时只结算残心；窗外输入不会缓存。</summary>
        public bool TryConsume(float timestamp, out float amount, out bool perfect)
            => TryConsume(timestamp, -1, 0f, 0f, out amount, out perfect);

        /// <summary>时机决定残心量与完美判定；普通和完美残心均可独立获得一次流转资格。</summary>
        public bool TryConsume(float timestamp, int stance, float stanceWindow, float fluxRecovery,
            out float amount, out bool perfect)
        {
            amount = 0f; perfect = false;
            if (!WindowOpenAt(timestamp)) return false;
            float elapsed = timestamp - openedAt;
            perfect = elapsed + BoundaryTolerance >= gatherSeconds;
            amount = perfect ? PendingAmount : PendingAmount * Mathf.Clamp01(elapsed / gatherSeconds);
            Clear();
            if (stance >= 0 && stanceWindow > 0f && fluxRecovery > 0f)
            {
                awaitingFlux = true;
                fluxPulseWasPerfect = perfect;
                pulseStance = stance;
                stanceOpenedAt = timestamp;
                stanceDeadline = timestamp + stanceWindow;
                FluxRecoveryAmount = fluxRecovery;
            }
            return true;
        }

        bool FluxWindowAt(float timestamp) => awaitingFlux && timestamp >= stanceOpenedAt
            && timestamp <= stanceDeadline + BoundaryTolerance;

        /// <summary>切到另一架势获得额外回精，不补白条、不改变先前残心的时机判定。</summary>
        public bool TryConsumeFlux(float timestamp, int stance, out float amount, out bool pulseWasPerfect)
        {
            amount = 0f; pulseWasPerfect = false;
            if (!FluxWindowAt(timestamp) || stance < 0 || stance == pulseStance) return false;
            amount = FluxRecoveryAmount;
            pulseWasPerfect = fluxPulseWasPerfect;
            ClearFlux();
            return true;
        }

        public void DeductNaturalRecovery(float restored)
        {
            PendingAmount = Mathf.Max(0f, PendingAmount - Mathf.Max(0f, restored));
            if (PendingAmount <= 0f) ClearRecovery();
        }

        public void Clear()
        {
            ClearRecovery();
            ClearFlux();
        }

        public void ClearFlux()
        {
            awaitingFlux = fluxPulseWasPerfect = false;
            pulseStance = -1;
            FluxRecoveryAmount = 0f;
            stanceOpenedAt = stanceDeadline = 0f;
        }

        void ClearRecovery()
        {
            PendingAmount = 0f;
            awaitingWindow = hasWindow = false;
            openedAt = 0f;
        }
    }
}
