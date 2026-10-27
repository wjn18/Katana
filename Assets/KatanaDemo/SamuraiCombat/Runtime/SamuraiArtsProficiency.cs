using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>角色独立的琢技资源。攻击资格/费用先预览，动作成功提交后才消费满槽。</summary>
    public sealed class SamuraiArtsProficiency
    {
        readonly SamuraiCombatProfile profile;
        float gauge, remaining;
        int chargedAttackSerial;
        bool hasChargedAttack;

        public SamuraiArtsProficiency(SamuraiCombatProfile profile) { this.profile = profile; }
        public float Maximum => Mathf.Max(1f, profile.artsGaugeMaximum);
        public float Gauge => profile.artsEnabled ? Mathf.Clamp(gauge, 0f, Maximum) : 0f;
        public float Duration => Mathf.Max(.1f, profile.artsDurationSeconds);
        public float Remaining => profile.artsEnabled ? Mathf.Max(0f, remaining) : 0f;
        public bool IsActive => Remaining > 0f;
        public bool IsReady => profile.artsEnabled && !IsActive && Gauge >= Maximum;

        public void Reset() { gauge = remaining = 0f; hasChargedAttack = false; }
        public void Advance(float dt)
        {
            if (!profile.artsEnabled) { Reset(); return; }
            if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt)) remaining = Mathf.Max(0f, remaining - dt);
        }
        public void EndActivation() => remaining = 0f;
        public bool IsEligible(SamuraiActionDefinition action)
        {
            if (action == null || !profile.artsEnabled) return false;
            if (action.isMartialArt || action == profile.heavy || action == profile.charged) return true;
            // 架势强击替换了旧Heavy/Charged的动作，继续沿用相同强击琢技资格。
            if (profile.stances != null)
                foreach (var stance in profile.stances)
                {
                    if (stance == null) continue;
                    if (action == stance.strongAttack || action == stance.chargedStrongAttack) return true;
                    if (stance.strongAttackChain?.attacks != null)
                        foreach (var strongAttack in stance.strongAttackChain.attacks)
                            if (action == strongAttack) return true;
                }
            // 兼容旧的显式扩展名单；普通轻击/冲刺/闪避追击不会自动成为武技。
            if (profile.artsAdditionalAttacks != null)
                foreach (var extra in profile.artsAdditionalAttacks) if (extra == action) return true;
            return false;
        }
        public float DamageMultiplier(SamuraiActionDefinition action)
        {
            if (!IsEligible(action)) return 1f;
            return Mathf.Max(1f, action.isMartialArt ? profile.artsMartialArtDamageMultiplier : profile.artsDamageMultiplier);
        }
        public float StaminaCost(SamuraiActionDefinition action)
        {
            if (action == null) return 0f;
            float cost = Mathf.Max(0f, action.staminaCost);
            if (!IsEligible(action)) return cost;
            if (IsReady) return 0f;
            return IsActive ? cost * Mathf.Clamp01(profile.artsStaminaCostMultiplier) : cost;
        }
        /// <summary>仅由已通过动画/费用检查的 CommitAttack 调用。</summary>
        public bool CommitAttack(SamuraiActionDefinition action, out bool activated)
        {
            activated = false;
            if (!IsEligible(action)) return false;
            if (IsReady)
            {
                gauge = 0f; remaining = Duration; activated = true;
            }
            return IsActive;
        }
        public void RecordAttack(int attackSerial, SamuraiHitResult result)
        {
            if (result != SamuraiHitResult.Hit && result != SamuraiHitResult.Killed
                && result != SamuraiHitResult.GuardBroken && result != SamuraiHitResult.Blocked) return;
            if (hasChargedAttack && chargedAttackSerial == attackSerial) return;
            hasChargedAttack = true; chargedAttackSerial = attackSerial;
            Add(profile.artsAttackGain);
        }
        public void RecordDefense(SamuraiHitResult result)
        {
            if (result == SamuraiHitResult.PerfectGuard) Add(profile.artsDeflectGain);
            else if (result == SamuraiHitResult.Blocked) Add(profile.artsGuardGain);
        }
        void Add(float amount)
        {
            if (!profile.artsEnabled || IsActive || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            gauge = Mathf.Clamp(Gauge + Mathf.Max(0f, amount), 0f, Maximum);
        }
    }
}
