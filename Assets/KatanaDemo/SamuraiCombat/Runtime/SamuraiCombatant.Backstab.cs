using UnityEngine;

namespace OCDesign.Samurai
{
    public sealed partial class SamuraiCombatant
    {
        float backstabLastSourceTime;
        Vector3 backstabReceiverCheckedPoint;
        public bool IsBackstabbing=>IsExecuting&&activeExecutionPair!=null&&activeExecutionPair.isBackstab;
        public SamuraiCombatant ActiveBackstabTarget=>IsBackstabbing?executionTarget:null;
        public bool HasBackstabReceiver=>profile!=null&&profile.backstabPair!=null&&profile.backstabPair.isBackstab;
        public bool IsBackstabCandidate(SamuraiCombatant target)=>BackstabCandidate(target);

        bool BackstabCandidate(SamuraiCombatant target)
        {
            if(!isActiveAndEnabled||profile==null||!profile.backstabEnabled||GetComponent<SamuraiPlayerInput>()==null
                ||Time.timeScale<=0||!CanAct||IsDead||IsHitStopped||IsBeingKnockedBack||animationDriver==null
                ||motor==null||!motor.enabled||!motor.isGrounded||target==null||target==this||target.Team==Team
                ||!target.isActiveAndEnabled||target.gameObject.scene!=gameObject.scene||!target.HasBackstabReceiver
                ||target.IsDead||!target.CanAct||target.IsBeingKnockedBack||target.IsHitStopped||target.executionOwner!=null
                ||target.motor==null||!target.motor.enabled||!target.motor.isGrounded)return false;
            float height=target.motor.height*Mathf.Abs(target.transform.lossyScale.y);
            if(height>Mathf.Max(.1f,profile.backstabMaximumTargetHeight)||Mathf.Abs(transform.position.y-target.transform.position.y)>.25f)return false;
            var pair=target.profile.backstabPair;
            float limit=new Vector2(pair.receiverOffset.x,pair.receiverOffset.z).magnitude+Mathf.Clamp(profile.backstabApproachRange,.05f,.6f)+.15f;
            Vector3 fromTarget=transform.position-target.transform.position;fromTarget.y=0;
            if(fromTarget.sqrMagnitude<.0001f||fromTarget.sqrMagnitude>limit*limit
                ||Vector3.Angle(-target.transform.forward,fromTarget)>Mathf.Clamp(profile.backstabRearHalfAngle,5,80)
                ||Vector3.Angle(transform.forward,-fromTarget)>Mathf.Clamp(profile.backstabFacingAngle,5,90))return false;
            var ai=target.GetComponent<SamuraiTrainingEnemy>();
            if(ai==null||ai.actor!=target||!ai.IsUnawareForBackstab(this))return false;
            return pair.attackerClip!=null&&pair.receiverClip!=null&&pair.receiverLowHealthClip!=null
                &&target.animationDriver!=null&&animationDriver.HasState(pair.attackerState)
                &&target.animationDriver.HasState(pair.receiverState)&&target.animationDriver.HasState(pair.receiverLowHealthState)
                &&target.CanBindExecutionPose(pair);
        }
        public bool CanBackstabTarget(SamuraiCombatant target)
        {
            if(!BackstabCandidate(target))return false;
            Vector3 anchor;Quaternion facing;float floor;
            return PlanBackstabApproach(target,out anchor,out facing,out floor);
        }
        public bool TryGetBackstabApproach(SamuraiCombatant target,out Vector3 position,out Quaternion facing)
        {
            position=transform.position;facing=transform.rotation;
            float floor;return BackstabCandidate(target)&&PlanBackstabApproach(target,out position,out facing,out floor);
        }
        public SamuraiCombatant FindBackstabTarget()
        {
            if(profile==null||!profile.backstabEnabled||!CanAct||Time.timeScale<=0)return null;
            var input=GetComponent<SamuraiPlayerInput>();if(input==null)return null;
            var locked=input.targetLock!=null?input.targetLock.CurrentTarget:null;
            if(CanBackstabTarget(locked))return locked;
            SamuraiCombatant nearest=null;float distance=float.PositiveInfinity;
            foreach(var candidate in FindObjectsByType<SamuraiCombatant>())
            {
                if(!BackstabCandidate(candidate))continue;
                float d=(candidate.transform.position-transform.position).sqrMagnitude;
                if(d>=distance||!CanBackstabTarget(candidate))continue;
                nearest=candidate;distance=d;
            }
            return nearest;
        }
        bool PlanBackstabApproach(SamuraiCombatant target,out Vector3 anchor,out Quaternion facing,out float floor)
        {
            anchor=transform.position;facing=transform.rotation;floor=0;
            if(target==null||target.profile==null||target.profile.backstabPair==null)return false;
            var pair=target.profile.backstabPair;Vector3 forward=target.transform.forward;forward.y=0;
            if(forward.sqrMagnitude<.0001f||!executionSensors.HasLineOfSight(this,target,profile.obstructionMask))return false;
            facing=Quaternion.LookRotation(forward)*Quaternion.Euler(0,-pair.receiverYaw,0);
            if(!TryExecutionFloor(target.transform.position,out floor))return false;
            anchor=target.transform.position-facing*new Vector3(pair.receiverOffset.x,0,pair.receiverOffset.z);anchor.y=floor;
            Vector3 toAnchor=anchor-transform.position;toAnchor.y=0;
            if(toAnchor.sqrMagnitude>Mathf.Pow(Mathf.Clamp(profile.backstabApproachRange,.05f,.6f),2)
                ||!ExecutionSpaceClear(target,transform.position,anchor)||!ExecutionGround(anchor,floor))return false;
            Vector3 previous=anchor,receiverPrevious=target.transform.position;
            for(int i=0;i<=24;i++)
            {
                float time=pair.Duration*i/24f;
                Vector3 next=anchor+facing*pair.RootAt(time);
                if(!ExecutionSpaceClear(target,previous,next)||!ExecutionGround(next,floor))return false;
                Vector3 receiverNext=target.transform.position+target.transform.rotation*pair.ReceiverRootAt(pair.ReceiverSecondsAt(time));
                if(!target.ExecutionSpaceClear(this,receiverPrevious,receiverNext)||!target.ExecutionGround(receiverNext,floor))return false;
                previous=next;receiverPrevious=receiverNext;
            }
            return true;
        }
        public bool TryBackstab(SamuraiCombatant target)
        {
            if(!BackstabCandidate(target))return false;
            Vector3 anchor;Quaternion facing;float floor;
            // Fresh physical checks on every actual E activation; cues cannot reserve an obsolete open doorway.
            if(!PlanBackstabApproach(target,out anchor,out facing,out floor))return false;
            return BeginPairedExecution(target,target.profile.backstabPair,anchor,facing,floor,
                Mathf.Max(target.MaxHealth,target.Health),true);
        }
        bool BackstabReceiverSpaceClear(float sourceSeconds)
        {
            var target=executionTarget;var pair=activeExecutionPair;
            if(target==null||pair==null||!pair.isBackstab)return true;
            Vector3 next=target.transform.position+target.transform.rotation*pair.ReceiverRootAt(pair.ReceiverSecondsAt(sourceSeconds));
            if(!target.ExecutionSpaceClear(this,backstabReceiverCheckedPoint,next,true)
                ||!target.ExecutionGround(next,target.executionPoseFloor))return false;
            backstabReceiverCheckedPoint=next;return true;
        }
    }
}
