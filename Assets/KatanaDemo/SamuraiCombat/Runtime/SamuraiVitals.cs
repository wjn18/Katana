using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>生命、耐力及恢复计时。时间由调用方传入，独立于输入、动画和Unity帧循环。</summary>
    public sealed class SamuraiVitals : ISamuraiVitalsView
    {
        readonly SamuraiCombatProfile profile;
        float recoveryAfter;
        public float Health { get; private set; }
        public float Stamina { get; private set; }
        public float MaxHealth => Mathf.Max(1f, profile.maxHealth);
        public float MaxStamina => Mathf.Max(1f, profile.maxStamina);
        public event Action Changed;

        public SamuraiVitals(SamuraiCombatProfile profile) { this.profile = profile; Reset(); }
        public void Reset()
        {
            Health = MaxHealth; Stamina = MaxStamina; recoveryAfter = 0f;
            Changed?.Invoke();
        }
        public bool CanAfford(float cost) => Stamina + .0001f >= Mathf.Max(0f, cost);
        public void Spend(float cost, float now)
        {
            Stamina = Mathf.Max(0f, Stamina - Mathf.Max(0f, cost));
            DelayRecovery(now);
            Changed?.Invoke();
        }
        public void Damage(float amount)
        {
            Health = Mathf.Max(0f, Health - Mathf.Max(0f, amount));
            Changed?.Invoke();
        }
        public void DelayRecovery(float now) => recoveryAfter = now + Mathf.Max(0f, profile.staminaRecoveryDelay);
        /// <summary>立即返还耐力；返回实际恢复值，不重置原有自然恢复延迟。</summary>
        public float RestoreStamina(float amount)
        {
            float before = Stamina;
            Stamina = Mathf.Min(MaxStamina, Stamina + Mathf.Max(0f, amount));
            float restored = Stamina - before;
            if (restored > 0f) Changed?.Invoke();
            return restored;
        }
        /// <summary>返回本次实际自然恢复量，供残心扣除已返还的白条。</summary>
        public float Recover(float now, float dt, bool allowed, bool guarding)
        {
            if (!allowed || now < recoveryAfter || Stamina >= MaxStamina) return 0f;
            float rate = Mathf.Max(0f, profile.staminaRecovery) * (guarding ? profile.guardRecoveryMultiplier : 1f);
            float before = Stamina;
            Stamina = Mathf.Min(MaxStamina, Stamina + rate * dt);
            Changed?.Invoke();
            return Stamina - before;
        }
    }
}
