using UnityEngine;

namespace OCDesign.Samurai
{
    public enum SamuraiExecutionDamageMode { FixedAmount, MaximumHealthFraction }

    /// <summary>Target-specific pair: authored in attacker forward coordinates, shared seconds.</summary>
    [CreateAssetMenu(menuName="OC Design/Samurai/Execution pair")]
    public sealed class SamuraiExecutionPair : ScriptableObject
    {
        public AnimationClip attackerClip, receiverClip;
        public AnimationClip receiverLowHealthClip;
        public string attackerState, receiverState;
        public string receiverLowHealthState;
        [Min(0f)] public float strikeSeconds=.45f;
        [Min(0f)] public float receiverDelay=.35f;
        public Vector3 receiverOffset=new Vector3(0,0,.85f);
        public float receiverYaw=180f;
        [Min(.05f)] public float alignSeconds=.18f;
        [Min(.01f)] public float receiverBlendSeconds=.18f;
        [Min(0f)] public float attackerStartSeconds;
        [Tooltip("Skip a baked preparation prefix without modifying the original receiver curves.")]
        [Min(0f)] public float receiverStartSeconds;
        [Tooltip("Hold the attacker at its first pose while the receiver settles from its actual down pose. Zero keeps the original entry.")]
        [Min(0f)] public float receiverPreparationSeconds;
        [Tooltip("Continue the receiver's current down loop until contact, then directly play the authored hit reaction.")]
        public bool receiverHoldDownUntilStrike;
        [HideInInspector] public int receiverPlaybackVersion;
        [Tooltip("Optional target-rig playback preserving the approved bones. Null keeps the existing Animator path.")]
        public SamuraiExecutionPoseClip receiverPoseHealthy,receiverPoseLowHealth,getupPoseHealthy,getupPoseLowHealth;
        [Tooltip("Optional rigid settling pose after the complete lethal reaction; does not extend or lock the attacker.")]
        public SamuraiExecutionPoseClip corpseRestPose;
        public AnimationCurve attackerRootX=AnimationCurve.Constant(0,3,0);
        public AnimationCurve attackerRootZ=AnimationCurve.Constant(0,3,0);
        public AnimationClip getupHealthy, getupLowHealth;
        public string getupHealthyState="ExecutionGetupHealthy",getupLowHealthState="ExecutionGetupLowHealth";
        [Tooltip("Blend from a completed execution getup to idle or guard. Negative keeps the actor's normal transition; zero cuts directly.")]
        public float getupExitBlendSeconds=-1f;
        public Material bloodMaterial;
        [Min(0f)] public float bloodIntensity=1.8f;
        [Range(0f,8f)] public float shakeDegrees=5f;
        [Range(.05f,.4f)] public float shakeSeconds=.16f;
        [Header("Execution rhythm (both actors share this clock)")]
        [Range(.1f,2f)] public float playbackRate=1f;
        [Range(.1f,2f)] public float strikePlaybackRate=1f;
        [Min(0f)] public float strikeLeadSeconds=.10f, strikeTailSeconds=.08f;
        [Range(0f,.2f)] public float impactHoldSeconds;
        [Range(0f,15f)] public float cameraPushFov;
        [Range(0f,6f)] public float cameraRecoilFov;
        [Tooltip("Additional valid input area around the authored standing point, for large receivers whose capsule surface is misleading.")]
        [Range(0f,.6f)] public float standingEntryRadius;
        [HideInInspector] public int presentationVersion;
        public float Duration => Mathf.Max(attackerClip!=null?attackerStartSeconds+attackerClip.length:0,receiverClip!=null?Mathf.Max(0,receiverClip.length-receiverStartSeconds):0);
        static float Rate(float value)=>float.IsNaN(value)||float.IsInfinity(value)?1f:Mathf.Clamp(value,.1f,2f);
        static float Nonnegative(float value)=>float.IsNaN(value)||float.IsInfinity(value)?0f:Mathf.Max(0,value);
        float Contact=>Mathf.Clamp(strikeSeconds,0,Duration);
        float Lead=>Mathf.Max(0,Contact-Nonnegative(strikeLeadSeconds));
        float Tail=>Mathf.Min(Duration,Contact+Nonnegative(strikeTailSeconds));
        float Hold=>Mathf.Min(.2f,Nonnegative(impactHoldSeconds));
        // The inverse returns the first instant at contact, before the contact hold.
        public float PlaybackSecondsAt(float sourceTime)
        {
            float source=Mathf.Clamp(sourceTime,0,Duration),slow=Rate(playbackRate),fast=Rate(strikePlaybackRate);
            return Mathf.Min(source,Lead)/slow+Mathf.Max(0,Mathf.Min(source,Tail)-Lead)/fast
                +Mathf.Max(0,source-Tail)/slow+(source>Contact?Hold:0);
        }
        public float PlaybackDuration=>PlaybackSecondsAt(Duration)+(Contact>=Duration?Hold:0);
        public float PreparationSeconds=>!receiverHoldDownUntilStrike&&receiverPoseHealthy!=null&&receiverPoseLowHealth!=null?Nonnegative(receiverPreparationSeconds):0f;
        public float TotalDuration=>Mathf.Max(0,alignSeconds)+PreparationSeconds+PlaybackDuration;
        public float SourceSecondsAt(float playbackTime)
        {
            float time=Mathf.Max(0,playbackTime),slow=Rate(playbackRate),fast=Rate(strikePlaybackRate);
            float preparation=Lead/slow,preContact=(Contact-Lead)/fast,postContact=(Tail-Contact)/fast;
            if(time<preparation)return time*slow;time-=preparation;
            if(time<preContact)return Lead+time*fast;time-=preContact;
            if(time<Hold)return Contact;time-=Hold;
            if(time<postContact)return Contact+time*fast;time-=postContact;
            return Mathf.Min(Duration,Tail+time*slow);
        }
        public Vector3 RootAt(float seconds)=>new Vector3(attackerRootX.Evaluate(seconds),0,attackerRootZ.Evaluate(seconds));
    }
}
