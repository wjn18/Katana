using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    public static partial class SamuraiDownPoseAdaptation
    {
        sealed class EVContactVertex
        {
            public SkinnedMeshRenderer renderer;public int vertex;public string label,bone;
            public Vector3 Position()
            {
                var mesh=renderer.sharedMesh;var v=mesh.vertices[vertex];var w=mesh.boneWeights[vertex];var binds=mesh.bindposes;var bones=renderer.bones;
                return bones[w.boneIndex0].localToWorldMatrix.MultiplyPoint3x4(binds[w.boneIndex0].MultiplyPoint3x4(v))*w.weight0
                    +bones[w.boneIndex1].localToWorldMatrix.MultiplyPoint3x4(binds[w.boneIndex1].MultiplyPoint3x4(v))*w.weight1
                    +bones[w.boneIndex2].localToWorldMatrix.MultiplyPoint3x4(binds[w.boneIndex2].MultiplyPoint3x4(v))*w.weight2
                    +bones[w.boneIndex3].localToWorldMatrix.MultiplyPoint3x4(binds[w.boneIndex3].MultiplyPoint3x4(v))*w.weight3;
            }
        }
        static AnimationClip EVOriginalReceiver(int index)
        {
            var imported=AssetDatabase.LoadAllAssetsAtPath(ExecutionFolder+"/SourceFBX/"+ReceiverFiles[index]+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
            var source=UnityEngine.Object.Instantiate(imported);source.hideFlags=HideFlags.HideAndDontSave;source.wrapMode=WrapMode.ClampForever;
            var settings=AnimationUtility.GetAnimationClipSettings(source);settings.loopTime=false;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(source,settings);
            return source; // No RootT edits, foot IK, bone rotation/translation/scale edits, or time warp.
        }
        static EVContactVertex EVFindNaturalContact(Rig rig,float height)
        {
            var ids=new[]{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg};
            var labels=new[]{"腰胯","腹部","躯干","躯干","大腿侧","大腿侧","膝腿侧","膝腿侧"};
            var allowed=new Dictionary<Transform,string>();for(int i=0;i<ids.Length;i++){var bone=rig.animator.GetBoneTransform(ids[i]);if(bone!=null)allowed[bone]=labels[i];}
            EVContactVertex best=null;float score=float.PositiveInfinity;
            foreach(var renderer in EVSurfaces(rig.gameObject).OfType<SkinnedMeshRenderer>())
            {
                var mesh=renderer.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var bones=renderer.bones;var matrices=bones.Select((b,i)=>b.localToWorldMatrix*mesh.bindposes[i]).ToArray();
                for(int v=0;v<vertices.Length;v++)
                {
                    var w=weights[v];int dominant=w.boneIndex0;float weight=w.weight0;
                    if(w.weight1>weight){dominant=w.boneIndex1;weight=w.weight1;}if(w.weight2>weight){dominant=w.boneIndex2;weight=w.weight2;}if(w.weight3>weight){dominant=w.boneIndex3;weight=w.weight3;}
                    string label;if(!allowed.TryGetValue(bones[dominant],out label)||weight<.45f)continue;
                    var p=matrices[w.boneIndex0].MultiplyPoint3x4(vertices[v])*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(vertices[v])*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(vertices[v])*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(vertices[v])*w.weight3;
                    float penalty=label=="膝腿侧"?.15f:label=="大腿侧"?.025f:0;
                    float cost=Mathf.Abs(p.y-height)*12+Mathf.Abs(p.x)*.03f-p.z*.08f+penalty;
                    if(cost<score){score=cost;best=new EVContactVertex{renderer=renderer,vertex=v,label=label,bone=bones[dominant].name};}
                }
            }
            if(best==null)throw new InvalidOperationException("No natural-height torso/leg surface found.");return best;
        }
        public static string BakeEliteNaturalPreview()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Preview baking requires Edit mode.");
            Directory.CreateDirectory(ElitePreviewFolder);AssetDatabase.Refresh();var scene=EditorSceneManager.NewPreviewScene();var temp=new List<UnityEngine.Object>();var report=new List<object>();
            try
            {
                var e=ExecutionSceneClone("Elite1",scene,temp);var p=ExecutionSceneClone("SamuraiPlayer",scene,temp);var skin=new SkinSampler(e.gameObject,e.animator);
                var bones=e.animator.GetComponentsInChildren<Transform>(true).Where(b=>b!=e.animator.transform).ToArray();
                var so=new SerializedObject(p.gameObject.GetComponent<OCDesign.Samurai.SamuraiCombatant>());var tip=(Transform)so.FindProperty("weaponTip").objectReferenceValue;var bladeBase=(Transform)so.FindProperty("weaponBase").objectReferenceValue;
                for(int index=0;index<2;index++)
                {
                    e.gameObject.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    string code=index==0?"09600":"09702";float strike=index==0?26f/60:37f/60,delay=strike,yawDegrees=index==0?150:120;
                    var attack=AssetDatabase.LoadAssetAtPath<AnimationClip>(ExecutionFolder+"/Execution"+code+".anim");var source=EVOriginalReceiver(index);temp.Add(source);
                    Vector3 contact;using(var sample=new Sampling(p.animator,attack,false)){sample.At(strike);contact=Vector3.Lerp(bladeBase.position,tip.position,.88f);}
                    int steps=Mathf.CeilToInt((source.length+delay)*60);float duration=steps/60f;var raw=new List<EVPose>();var minimums=new List<float>();
                    using(var sample=new Sampling(e.animator,source,false))for(int f=0;f<=steps;f++)
                    {sample.At(Mathf.Clamp(f/60f-delay,0,source.length));raw.Add(new EVPose(bones));minimums.Add(skin.Minimum(5));}
                    float lift=.005f-minimums[0];raw[0].Apply(bones);var selected=EVFindNaturalContact(e,contact.y-lift);var localPoint=selected.Position();
                    var yaw=Quaternion.Euler(0,yawDegrees,0);Vector3 offset=contact-yaw*localPoint;offset.y=lift;
                    var receiver=EVBake("Elite1_"+code+"_NaturalReceiver",bones,e.animator,raw,duration);temp.Add(receiver);
                    // Explicitly measure the sampled derivative against the untouched source poses.
                    var replay=new EVGenericSampler(e.animator,receiver);float maxP=0,maxS=0,maxQ=0;
                    for(int f=0;f<=steps;f++){replay.At(f/60f);for(int b=0;b<bones.Length;b++){maxP=Mathf.Max(maxP,Vector3.Distance(bones[b].localPosition,raw[f].p[b]));maxS=Mathf.Max(maxS,Vector3.Distance(bones[b].localScale,raw[f].s[b]));var q=bones[b].localRotation;var q0=raw[f].q[b];float dot=Quaternion.Dot(q,q0);if(dot<0)q0=new Quaternion(-q0.x,-q0.y,-q0.z,-q0.w);maxQ=Mathf.Max(maxQ,Mathf.Max(Mathf.Abs(q.x-q0.x),Mathf.Max(Mathf.Abs(q.y-q0.y),Mathf.Max(Mathf.Abs(q.z-q0.z),Mathf.Abs(q.w-q0.w)))));}}
                    var rootLifts=minimums.Select(y=>Mathf.Max(0,minimums[0]-y)).ToArray();
                    for(int branch=0;branch<2;branch++)
                    {
                        string suffix=branch==0?"Healthy":"LowHealth";var down=AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination("Elite1",branch==0?"DownHealthy":"DownLowHealth"));EVPose downPose;
                        using(var sample=new Sampling(e.animator,down,false)){sample.At(down.length*.5f);downPose=new EVPose(bones);}
                        var entrance=new List<EVPose>();for(int f=0;f<=48;f++){EVPose.Blend(bones,downPose,raw[0],Mathf.SmoothStep(0,1,f/48f));entrance.Add(new EVPose(bones));}
                        temp.Add(EVBake("Elite1_"+code+"_NaturalEntrance"+suffix,bones,e.animator,entrance,.8f));
                        if(index==0)
                        {
                            var getup=AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination("Elite1",branch==0?"GetupHealthy":"GetupLowHealth"));var samples=new List<EVPose>();int count=Mathf.CeilToInt(getup.length*60);
                            using(var sample=new Sampling(e.animator,getup,false))for(int f=0;f<=count;f++){sample.At(Mathf.Min(f/60f,getup.length));samples.Add(new EVPose(bones));}
                            temp.Add(EVBake("Elite1_NaturalGetup"+suffix,bones,e.animator,samples,count/60f));
                            var bridge=new List<EVPose>();for(int f=0;f<=24;f++){EVPose.Blend(bones,raw.Last(),samples[0],Mathf.SmoothStep(0,1,f/24f));bridge.Add(new EVPose(bones));}
                            temp.Add(EVBake("Elite1_NaturalRecoveryBridge"+suffix,bones,e.animator,bridge,.4f));
                        }
                    }
                    report.Add(new{code,strike,delay,duration,sourceDuration=source.length,offset=EVP(offset),yaw=yawDegrees,contact=EVP(contact),contactAtImpact=EVP(yaw*localPoint+offset),contactHeightError=Mathf.Abs(localPoint.y+lift-contact.y),contactLabel=selected.label,contactBone=selected.bone,contactRenderer=AnimationUtility.CalculateTransformPath(selected.renderer.transform,e.gameObject.transform),contactVertex=selected.vertex,rootLifts,sourceMinimums=minimums,posePreservation=new{maxLocalPositionError=maxP,maxLocalScaleError=maxS,maxQuaternionComponentError=maxQ,samples=raw.Count,extraIK=false,extraBoneRotation=false,extraBoneScaling=false,timeScale=1f},playerClip=AssetDatabase.GetAssetPath(attack),playerClipUnchanged=true,previewOnly=true,version="natural-v2"});
                }
                EVSave("adaptation.json",report);EVSave("natural-adaptation.json",report);return Newtonsoft.Json.JsonConvert.SerializeObject(report);
            }
            finally{foreach(var o in temp.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
