using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    // Read the original rig, including its left-hand weapon frame. Do not alter the Humanoid sampler with IK.
    public static class Elite1MotionBaker
    {
        const string SourceRig="Assets/2Handed_Hammer_Set/Animations/Humanoid/00_T-pose.FBX";
        const string Model="Assets/KatanaDemo/Characters/Elite1/Models/Elite1.fbx";
        const string Folder="Assets/2Handed_Hammer_Set/Animations/Generic/Inplace/";
        static readonly string[] Human={"Hips","Spine","Chest","UpperChest","Neck","Head","LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand","RightShoulder","RightUpperArm","RightLowerArm","RightHand","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes"};
        static readonly string[] Names={"Hips","Spine","Spine1","Spine2","Neck","Head","LeftShoulder","LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm","RightHand","LeftUpLeg","LeftLeg","LeftFoot","LeftToeBase","RightUpLeg","RightLeg","RightFoot","RightToeBase"};
        static readonly string[] Props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
        static readonly string[] Walk={"Walk","WalkForwardRight","WalkRight","WalkBackwardRight","WalkBackward","WalkBackwardLeft","WalkLeft","WalkForwardLeft"};
        static readonly string[] Run={"Run","RunForwardRight","RunRight","RunBackwardRight","RunBackward","RunBackwardLeft","RunLeft","RunForwardLeft"};
        static readonly string[] Directions={"front","frontR45","right","backR45","back","backL45","left","frontL45"};
        sealed class Spec{public string state,source;public bool loop;public float hold=-1;public Spec(string s,string c,bool l=false,float h=-1){state=s;source=c;loop=l;hold=h;}}
        static List<Spec> Specs(){var s=new List<Spec>{new Spec("Idle","idle",true),new Spec("Heavy","attack01"),new Spec("Sweep","attack02"),new Spec("SweepReverse","attack04"),new Spec("Strike","attack06"),new Spec("Combo01","combo01"),new Spec("Combo02","combo02"),new Spec("Combo03","Combo03"),new Spec("Combo04","Combo04"),new Spec("Guard","defense",true),new Spec("GuardHit","hit_body_front"),new Spec("GuardHit2","hit_body_left"),new Spec("GuardHit3","hit_body_right"),new Spec("GuardBreak","down_start"),new Spec("Hit","hit_body_front"),new Spec("Death","dead01"),new Spec("Deflected","hit_head_front"),new Spec("Charge","attack01",true,.12f)};for(int i=0;i<8;i++){s.Add(new Spec(Walk[i],"move_walk_"+Directions[i],true));s.Add(new Spec(Run[i],"move_run_"+Directions[i],true));}return s;}
        static float Duration(string state,float original)=>original;
        sealed class Skin
        {
            public Transform[] bones;public Matrix4x4[] bind;public Vector3[] v;public BoneWeight[] w;public int[] left,right;
            public Skin(SkinnedMeshRenderer r){bones=r.bones;bind=r.sharedMesh.bindposes;v=r.sharedMesh.vertices;w=r.sharedMesh.boneWeights;left=Enumerable.Range(0,v.Length).Where(i=>Foot(bones[w[i].boneIndex0].name,true)).ToArray();right=Enumerable.Range(0,v.Length).Where(i=>Foot(bones[w[i].boneIndex0].name,false)).ToArray();}
            static bool Foot(string n,bool left){return (n.Contains(left?"Left":"Right")||n.Contains(left?" L ":" R "))&&(n.Contains("Foot")||n.Contains("Toe"));}
            public float Minimum(int side){var m=bones.Select((b,i)=>b.localToWorldMatrix*bind[i]).ToArray();float result=float.PositiveInfinity;foreach(int i in side<0?Enumerable.Range(0,v.Length):side==0?left:right){var q=w[i];var p=m[q.boneIndex0].MultiplyPoint3x4(v[i])*q.weight0+m[q.boneIndex1].MultiplyPoint3x4(v[i])*q.weight1+m[q.boneIndex2].MultiplyPoint3x4(v[i])*q.weight2+m[q.boneIndex3].MultiplyPoint3x4(v[i])*q.weight3;result=Mathf.Min(result,p.y);}return result;}

        }
        static Transform[] TargetBones(GameObject g)=>Names.Select(n=>g.GetComponentsInChildren<Transform>().First(t=>t.name==n)).ToArray();
        static Transform[] SourceBones(Animator a)=>Human.Select(h=>a.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),h))).ToArray();
        public static void Reference(GameObject g,SkinnedMeshRenderer skin,Elite1CombatSetup.InstallReport report)
        {
            var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;var setup=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool");setup.GetMethod("SampleBindPose",flags).Invoke(null,new object[]{g});
            var map=Human.Select((h,i)=>new{h,n=Names[i]}).ToDictionary(p=>p.h,p=>p.n);var selected=new HashSet<Transform>(skin.bones);selected.Add(g.transform);foreach(var t in g.GetComponentsInChildren<Transform>())if(t.name=="Armature")selected.Add(t);
            var actual=selected.ToDictionary(t=>t,t=>true);var wrappers=setup.GetMethods(flags).First(m=>m.Name=="GetHumanBones"&&m.GetParameters()[0].ParameterType==map.GetType()).Invoke(null,new object[]{map,actual});setup.GetMethod("MakePoseValid",flags).Invoke(null,new[]{wrappers});report.tPoseError=(float)setup.GetMethod("GetPoseError",flags).Invoke(null,new[]{wrappers});report.validTPose=(bool)setup.GetMethod("IsPoseValid",flags).Invoke(null,new[]{wrappers});if(!report.validTPose)throw new InvalidOperationException("Elite1 invalid reference pose");report.mappedBones=Human.Length;
        }
        static Vector3 PalmLocal(Transform hand)=>hand.GetChild(0).localPosition*.45f;
        static Vector3 SourcePalm(Animator a,bool left){var h=a.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);var finger=a.GetBoneTransform(left?HumanBodyBones.LeftMiddleProximal:HumanBodyBones.RightMiddleProximal);return Vector3.Lerp(h.position,finger!=null?finger.position:h.position,.55f);}
        static readonly Dictionary<string,Vector3> JointSides=new Dictionary<string,Vector3>();
        static readonly Dictionary<string,Vector3> JointBasis=new Dictionary<string,Vector3>();
        static readonly Dictionary<string,Quaternion> Corrections=new Dictionary<string,Quaternion>();
        static float SampleDelta;
        static bool DampenArms;
        static void Solve(Transform upper,Transform lower,Transform end,Vector3 target,Quaternion rotation)
        {
            var start=upper.position;var oldJoint=lower.position;var oldEnd=end.position;var uq=upper.rotation;var lq=lower.rotation;
            float a=Vector3.Distance(start,oldJoint),b=Vector3.Distance(oldJoint,oldEnd);var delta=target-start;float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f);var axis=delta.normalized;
            var side=Vector3.ProjectOnPlane(oldJoint-start,axis);Vector3 previous;
            bool arm=upper.name.Contains("Arm");bool hasPrevious=JointSides.TryGetValue(upper.name,out previous);
            if(hasPrevious&&arm)previous=upper.parent.TransformDirection(previous);
            if(side.sqrMagnitude<.000001f&&hasPrevious)side=Vector3.ProjectOnPlane(previous,axis);
            if(side.sqrMagnitude<.000001f)side=Vector3.ProjectOnPlane(upper.up,axis);side.Normalize();
            if(hasPrevious&&Vector3.Dot(previous,side)<0)side=-side;
            if(hasPrevious&&DampenArms&&upper.name.Contains("Arm")){
                var prior=Vector3.ProjectOnPlane(previous,axis).normalized;
                if(prior.sqrMagnitude>.1f)side=Vector3.ProjectOnPlane(Vector3.RotateTowards(prior,side,360*Mathf.Deg2Rad*SampleDelta,0),axis).normalized;
            }
            JointSides[upper.name]=arm?upper.parent.InverseTransformDirection(side):side;
            float along=(a*a+d*d-b*b)/(2*d);var joint=start+axis*along+side*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));var endpoint=start+axis*d;
            var oldUpper=oldJoint-start;var oldLower=oldEnd-oldJoint;
            var newUpper=joint-start;var newLower=endpoint-joint;
            // Swing each segment toward the solved endpoint without imposing a new axial roll.
            // Rate-limiting the full IK correction breaks the palm contact during fast strikes.
            var solvedUpper=Quaternion.FromToRotation(oldUpper,newUpper)*uq;
            var solvedLower=Quaternion.FromToRotation(oldLower,newLower)*lq;
            if(!arm){
                var normal=Vector3.Cross(oldUpper,oldLower);Vector3 basis;
                if(!JointBasis.TryGetValue(upper.name,out basis)){if(normal.sqrMagnitude<.0000001f)normal=Vector3.Cross(oldUpper,side);basis=Quaternion.Inverse(uq)*normal.normalized;JointBasis[upper.name]=basis;}
                normal=(uq*basis).normalized;var newNormal=Vector3.Cross(newUpper,newLower).normalized;if(newNormal.sqrMagnitude<.01f)newNormal=Vector3.Cross(newUpper,side).normalized;
                solvedUpper=Quaternion.LookRotation(newUpper,newNormal)*Quaternion.Inverse(Quaternion.LookRotation(oldUpper,normal))*uq;
                solvedLower=Quaternion.LookRotation(newLower,newNormal)*Quaternion.Inverse(Quaternion.LookRotation(oldLower,normal))*lq;
            }
            upper.rotation=solvedUpper;
            if(DampenArms&&upper.name.Contains("Arm"))LimitAxialRoll(upper,Vector3.Scale(upper.localScale,lower.localPosition));
            lower.rotation=solvedLower;
            if(DampenArms&&upper.name.Contains("Arm"))LimitAxialRoll(lower,Vector3.Scale(lower.localScale,end.localPosition));
            end.rotation=rotation;
        }
        static void LimitAxialRoll(Transform bone,Vector3 childAxis)
        {
            var desired=bone.localRotation;Quaternion previous;
            if(Corrections.TryGetValue(bone.name,out previous)){
                // Parallel transport retains the solved segment direction; only its free axial roll is filtered.
                var transported=Quaternion.FromToRotation(previous*childAxis,desired*childAxis)*previous;
                var direction=desired*childAxis;
                desired=Quaternion.RotateTowards(transported,desired,360*SampleDelta);
                desired=Quaternion.FromToRotation(desired*childAxis,direction)*desired;
                bone.localRotation=desired;
            }
            Corrections[bone.name]=desired;
        }
        public sealed class SourceSampler
        {
            sealed class Track{public Transform t;public Vector3 p,s,e;public Quaternion q;public readonly Dictionary<string,AnimationCurve> curves=new Dictionary<string,AnimationCurve>();}
            readonly Track[] tracks;
            public SourceSampler(GameObject root,AnimationClip clip)
            {
                var map=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,root.transform),t=>t);var list=new Dictionary<string,Track>();
                foreach(var binding in AnimationUtility.GetCurveBindings(clip)){
                    Transform t;if(binding.type!=typeof(Transform)||!map.TryGetValue(binding.path,out t))continue;Track track;if(!list.TryGetValue(binding.path,out track)){track=new Track{t=t,p=t.localPosition,q=t.localRotation,s=t.localScale,e=t.localEulerAngles};list[binding.path]=track;}track.curves[binding.propertyName]=AnimationUtility.GetEditorCurve(clip,binding);
                }
                tracks=list.Values.ToArray();
            }
            static float V(Track t,string property,float fallback,float time){AnimationCurve curve;return t.curves.TryGetValue(property,out curve)?curve.Evaluate(time):fallback;}
            public void Sample(float time)
            {
                foreach(var t in tracks){t.t.localPosition=new Vector3(V(t,"m_LocalPosition.x",t.p.x,time),V(t,"m_LocalPosition.y",t.p.y,time),V(t,"m_LocalPosition.z",t.p.z,time));t.t.localScale=new Vector3(V(t,"m_LocalScale.x",t.s.x,time),V(t,"m_LocalScale.y",t.s.y,time),V(t,"m_LocalScale.z",t.s.z,time));
                    if(t.curves.Keys.Any(k=>k.Contains("Euler")))t.t.localRotation=Quaternion.Euler(V(t,"localEulerAnglesRaw.x",t.e.x,time),V(t,"localEulerAnglesRaw.y",t.e.y,time),V(t,"localEulerAnglesRaw.z",t.e.z,time));
                    else t.t.localRotation=new Quaternion(V(t,"m_LocalRotation.x",t.q.x,time),V(t,"m_LocalRotation.y",t.q.y,time),V(t,"m_LocalRotation.z",t.q.z,time),V(t,"m_LocalRotation.w",t.q.w,time)).normalized;
                }
            }
        }
        public static Dictionary<string,AnimationClip> Bake(Elite1CombatSetup.InstallReport report,string onlyState=null)
        {
            var scene=EditorSceneManager.NewPreviewScene();GameObject src=null,dst=null;var graph=default(PlayableGraph);var clips=new Dictionary<string,AnimationClip>();
            try{
                src=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SourceRig));dst=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));SceneManager.MoveGameObjectToScene(src,scene);SceneManager.MoveGameObjectToScene(dst,scene);
                src.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);src.transform.localScale=Vector3.one;dst.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);dst.transform.localScale=Vector3.one*1.8f;
                if(PrefabUtility.IsPartOfPrefabInstance(dst))PrefabUtility.UnpackPrefabInstance(dst,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                var sa=src.GetComponentInChildren<Animator>();sa.runtimeAnimatorController=null;sa.cullingMode=AnimatorCullingMode.AlwaysAnimate;sa.applyRootMotion=false;sa.Rebind();sa.enabled=false;report.avatarValid=sa.avatar.isValid&&sa.avatar.isHuman;
                var da=dst.GetComponentInChildren<Animator>()??dst.AddComponent<Animator>();da.enabled=false;da.runtimeAnimatorController=null;da.avatar=null;var renderer=dst.GetComponentInChildren<SkinnedMeshRenderer>();Reference(dst,renderer,report);
                var s=SourceBones(sa);var t=TargetBones(dst);var sq=s.Select(b=>b.rotation).ToArray();var tq=t.Select(b=>b.rotation).ToArray();var sp=s.Select(b=>b.position).ToArray();var tp=t.Select(b=>b.position).ToArray();
                float ratio=(Vector3.Distance(t[14].position,t[15].position)+Vector3.Distance(t[15].position,t[16].position))/(Vector3.Distance(s[14].position,s[15].position)+Vector3.Distance(s[15].position,s[16].position));
                ConfigureHeadMesh(dst);var targetSkin=new Skin(renderer);var sourceSkin=new Skin(src.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name.Contains("BASEMESH")));float sourceGround=Mathf.Min(sourceSkin.Minimum(0),sourceSkin.Minimum(1));
                var selected=new HashSet<Transform>(renderer.bones);foreach(var b in dst.GetComponentsInChildren<Transform>())if(b.name=="Armature")selected.Add(b);var bones=dst.GetComponentsInChildren<Transform>().Where(selected.Contains).ToArray();var paths=bones.Select(b=>AnimationUtility.CalculateTransformPath(b,dst.transform)).ToArray();report.bakedTransforms=bones.Length;

                var armature=bones.First(b=>b.name=="Armature");var armatureReference=armature.localRotation;
                var sourceTransforms=src.GetComponentsInChildren<Transform>();var sourcePositions=sourceTransforms.Select(b=>b.localPosition).ToArray();var sourceRotations=sourceTransforms.Select(b=>b.localRotation).ToArray();var sourceScales=sourceTransforms.Select(b=>b.localScale).ToArray();
                foreach(var spec in Specs().Where(v=>onlyState==null||v.state==onlyState)){
                    for(int i=0;i<sourceTransforms.Length;i++){sourceTransforms[i].localPosition=sourcePositions[i];sourceTransforms[i].localRotation=sourceRotations[i];sourceTransforms[i].localScale=sourceScales[i];}
                    JointSides.Clear();JointBasis.Clear();Corrections.Clear();DampenArms=!spec.loop;
                    var sourcePath=Folder+spec.source+"_inplace.fbx";var source=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));float duration=spec.hold>=0?.5f:Duration(spec.state,source.length);int steps=Mathf.RoundToInt(duration*(spec.loop?120:240));
                    SampleDelta=duration/steps;
                    var sampler=new SourceSampler(src,source);
                    var values=Enumerable.Range(0,bones.Length*10).Select(_=>new float[steps+1]).ToArray();var previous=new Quaternion[bones.Length];var footSamples=new Vector3[2][]{new Vector3[steps+1],new Vector3[steps+1]};var contact=new bool[2][]{new bool[steps+1],new bool[steps+1]};float minBody=999,maxBody=-999,maxLift=0;
                    for(int f=0;f<=steps;f++){
                        armature.localRotation=armatureReference;
                        sampler.Sample(spec.hold>=0?source.length*spec.hold:source.length*f/(float)steps);
                        for(int i=0;i<t.Length;i++)t[i].rotation=s[i].rotation*Quaternion.Inverse(sq[i])*tq[i];t[0].position=tp[0]+(s[0].position-sp[0])*ratio;
                        if(spec.state!="Death"){
                            var yawDelta=s[0].rotation*Quaternion.Inverse(sq[0]);var forward=Vector3.ProjectOnPlane(yawDelta*Vector3.forward,Vector3.up);var yaw=forward.sqrMagnitude>.01f?Quaternion.LookRotation(forward,Vector3.up):Quaternion.identity;
                            for(int leg=0;leg<2;leg++){int j=leg==0?14:18;var offset=tp[j+2]-tp[0]-(sp[j+2]-sp[0])*ratio;var goal=t[0].position+(s[j+2].position-s[0].position)*ratio+yaw*offset;var rotation=t[j+2].rotation;Solve(t[j],t[j+1],t[j+2],goal,rotation);float sourceHeight=sourceSkin.Minimum(leg)-sourceGround;float support=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.005f,.045f,sourceHeight));bool planted=support>.75f;float min=targetSkin.Minimum(leg);float correction=.001f-min;float lift=correction>=0?correction:correction*support;goal.y+=lift;Solve(t[j],t[j+1],t[j+2],goal,rotation);for(int iteration=0;iteration<6;iteration++){float penetration=.001f-targetSkin.Minimum(leg);if(penetration<=.0001f)break;goal.y+=penetration*1.2f;Solve(t[j],t[j+1],t[j+2],goal,rotation);}maxLift=Mathf.Max(maxLift,Mathf.Abs(lift));footSamples[leg][f]=t[j+2].position;contact[leg][f]=planted;}
                        }else{float lift=Mathf.Max(0,.001f-targetSkin.Minimum(-1));t[0].position+=Vector3.up*lift;maxLift=Mathf.Max(maxLift,lift);}





                        float minimum=targetSkin.Minimum(-1);minBody=Mathf.Min(minBody,minimum);maxBody=Mathf.Max(maxBody,minimum);
                        for(int i=0;i<bones.Length;i++){var p=bones[i].localPosition;var q=bones[i].localRotation;var z=bones[i].localScale;if(f>0&&Quaternion.Dot(q,previous[i])<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[i]=q;var v=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,z.x,z.y,z.z};for(int k=0;k<10;k++)values[i*10+k][f]=v[k];}
                    }
                    float[] phaseTimes=null; // Preserve native clip timing; no weapon-driven retiming.
                    var clip=new AnimationClip{name="Elite1_"+spec.state,frameRate=steps/duration};for(int i=0;i<bones.Length;i++)for(int k=0;k<10;k++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(paths[i],typeof(Transform),Props[k]),Curve(values[i*10+k],duration,phaseTimes));clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=spec.loop;settings.loopBlend=false;settings.startTime=0;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(clip,settings);

                    string path="Assets/KatanaDemo/Characters/Elite1/Animations/Generic/Combat/"+clip.name+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(existing==null){AssetDatabase.CreateAsset(clip,path);existing=clip;}else{EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);}EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);clips[spec.state]=existing;
                    var velocities=new List<float>();for(int leg=0;leg<2;leg++)for(int f=1;f<=steps;f++)if(contact[leg][f]&&contact[leg][f-1]){var d=footSamples[leg][f]-footSamples[leg][f-1];d.y=0;float speed=d.magnitude*steps/duration;if(speed>.1f)velocities.Add(speed);}velocities.Sort();
                    report.clips.Add(new Elite1CombatSetup.ClipReport{state=spec.state,source=sourcePath,path=path,duration=duration,frames=steps+1,curves=bones.Length*10,minBodyY=minBody,maxBodyY=maxBody,maximumGroundLift=maxLift,loop=spec.loop,strideSpeed=velocities.Count>0?velocities[velocities.Count/2]:0,alignmentYaw=0,phaseTimes=phaseTimes});
                }
                return clips;
            }finally{if(graph.IsValid())graph.Destroy();if(src!=null)UnityEngine.Object.DestroyImmediate(src);if(dst!=null)UnityEngine.Object.DestroyImmediate(dst);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static AnimationCurve Curve(float[] v,float duration,float[] times=null){if(v.Max()-v.Min()<.0000001f)return new AnimationCurve(new Keyframe(0,v[0]),new Keyframe(duration,v[0]));float dt=duration/(v.Length-1);return new AnimationCurve(v.Select((a,i)=>new Keyframe(times==null?dt*i:times[i]*duration,a,i>0?(a-v[i-1])/(times==null?dt:(times[i]-times[i-1])*duration):0,i+1<v.Length?(v[i+1]-a)/(times==null?dt:(times[i+1]-times[i])*duration):0)).ToArray());}
        static void GroundInterpolatedFeet(GameObject root,AnimationClip clip,Skin skin,Transform[] t,Transform[] bones,string[] paths,float duration)
        {
            int steps=Mathf.CeilToInt(duration*480);var selected=Enumerable.Range(0,bones.Length).Where(i=>new[]{"LeftUpLeg","LeftLeg","LeftFoot","RightUpLeg","RightLeg","RightFoot"}.Contains(bones[i].name)).ToArray();
            var values=Enumerable.Range(0,selected.Length*4).Select(_=>new float[steps+1]).ToArray();var previous=new Quaternion[selected.Length];
            for(int f=0;f<=steps;f++){
                clip.SampleAnimation(root,duration*f/steps);
                for(int leg=0;leg<2;leg++){int j=leg==0?14:18;var goal=t[j+2].position;var rotation=t[j+2].rotation;for(int iteration=0;iteration<8;iteration++){float lift=.0015f-skin.Minimum(leg);if(lift<=.0001f)break;goal.y+=lift*1.2f;Solve(t[j],t[j+1],t[j+2],goal,rotation);}}
                for(int k=0;k<selected.Length;k++){var q=bones[selected[k]].localRotation;if(f>0&&Quaternion.Dot(q,previous[k])<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[k]=q;values[k*4][f]=q.x;values[k*4+1][f]=q.y;values[k*4+2][f]=q.z;values[k*4+3][f]=q.w;}
            }
            for(int k=0;k<selected.Length;k++)for(int c=0;c<4;c++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(paths[selected[k]],typeof(Transform),Props[3+c]),Curve(values[k*4+c],duration));clip.EnsureQuaternionContinuity();
        }
        public static float SourcePhase(Elite1CombatSetup.ClipReport spec,float time){var times=spec.phaseTimes;if(times==null||times.Length<2)return time;int index=Array.BinarySearch(times,Mathf.Clamp01(time));if(index>=0)return index/(float)(times.Length-1);index=Mathf.Clamp(~index,1,times.Length-1);return (index-1+Mathf.InverseLerp(times[index-1],times[index],time))/(times.Length-1);}
        public static float OutputPhase(Elite1CombatSetup.ClipReport spec,float phase){var times=spec.phaseTimes;if(times==null||times.Length<2)return phase;float index=Mathf.Clamp01(phase)*(times.Length-1);int start=Mathf.Min((int)index,times.Length-2);return Mathf.Lerp(times[start],times[start+1],index-start);}
        public static Quaternion BodyAim(string state,float phase)=>Quaternion.identity;

        public const float WeaponScale=3.75f/1.3f;
        public static void ConfigureWeapon(GameObject root)
        {
            var grip=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="EliteCleaverGrip");if(grip==null)grip=new GameObject("EliteCleaverGrip").transform;
            var hand=root.GetComponentsInChildren<Transform>().First(t=>t.name=="LeftHand");grip.SetParent(hand,false);grip.localPosition=PalmLocal(hand);grip.localScale=Vector3.one/hand.lossyScale.x;
            // Recover the calibrated hand-reference rotation from the original model, not the saved idle.
            var scene=EditorSceneManager.NewPreviewScene();GameObject model=null,source=null;
            try{model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SourceRig));SceneManager.MoveGameObjectToScene(model,scene);SceneManager.MoveGameObjectToScene(source,scene);model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);var report=new Elite1CombatSetup.InstallReport();Reference(model,model.GetComponentInChildren<SkinnedMeshRenderer>(),report);var refHand=TargetBones(model)[9];var sa=source.GetComponentInChildren<Animator>();sa.runtimeAnimatorController=null;sa.Rebind();var srcHand=sa.GetBoneTransform(HumanBodyBones.LeftHand);var weapon=source.GetComponentsInChildren<MeshFilter>().First(m=>m.name.Contains("hammer")).transform;grip.localRotation=Quaternion.Inverse(refHand.rotation)*srcHand.rotation*weapon.localRotation*Quaternion.LookRotation(Vector3.right,Vector3.forward);}finally{if(model!=null)UnityEngine.Object.DestroyImmediate(model);if(source!=null)UnityEngine.Object.DestroyImmediate(source);EditorSceneManager.ClosePreviewScene(scene);}
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KatanaDemo/Weapons/RustCleaver/Prefabs/RustCleaver.prefab");var sourceVisual=original.GetComponentInChildren<MeshFilter>();
            var visual=WeaponChild(grip,"EliteCleaver");visual.localPosition=new Vector3(0,-.14f*WeaponScale,0);visual.localRotation=sourceVisual.transform.localRotation;visual.localScale=Vector3.one*WeaponScale;
            var filter=visual.GetComponent<MeshFilter>()??visual.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=sourceVisual.sharedMesh;
            var renderer=visual.GetComponent<MeshRenderer>()??visual.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterials=sourceVisual.GetComponent<MeshRenderer>().sharedMaterials;
            WeaponChild(grip,"BladeBase").localPosition=new Vector3(0,(.36f-.14f)*WeaponScale,0);WeaponChild(grip,"BladeTip").localPosition=new Vector3(0,(1.285f-.14f)*WeaponScale,-.03f*WeaponScale);
        }
        static Transform WeaponChild(Transform parent,string name){var t=parent.Find(name);if(t==null){t=new GameObject(name).transform;t.SetParent(parent,false);}t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;return t;}
        public static void ConfigureHeadMesh(GameObject root)
        {
            const string path="Assets/KatanaDemo/Characters/Elite1/Models/Elite1_HeadStable.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);var mesh=existing;var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();
            if(mesh==null||mesh.name!="Elite1_HeadBootStable_v6"){
                var original=AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponentInChildren<SkinnedMeshRenderer>();mesh=UnityEngine.Object.Instantiate(original.sharedMesh);mesh.name="Elite1_HeadBootStable_v6";
                int head=Array.FindIndex(original.bones,b=>b.name=="Head");var v=mesh.vertices;var weights=mesh.boneWeights;var positions=v.Select(p=>original.transform.TransformPoint(p)).ToArray();var core=new List<int>();
                Func<BoneWeight,float> headWeight=w=>(w.boneIndex0==head?w.weight0:0)+(w.boneIndex1==head?w.weight1:0)+(w.boneIndex2==head?w.weight2:0)+(w.boneIndex3==head?w.weight3:0);
                var face=FaceRegion(original);for(int i=0;i<v.Length;i++)if(headWeight(weights[i])>.5f)core.Add(i);var box=new Bounds(positions[core[0]],Vector3.zero);foreach(int i in core)box.Encapsulate(positions[i]);int changed=0;
                for(int i=0;i<v.Length;i++){
                    var p=positions[i];float x=Mathf.Abs(p.x-box.center.x),z=Mathf.Abs(p.z-box.center.z);
                    float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(box.min.y-.015f,box.min.y,p.y))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(box.extents.x,box.extents.x+.018f,x)))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(box.extents.z,box.extents.z+.018f,z)));
                    if(face[i])blend=1;
                    if(blend<.001f)continue;var old=weights[i];var entries=new Dictionary<int,float>();int[] ids={old.boneIndex0,old.boneIndex1,old.boneIndex2,old.boneIndex3};float[] ws={old.weight0,old.weight1,old.weight2,old.weight3};for(int k=0;k<4;k++){if(!entries.ContainsKey(ids[k]))entries[ids[k]]=0;entries[ids[k]]+=ws[k]*(1-blend);}if(!entries.ContainsKey(head))entries[head]=0;entries[head]+=blend;var pairs=entries.OrderByDescending(q=>q.Value).Take(4).ToArray();float sum=pairs.Sum(q=>q.Value);var nw=new BoneWeight();if(pairs.Length>0){nw.boneIndex0=pairs[0].Key;nw.weight0=pairs[0].Value/sum;}if(pairs.Length>1){nw.boneIndex1=pairs[1].Key;nw.weight1=pairs[1].Value/sum;}if(pairs.Length>2){nw.boneIndex2=pairs[2].Key;nw.weight2=pairs[2].Value/sum;}if(pairs.Length>3){nw.boneIndex3=pairs[3].Key;nw.weight3=pairs[3].Value/sum;}weights[i]=nw;changed++;
                }
                int boots=0;
                foreach(string footName in new[]{"LeftFoot","RightFoot"}){
                    int foot=Array.FindIndex(original.bones,b=>b.name==footName);var bindWorld=original.transform.localToWorldMatrix*original.sharedMesh.bindposes[foot].inverse;float ankle=bindWorld.GetColumn(3).y;
                    for(int i=0;i<v.Length;i++){
                        var old=weights[i];int[] ids={old.boneIndex0,old.boneIndex1,old.boneIndex2,old.boneIndex3};float[] ws={old.weight0,old.weight1,old.weight2,old.weight3};float influence=0;for(int k=0;k<4;k++){string n=original.bones[ids[k]].name;if(n.StartsWith(footName.StartsWith("Left")?"Left":"Right")&&(n.Contains("Foot")||n.Contains("Toe")))influence+=ws[k];}
                        float blend=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(ankle+.025f,ankle+.10f,positions[i].y)))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.15f,.5f,influence));if(blend<.001f)continue;
                        var entries=new Dictionary<int,float>();for(int k=0;k<4;k++){if(!entries.ContainsKey(ids[k]))entries[ids[k]]=0;entries[ids[k]]+=ws[k]*(1-blend);}if(!entries.ContainsKey(foot))entries[foot]=0;entries[foot]+=blend;var q=entries.OrderByDescending(a=>a.Value).Take(4).ToArray();float total=q.Sum(a=>a.Value);var nw=new BoneWeight();nw.boneIndex0=q[0].Key;nw.weight0=q[0].Value/total;if(q.Length>1){nw.boneIndex1=q[1].Key;nw.weight1=q[1].Value/total;}if(q.Length>2){nw.boneIndex2=q[2].Key;nw.weight2=q[2].Value/total;}if(q.Length>3){nw.boneIndex3=q[3].Key;nw.weight3=q[3].Value/total;}weights[i]=nw;boots++;
                    }
                }
                mesh.boneWeights=weights;if(existing==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}
                var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../镇门鬼将/动画资源/elite1-combat/analysis/repair-20261027"));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"头部蒙皮修复.json"),"{\"changedVertices\":"+changed+",\"bootVertices\":"+boots+",\"sourceVertices\":"+v.Length+",\"originalGeometryUvAndBindposesPreserved\":true}");
            }
            skin.sharedMesh=mesh;
        }
        // Keep whole imported face islands, and match duplicate seam vertices without joining the collar island.
        public static bool[] FaceRegion(SkinnedMeshRenderer skin)
        {
            var mesh=skin.sharedMesh;var positions=mesh.vertices.Select(p=>skin.transform.TransformPoint(p)).ToArray();var weights=mesh.boneWeights;int head=Array.FindIndex(skin.bones,b=>b.name=="Head");var parent=Enumerable.Range(0,positions.Length).ToArray();
            var triangles=mesh.triangles;for(int i=0;i<triangles.Length;i+=3)for(int k=0;k<3;k++){int a=triangles[i+k],b=triangles[i+(k+1)%3];parent[RegionRoot(parent,a)]=RegionRoot(parent,b);}
            var boxes=new Dictionary<int,Bounds>();for(int i=0;i<positions.Length;i++){int r=RegionRoot(parent,i);Bounds box;if(!boxes.TryGetValue(r,out box))box=new Bounds(positions[i],Vector3.zero);else box.Encapsulate(positions[i]);boxes[r]=box;}
            var eligible=Enumerable.Range(0,positions.Length).Select(i=>{var b=boxes[RegionRoot(parent,i)];return b.min.y>=1.05f&&b.min.x>-.17f&&b.max.x<.17f&&b.min.z>-.15f&&b.max.z<.14f;}).ToArray();
            var weld=new Dictionary<Vector3Int,int>();for(int i=0;i<positions.Length;i++)if(eligible[i]){var p=positions[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000));int prior;if(weld.TryGetValue(key,out prior))parent[RegionRoot(parent,i)]=RegionRoot(parent,prior);else weld[key]=i;}
            var roots=new HashSet<int>();for(int i=0;i<positions.Length;i++){var w=weights[i];float influence=(w.boneIndex0==head?w.weight0:0)+(w.boneIndex1==head?w.weight1:0)+(w.boneIndex2==head?w.weight2:0)+(w.boneIndex3==head?w.weight3:0);if(eligible[i]&&influence>.5f&&positions[i].y>1.12f&&positions[i].z>.06f)roots.Add(RegionRoot(parent,i));}
            var selected=Enumerable.Range(0,positions.Length).Select(i=>roots.Contains(RegionRoot(parent,i))).ToArray();var seam=new HashSet<Vector3Int>();for(int i=0;i<positions.Length;i++)if(selected[i]){var p=positions[i];seam.Add(new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000)));}
            for(int i=0;i<positions.Length;i++){var p=positions[i];if(seam.Contains(new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000))))selected[i]=true;}return selected;
        }
        static int RegionRoot(int[] parent,int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
    }
}
