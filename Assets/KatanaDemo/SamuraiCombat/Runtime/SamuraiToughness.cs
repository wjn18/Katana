using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>敌人失衡资源和动态硬直抗性。命中前比较，命中后积累；不控制动画或输入。</summary>
    public sealed class SamuraiToughness
    {
        readonly SamuraiCombatProfile profile;
        float lastPressure = float.NegativeInfinity;
        public bool Enabled => profile != null && profile.toughnessEnabled;
        public float Maximum => Mathf.Max(1f, profile.maxToughness);
        public float Value { get; private set; }
        public float AccumulatedResistance { get; private set; }
        public bool LastHitResisted { get; private set; }
        public float LastComparedResistance { get; private set; }
        public float LastComparedImpact { get; private set; }
        public SamuraiToughness(SamuraiCombatProfile profile) { this.profile = profile; Reset(); }
        public float Resistance(float actionBonus) => Enabled
            ? Safe(profile.baseStaggerResistance) + AccumulatedResistance + Safe(actionBonus) : 0f;
        public void Reset()
        {
            Value = Maximum; AccumulatedResistance = 0f;
            lastPressure = float.NegativeInfinity;
            LastHitResisted = false; LastComparedResistance = LastComparedImpact = 0f;
        }
        public void ClearContact() => LastHitResisted = false;
        public bool Contact(SamuraiDamageHit hit, float now, float actionBonus, bool guarded, bool perfect = false)
        {
            LastHitResisted = false;
            if (!Enabled) return false;
            LastComparedResistance = Resistance(actionBonus);
            LastComparedImpact = Safe(hit.ImpactStrength) * (perfect ? .3f : guarded ? .6f : 1f);
            // 本次增加的积累不能抵消本次攻击；等于阈值仍可触发硬直。
            LastHitResisted = !perfect && LastComparedResistance > LastComparedImpact;
            float damageMultiplier = perfect ? Mathf.Clamp01(profile.toughnessPerfectGuardDamageMultiplier)
                : guarded ? Mathf.Clamp01(profile.toughnessGuardDamageMultiplier) : 1f;
            Value = Mathf.Max(0f, Value - Safe(hit.ToughnessDamage) * damageMultiplier);
            AccumulatedResistance = Mathf.Min(Safe(profile.maximumAccumulatedResistance),
                AccumulatedResistance + Safe(hit.ImpactStrength) * (perfect ? .1f : guarded ? .25f : .5f));
            lastPressure = now;
            if (Value <= 0f) LastHitResisted = false;
            return Value <= 0f;
        }
        public bool Deflected(float now)
        {
            LastHitResisted = false;
            if (!Enabled) return false;
            Value = Mathf.Max(0f, Value - Maximum * Mathf.Clamp01(profile.deflectedToughnessFraction));
            lastPressure = now;
            return Value <= 0f;
        }
        public void Advance(float now, float dt, bool mayRecover, bool guarding, bool frozen)
        {
            if (!Enabled || frozen || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            float decayTime = Mathf.Clamp(now - lastPressure - Safe(profile.resistanceDecayDelay), 0f, dt);
            AccumulatedResistance = Mathf.Max(0f, AccumulatedResistance - Safe(profile.resistanceDecayPerSecond) * decayTime);
            if (!mayRecover || Value <= 0f) return;
            float recoveryTime = Mathf.Clamp(now - lastPressure - Safe(profile.toughnessRecoveryDelay), 0f, dt);
            Value = Mathf.Min(Maximum, Value + Safe(profile.toughnessRecoveryPerSecond) * recoveryTime
                * (guarding ? Mathf.Clamp01(profile.toughnessGuardRecoveryMultiplier) : 1f));
        }
        static float Safe(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
    }
}
