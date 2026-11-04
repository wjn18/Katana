using UnityEngine;

namespace OCDesign.Samurai
{
    public sealed partial class SamuraiCombatant
    {
        SamuraiCombatant executionTarget, executionOwner;
        readonly SamuraiEnemySensors executionSensors=new SamuraiEnemySensors();
        readonly Collider[] executionOverlaps=new Collider[64];
        readonly RaycastHit[] executionHits=new RaycastHit[64];
        SamuraiExecutionPair activeExecutionPair;
        float executionElapsed,executionDamage,savedBreakRemaining,executionActorSpeed,executionReceiverSpeed;
        bool executionDamageApplied,executionHealthyBranch;
        Vector3 executionStart,executionAnchor;
        Quaternion executionFacing,executionStartRotation;
        public SamuraiExecutionPair ActiveExecutionPair=>activeExecutionPair;
        public bool HasExecutionReceiver=>profile!=null&&ConfiguredExecutionDamage>0
            &&(Health<=ConfiguredExecutionDamage?profile.executionLethalPair:profile.executionSurvivingPair)!=null;
        public float ExecutionPlaybackElapsed=>Mathf.Max(0,executionElapsed-(activeExecutionPair!=null?activeExecutionPair.alignSeconds+activeExecutionPair.PreparationSeconds:0));
        public float ExecutionElapsed=>activeExecutionPair!=null?activeExecutionPair.SourceSecondsAt(ExecutionPlaybackElapsed):0;
        bool IsExecutionAligning=>activeExecutionPair!=null&&executionElapsed<Mathf.Max(.0001f,activeExecutionPair.alignSeconds);
        bool IsExecutionHoldingDown=>activeExecutionPair!=null&&activeExecutionPair.receiverHoldDownUntilStrike
            &&(IsExecutionAligning||ExecutionElapsed<activeExecutionPair.strikeSeconds);
        bool IsExecutionPreparing=>activeExecutionPair!=null&&!IsExecutionAligning
            &&executionElapsed<activeExecutionPair.alignSeconds+activeExecutionPair.PreparationSeconds;
        float ExecutionPreparationProgress=>activeExecutionPair==null||activeExecutionPair.PreparationSeconds<=0?1f:
            Mathf.Clamp01((executionElapsed-activeExecutionPair.alignSeconds)/activeExecutionPair.PreparationSeconds);
        float ExecutionReceiverTime=>ExecutionElapsed+(activeExecutionPair!=null?Mathf.Max(0,activeExecutionPair.receiverStartSeconds):0);
        public float ConfiguredExecutionDamage=>profile==null?0:profile.executionDamageMode==SamuraiExecutionDamageMode.FixedAmount
            ?Mathf.Max(0,profile.executionFixedDamage):MaxHealth*Mathf.Clamp01(profile.executionHealthFraction);

        SamuraiExecutionPair ChooseExecution(SamuraiCombatant target)=>target.Health<=target.ConfiguredExecutionDamage
            ?target.profile.executionLethalPair:target.profile.executionSurvivingPair;
        public bool CanExecuteTarget(SamuraiCombatant target)
        {
            if(!isActiveAndEnabled||profile==null||!profile.executionEnabled||GetComponent<SamuraiPlayerInput>()==null
                ||!CanAct||IsDead||IsHitStopped||animationDriver==null||target==null||target==this||target.Team==Team
                ||!target.isActiveAndEnabled||target.gameObject.scene!=gameObject.scene||!target.IsExecutionVulnerable
                ||target.IsDead||target.executionOwner!=null||target.profile==null||target.ConfiguredExecutionDamage<=0)return false;
            var pair=ChooseExecution(target);
            // Receiver assets are the opt-in. A large Elite with no matching pair is never eligible.
            if(pair==null||pair.attackerClip==null||pair.receiverClip==null||pair.receiverLowHealthClip==null
                ||target.animationDriver==null||!animationDriver.HasState(pair.attackerState)||!target.animationDriver.HasState(pair.receiverState)
                ||!target.animationDriver.HasState(pair.receiverLowHealthState)||!target.CanBindExecutionPose(pair))return false;
            Vector3 body=BodyPoint(),destination=target.BodyPoint();
            Vector3 closest=target.motor!=null?target.motor.ClosestPoint(body):destination;
            Vector3 delta=closest-body;delta.y=0;
            if(Mathf.Abs(transform.position.y-target.transform.position.y)>.25f)return false;
            Vector3 direction=destination-body;direction.y=0;
            if(direction.sqrMagnitude>.001f&&Vector3.Angle(transform.forward,direction)>60)return false;
            Vector3 anchor;Quaternion facing;float floor;
            if(!executionSensors.HasLineOfSight(this,target,profile.obstructionMask)
                ||!PlanExecutionApproach(target,out anchor,out facing,out floor))return false;
            Vector3 toStandingPoint=transform.position-anchor;toStandingPoint.y=0;
            return delta.magnitude<=Mathf.Max(0,profile.executionSurfaceRange)
                ||pair.standingEntryRadius>0&&toStandingPoint.sqrMagnitude<=Mathf.Pow(Mathf.Clamp(pair.standingEntryRadius,0,.6f),2);
        }
        // Used by the standing cue even outside the input range/angle. Never moves either actor.
        public bool TryGetExecutionApproach(SamuraiCombatant target,out Vector3 position,out Quaternion facing)
        {float floor;return PlanExecutionApproach(target,out position,out facing,out floor);}
        bool PlanExecutionApproach(SamuraiCombatant target,out Vector3 anchor,out Quaternion facing,out float targetFloor)
        {
            anchor=transform.position;facing=transform.rotation;targetFloor=0;
            if(profile==null||target==null||target.profile==null||target.ConfiguredExecutionDamage<=0)return false;
            var pair=ChooseExecution(target);if(pair==null)return false;
            Vector3 direction=target.transform.forward;direction.y=0;if(direction.sqrMagnitude<.0001f)return false;
            facing=Quaternion.LookRotation(direction)*Quaternion.Euler(0,-pair.receiverYaw,0);
            anchor=target.transform.position-facing*new Vector3(pair.receiverOffset.x,0,pair.receiverOffset.z);
            anchor.y=transform.position.y;
            if(!TryExecutionFloor(target.transform.position,out targetFloor)
                ||!ExecutionSpaceClear(target,transform.position,anchor)||!ExecutionGround(anchor,anchor.y))return false;
            Vector3 previous=anchor;
            for(int i=0;i<=20;i++)
            {
                Vector3 next=anchor+facing*pair.RootAt(pair.Duration*i/20f);
                if((next-previous).sqrMagnitude<.000001f)continue;
                if(!ExecutionSpaceClear(target,previous,next)||!ExecutionGround(next,anchor.y))return false;
                previous=next;
            }
            return true;
        }
        public SamuraiCombatant FindExecutionTarget()
        {
            var input=GetComponent<SamuraiPlayerInput>();if(input==null||profile==null||!profile.executionEnabled)return null;
            var locked=input.targetLock!=null?input.targetLock.CurrentTarget:null;if(CanExecuteTarget(locked))return locked;
            SamuraiCombatant nearest=null;float distance=float.PositiveInfinity;
            foreach(var candidate in FindObjectsByType<SamuraiCombatant>())
            {if(!CanExecuteTarget(candidate))continue;float d=(candidate.transform.position-transform.position).sqrMagnitude;if(d<distance){distance=d;nearest=candidate;}}
            return nearest;
        }
        public bool TryExecute(SamuraiCombatant target)
        {
            if(!CanExecuteTarget(target))return false;
            var pair=ChooseExecution(target);Vector3 anchor;Quaternion facing;float targetFloor;
            if(!PlanExecutionApproach(target,out anchor,out facing,out targetFloor))return false;
            ClearHitStop();knockback.Clear();target.ClearHitStop();target.knockback.Clear();
            activeExecutionPair=pair;executionTarget=target;target.executionOwner=this;
            executionDamage=target.ConfiguredExecutionDamage;executionHealthyBranch=target.actions.DownBranchHealthy;
            savedBreakRemaining=target.ToughnessBreakRemaining;executionElapsed=0;executionDamageApplied=false;
            executionStart=transform.position;executionStartRotation=transform.rotation;executionAnchor=anchor;executionFacing=facing;
            executionActorSpeed=animator.speed;executionReceiverSpeed=target.animator.speed;
            target.SetExecutionPose(executionHealthyBranch?pair.receiverPoseHealthy:pair.receiverPoseLowHealth,ReceiverState(),targetFloor);
            target.CaptureExecutionAlignmentPose();
            actions.BeginPairedExecution(pair.attackerState,pair.TotalDuration,false);
            target.actions.BeginPairedExecution(ReceiverState(),pair.TotalDuration,true);
            SampleExecution(0);return true;
        }
        string ReceiverState()=>executionHealthyBranch?activeExecutionPair.receiverState:activeExecutionPair.receiverLowHealthState;
        AnimationClip ReceiverClip()=>executionHealthyBranch?activeExecutionPair.receiverClip:activeExecutionPair.receiverLowHealthClip;
        void SampleExecution(float time)
        {
            var pair=activeExecutionPair;if(pair==null||executionTarget==null)return;
            animator.speed=executionTarget.animator.speed=0;
            animator.Play(pair.attackerState,0,Mathf.Clamp((time-pair.attackerStartSeconds)/pair.attackerClip.length,0,.99999f));animator.Update(0);
            if(IsExecutionHoldingDown){executionTarget.ApplyExecutionDownPose(executionElapsed);return;}
            if(IsExecutionAligning){executionTarget.ApplyExecutionAlignmentPose();return;}
            float receiverTime=time+Mathf.Max(0,pair.receiverStartSeconds);
            var clip=ReceiverClip();executionTarget.animator.Play(ReceiverState(),0,Mathf.Clamp(receiverTime/clip.length,0,.99999f));executionTarget.animator.Update(0);
            if(IsExecutionPreparing)executionTarget.ApplyExecutionPreparation(receiverTime,ExecutionPreparationProgress);
            else {executionTarget.executionAlignmentPose=null;executionTarget.ApplyExecutionPose(receiverTime);}
        }
        void AdvanceExecution(float dt)
        {
            if(!IsExecuting)return;
            if(profile==null||!profile.executionEnabled||activeExecutionPair==null||executionTarget==null
                ||!executionTarget.isActiveAndEnabled||executionTarget.executionOwner!=this
                ||executionTarget.gameObject.scene!=gameObject.scene){AbortExecution();return;}
            if(Time.timeScale<=0||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            executionElapsed+=Mathf.Min(dt,.1f);var pair=activeExecutionPair;
            float time=ExecutionElapsed;float align=Mathf.SmoothStep(0,1,Mathf.Clamp01(executionElapsed/Mathf.Max(.0001f,pair.alignSeconds)));
            Vector3 desired=Vector3.Lerp(executionStart,executionAnchor,align)+executionFacing*pair.RootAt(time);
            if(!ExecutionSpaceClear(executionTarget,transform.position,desired)||!ExecutionGround(desired,executionAnchor.y)) {AbortExecution();return;}
            transform.SetPositionAndRotation(desired,Quaternion.Slerp(executionStartRotation,executionFacing,align));
            SampleExecution(time);
            if(!executionDamageApplied&&time>=pair.strikeSeconds)
            {
                executionDamageApplied=true;
                SampleExecution(pair.strikeSeconds);
                Vector3 contact=ExecutionContact();
                executionTarget.ReceiveExecutionDamage(this,executionDamage,contact);
                SamuraiExecutionBlood.Spawn(this,executionTarget,contact,transform.forward,pair);
                foreach(var camera in FindObjectsByType<SamuraiCombatCameraFeedback>())if(camera.TargetPlayer==this)camera.PlayExecutionImpact(pair.shakeDegrees,pair.shakeSeconds);
                SampleExecution(time);
            }
            if(ExecutionPlaybackElapsed>=pair.PlaybackDuration)CompleteExecution();
        }
        Vector3 ExecutionContact()
        {
            var bone=executionTarget.animator.GetComponentsInChildren<Transform>();Transform chest=null;
            foreach(var t in bone)if(t.name=="Spine2"){chest=t;break;}
            Vector3 point=chest!=null?chest.position:executionTarget.BodyPoint();
            if(weaponBase!=null&&weaponTip!=null){var d=weaponTip.position-weaponBase.position;if(d.sqrMagnitude>.0001f)point=weaponBase.position+d*Mathf.Clamp01(Vector3.Dot(point-weaponBase.position,d)/d.sqrMagnitude);}
            return point;
        }
        void ReceiveExecutionDamage(SamuraiCombatant source,float amount,Vector3 point)
        {
            if(executionOwner!=source||!IsBeingExecuted||IsDead)return;
            float before=vitals.Health;vitals.Damage(Mathf.Max(0,amount));
            var hit=new SamuraiDamageHit {Damage=amount,Source=source,SourcePosition=source.transform.position,Point=point,Unblockable=true};
            var result=vitals.Health<=0?SamuraiHitResult.Killed:SamuraiHitResult.Hit;
            // Death events happen at contact; the selected V2 continues to its prone end.
            if(result==SamuraiHitResult.Killed)OnActionStateChanged(StateName);
            LastHitResult=result;HitReceived?.Invoke(result);AttackReceived?.Invoke(hit,result);
            if(before>vitals.Health)DamageReceived?.Invoke(new SamuraiDamageFeedback(hit,result,before-vitals.Health));
            if(result==SamuraiHitResult.Killed)Died?.Invoke();
        }
        void CompleteExecution()
        {
            var target=executionTarget;var pair=activeExecutionPair;executionTarget=null;activeExecutionPair=null;
            animator.speed=executionActorSpeed;
            if(target!=null&&target.executionOwner==this)
            {target.executionOwner=null;target.animator.speed=executionReceiverSpeed;target.actions.CompletePairedExecution(pair,target.Health<=0,executionHealthyBranch);target.CompleteExecutionPose(pair,target.Health<=0,executionHealthyBranch);}
            actions.FinishExecution();
        }
        void HandleExecutionStateChanged()
        {
            if(executionTarget!=null&&!IsExecuting)AbortExecution();
            if(executionOwner!=null&&!IsBeingExecuted){var owner=executionOwner;executionOwner=null;owner.AbortExecution();}
        }
        void AbortExecution()
        {
            if(executionOwner!=null&&executionOwner.executionTarget==this)
            {var source=executionOwner;source.AbortExecution();executionOwner=null;return;}
            var target=executionTarget;var owner=executionOwner;var pair=activeExecutionPair;
            executionTarget=executionOwner=null;activeExecutionPair=null;
            if(pair!=null&&animator!=null)animator.speed=executionActorSpeed;
            if(target!=null&&target.executionOwner==this)
            {
                target.executionOwner=null;if(target.animator!=null)target.animator.speed=executionReceiverSpeed;
                if(target.actions!=null&&target.IsBeingExecuted)
                {
                    if(executionDamageApplied){target.actions.CompletePairedExecution(pair,target.Health<=0,executionHealthyBranch);target.CompleteExecutionPose(pair,target.Health<=0,executionHealthyBranch);}
                    else {target.ClearExecutionPose();target.actions.ResumeToughnessBreak(savedBreakRemaining-executionElapsed);}
                }
            }
            if(owner!=null&&owner.executionTarget==this)owner.AbortExecution();
            executionElapsed=0;if(actions!=null&&actions.IsExecuting)actions.FinishExecution();
        }
        bool ExecutionSpaceClear(SamuraiCombatant target,Vector3 from,Vector3 to)
        {
            if(motor==null||!motor.enabled)return false;
            float radius=motor.radius*Mathf.Max(transform.lossyScale.x,transform.lossyScale.z)*.95f;
            float height=Mathf.Max(radius*2,motor.height*transform.lossyScale.y);
            Vector3 a=from+Vector3.up*(radius+.08f),b=from+Vector3.up*(height-radius);
            int count=Physics.OverlapCapsuleNonAlloc(a+(to-from),b+(to-from),radius,executionOverlaps,profile.obstructionMask,QueryTriggerInteraction.Ignore);
            if(count==executionOverlaps.Length)return false;
            for(int i=0;i<count;i++)
            {var collider=executionOverlaps[i];
                if(collider.GetComponentInParent<SamuraiCombatant>()!=this&&collider.GetComponentInParent<SamuraiCombatant>()!=target)return false;
            }
            var delta=to-from;if(delta.sqrMagnitude<.000001f)return true;
            count=Physics.CapsuleCastNonAlloc(a,b,radius,delta.normalized,executionHits,delta.magnitude,profile.obstructionMask,QueryTriggerInteraction.Ignore);
            if(count==executionHits.Length)return false;
            for(int i=0;i<count;i++)
            {var hit=executionHits[i];
                if(hit.collider.GetComponentInParent<SamuraiCombatant>()!=this&&hit.collider.GetComponentInParent<SamuraiCombatant>()!=target)return false;
            }
            return true;
        }
        bool ExecutionGround(Vector3 point,float expectedY)
        {
            float floor;return TryExecutionFloor(point,out floor)&&Mathf.Abs(floor-expectedY)<.10f;
        }
        bool TryExecutionFloor(Vector3 point,out float floor)
        {
            floor=float.NegativeInfinity;
            int count=Physics.RaycastNonAlloc(point+Vector3.up*.3f,Vector3.down,executionHits,.65f,profile.obstructionMask,QueryTriggerInteraction.Ignore);
            if(count==executionHits.Length)return false;
            for(int i=0;i<count;i++)
            {var hit=executionHits[i];
                if(hit.collider.GetComponentInParent<SamuraiCombatant>()==null&&hit.normal.y>.85f&&Mathf.Abs(hit.point.y-point.y)<.35f)
                    floor=Mathf.Max(floor,hit.point.y);
            }
            return !float.IsNegativeInfinity(floor);
        }
        Vector3 BodyPoint()=>motor!=null?transform.TransformPoint(motor.center):transform.position+Vector3.up*.8f;
    }
}

