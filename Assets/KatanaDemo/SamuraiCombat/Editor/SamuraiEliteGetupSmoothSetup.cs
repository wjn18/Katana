using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    public static partial class SamuraiDownPoseAdaptation
    {
        const float SmoothGetupHold=.06f, SmoothGetupBridge=.72f, SmoothGetupWarmup=.20f;
        static float SmoothGetupSource(float time,float length)
        {
            float elapsed=Mathf.Max(0,time-SmoothGetupHold-SmoothGetupBridge);
            if(elapsed<SmoothGetupWarmup)
            {
                float x=elapsed/SmoothGetupWarmup;
                // Integral of smoothstep: source speed rises continuously from zero to one.
                return SmoothGetupWarmup*(x*x*x-.5f*x*x*x*x);
            }
            return Mathf.Min(length,elapsed-SmoothGetupWarmup*.5f);
        }
        public static void BuildEliteSmoothGetupAssets()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before building getups.");
            if(new[]{"Healthy","LowHealth"}.All(s=>AssetDatabase.LoadAssetAtPath<AnimationClip>(EliteContinuityFolder+"/Elite1_SmoothGetup"+s+".anim")!=null
                &&AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(EliteContinuityFolder+"/Elite1_SmoothGetup"+s+"Pose.asset")!=null))return;
            var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var temporary=new List<UnityEngine.Object>();
            try
            {
                var rig=ExecutionSceneClone("Elite1",scene,temporary);var skin=new SkinSampler(rig.gameObject,rig.animator);
                var bones=rig.animator.GetComponentsInChildren<Transform>(true).Where(b=>b!=rig.animator.transform).ToArray();
                var profile=rig.gameObject.GetComponent<SamuraiCombatant>().Profile;
                foreach(bool healthy in new[]{true,false})
                {
                    string suffix=healthy?"Healthy":"LowHealth",name="Elite1_SmoothGetup"+suffix,path=EliteContinuityFolder+"/"+name;
                    if(AssetDatabase.LoadAssetAtPath<AnimationClip>(path+".anim")!=null&&AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(path+"Pose.asset")!=null)continue;
                    var native=healthy?profile.toughnessGetupHealthy:profile.toughnessGetupLowHealth;
                    float duration=SmoothGetupHold+SmoothGetupBridge+SmoothGetupWarmup*.5f+native.length;
                    int count=Mathf.CeilToInt(duration*120);
                    var humanoid=UnityEngine.Object.Instantiate(native);temporary.Add(humanoid);humanoid.name=name;
                    foreach(var binding in AnimationUtility.GetCurveBindings(native))
                    {
                        var source=AnimationUtility.GetEditorCurve(native,binding);var values=new float[count+1];
                        for(int f=0;f<=count;f++)values[f]=source.Evaluate(SmoothGetupSource(duration*f/count,native.length));
                        AnimationUtility.SetEditorCurve(humanoid,binding,Curve(values,duration));
                    }
                    var settings=AnimationUtility.GetAnimationClipSettings(humanoid);settings.startTime=0;settings.stopTime=duration;settings.loopTime=false;
                    AnimationUtility.SetAnimationClipSettings(humanoid,settings);humanoid.wrapMode=WrapMode.ClampForever;humanoid.EnsureQuaternionContinuity();
                    if(!humanoid.isHumanMotion)throw new InvalidOperationException("Humanoid getup required for a continuous return to locomotion.");
                    var receiver=healthy?profile.executionSurvivingPair.receiverPoseHealthy:profile.executionSurvivingPair.receiverPoseLowHealth;
                    var receiverBinding=new SamuraiExecutionPoseClip.Binding(receiver,rig.animator);
                    if(!receiverBinding.Valid)throw new InvalidOperationException("Complete Elite rig required.");
                    receiverBinding.Apply(receiver.duration);var first=new EVPose(bones);var frames=new List<EVPose>();
                    using(var sample=new ECMirroredSampler(rig.animator,humanoid))for(int f=0;f<=count;f++)
                    {
                        float time=duration*f/count;sample.At(time);
                        if(time<SmoothGetupHold+SmoothGetupBridge)
                        {
                            var destination=new EVPose(bones);float x=Mathf.Clamp01((time-SmoothGetupHold)/SmoothGetupBridge);
                            float weight=x*x*x*(10+x*(-15+6*x));
                            ECBlendRigid(bones,first,destination,weight);
                        }
                        frames.Add(new EVPose(bones));
                    }
                    var pose=ScriptableObject.CreateInstance<SamuraiExecutionPoseClip>();pose.name=name+"Pose";pose.duration=duration;
                    pose.referenceVisualScale=rig.animator.transform.lossyScale.y;pose.groundContactY=-.015f;
                    pose.tracks=bones.Select((bone,index)=>
                    {
                        var channels=Enumerable.Range(0,10).Select(_=>new float[frames.Count]).ToArray();Quaternion previous=Quaternion.identity;
                        for(int f=0;f<frames.Count;f++)
                        {
                            var p=frames[f].p[index];var q=frames[f].q[index];var s=frames[f].s[index];
                            if(f>0&&Quaternion.Dot(previous,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous=q;
                            var values=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int k=0;k<10;k++)channels[k][f]=values[k];
                        }
                        return new SamuraiExecutionPoseClip.Track{path=AnimationUtility.CalculateTransformPath(bone,rig.animator.transform),curves=channels.Select(v=>Curve(v,duration)).ToArray()};
                    }).ToArray();
                    var bindingPose=new SamuraiExecutionPoseClip.Binding(pose,rig.animator);int samples=Mathf.CeilToInt(duration*240);var heights=new float[samples+1];
                    for(int i=0;i<=samples;i++)
                    {
                        float time=duration*i/samples;bindingPose.Apply(time);
                        float floor=Mathf.Lerp(-.015f,.0015f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(duration-.3f,duration,time)));
                        heights[i]=floor-skin.Minimum(5);
                    }
                    pose.groundLift=Curve(heights,duration);SaveExecutionClip(humanoid,path+".anim");
                    if(AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(path+"Pose.asset")==null)
                    {AssetDatabase.CreateAsset(pose,path+"Pose.asset");AssetDatabase.SaveAssetIfDirty(pose);}
                    else{EditorUtility.CopySerialized(pose,AssetDatabase.LoadMainAssetAtPath(path+"Pose.asset"));temporary.Add(pose);AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path+"Pose.asset"));}
                }
            }
            finally
            {
                foreach(var item in temporary.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(item);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);
            }
        }
    }
}
