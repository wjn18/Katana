using UnityEngine;

namespace OCDesign.Samurai
{
    public sealed partial class SamuraiCombatant
    {
        SamuraiCombatant executionTarget, executionOwner;
        readonly SamuraiEnemySensors executionSensors = new SamuraiEnemySensors();
        float executionElapsed, executionDuration, executionStrikeTime, executionDamage, savedBreakRemaining;
        bool executionDamageApplied;

        public bool CanExecuteTarget(SamuraiCombatant target)
        {
            // 只允许明确的玩家入口；不接入敌人处决玩家或玩家空精状态。
            if (!isActiveAndEnabled || profile == null || GetComponent<SamuraiPlayerInput>() == null
                || !CanAct || IsDead || IsHitStopped || profile.executionAction == null
                || animationDriver == null || !animationDriver.CanPlay(profile.executionAction)
                || target == null || target == this || target.Team == Team || !target.isActiveAndEnabled
                || target.gameObject.scene != gameObject.scene || !target.IsExecutionVulnerable
                || target.IsDead || target.executionOwner != null) return false;
            Vector3 body = BodyPoint(), destination = target.BodyPoint();
            Vector3 closest = target.motor != null ? target.motor.ClosestPoint(body) : destination;
            Vector3 delta = closest - body; delta.y = 0f;
            if (delta.magnitude > Mathf.Max(0f, profile.executionSurfaceRange)) return false;
            Vector3 direction = destination - body; direction.y = 0f;
            if (direction.sqrMagnitude > .001f && Vector3.Angle(transform.forward, direction) > 60f) return false;
            return executionSensors.HasLineOfSight(this, target, profile.obstructionMask);
        }
        public SamuraiCombatant FindExecutionTarget()
        {
            var input = GetComponent<SamuraiPlayerInput>();
            if (input == null) return null;
            var locked = input.targetLock != null ? input.targetLock.CurrentTarget : null;
            if (CanExecuteTarget(locked)) return locked;
            SamuraiCombatant nearest = null; float distance = float.PositiveInfinity;
            foreach (var candidate in FindObjectsByType<SamuraiCombatant>())
            {
                if (!CanExecuteTarget(candidate)) continue;
                float current = (candidate.transform.position - transform.position).sqrMagnitude;
                if (current < distance) { distance = current; nearest = candidate; }
            }
            return nearest;
        }
        public bool TryExecute(SamuraiCombatant target)
        {
            if (!CanExecuteTarget(target)) return false;
            var action = profile.executionAction;
            ClearHitStop(); knockback.Clear(); target.ClearHitStop(); target.knockback.Clear();
            movement.TurnForAction(target.transform.position - transform.position);
            float remaining = target.ToughnessBreakRemaining;
            actions.BeginExecution(action);
            target.actions.BeginBeingExecuted(action.Duration);
            executionTarget = target; target.executionOwner = this;
            savedBreakRemaining = remaining;
            executionElapsed = 0f; executionDuration = action.Duration;
            executionStrikeTime = action.Duration * Mathf.Clamp(action.GetDamageWindow(0).x, .05f, .9f);
            executionDamage = target.MaxHealth * Mathf.Clamp01(target.profile.executionHealthFraction);
            executionDamageApplied = false;
            return true;
        }
        void AdvanceExecution(float dt)
        {
            if (!IsExecuting || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            executionElapsed += Mathf.Min(dt, .1f);
            if (executionTarget == null && !executionDamageApplied) { AbortExecution(); return; }
            if (executionTarget != null && (!executionTarget.isActiveAndEnabled || executionTarget.IsDead
                || executionTarget.executionOwner != this || executionTarget.gameObject.scene != gameObject.scene))
            { AbortExecution(); return; }
            if (!executionDamageApplied && executionElapsed >= executionStrikeTime)
            {
                executionDamageApplied = true;
                executionTarget.ReceiveExecutionDamage(this, executionDamage);
            }
            if (executionElapsed < executionDuration) return;
            var target = executionTarget;
            executionTarget = null; executionDuration = 0f;
            if (target != null && target.executionOwner == this)
            {
                target.executionOwner = null;
                if (!target.IsDead) target.actions.RecoverFromToughnessBreak();
            }
            actions.FinishExecution();
        }
        void ReceiveExecutionDamage(SamuraiCombatant source, float amount)
        {
            if (executionOwner != source || !IsBeingExecuted || IsDead) return;
            float before = vitals.Health;
            vitals.Damage(Mathf.Max(0f, amount));
            var hit = new SamuraiDamageHit { Damage = amount, Source = source, SourcePosition = source.transform.position, Point = BodyPoint(), Unblockable = true };
            var result = vitals.Health <= 0f ? SamuraiHitResult.Killed : SamuraiHitResult.Hit;
            if (result == SamuraiHitResult.Killed) actions.ReactToDeath();
            LastHitResult = result; HitReceived?.Invoke(result);
            AttackReceived?.Invoke(hit, result);
            if (before > vitals.Health) DamageReceived?.Invoke(new SamuraiDamageFeedback(hit, result, before - vitals.Health));
            if (result == SamuraiHitResult.Killed) Died?.Invoke();
        }
        void HandleExecutionStateChanged()
        {
            if (executionTarget != null && !IsExecuting) AbortExecution();
            if (executionOwner == null || IsBeingExecuted) return;
            var owner = executionOwner; executionOwner = null;
            if (IsDead && owner.executionDamageApplied) owner.executionTarget = null;
            else owner.AbortExecution();
        }
        void AbortExecution()
        {
            var target = executionTarget; var owner = executionOwner;
            float remaining = savedBreakRemaining - executionElapsed;
            executionTarget = executionOwner = null;
            executionElapsed = executionDuration = 0f;
            if (target != null && target.executionOwner == this)
            {
                target.executionOwner = null;
                if (!target.IsDead && target.IsBeingExecuted)
                {
                    if (executionDamageApplied) target.actions.RecoverFromToughnessBreak();
                    else target.actions.ResumeToughnessBreak(remaining);
                }
            }
            if (owner != null && owner.executionTarget == this) owner.AbortExecution();
            if (actions != null && actions.IsExecuting) actions.FinishExecution();
        }
        Vector3 BodyPoint() => motor != null ? transform.TransformPoint(motor.center) : transform.position + Vector3.up * .8f;
    }
}
