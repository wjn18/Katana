using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    public sealed partial class SamuraiCombatant
    {
        readonly Dictionary<SamuraiExecutionPoseClip,SamuraiExecutionPoseClip.Binding> executionPoseBindings=new Dictionary<SamuraiExecutionPoseClip,SamuraiExecutionPoseClip.Binding>();
        Animator executionPoseAnimator;
        SamuraiExecutionPoseClip executionPose;
        string executionPoseState;
        Vector3 executionVisualHome;
        float executionPoseFloor;
        bool settlingCorpsePose;
        float corpseRestStartedAt;
        struct AlignmentTransform
        {
            public Transform bone;
            public Vector3 position,scale;
            public Quaternion rotation;
        }
        AlignmentTransform[] executionAlignmentPose;
        string executionDownState;
        float executionDownPhase,executionDownLength;
        ExecutionGroundSkin[] executionGroundSkins;
        sealed class ExecutionGroundSkin
        {
            readonly Transform[] bones;readonly Matrix4x4[] bind,matrices;
            readonly Vector3[] vertices;readonly BoneWeight[] weights;
            public ExecutionGroundSkin(SkinnedMeshRenderer skin)
            {bones=skin.bones;bind=skin.sharedMesh.bindposes;vertices=skin.sharedMesh.vertices;weights=skin.sharedMesh.boneWeights;matrices=new Matrix4x4[bones.Length];}
            public float Minimum()
            {
                for(int i=0;i<bones.Length;i++)matrices[i]=bones[i].localToWorldMatrix*bind[i];
                float min=float.PositiveInfinity;
                for(int i=0;i<vertices.Length;i++)
                {var w=weights[i];var v=vertices[i];var p=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;min=Mathf.Min(min,p.y);}
                return min;
            }
        }
        void CaptureExecutionAlignmentPose()
        {
            executionGroundSkins=null;
            executionDownState=StateName;
            var state=animator.GetCurrentAnimatorStateInfo(0);
            if(animator.IsInTransition(0))
            {
                var next=animator.GetNextAnimatorStateInfo(0);
                if(next.shortNameHash==Animator.StringToHash(executionDownState))state=next;
            }
            executionDownPhase=state.shortNameHash==Animator.StringToHash(executionDownState)?state.normalizedTime:0;
            var down=actions.DownBranchHealthy?profile.toughnessBrokenHealthyLoop:profile.toughnessBrokenLowHealthLoop;
            executionDownLength=down!=null?Mathf.Max(.0001f,down.length):0;
            var bones=animator.GetComponentsInChildren<Transform>(true);
            executionAlignmentPose=new AlignmentTransform[bones.Length];
            for(int i=0;i<bones.Length;i++)executionAlignmentPose[i]=new AlignmentTransform
            {bone=bones[i],position=bones[i].localPosition,rotation=bones[i].localRotation,scale=bones[i].localScale};
        }
        void ApplyExecutionAlignmentPose()
        {
            if(executionAlignmentPose==null)return;
            foreach(var pose in executionAlignmentPose)if(pose.bone!=null)
            {pose.bone.localPosition=pose.position;pose.bone.localRotation=pose.rotation;pose.bone.localScale=pose.scale;}
        }
        void ApplyExecutionDownPose(float elapsed)
        {
            ApplyExecutionAlignmentPose();
            if(executionDownLength<=0||string.IsNullOrEmpty(executionDownState))return;
            float speed=executionOwner!=null?Mathf.Max(0,executionOwner.executionReceiverSpeed):1;
            animator.Play(executionDownState,0,executionDownPhase+Mathf.Max(0,elapsed)*speed/executionDownLength);
            animator.Update(0);
        }
        void ApplyExecutionPreparation(float time,float progress)
        {
            ApplyExecutionPose(time);
            if(executionAlignmentPose==null)return;
            float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(progress));
            foreach(var pose in executionAlignmentPose)if(pose.bone!=null)
            {
                // Interpolate rigid joint rotations, never limb length or scale. Only hips and the
                // visual root translate to settle the body; the logical enemy root stays fixed.
                pose.bone.localPosition=pose.bone==animator.transform||pose.bone.name=="Hips"
                    ?Vector3.Lerp(pose.position,pose.bone.localPosition,blend):pose.position;
                pose.bone.localRotation=Quaternion.Slerp(pose.rotation,pose.bone.localRotation,blend);
                pose.bone.localScale=pose.scale;
            }
            if(executionGroundSkins==null)
            {
                var skins=animator.GetComponentsInChildren<SkinnedMeshRenderer>();
                var list=new List<ExecutionGroundSkin>();
                foreach(var skin in skins)if(skin.sharedMesh!=null&&skin.sharedMesh.vertexCount>0&&skin.bones.Length>0)list.Add(new ExecutionGroundSkin(skin));
                executionGroundSkins=list.ToArray();
            }
            float minimum=float.PositiveInfinity;
            foreach(var skin in executionGroundSkins)minimum=Mathf.Min(minimum,skin.Minimum());
            float floor=executionPoseFloor+(executionPose!=null?executionPose.groundContactY:0);
            if(minimum<floor)animator.transform.position+=Vector3.up*(floor-minimum);
        }

        SamuraiExecutionPoseClip.Binding PoseBinding(SamuraiExecutionPoseClip clip)
        {
            if(executionPoseAnimator!=animator){executionPoseBindings.Clear();executionPoseAnimator=animator;}
            SamuraiExecutionPoseClip.Binding binding;
            if(!executionPoseBindings.TryGetValue(clip,out binding))
            {binding=new SamuraiExecutionPoseClip.Binding(clip,animator);executionPoseBindings[clip]=binding;}
            return binding;
        }
        bool CanBindExecutionPose(SamuraiExecutionPair pair)
        {
            if(pair.receiverPoseHealthy==null&&pair.receiverPoseLowHealth==null)return true;
            return pair.receiverPoseHealthy!=null&&pair.receiverPoseLowHealth!=null
                &&PoseBinding(pair.receiverPoseHealthy).Valid&&PoseBinding(pair.receiverPoseLowHealth).Valid
                &&(pair.getupPoseHealthy==null||PoseBinding(pair.getupPoseHealthy).Valid)
                &&(pair.getupPoseLowHealth==null||PoseBinding(pair.getupPoseLowHealth).Valid)
                &&(pair.corpseRestPose==null||PoseBinding(pair.corpseRestPose).Valid);
        }
        void SetExecutionPose(SamuraiExecutionPoseClip clip,string state,float floor)
        {
            if(clip==null){ClearExecutionPose();return;}
            if(executionPose==null)executionVisualHome=animator.transform.localPosition;
            executionPose=clip;executionPoseState=state;executionPoseFloor=floor;settlingCorpsePose=false;
        }
        void ApplyExecutionPose(float time)
        {
            if(executionPose==null||animator==null)return;
            animator.transform.localPosition=executionVisualHome;
            PoseBinding(executionPose).Apply(time);
            float scale=animator.transform.lossyScale.y/Mathf.Max(.00001f,executionPose.referenceVisualScale);
            float lift=(executionPose.groundLift.Evaluate(Mathf.Clamp(time,0,executionPose.duration))-executionPose.groundContactY)*scale+executionPose.groundContactY;
            // Move the whole visual, including attachments. The capsule remains at its checked ground anchor.
            animator.transform.position+=Vector3.up*(executionPoseFloor-transform.position.y+lift);
        }
        /// <summary>After Animator evaluation; also used by deterministic simulation/validation.</summary>
        public void EvaluateExecutionPose()
        {
            if(isActiveAndEnabled&&IsBeingExecuted&&executionOwner!=null&&executionOwner.IsExecutionHoldingDown)
            {ApplyExecutionDownPose(executionOwner.executionElapsed);return;}
            if(executionAlignmentPose!=null)
            {
                if(isActiveAndEnabled&&IsBeingExecuted&&executionOwner!=null&&executionOwner.IsExecutionAligning)
                {ApplyExecutionAlignmentPose();return;}
                if(isActiveAndEnabled&&IsBeingExecuted&&executionOwner!=null&&executionOwner.IsExecutionPreparing)
                {ApplyExecutionPreparation(executionOwner.ExecutionReceiverTime,executionOwner.ExecutionPreparationProgress);return;}
                executionAlignmentPose=null;
            }
            if(executionPose==null)return;
            if(!isActiveAndEnabled||StateName!=executionPoseState){ClearExecutionPose();return;}
            if(executionOwner!=null&&IsBeingExecuted)ApplyExecutionPose(executionOwner.ExecutionReceiverTime);
            else if(IsDead)ApplyExecutionPose(settlingCorpsePose&&actions!=null
                ?Mathf.Clamp(actions.Clock-corpseRestStartedAt,0,executionPose.duration):executionPose.duration);
            else if(actions!=null&&actions.IsGettingUp)ApplyExecutionPose(actions.StateNormalizedTime*executionPose.duration);
            else ClearExecutionPose();
        }
        void CompleteExecutionPose(SamuraiExecutionPair pair,bool killed,bool healthy)
        {
            if(executionPose==null)return;
            bool rest=killed&&pair.corpseRestPose!=null;
            var next=rest?pair.corpseRestPose:killed?(healthy?pair.receiverPoseHealthy:pair.receiverPoseLowHealth):(healthy?pair.getupPoseHealthy:pair.getupPoseLowHealth);
            SetExecutionPose(next,StateName,executionPoseFloor);settlingCorpsePose=rest;
            corpseRestStartedAt=actions!=null?actions.Clock:0;EvaluateExecutionPose();
        }
        void ClearExecutionPose()
        {
            SuspendExecutionPose();
            executionPose=null;executionPoseState=null;executionAlignmentPose=null;executionGroundSkins=null;settlingCorpsePose=false;
            executionDownState=null;executionDownPhase=executionDownLength=0;
        }
        void SuspendExecutionPose()
        {if(executionPose!=null&&animator!=null)animator.transform.localPosition=executionVisualHome;}
    }
}
