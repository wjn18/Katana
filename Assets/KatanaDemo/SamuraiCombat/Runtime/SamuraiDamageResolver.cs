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
            var toughnessReaction = reactions as ISamuraiToughnessReactions;
            var toughness = toughnessReaction != null && toughnessReaction.Toughness.Enabled ? toughnessReaction.Toughness : null;
            toughness?.ClearContact();
            float damage = Mathf.Max(0f, hit.Damage);
            // 破韧/受处决期间只结算生命；普通连击不能刷新窗口或覆盖失衡姿势。
            if (toughness != null && toughnessReaction.IsToughnessBroken)
            {
                vitals.Damage(damage);
                return vitals.Health <= 0f ? Kill() : SamuraiHitResult.Hit;
            }
            Vector3 toward = hit.SourcePosition - owner.DamageRoot.position;
            toward.y = 0f;
            bool front = toward.sqrMagnitude < .0001f || Vector3.Angle(owner.DamageRoot.forward, toward) <= profile.guardHalfAngle;
            var actionDefense = reactions as ISamuraiActionDefense;
            bool attackGuard = actionDefense != null && actionDefense.HasAttackGuard;
            if (defense.WindowOpen(now, reactions.IsGuarding) && front && !hit.Unblockable)
            {
                defense.ConsumeWindow();
                if (profile.deflectEnabled)
                    vitals.RestoreStamina(vitals.MaxStamina * Mathf.Clamp01(profile.deflectRecoveryFraction));
                bool broken = toughness != null && toughness.Contact(hit, now, toughnessReaction.ActionStaggerResistance, true, true);
                if (broken) toughnessReaction.ReactToToughnessBreak();
                else ReactToGuard(true, hit);
                if (hit.Source != null && hit.Source.CanReceiveDamage)
                {
                    if (profile.deflectEnabled)
                    {
                        float seconds = Mathf.Max(.05f, profile.deflectStunSeconds);
                        var deflectReaction = hit.Source as ISamuraiDeflectReaction;
                        if (deflectReaction != null) deflectReaction.ReceiveDeflect(seconds);
                        else hit.Source.Stagger(seconds);
                    }
                    else hit.Source.Stagger(profile.parryStunSeconds);
                }
                return SamuraiHitResult.PerfectGuard;
            }
            if ((reactions.IsGuarding || attackGuard) && front && !hit.Unblockable)
            {
                if (toughness != null)
                {
                    bool broken = toughness.Contact(hit, now, toughnessReaction.ActionStaggerResistance, true);
                    if (broken) { toughnessReaction.ReactToToughnessBreak(); return SamuraiHitResult.GuardBroken; }
                    if (!attackGuard && !toughness.LastHitResisted) ReactToGuard(false, hit);
                    return SamuraiHitResult.Blocked;
                }
                vitals.Spend(hit.StaminaDamage, now);
                if (vitals.Stamina > .001f)
                {
                    // 冬月起手挡住攻击后继续本招；零精、死亡仍走原破防/死亡分支。
                    if (!attackGuard) ReactToGuard(false, hit);
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
            if (toughness != null)
            {
                if (toughness.Contact(hit, now, toughnessReaction.ActionStaggerResistance, false))
                { toughnessReaction.ReactToToughnessBreak(); return SamuraiHitResult.Hit; }
                if (toughness.LastHitResisted) return SamuraiHitResult.Hit;
            }
            if (actionDefense != null && actionDefense.HasSuperArmor && !hit.Unblockable)
                return SamuraiHitResult.Hit;
            var contextualHit = reactions as ISamuraiHitReactions;
            if (contextualHit != null) contextualHit.ReactToHit(hit);
            else reactions.ReactToHit(SamuraiAnimationDriver.ClipDuration(profile.hit));
            return SamuraiHitResult.Hit;
        }

        void ReactToGuard(bool perfect, SamuraiDamageHit hit)
        {
            var contextual = reactions as ISamuraiGuardReactions;
            if (contextual != null) contextual.ReactToGuard(perfect, hit);
            else reactions.ReactToGuard(perfect);
        }

        SamuraiHitResult Kill()
        {
            reactions.ReactToDeath();
            return SamuraiHitResult.Killed;
        }
    }
}
