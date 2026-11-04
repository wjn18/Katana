using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    public static partial class SamuraiDownPoseAdaptation
    {
        public const string PlayerTwoStepPath="Assets/KatanaDemo/SamuraiCombat/Animations/Samurai_DeflectedTwoStep.anim";
        public const float TwoStepStopSeconds=.42f,TwoStepDuration=1.10f;
        static float TwoStepPhase(float seconds)
        {float x=Mathf.Clamp01(seconds/TwoStepStopSeconds);return 1-Mathf.Pow(1-x,1.3f);}
        static float TwoStepValue(string property,float time,AnimationClip hitClip,AnimationClip walk,
            Dictionary<string,AnimationCurve> hit,Dictionary<string,AnimationCurve> steps,Dictionary<string,AnimationCurve> rest)
        {
            if(property=="RootT.x"||property=="RootT.z")return 0;
            float phase=TwoStepPhase(time);
            bool legs=property.Contains("Leg")||property.Contains("Foot")||property.Contains("Toes")||property.StartsWith("RootT.")||property.StartsWith("RootQ.");
            float walking=StumbleSample(steps,property,phase*walk.length);
            float recoil=StumbleSample(hit,property,Mathf.Min(time*3.3f,hitClip.length*.74f));
            float value=legs?walking:recoil;
            float recoilWeight=Mathf.Sin(Mathf.PI*Mathf.Clamp01(time/.23f));
            float brake=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.27f,.42f,time))
                *(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.50f,.74f,time)));
            if(property=="Spine Front-Back")value+=-.20f*recoilWeight+.22f*brake;
            if(property=="Chest Front-Back")value+=-.12f*recoilWeight+.12f*brake;
            if(property=="Left Lower Leg Stretch"||property=="Right Lower Leg Stretch")value-=.14f*brake;
            if(property=="Spine Left-Right")value+=.08f*Mathf.Sin(phase*Mathf.PI*2)*(1-Mathf.Clamp01(time/TwoStepStopSeconds));
            float settle=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.54f,.90f,time));
            return Mathf.Lerp(value,StumbleSample(rest,property,0),settle);
        }
        public static AnimationCurve BuildPlayerQuickTwoStep()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before building the two-step reaction.");
            var profile=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>("Assets/KatanaDemo/SamuraiCombat/Config/SamuraiPlayer.asset");
            var impact=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ARPGPack/ARPGSamurai/Animations/Humanoid/ARPG_Samurai_Collide.anim");
            var walk=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ARPGPack/ARPGSamurai/Animations/Humanoid/ARPG_Samurai_Walk_Backward_Rootmotion.anim");
            if(profile==null||impact==null||walk==null||profile.idle==null)throw new InvalidOperationException("Existing reaction sources required.");
            var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);var temporary=new List<UnityEngine.Object>();
            try
            {
                var rig=ExecutionSceneClone("SamuraiPlayer",scene,temporary);float height=rig.animator.humanScale*rig.animator.transform.lossyScale.y;
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerTwoStepPath);const int count=330;
                if(clip==null)
                {
                    var source=UnityEngine.Object.Instantiate(impact);temporary.Add(source);source.name="Samurai_DeflectedTwoStep";
                    var hit=StumbleCurves(impact);var steps=StumbleCurves(walk);var rest=StumbleCurves(profile.idle);
                    foreach(string property in hit.Keys.Union(steps.Keys).Union(rest.Keys))
                    {
                        var values=new float[count+1];for(int f=0;f<=count;f++)values[f]=TwoStepValue(property,TwoStepDuration*f/count,impact,walk,hit,steps,rest);
                        AnimationUtility.SetEditorCurve(source,EditorCurveBinding.FloatCurve("",typeof(Animator),property),Curve(values,TwoStepDuration));
                    }
                    var settings=AnimationUtility.GetAnimationClipSettings(source);settings.startTime=0;settings.stopTime=TwoStepDuration;settings.loopTime=false;settings.loopBlend=false;
                    AnimationUtility.SetAnimationClipSettings(source,settings);AnimationUtility.SetAnimationEvents(source,new AnimationEvent[0]);source.EnsureQuaternionContinuity();
                    var skin=new SkinSampler(rig.gameObject,rig.animator);
                    for(int pass=0;pass<3;pass++)
                    {
                        var root=AnimationUtility.GetEditorCurve(source,Binding("RootT.y"));var heights=new float[count+1];
                        using(var sample=new Sampling(rig.animator,source,false))for(int f=0;f<=count;f++)
                        {float time=TwoStepDuration*f/count;sample.At(time);heights[f]=root.Evaluate(time)+(.0015f-skin.Minimum(5))/height;}
                        AnimationUtility.SetEditorCurve(source,Binding("RootT.y"),Curve(heights,TwoStepDuration));
                    }
                    SaveExecutionClip(source,PlayerTwoStepPath);clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerTwoStepPath);
                }
                // Anchor the support foot in each half-cycle instead of using the source's large root travel.
                // The first 8 cm recoil beat and the two steps finish together at the authored stop time.
                var distance=new float[count+1];float total=0;Vector3 previousLeft=Vector3.zero,previousRight=Vector3.zero;
                using(var sample=new Sampling(rig.animator,clip,false))for(int f=0;f<=count;f++)
                {
                    float time=TwoStepDuration*f/count;sample.At(time);
                    var left=rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                    var right=rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                    if(f>0&&time<=TwoStepStopSeconds+.00001f)
                        total+=Mathf.Max(0,TwoStepPhase(time)<.5f?right.z-previousRight.z:left.z-previousLeft.z);
                    float kick=.08f*(1-Mathf.Pow(1-Mathf.Clamp01(time/.04f),2));
                    distance[f]=total+kick;previousLeft=left;previousRight=right;
                }
                return Curve(distance,TwoStepDuration);
            }
            finally
            {
                foreach(var item in temporary.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(item);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);
            }
        }
    }
    public static class SamuraiQuickDeflectedSetup
    {
        [MenuItem("OC Design/Samurai Combat/Install Quick Two-Step Deflected Reaction")]
        public static void Install()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before maintenance.");
            var profile=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>("Assets/KatanaDemo/SamuraiCombat/Config/SamuraiPlayer.asset");
            if(profile==null||profile.deflectedStumbleVersion<2)throw new InvalidOperationException("Install the player-specific deflection state first.");
            if(profile.deflectedStumbleVersion>=3)return;
            var previous=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.PlayerStumblePath);
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/KatanaDemo/SamuraiCombat/Animations/SamuraiCombat.controller");
            var state=controller.layers[0].stateMachine.states.First(s=>s.state.name==profile.deflectedStateName).state;
            if(profile.deflected==previous&&state.motion==previous)
            {
                profile.deflectedRetreat=SamuraiDownPoseAdaptation.BuildPlayerQuickTwoStep();
                profile.deflected=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.PlayerTwoStepPath);state.motion=profile.deflected;
                EditorUtility.SetDirty(state);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
            }
            profile.deflectedStumbleVersion=3;EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
        }
    }
}
