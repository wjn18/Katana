using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    public static partial class SamuraiDownPoseAdaptation
    {
        public const string EliteExecutionFolder=ExecutionFolder+"/Elite1";
        static AnimationClip EliteRequiredClip(string path)
        {var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(clip==null)throw new InvalidOperationException("Missing "+path);return clip;}
        public static void BuildEliteExecutionAssets()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before building execution assets.");
            var names=new[]{"Elite1_ExecutionV1Healthy","Elite1_ExecutionV1LowHealth","Elite1_ExecutionV2Healthy","Elite1_ExecutionV2LowHealth","Elite1_ExecutionGetupHealthy","Elite1_ExecutionGetupLowHealth"};
            if(names.All(n=>AssetDatabase.LoadAssetAtPath<AnimationClip>(EliteExecutionFolder+"/"+n+".anim")!=null&&AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(EliteExecutionFolder+"/"+n+"Pose.asset")!=null))return;
            Directory.CreateDirectory(EliteExecutionFolder);AssetDatabase.Refresh();var scene=EditorSceneManager.NewPreviewScene();var temp=new List<UnityEngine.Object>();
            try
            {
                var rig=ExecutionSceneClone("Elite1",scene,temp);rig.animator.enabled=false;
                var bones=rig.animator.GetComponentsInChildren<Transform>(true).Where(b=>b!=rig.animator.transform).ToArray();var skin=new SkinSampler(rig.gameObject,rig.animator);
                for(int index=0;index<2;index++)for(int branch=0;branch<2;branch++)
                {
                    string code=index==0?"09600":"09702",suffix=branch==0?"Healthy":"LowHealth";
                    var receiver=EliteRequiredClip(ElitePreviewFolder+"/Elite1_"+code+"_NaturalReceiver.anim");
                    var entrance=EliteRequiredClip(ElitePreviewFolder+"/Elite1_"+code+"_NaturalEntrance"+suffix+".anim");
                    var rs=new EVGenericSampler(rig.animator,receiver);var es=new EVGenericSampler(rig.animator,entrance);
                    SaveEliteSequence("Elite1_ExecutionV"+(index+1)+suffix,.8f+receiver.length,rig,bones,skin,t=>{if(t<.8f)es.At(t);else rs.At(t-.8f);},temp);
                    if(index==0)
                    {
                        var bridge=EliteRequiredClip(ElitePreviewFolder+"/Elite1_NaturalRecoveryBridge"+suffix+".anim");
                        var getup=EliteRequiredClip(ElitePreviewFolder+"/Elite1_NaturalGetup"+suffix+".anim");
                        var bs=new EVGenericSampler(rig.animator,bridge);var gs=new EVGenericSampler(rig.animator,getup);
                        SaveEliteSequence("Elite1_ExecutionGetup"+suffix,.6f+getup.length,rig,bones,skin,t=>{if(t<.2f)rs.At(receiver.length);else if(t<.6f)bs.At(t-.2f);else gs.At(t-.6f);},temp);
                    }
                }
            }
            finally{foreach(var o in temp.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void SaveEliteSequence(string name,float duration,Rig rig,Transform[] bones,SkinSampler skin,Action<float> sample,List<UnityEngine.Object> temp)
        {
            string path=EliteExecutionFolder+"/"+name;var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path+".anim");
            if(clip==null)
            {
                int steps=Mathf.RoundToInt(duration*60);var frames=new List<EVPose>();
                for(int f=0;f<=steps;f++){sample(Mathf.Min(f/60f,duration));frames.Add(new EVPose(bones));}
                var baked=EVBake(name,bones,rig.animator,frames,duration,EliteExecutionFolder);temp.Add(baked);clip=EliteRequiredClip(path+".anim");
            }
            if(AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(path+"Pose.asset")!=null)return;
            var pose=ScriptableObject.CreateInstance<SamuraiExecutionPoseClip>();pose.name=name+"Pose";pose.duration=clip.length;
            pose.referenceVisualScale=rig.animator.transform.lossyScale.y;pose.groundContactY=ElitePreviewGroundY;
            pose.tracks=AnimationUtility.GetCurveBindings(clip).Select(b=>b.path).Distinct().Select(p=>new SamuraiExecutionPoseClip.Track{path=p,curves=TransformProperties.Select(n=>AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(p,typeof(Transform),n))).ToArray()}).ToArray();
            var sampler=new EVGenericSampler(rig.animator,clip);int count=Mathf.CeilToInt(clip.length*240);var heights=new float[count+1];
            for(int i=0;i<=count;i++){sampler.At(clip.length*i/count);heights[i]=ElitePreviewGroundY-skin.Minimum(5);}
            pose.groundLift=Curve(heights,clip.length);AssetDatabase.CreateAsset(pose,path+"Pose.asset");AssetDatabase.SaveAssetIfDirty(pose);
        }
    }

    public static class SamuraiEliteExecutionSetup
    {
        const string Root="Assets/KatanaDemo/SamuraiCombat";
        [MenuItem("OC Design/Samurai Combat/Install Elite1 Paired Executions")]
        public static void Install()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before installation.");
            SamuraiDownPoseAdaptation.BuildEliteExecutionAssets();
            var target=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Elite1CombatSetup.ControllerPath);
            var pairs=new SamuraiExecutionPair[2];
            for(int i=0;i<2;i++)
            {
                string code=i==0?"09600":"09702",name="Elite1_"+code+"_V"+(i+1),path=Root+"/Config/Execution/"+name+".asset";
                var pair=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPair>(path);
                if(pair==null)
                {
                    var reports=Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Path.Combine(SamuraiDownPoseAdaptation.ElitePreviewOutput,"analysis/adaptation.json")));
                    var source=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPair>(Root+"/Config/Execution/Enemy1_"+code+"_V"+(i+1)+".asset");
                    if(source==null)throw new InvalidOperationException("Enemy1 execution effects/template missing.");
                    pair=ScriptableObject.CreateInstance<SamuraiExecutionPair>();pair.name=name;
                    pair.attackerClip=source.attackerClip;pair.attackerState=source.attackerState;
                    pair.receiverStartSeconds=.8f;pair.receiverPlaybackVersion=1;
                    pair.strikeSeconds=source.strikeSeconds;pair.receiverDelay=source.strikeSeconds;pair.alignSeconds=source.alignSeconds;
                    var row=reports.First(r=>(string)r["code"]==code);var offset=row["offset"].ToObject<float[]>();
                    pair.receiverOffset=new Vector3(offset[0],0,offset[2]);pair.receiverYaw=(float)row["yaw"];
                    pair.receiverState="ExecutionV"+(i+1)+"Healthy";pair.receiverLowHealthState="ExecutionV"+(i+1)+"LowHealth";
                    pair.receiverClip=Clip("Elite1_"+pair.receiverState);pair.receiverLowHealthClip=Clip("Elite1_"+pair.receiverLowHealthState);
                    pair.receiverPoseHealthy=Pose("Elite1_"+pair.receiverState);pair.receiverPoseLowHealth=Pose("Elite1_"+pair.receiverLowHealthState);
                    pair.getupHealthy=Clip("Elite1_ExecutionGetupHealthy");pair.getupLowHealth=Clip("Elite1_ExecutionGetupLowHealth");
                    pair.getupPoseHealthy=Pose("Elite1_ExecutionGetupHealthy");pair.getupPoseLowHealth=Pose("Elite1_ExecutionGetupLowHealth");
                    pair.bloodMaterial=source.bloodMaterial;pair.bloodIntensity=source.bloodIntensity;pair.shakeDegrees=source.shakeDegrees;pair.shakeSeconds=source.shakeSeconds;
                    AssetDatabase.CreateAsset(pair,path);
                }
                // Migrate only the known baked entrance timing; keep all authored offsets/effects and later tuning.
                if(pair.receiverPlaybackVersion<1)
                {
                    if(Mathf.Abs(pair.attackerStartSeconds-.8f)<.0001f&&pair.receiverStartSeconds==0)
                    {pair.attackerStartSeconds=0;pair.receiverStartSeconds=.8f;pair.strikeSeconds=Mathf.Max(0,pair.strikeSeconds-.8f);}
                    pair.receiverPlaybackVersion=1;EditorUtility.SetDirty(pair);AssetDatabase.SaveAssetIfDirty(pair);
                }
                if(pair.presentationVersion<1)
                {
                    pair.playbackRate=.8f;pair.strikePlaybackRate=1f;pair.strikeLeadSeconds=.10f;pair.strikeTailSeconds=.08f;
                    pair.impactHoldSeconds=.12f;pair.cameraPushFov=6f;pair.cameraRecoilFov=2f;pair.presentationVersion=1;
                    EditorUtility.SetDirty(pair);AssetDatabase.SaveAssetIfDirty(pair);
                }
                if(pair.presentationVersion<2)
                {pair.standingEntryRadius=.45f;pair.presentationVersion=2;EditorUtility.SetDirty(pair);AssetDatabase.SaveAssetIfDirty(pair);}
                AddState(controller,pair.receiverState,pair.receiverClip);AddState(controller,pair.receiverLowHealthState,pair.receiverLowHealthClip);
                AddState(controller,pair.getupHealthyState,pair.getupHealthy);AddState(controller,pair.getupLowHealthState,pair.getupLowHealth);pairs[i]=pair;
            }
            if(target.executionPairConfigurationVersion<1)
            {
                target.executionDamageMode=SamuraiExecutionDamageMode.MaximumHealthFraction;
                target.executionSurvivingPair=pairs[0];target.executionLethalPair=pairs[1];target.executionPairConfigurationVersion=1;
                // Keep the enemy's authored percentage, including an explicit zero; never hard-code damage.
                EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);
            }
            AssetDatabase.SaveAssetIfDirty(controller);
            SamuraiElitePoseContinuitySetup.Install();
            Debug.Log("Elite1 paired executions installed; damage uses the enemy profile percentage.");
        }
        static AnimationClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.EliteExecutionFolder+"/"+name+".anim");
        static SamuraiExecutionPoseClip Pose(string name)=>AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(SamuraiDownPoseAdaptation.EliteExecutionFolder+"/"+name+"Pose.asset");
        static void AddState(AnimatorController controller,string name,AnimationClip clip)
        {
            var machine=controller.layers[0].stateMachine;if(machine.states.Any(s=>s.state.name==name))return;
            var state=machine.AddState(name);state.motion=clip;state.speed=1;state.iKOnFeet=false;state.writeDefaultValues=true;EditorUtility.SetDirty(controller);
        }
    }
}
