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
        public const string EliteContinuityFolder="Assets/KatanaDemo/SamuraiCombat/Animations/Execution/Elite1PoseContinuity";
        public static void BuildEliteContinuityAssets(bool rebuildDeath=false)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before building Elite1 pose assets.");
            string[] required={"Elite1_SettledDeath.anim","Elite1_CorpseRest.anim","Elite1_CorpseRestPose.asset","Elite1_LeftRecoveryHealthy.anim","Elite1_LeftRecoveryLowHealth.anim","Elite1_LeftGetupHealthy.anim","Elite1_LeftGetupHealthyPose.asset","Elite1_LeftGetupLowHealth.anim","Elite1_LeftGetupLowHealthPose.asset"};
            if(!rebuildDeath&&required.All(n=>AssetDatabase.LoadMainAssetAtPath(EliteContinuityFolder+"/"+n)!=null))return;
            Directory.CreateDirectory(EliteContinuityFolder);AssetDatabase.Refresh();
            var originalScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);var temp=new List<UnityEngine.Object>();
            try
            {
                var rig=ExecutionSceneClone("Elite1",scene,temp);var skin=new SkinSampler(rig.gameObject,rig.animator);
                var profile=rig.gameObject.GetComponent<SamuraiCombatant>().Profile;
                string deathPath=EliteContinuityFolder+"/Elite1_SettledDeath.anim";
                if(rebuildDeath||AssetDatabase.LoadAssetAtPath<AnimationClip>(deathPath)==null)
                {
                    var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Elite1CombatSetup.ClipFolder+"/Elite1_Death.anim");
                    var clip=UnityEngine.Object.Instantiate(source);temp.Add(clip);clip.name="Elite1_SettledDeath";
                    var feet=new ECFeet(rig.gameObject);int count=Mathf.CeilToInt(source.length*120);
                    string[] names=HumanTrait.MuscleName.Where(n=>(n.StartsWith("Left ")||n.StartsWith("Right "))&&(n.Contains("Leg")||n.Contains("Foot")||n.Contains("Toes"))).ToArray();
                    using(var handler=new HumanPoseHandler(rig.animator.avatar,rig.animator.transform))
                    using(var sampling=new Sampling(rig.animator,source,false))
                    {
                        sampling.At(source.length);
                        ECSettleLeg(rig.animator,feet,true,1);ECSettleLeg(rig.animator,feet,false,1);
                        float support=skin.Minimum(5);
                        ECFootRest(rig.animator,feet,true,support,1);ECFootRest(rig.animator,feet,false,support,1);
                        var pose=new HumanPose();handler.GetHumanPose(ref pose);
                        // Solve a resting pose once, then approach it continuously. Solving each falling
                        // frame independently can flip the knee pole while the body rolls.
                        foreach(string name in names)ECBlendDeathMuscle(clip,source,name,pose.muscles[Array.IndexOf(HumanTrait.MuscleName,name)],count);
                    }
                    // Retargeted muscle coordinates can move the body centre; calibrate only the new derivative.
                    for(int pass=0;pass<3;pass++)ECCalibrateDeathHeight(rig.animator,clip,skin,count);
                    // Re-evaluate the encoded Humanoid result before placing the toes: retargeting
                    // slightly changes the calf armour contact compared with the raw IK solution.
                    var footNames=names.Where(n=>n.Contains("Foot")||n.Contains("Toes")).ToArray();
                    for(int pass=0;pass<3;pass++)
                    {
                        using(var handler=new HumanPoseHandler(rig.animator.avatar,rig.animator.transform))
                        using(var sampling=new Sampling(rig.animator,clip,false))
                        {
                            sampling.At(clip.length);float support=skin.Minimum(5);
                            ECFootRest(rig.animator,feet,true,support,1);ECFootRest(rig.animator,feet,false,support,1);
                            var pose=new HumanPose();handler.GetHumanPose(ref pose);
                            foreach(string name in footNames)ECBlendDeathMuscle(clip,source,name,pose.muscles[Array.IndexOf(HumanTrait.MuscleName,name)],count);
                        }
                        ECCalibrateDeathHeight(rig.animator,clip,skin,count);
                    }
                    SaveExecutionClip(clip,deathPath);
                    var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(deathPath);
                    ECPreciseDeathGrounding(saved,rig.animator.humanScale*rig.animator.transform.lossyScale.y);
                }
                var bones=rig.animator.GetComponentsInChildren<Transform>(true).Where(t=>t!=rig.animator.transform).ToArray();
                for(int branch=0;branch<2;branch++)
                {
                    string suffix=branch==0?"Healthy":"LowHealth",name="Elite1_LeftGetup"+suffix,path=EliteContinuityFolder+"/"+name;
                    string recoveryPath=EliteContinuityFolder+"/Elite1_LeftRecovery"+suffix+".anim";
                    if(AssetDatabase.LoadAssetAtPath<AnimationClip>(recoveryPath)==null)
                    {
                        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination("Elite1",branch==0?"GetupHealthy":"GetupLowHealth"));
                        var recovery=UnityEngine.Object.Instantiate(source);temp.Add(recovery);recovery.name="Elite1_LeftRecovery"+suffix;
                        int count=Mathf.CeilToInt(source.length*480);float height=rig.animator.humanScale*rig.animator.transform.lossyScale.y;
                        for(int pass=0;pass<3;pass++)
                        {
                            var root=AnimationUtility.GetEditorCurve(recovery,Binding("RootT.y"));var recoveryHeights=new float[count+1];
                            using(var mirrored=new ECMirroredSampler(rig.animator,recovery))for(int f=0;f<=count;f++)
                            {float time=source.length*f/count;mirrored.At(time);recoveryHeights[f]=root.Evaluate(time)+(.0015f-skin.Minimum(5))/height;}
                            AnimationUtility.SetEditorCurve(recovery,Binding("RootT.y"),Curve(recoveryHeights,source.length));
                        }
                        SaveExecutionClip(recovery,recoveryPath);
                    }
                    if(AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(path+"Pose.asset")!=null&&AssetDatabase.LoadAssetAtPath<AnimationClip>(path+".anim")!=null)continue;
                    var native=branch==0?profile.toughnessGetupHealthy:profile.toughnessGetupLowHealth;
                    var pair=profile.executionSurvivingPair;var end=branch==0?pair.receiverPoseHealthy:pair.receiverPoseLowHealth;
                    var binding=new SamuraiExecutionPoseClip.Binding(end,rig.animator);if(!binding.Valid)throw new InvalidOperationException("Complete Elite rig required.");
                    binding.Apply(end.duration);var first=new EVPose(bones);
                    var frames=new List<EVPose>();int steps=Mathf.RoundToInt((native.length+.6f)*60);float duration=steps/60f;
                    using(var mirrored=new ECMirroredSampler(rig.animator,native))
                    for(int f=0;f<=steps;f++)
                    {
                        float time=f/60f;
                        if(time<.2f)first.Apply(bones);
                        else
                        {
                            mirrored.At(Mathf.Max(0,time-.6f));
                            if(time<.6f){var destination=new EVPose(bones);ECBlendRigid(bones,first,destination,Mathf.SmoothStep(0,1,(time-.2f)/.4f));}
                        }
                        frames.Add(new EVPose(bones));
                    }
                    temp.Add(EVBake(name,bones,rig.animator,frames,duration,EliteContinuityFolder));
                    if(AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(path+"Pose.asset")!=null)continue;
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path+".anim");
                    var pose=ScriptableObject.CreateInstance<SamuraiExecutionPoseClip>();pose.name=name+"Pose";pose.duration=duration;pose.referenceVisualScale=rig.animator.transform.lossyScale.y;pose.groundContactY=-.015f;
                    pose.tracks=AnimationUtility.GetCurveBindings(clip).Select(b=>b.path).Distinct().Select(p=>new SamuraiExecutionPoseClip.Track{path=p,curves=TransformProperties.Select(n=>AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(p,typeof(Transform),n))).ToArray()}).ToArray();
                    int samples=Mathf.CeilToInt(duration*240);var heights=new float[samples+1];var sampler=new EVGenericSampler(rig.animator,clip);
                    for(int i=0;i<=samples;i++){sampler.At(duration*i/samples);heights[i]=pose.groundContactY-skin.Minimum(5);}
                    pose.groundLift=Curve(heights,duration);AssetDatabase.CreateAsset(pose,path+"Pose.asset");AssetDatabase.SaveAssetIfDirty(pose);
                }
                if(AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(EliteContinuityFolder+"/Elite1_CorpseRestPose.asset")==null)
                {
                    var original=profile.executionLethalPair.receiverPoseHealthy;
                    var binding=new SamuraiExecutionPoseClip.Binding(original,rig.animator);
                    if(!binding.Valid)throw new InvalidOperationException("Complete Elite corpse rig required.");
                    binding.Apply(original.duration);var first=new EVPose(bones);var feet=new ECFeet(rig.gameObject);
                    float floor=skin.Minimum(5)+.002f;
                    ECSettleLeg(rig.animator,feet,true,1,floor);ECSettleLeg(rig.animator,feet,false,1,floor);
                    float support=skin.Minimum(5);ECFootRest(rig.animator,feet,true,support,1);ECFootRest(rig.animator,feet,false,support,1);
                    var last=new EVPose(bones);var frames=new List<EVPose>();
                    const float duration=.8f;for(int f=0;f<=48;f++){ECBlendRigid(bones,first,last,Mathf.SmoothStep(0,1,f/48f));frames.Add(new EVPose(bones));}
                    temp.Add(EVBake("Elite1_CorpseRest",bones,rig.animator,frames,duration,EliteContinuityFolder));
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(EliteContinuityFolder+"/Elite1_CorpseRest.anim");
                    var pose=ScriptableObject.CreateInstance<SamuraiExecutionPoseClip>();pose.name="Elite1_CorpseRestPose";pose.duration=duration;pose.referenceVisualScale=rig.animator.transform.lossyScale.y;pose.groundContactY=-.015f;
                    pose.tracks=AnimationUtility.GetCurveBindings(clip).Select(b=>b.path).Distinct().Select(p=>new SamuraiExecutionPoseClip.Track{path=p,curves=TransformProperties.Select(n=>AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(p,typeof(Transform),n))).ToArray()}).ToArray();
                    var sampler=new EVGenericSampler(rig.animator,clip);var lifts=new float[193];
                    for(int f=0;f<lifts.Length;f++){sampler.At(duration*f/(lifts.Length-1));lifts[f]=pose.groundContactY-skin.Minimum(5);}
                    pose.groundLift=Curve(lifts,duration);AssetDatabase.CreateAsset(pose,EliteContinuityFolder+"/Elite1_CorpseRestPose.asset");AssetDatabase.SaveAssetIfDirty(pose);
                }
            }
            finally{foreach(var o in temp.Where(o=>o!=null).Reverse<UnityEngine.Object>())UnityEngine.Object.DestroyImmediate(o);UnityEngine.SceneManagement.SceneManager.SetActiveScene(originalScene);EditorSceneManager.CloseScene(scene,true);}
        }
        static void ECBlendRigid(Transform[] bones,EVPose from,EVPose to,float weight)
        {
            for(int i=0;i<bones.Length;i++)
            {bones[i].localPosition=bones[i].name=="Hips"?Vector3.Lerp(from.p[i],to.p[i],weight):from.p[i];bones[i].localRotation=Quaternion.Slerp(from.q[i],to.q[i],weight);bones[i].localScale=from.s[i];}
        }
        static void ECCalibrateDeathHeight(Animator animator,AnimationClip clip,SkinSampler skin,int count)
        {
            var root=AnimationUtility.GetEditorCurve(clip,Binding("RootT.y"));var heights=new float[count+1];
            using(var sampling=new Sampling(animator,clip,false))for(int f=0;f<=count;f++)
            {float time=clip.length*f/count;sampling.At(time);heights[f]=root.Evaluate(time)+(.0015f-skin.Minimum(5))/(animator.humanScale*animator.transform.lossyScale.y);}
            AnimationUtility.SetEditorCurve(clip,Binding("RootT.y"),Curve(heights,clip.length));
        }
        static void ECBlendDeathMuscle(AnimationClip clip,AnimationClip source,string name,float resting,int count)
        {
            var original=AnimationUtility.GetEditorCurve(source,Binding(name));var values=new float[count+1];
            for(int f=0;f<=count;f++){float phase=(float)f/count;float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.55f,.95f,phase));values[f]=Mathf.Lerp(original.Evaluate(source.length*phase),resting,weight);}
            AnimationUtility.SetEditorCurve(clip,Binding(name),Curve(values,source.length));
        }
        static void ECPreciseDeathGrounding(AnimationClip clip,float height)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Elite1CombatSetup.PrefabPath);int count=Mathf.CeilToInt(clip.length*960)+1;
            for(int pass=0;pass<3;pass++)
            {
                var values=SamuraiEnemyReadabilitySetup.SampleDeath(prefab,clip,count);
                if(values.All(v=>Mathf.Abs(v-.0015f)<.0005f))break;
                var original=AnimationUtility.GetEditorCurve(clip,Binding("RootT.y"));
                for(int i=0;i<values.Length;i++){float time=clip.length*i/(count-1);values[i]=original.Evaluate(time)+(.0015f-values[i])/height;}
                AnimationUtility.SetEditorCurve(clip,Binding("RootT.y"),Curve(values,clip.length));
            }
            EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
        }
        sealed class ECMirroredSampler:IDisposable
        {
            readonly Animator animator;readonly AnimatorController controller;readonly AnimatorStateMachine machine;readonly AnimatorState state;readonly AnimationClip clip;
            public ECMirroredSampler(Animator a,AnimationClip source)
            {
                animator=a;clip=source;controller=new AnimatorController();machine=new AnimatorStateMachine();
                state=machine.AddState("Left handed getup");state.motion=source;state.mirror=true;state.iKOnFeet=false;
                controller.AddLayer(new AnimatorControllerLayer{name="Base Layer",stateMachine=machine,defaultWeight=1});
                animator.enabled=true;animator.runtimeAnimatorController=controller;animator.Rebind();
            }
            public void At(float time){animator.Play(state.name,0,Mathf.Clamp(time/clip.length,0,.999999f));animator.Update(0);}
            public void Dispose(){animator.runtimeAnimatorController=null;UnityEngine.Object.DestroyImmediate(state);UnityEngine.Object.DestroyImmediate(machine);UnityEngine.Object.DestroyImmediate(controller);}
        }
        sealed class ECFeet
        {
            sealed class Skin{public Transform[] bones;public Matrix4x4[] binds;public Vector3[] vertices;public BoneWeight[] weights;public int[][] indices;}
            readonly Skin[] skins;
            public ECFeet(GameObject go)
            {
                skins=go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).Select(r=>{
                    var m=r.sharedMesh;var s=new Skin{bones=r.bones,binds=m.bindposes,vertices=m.vertices,weights=m.boneWeights};
                    s.indices=new[]{"Left","Right"}.Select(side=>Enumerable.Range(0,m.vertexCount).Where(i=>{string n=s.bones[s.weights[i].boneIndex0].name;return n.StartsWith(side)&&(n.Contains("Foot")||n.Contains("Toe"));}).ToArray()).ToArray();return s;
                }).ToArray();
            }
            public float Minimum(bool left)
            {
                float min=float.PositiveInfinity;
                foreach(var s in skins){var matrices=s.bones.Select((b,i)=>b.localToWorldMatrix*s.binds[i]).ToArray();foreach(int i in s.indices[left?0:1]){var w=s.weights[i];var v=s.vertices[i];var p=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;min=Mathf.Min(min,p.y);}}
                return min;
            }
        }
        static void ECSettleLeg(Animator a,ECFeet feet,bool left,float weight,float floor=.003f)
        {
            var upper=a.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);var lower=a.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);var foot=a.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);var toes=a.GetBoneTransform(left?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
            var hips=a.GetBoneTransform(HumanBodyBones.Hips);var spine=a.GetBoneTransform(HumanBodyBones.Spine);
            Vector3 away=Vector3.ProjectOnPlane(hips.position-spine.position,Vector3.up).normalized;if(away.sqrMagnitude<.001f)away=-a.transform.forward;
            float reach=(lower.position-upper.position).magnitude+(foot.position-lower.position).magnitude-.0001f;
            var rotation=foot.rotation;
            if(toes!=null)rotation=Quaternion.Slerp(rotation,Quaternion.FromToRotation(toes.position-foot.position,away)*rotation,weight);
            Vector3 initial=foot.position;Vector3 target=initial;target.y-=feet.Minimum(left)-floor;
            float vertical=target.y-upper.position.y;float horizontal=Mathf.Sqrt(Mathf.Max(.001f,reach*reach-vertical*vertical))*.92f;
            target.x=upper.position.x+away.x*horizontal;target.z=upper.position.z+away.z*horizontal;
            target=Vector3.Lerp(initial,target,weight);var pole=Vector3.Cross(Vector3.up,away)*(left?1:-1)+Vector3.up*2f;
            SolveTwoBone(upper,lower,foot,target,pole,rotation);
            for(int i=0;i<3;i++){float gap=feet.Minimum(left)-floor;if(Mathf.Abs(gap)<.0003f)break;target=foot.position-Vector3.up*gap*weight;SolveTwoBone(upper,lower,foot,target,pole,rotation);}
        }
        static void ECFootRest(Animator a,ECFeet feet,bool left,float floor,float weight)
        {
            var foot=a.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
            var toe=a.GetBoneTransform(left?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
            if(toe==null||feet.Minimum(left)<=floor+.004f)return;
            var direction=Vector3.ProjectOnPlane(toe.position-foot.position,Vector3.up).normalized;
            var axis=Vector3.Cross(Vector3.up,direction);var original=foot.rotation;
            float lo=0,hi=55;
            for(int i=0;i<12;i++)
            {float angle=(lo+hi)*.5f;foot.rotation=Quaternion.AngleAxis(angle,axis)*original;if(feet.Minimum(left)>floor+.003f)lo=angle;else hi=angle;}
            foot.rotation=Quaternion.Slerp(original,Quaternion.AngleAxis(hi,axis)*original,weight);
        }
    }
    public static class SamuraiElitePoseContinuitySetup
    {
        [MenuItem("Katana/Samurai/Enemies/Install Elite1 Pose Continuity Fixes")]
        public static void Install()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before maintenance.");
            var p=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
            if(p==null||p.executionSurvivingPair==null||p.executionLethalPair==null||p.toughnessGetupHealthy==null||p.toughnessGetupLowHealth==null)
                throw new InvalidOperationException("Existing Elite1 paired execution and getup assets are required.");
            SamuraiDownPoseAdaptation.BuildEliteContinuityAssets();
            var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(Elite1CombatSetup.ControllerPath);
            if(p.elitePoseContinuityVersion<1)
            {
                var old=p.death;p.death=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_SettledDeath.anim");
                foreach(var child in c.layers[0].stateMachine.states)
                {
                    if(child.state.name=="Death"&&child.state.motion==old){child.state.motion=p.death;EditorUtility.SetDirty(child.state);}
                    if(child.state.name==SamuraiActionController.ToughnessGetupHealthyState||child.state.name==SamuraiActionController.ToughnessGetupLowHealthState){child.state.mirror=true;EditorUtility.SetDirty(child.state);}
                }
                if(p.toughnessGetupTransitionSeconds<0)p.toughnessGetupTransitionSeconds=.3f;
                foreach(var pair in new[]{p.executionSurvivingPair,p.executionLethalPair})
                {
                    pair.receiverPreparationSeconds=.4f;
                    pair.getupHealthy=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_LeftGetupHealthy.anim");pair.getupLowHealth=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_LeftGetupLowHealth.anim");
                    pair.getupPoseHealthy=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_LeftGetupHealthyPose.asset");pair.getupPoseLowHealth=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_LeftGetupLowHealthPose.asset");
                    foreach(var child in c.layers[0].stateMachine.states){if(child.state.name==pair.getupHealthyState){child.state.motion=pair.getupHealthy;EditorUtility.SetDirty(child.state);}if(child.state.name==pair.getupLowHealthState){child.state.motion=pair.getupLowHealth;EditorUtility.SetDirty(child.state);}}
                    EditorUtility.SetDirty(pair);AssetDatabase.SaveAssetIfDirty(pair);
                }
                p.elitePoseContinuityVersion=1;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);EditorUtility.SetDirty(c);AssetDatabase.SaveAssetIfDirty(c);
            }
            if(p.elitePoseContinuityVersion<2)
            {
                var oldHealthy=p.toughnessGetupHealthy;var oldLow=p.toughnessGetupLowHealth;
                p.toughnessGetupHealthy=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_LeftRecoveryHealthy.anim");
                p.toughnessGetupLowHealth=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_LeftRecoveryLowHealth.anim");
                foreach(var child in c.layers[0].stateMachine.states)
                {
                    if(child.state.name==SamuraiActionController.ToughnessGetupHealthyState&&child.state.motion==oldHealthy){child.state.motion=p.toughnessGetupHealthy;EditorUtility.SetDirty(child.state);}
                    if(child.state.name==SamuraiActionController.ToughnessGetupLowHealthState&&child.state.motion==oldLow){child.state.motion=p.toughnessGetupLowHealth;EditorUtility.SetDirty(child.state);}
                }
                p.elitePoseContinuityVersion=2;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);EditorUtility.SetDirty(c);AssetDatabase.SaveAssetIfDirty(c);
            }
            if(p.elitePoseContinuityVersion<3)
            {
                p.executionLethalPair.corpseRestPose=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_CorpseRestPose.asset");
                EditorUtility.SetDirty(p.executionLethalPair);AssetDatabase.SaveAssetIfDirty(p.executionLethalPair);
                p.elitePoseContinuityVersion=3;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
            }
            if(p.elitePoseContinuityVersion<4)
            {
                foreach(var pair in new[]{p.executionSurvivingPair,p.executionLethalPair})
                {
                    pair.receiverPreparationSeconds=0;pair.receiverHoldDownUntilStrike=true;
                    EditorUtility.SetDirty(pair);AssetDatabase.SaveAssetIfDirty(pair);
                }
                p.elitePoseContinuityVersion=4;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
            }
            SamuraiDownPoseAdaptation.BuildEliteSmoothGetupAssets();
            if(p.elitePoseContinuityVersion<5)
            {
                foreach(var pair in new[]{p.executionSurvivingPair,p.executionLethalPair})
                {
                    foreach(bool healthy in new[]{true,false})
                    {
                        string suffix=healthy?"Healthy":"LowHealth",state=healthy?pair.getupHealthyState:pair.getupLowHealthState;
                        var old=healthy?pair.getupHealthy:pair.getupLowHealth;var oldPose=healthy?pair.getupPoseHealthy:pair.getupPoseLowHealth;
                        if(old==null||old.name!="Elite1_LeftGetup"+suffix||oldPose==null||oldPose.name!="Elite1_LeftGetup"+suffix+"Pose")continue;
                        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_SmoothGetup"+suffix+".anim");
                        var pose=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(SamuraiDownPoseAdaptation.EliteContinuityFolder+"/Elite1_SmoothGetup"+suffix+"Pose.asset");
                        if(healthy){pair.getupHealthy=clip;pair.getupPoseHealthy=pose;}else{pair.getupLowHealth=clip;pair.getupPoseLowHealth=pose;}
                        foreach(var child in c.layers[0].stateMachine.states)if(child.state.name==state&&child.state.motion==old)
                        {child.state.motion=clip;child.state.mirror=true;EditorUtility.SetDirty(child.state);}
                    }
                    EditorUtility.SetDirty(pair);AssetDatabase.SaveAssetIfDirty(pair);
                }
                p.elitePoseContinuityVersion=5;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);EditorUtility.SetDirty(c);AssetDatabase.SaveAssetIfDirty(c);
            }
            if(p.elitePoseContinuityVersion<6)
            {
                foreach(var pair in new[]{p.executionSurvivingPair,p.executionLethalPair})
                    if(pair.getupExitBlendSeconds<0){pair.getupExitBlendSeconds=.35f;EditorUtility.SetDirty(pair);AssetDatabase.SaveAssetIfDirty(pair);}
                p.elitePoseContinuityVersion=6;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
            }
            Debug.Log("Elite1 death settling, smooth left-handed getups and down-until-contact execution installed; scene retained.");
        }
    }
}
