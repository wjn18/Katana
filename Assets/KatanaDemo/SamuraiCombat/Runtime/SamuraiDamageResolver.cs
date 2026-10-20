using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>伤害/防御规则。通过反应接口切换状态，支持替换状态机或扩展受击对象。</summary>
    public sealed class SamuraiDamageResolver
    {
        readonly SamuraiCombatProfile profile;
        readonly SamuraiVitals vitals;
        readonly SamuraiDefense defense;
        readonly ISamuraiCombatReactions reactions;
        public SamuraiDamageResolver(SamuraiCombatProfile profile, SamuraiVitals vitals,
            SamuraiDefense defense, ISamuraiCombatReactions reactions)
        {
            this.profile = profile; this.vitals = vitals; this.defense = defense; this.reactions = reactions;
        }

        public SamuraiHitResult Resolve(SamuraiDamageHit hit, ISamuraiDamageReceiver owner, float now)
        {
            if (!owner.CanReceiveDamage || reactions.IsDead
                || (hit.Source != null && (ReferenceEquals(hit.Source, owner) || hit.Source.Team == owner.Team)))
                return SamuraiHitResult.Ignored;
            if (reactions.IsInvulnerable) return SamuraiHitResult.Dodged;
            float damage = Mathf.Max(0f, hit.Damage);
            Vector3 toward = hit.SourcePosition - owner.DamageRoot.position;
            toward.y = 0f;
            bool front = toward.sqrMagnitude < .0001f || Vector3.Angle(owner.DamageRoot.forward, toward) <= profile.guardHalfAngle;
            if (defense.WindowOpen(now, reactions.IsGuarding) && front && !hit.Unblockable)
            {
                defense.ConsumeWindow();
                reactions.ReactToGuard(true);
                if (hit.Source != null && hit.Source.CanReceiveDamage) hit.Source.Stagger(profile.parryStunSeconds);
                return SamuraiHitResult.PerfectGuard;
            }
            if (reactions.IsGuarding && front && !hit.Unblockable)
            {
                vitals.Spend(hit.StaminaDamage, now);
                if (vitals.Stamina > .001f)
                {
                    reactions.ReactToGuard(false);
                    return SamuraiHitResult.Blocked;
                }
                vitals.Damage(damage * profile.guardBreakDamageMultiplier);
                if (vitals.Health <= 0f) return Kill();
                reactions.ReactToGuardBreak();
                return SamuraiHitResult.GuardBroken;
            }
            vitals.Damage(damage);
            vitals.DelayRecovery(now);
            if (vitals.Health <= 0f) return Kill();
            reactions.ReactToHit(SamuraiAnimationDriver.ClipDuration(profile.hit));
            return SamuraiHitResult.Hit;
        }

        SamuraiHitResult Kill()
        {
            reactions.ReactToDeath();
            return SamuraiHitResult.Killed;
        }
    }
}
