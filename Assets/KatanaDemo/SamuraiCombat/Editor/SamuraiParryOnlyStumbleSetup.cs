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
        public const string PlayerStumblePath="Assets/KatanaDemo/SamuraiCombat/Animations/Samurai_DeflectedStumble.anim";
        const float StumbleImpactEnd=.32f,StumbleWalkEnd=1.70f,StumbleDuration=2.10f;
        static float StumbleWalkTime(float time,float length)
        {
            float u=Mathf.Clamp01((time-StumbleImpactEnd)/(StumbleWalkEnd-StumbleImpactEnd));
            return length*(1-Mathf.Pow(1-u,1.15f));
        }
        static float StumbleSample(Dictionary<string,AnimationCurve> curves,string property,float time)
        {AnimationCurve c;return curves.TryGetValue(property,out c)?c.Evaluate(time):property=="RootQ.w"?1f:0f;}
        static Dictionary<string,AnimationCurve> StumbleCurves(AnimationClip clip)=>AnimationUtility.GetCurveBindings(clip)
            .Where(b=>b.type==typeof(Animator)).ToDictionary(b=>b.propertyName,b=>AnimationUtility.GetEditorCurve(clip,b));
        static float StumbleValue(string property,float time,AnimationClip impact,AnimationClip walk,AnimationClip idle,
            Dictionary<string,AnimationCurve> hit,Dictionary<string,AnimationCurve> steps,Dictionary<string,AnimationCurve> rest)
        {
            if(property=="RootT.x"||property=="RootT.z")return 0;
            float recoil=StumbleSample(hit,property,Mathf.Min(time,impact.length));
            if(time<=StumbleImpactEnd)return recoil;
            float walkTime=StumbleWalkTime(time,walk.length);
            float walking=StumbleSample(steps,property,Mathf.Min(walkTime,walk.length));
            bool legs=property.Contains("Leg")||property.Contains("Foot")||property.Contains("Toes")||property=="RootT.y";
            float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01((time-StumbleImpactEnd)/(legs?.16f:.65f)));
            float value=Mathf.Lerp(recoil,walking,blend);
            if(time<StumbleWalkEnd)
            {
                float u=Mathf.Clamp01((time-StumbleImpactEnd)/(StumbleWalkEnd-StumbleImpactEnd));
                if(property=="Spine Front-Back")value+=.12f*Mathf.Sin(Mathf.PI*u);
                if(property=="Spine Left-Right")value+=.065f*Mathf.Sin(3*Mathf.PI*u)*Mathf.Sin(Mathf.PI*u);
                return value;
            }
            float settle=Mathf.SmoothStep(0,1,Mathf.InverseLerp(StumbleWalkEnd,StumbleDuration,time));
            return Mathf.Lerp(value,StumbleSample(rest,property,0),settle);
        }
        public static AnimationCurve BuildPlayerDeflectedStumble()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before building the player stumble.");
            var player=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>("Assets/KatanaDemo/SamuraiCombat/Config/SamuraiPlayer.asset");
            var impact=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ARPGPack/ARPGSamurai/Animations/Humanoid/ARPG_Samurai_Collide.anim");
            var walk=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ARPGPack/ARPGSamurai/Animations/Humanoid/ARPG_Samurai_Walk_Backward_Rootmotion.anim");
            if(player==null||impact==null||walk==null||player.idle==null)throw new InvalidOperationException("Existing player and Humanoid reaction sources are required.");
            var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);var temporary=new List<UnityEngine.Object>();
            try
            {
                var rig=ExecutionSceneClone("SamuraiPlayer",scene,temporary);float height=rig.animator.humanScale*rig.animator.transform.lossyScale.y;
                var hit=StumbleCurves(impact);var steps=StumbleCurves(walk);var rest=StumbleCurves(player.idle);
                int count=Mathf.CeilToInt(StumbleDuration*120);var distance=new float[count+1];
                float impactStart=StumbleSample(hit,"RootT.z",0),impactDistance=impactStart-StumbleSample(hit,"RootT.z",StumbleImpactEnd),walkStart=StumbleSample(steps,"RootT.z",0);
                for(int f=0;f<=count;f++)
                {
                    float time=StumbleDuration*f/count;
                    float value=time<=StumbleImpactEnd?impactStart-StumbleSample(hit,"RootT.z",time)
                        :impactDistance+walkStart-StumbleSample(steps,"RootT.z",StumbleWalkTime(time,walk.length));
                    distance[f]=Mathf.Max(f>0?distance[f-1]:0,value*height);
                }
                if(AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerStumblePath)==null)
                {
                    var clip=UnityEngine.Object.Instantiate(impact);temporary.Add(clip);clip.name="Samurai_DeflectedStumble";
                    foreach(string property in hit.Keys.Union(steps.Keys).Union(rest.Keys))
                    {
                        var values=new float[count+1];for(int f=0;f<=count;f++)values[f]=StumbleValue(property,StumbleDuration*f/count,impact,walk,player.idle,hit,steps,rest);
                        AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),property),Curve(values,StumbleDuration));
                    }
                    var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.startTime=0;settings.stopTime=StumbleDuration;settings.loopTime=false;settings.loopBlend=false;
                    AnimationUtility.SetAnimationClipSettings(clip,settings);AnimationUtility.SetAnimationEvents(clip,new AnimationEvent[0]);clip.EnsureQuaternionContinuity();
                    var skin=new SkinSampler(rig.gameObject,rig.animator);
                    for(int pass=0;pass<3;pass++)
                    {
                        var root=AnimationUtility.GetEditorCurve(clip,Binding("RootT.y"));var values=new float[count+1];
                        using(var sample=new Sampling(rig.animator,clip,false))for(int f=0;f<=count;f++)
                        {float time=StumbleDuration*f/count;sample.At(time);values[f]=root.Evaluate(time)+(.0015f-skin.Minimum(5))/height;}
                        AnimationUtility.SetEditorCurve(clip,Binding("RootT.y"),Curve(values,StumbleDuration));
                    }
                    SaveExecutionClip(clip,PlayerStumblePath);
                }
                return Curve(distance,StumbleDuration);
            }
            finally
            {
                foreach(var item in temporary.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(item);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);
            }
        }
    }
    public static class SamuraiParryOnlyStumbleSetup
    {
        [MenuItem("OC Design/Samurai Combat/Install Elite1 Parry Only And Player Stumble")]
        public static void Install(){InstallElite();InstallPlayer();SamuraiQuickDeflectedSetup.Install();}
        public static void InstallElite()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before maintenance.");
            var profile=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Elite1CombatSetup.ControllerPath);
            if(profile==null||controller==null||profile.parry==null)throw new InvalidOperationException("Existing Elite1 independent parry is required.");
            var machine=controller.layers[0].stateMachine;
            if(!machine.states.Any(s=>s.state.name=="ParryReady"))
            {
                var state=machine.AddState("ParryReady");state.motion=profile.idle;state.iKOnFeet=false;state.writeDefaultValues=true;
                EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
            }
            if(profile.parryOnlyConfigurationVersion<1)
            {profile.parryOnlyDefense=true;profile.parryOnlyConfigurationVersion=1;EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);}
        }
        public static void InstallPlayer()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before maintenance.");
            var profile=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>("Assets/KatanaDemo/SamuraiCombat/Config/SamuraiPlayer.asset");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/KatanaDemo/SamuraiCombat/Animations/SamuraiCombat.controller");
            if(profile==null||controller==null)throw new InvalidOperationException("Existing player combat setup required.");
            if(profile.deflectedStumbleVersion>=2)return;
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.PlayerStumblePath);
            if(profile.deflectedStumbleVersion<1)
            {
                var distance=SamuraiDownPoseAdaptation.BuildPlayerDeflectedStumble();
                clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.PlayerStumblePath);
                if(profile.deflected==null||profile.deflected.name=="Samurai_Deflected")
                {profile.deflected=clip;profile.deflectedUseFullClip=true;profile.deflectedRetreat=distance;}
            }
            if(profile.deflected==clip)
            {
                var machine=controller.layers[0].stateMachine;
                var legacy=machine.states.FirstOrDefault(s=>s.state.name=="Deflected").state;
                if(legacy!=null&&legacy.motion==clip){legacy.motion=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KatanaDemo/SamuraiCombat/Animations/Samurai_Deflected.anim");EditorUtility.SetDirty(legacy);}
                var state=machine.states.FirstOrDefault(s=>s.state.name=="DeflectedStumble").state;
                if(state==null){state=machine.AddState("DeflectedStumble");state.motion=clip;state.speed=1;state.iKOnFeet=false;state.writeDefaultValues=true;EditorUtility.SetDirty(state);}
                profile.deflectedStateName="DeflectedStumble";EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
            }
            profile.deflectedStumbleVersion=2;EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
        }
    }
}
