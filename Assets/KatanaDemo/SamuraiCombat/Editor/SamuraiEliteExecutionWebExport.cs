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
        static float[] EVMatrix(Matrix4x4 m){var a=new float[16];for(int c=0;c<4;c++)for(int r=0;r<4;r++)a[c*4+r]=m[r,c];return a;}
        static object EVArray(BinaryWriter writer,IEnumerable<float> values){long offset=writer.BaseStream.Position;var a=values.ToArray();foreach(float v in a)writer.Write(v);return new{offset,count=a.Length,type="float"};}
        static object EVIndices(BinaryWriter writer,int[] a){long offset=writer.BaseStream.Position;foreach(int v in a)writer.Write(v);return new{offset,count=a.Length,type="uint"};}
        static IEnumerable<Renderer> EVSurfaces(GameObject actor)=>actor.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&(r is SkinnedMeshRenderer||r is MeshRenderer)&&r.sharedMaterials.Any(m=>m!=null&&!m.shader.name.Contains("Outline")));
        static void EVWebExecutionPose(Rig rig,SamuraiExecutionPoseClip pose,SamuraiExecutionPoseClip.Binding binding,Vector3 visualHome,float time)
        {
            rig.animator.transform.localPosition=visualHome;binding.Apply(time);
            float scale=rig.animator.transform.lossyScale.y/Mathf.Max(.00001f,pose.referenceVisualScale);
            float lift=(pose.groundLift.Evaluate(Mathf.Clamp(time,0,pose.duration))-pose.groundContactY)*scale+pose.groundContactY;
            rig.animator.transform.position+=Vector3.up*lift;
        }
        static string EVTexture(Texture source,string folder,Dictionary<Texture,string> cache)
        {
            if(source==null)return null;if(cache.ContainsKey(source))return cache[source];string file="texture"+cache.Count+".png";cache[source]=file;
            int w=Mathf.Min(source.width,1024),h=Mathf.Max(1,Mathf.RoundToInt((float)source.height*w/source.width));
            var rt=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var previous=RenderTexture.active;
            Graphics.Blit(source,rt);RenderTexture.active=rt;var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,w,h),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(folder,file),texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);return file;
        }
        public static string ExportEliteExecutionWeb()
        {
            string folder=Path.Combine(ElitePreviewOutput,"preview");Directory.CreateDirectory(folder);
            var scene=EditorSceneManager.NewPreviewScene();var temp=new List<UnityEngine.Object>();
            try
            {
                var player=ExecutionSceneClone("SamuraiPlayer",scene,temp);var elite=ExecutionSceneClone("Elite1",scene,temp);var rigs=new[]{player,elite};
                var renderers=rigs.SelectMany(r=>EVSurfaces(r.gameObject)).ToArray();var nodes=new List<Transform>();
                Action<Transform> add=t=>{if(t!=null&&!nodes.Contains(t))nodes.Add(t);};
                foreach(var renderer in renderers){var sr=renderer as SkinnedMeshRenderer;if(sr!=null)foreach(var b in sr.bones)add(b);else add(renderer.transform);}
                foreach(var rig in rigs)foreach(var name in HumanNames)add(rig.animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),name)));
                var so=new SerializedObject(player.gameObject.GetComponent<OCDesign.Samurai.SamuraiCombatant>());var bladeBase=(Transform)so.FindProperty("weaponBase").objectReferenceValue;var bladeTip=(Transform)so.FindProperty("weaponTip").objectReferenceValue;add(bladeBase);add(bladeTip);
                var skeletons=new List<object>();
                for(int actor=0;actor<2;actor++)
                {
                    var human=HumanNames.Select(n=>rigs[actor].animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),n))).Where(b=>b!=null).ToArray();var edges=new List<int[]>();
                    foreach(var b in human){var parent=b.parent;while(parent!=null&&!human.Contains(parent))parent=parent.parent;if(parent!=null)edges.Add(new[]{nodes.IndexOf(parent),nodes.IndexOf(b)});}
                    skeletons.Add(new{actor,edges});
                }
                var meshes=new List<object>();var textures=new Dictionary<Texture,string>();
                using(var writer=new BinaryWriter(File.Open(Path.Combine(folder,"geometry.bin"),FileMode.Create)))
                foreach(var renderer in renderers)
                {
                    var sr=renderer as SkinnedMeshRenderer;var mesh=sr!=null?sr.sharedMesh:renderer.GetComponent<MeshFilter>().sharedMesh;if(mesh==null)continue;
                    Transform[] skinBones=sr!=null?sr.bones:new[]{renderer.transform};Matrix4x4[] binds=sr!=null?mesh.bindposes:new[]{Matrix4x4.identity};
                    var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;var weights=sr!=null?mesh.boneWeights:null;
                    var indices=new List<float>();var amounts=new List<float>();
                    for(int i=0;i<vertices.Length;i++){if(weights!=null){var w=weights[i];indices.AddRange(new float[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3});amounts.AddRange(new[]{w.weight0,w.weight1,w.weight2,w.weight3});}else{indices.AddRange(new float[]{0,0,0,0});amounts.AddRange(new float[]{1,0,0,0});}}
                    var positions=EVArray(writer,vertices.SelectMany(EVP));var normalArray=EVArray(writer,normals.SelectMany(EVP));var uvArray=EVArray(writer,uv.SelectMany(v=>new[]{v.x,v.y}));var boneIndices=EVArray(writer,indices);var boneWeights=EVArray(writer,amounts);var bindMatrices=EVArray(writer,binds.SelectMany(EVMatrix));
                    var groups=new List<object>();
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        var m=renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)];if(m==null||m.shader.name.Contains("Outline"))continue;
                        var tex=m.HasProperty("_BaseMap")?m.GetTexture("_BaseMap"):m.mainTexture;var color=m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):m.color;
                        var triangles=mesh.GetTriangles(sub);for(int j=0;j<triangles.Length;j+=3){int k=triangles[j+1];triangles[j+1]=triangles[j+2];triangles[j+2]=k;}
                        groups.Add(new{triangles=EVIndices(writer,triangles),texture=EVTexture(tex,folder,textures),color=new[]{color.r,color.g,color.b,color.a}});
                    }
                    meshes.Add(new{name=renderer.name,actor=renderer.transform.IsChildOf(player.gameObject.transform)?0:1,weapon=renderer is MeshRenderer,positions,normals=normalArray,uv=uvArray,boneIndices,boneWeights,bindMatrices,bones=skinBones.Select(b=>nodes.IndexOf(b)).ToArray(),groups});
                }
                var reports=Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Path.Combine(ElitePreviewOutput,"analysis/adaptation.json")));var clips=new List<object>();var checks=new List<object>();
                var eliteBones=elite.animator.GetComponentsInChildren<Transform>(true);var eliteVisualHome=elite.animator.transform.localPosition;
                var fixedReceiverPosition=new Vector3(0,0,1.65f);var fixedReceiverRotation=Quaternion.Euler(0,180,0);
                var nodeActors=nodes.Select(t=>t.IsChildOf(player.gameObject.transform)?0:1).ToArray();
                foreach(var row in reports)
                {
                    string code=(string)row["code"];bool lethal=code=="09702";
                    var pair=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPair>("Assets/KatanaDemo/SamuraiCombat/Config/Execution/Elite1_"+code+"_V"+(lethal?2:1)+".asset");
                    if(pair==null)throw new InvalidOperationException("Install the formal Elite1 execution pairs before exporting.");
                    var attack=pair.attackerClip;float alignSeconds=Mathf.Max(.0001f,pair.alignSeconds);
                    var facing=fixedReceiverRotation*Quaternion.Euler(0,-pair.receiverYaw,0);
                    var playerAnchor=fixedReceiverPosition-facing*new Vector3(pair.receiverOffset.x,0,pair.receiverOffset.z);
                    var playerStart=playerAnchor+facing*new Vector3(.85f,0,-.45f);
                    var playerStartRotation=Quaternion.LookRotation(fixedReceiverPosition-playerStart);
                    for(int branch=0;branch<2;branch++)
                    {
                        string suffix=branch==0?"Healthy":"LowHealth";
                        var receiver=branch==0?pair.receiverPoseHealthy:pair.receiverPoseLowHealth;
                        var getup=lethal?null:branch==0?pair.getupPoseHealthy:pair.getupPoseLowHealth;
                        var receiverBinding=new SamuraiExecutionPoseClip.Binding(receiver,elite.animator);
                        var getupBinding=getup!=null?new SamuraiExecutionPoseClip.Binding(getup,elite.animator):null;
                        if(!receiverBinding.Valid||(getupBinding!=null&&!getupBinding.Valid))throw new InvalidOperationException("Execution pose does not match the Elite1 rig.");
                        float playbackDuration=pair.PlaybackDuration;
                        float rawDuration=alignSeconds+playbackDuration+(getup!=null?getup.duration:1f);int steps=Mathf.CeilToInt(rawDuration*60);float duration=steps/60f;string file=code+"_"+suffix+".bin";var contactPoints=new List<float[]>();
                        var down=AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination("Elite1",branch==0?"DownHealthy":"DownLowHealth"));
                        elite.gameObject.transform.SetPositionAndRotation(fixedReceiverPosition,fixedReceiverRotation);elite.animator.transform.localPosition=eliteVisualHome;
                        EVPose alignmentPose;using(var downSampling=new Sampling(elite.animator,down,false)){downSampling.At(down.length*.17f);alignmentPose=new EVPose(eliteBones);}
                        var contactSurface=new EVContactVertex{renderer=elite.gameObject.transform.Find((string)row["contactRenderer"]).GetComponent<SkinnedMeshRenderer>(),vertex=(int)row["contactVertex"]};
                        Matrix4x4[] firstMatrices=null;float maxAlignmentMatrixDelta=0,maxReceiverXZDelta=0,maxReceiverYawDelta=0,maxPlayerYDelta=0;int alignmentFrames=0;
                        elite.animator.enabled=false;
                        using(var writer=new BinaryWriter(File.Open(Path.Combine(folder,file),FileMode.Create)))using(var sampling=new Sampling(player.animator,attack,false))
                        for(int f=0;f<=steps;f++)
                        {
                            float t=f/60f,playbackTime=Mathf.Max(0,t-alignSeconds),phase=pair.SourceSecondsAt(playbackTime),align=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/alignSeconds));
                            elite.gameObject.transform.SetPositionAndRotation(fixedReceiverPosition,fixedReceiverRotation);
                            sampling.At(Mathf.Clamp(phase-pair.attackerStartSeconds,0,attack.length-.00001f));
                            player.gameObject.transform.SetPositionAndRotation(Vector3.Lerp(playerStart,playerAnchor,align)+facing*pair.RootAt(phase),Quaternion.Slerp(playerStartRotation,facing,align));
                            if(t<alignSeconds)alignmentPose.Apply(eliteBones);
                            else if(playbackTime<playbackDuration||getup==null)EVWebExecutionPose(elite,receiver,receiverBinding,eliteVisualHome,phase+pair.receiverStartSeconds);
                            else EVWebExecutionPose(elite,getup,getupBinding,eliteVisualHome,playbackTime-playbackDuration);
                            if(t<alignSeconds)
                            {
                                alignmentFrames++;if(firstMatrices==null)firstMatrices=nodes.Select(n=>n.localToWorldMatrix).ToArray();
                                for(int n=0;n<nodes.Count;n++)if(nodeActors[n]==1)for(int k=0;k<16;k++)maxAlignmentMatrixDelta=Mathf.Max(maxAlignmentMatrixDelta,Mathf.Abs(nodes[n].localToWorldMatrix[k]-firstMatrices[n][k]));
                            }
                            var delta=elite.gameObject.transform.position-fixedReceiverPosition;delta.y=0;
                            maxReceiverXZDelta=Mathf.Max(maxReceiverXZDelta,delta.magnitude);maxReceiverYawDelta=Mathf.Max(maxReceiverYawDelta,Quaternion.Angle(elite.gameObject.transform.rotation,fixedReceiverRotation));
                            maxPlayerYDelta=Mathf.Max(maxPlayerYDelta,Mathf.Abs(player.gameObject.transform.position.y-playerStart.y));
                            foreach(var node in nodes)foreach(float v in EVMatrix(node.localToWorldMatrix))writer.Write(v);
                            contactPoints.Add(EVP(contactSurface.Position()));
                        }
                        clips.Add(new{id=code+"_"+suffix,code,branch=branch==0?"N7 / R7":"N8 / R8",lethal,duration,frames=steps+1,file,alignSeconds,
                            impact=alignSeconds+pair.PlaybackSecondsAt(pair.strikeSeconds),reactionEnd=alignSeconds+playbackDuration,
                            getupStart=getup==null?-1:alignSeconds+playbackDuration+.6f,bridgeStart=getup==null?-1:alignSeconds+playbackDuration+.2f,
                            playbackDuration,sourceTimelineDuration=pair.Duration,playbackRate=pair.playbackRate,strikePlaybackRate=pair.strikePlaybackRate,
                            impactHoldSeconds=pair.impactHoldSeconds,cameraPushFov=pair.cameraPushFov,cameraRecoilFov=pair.cameraRecoilFov,
                            contactPoints,contactLabel=(string)row["contactLabel"],offset=EVP(pair.receiverOffset),yaw=pair.receiverYaw,sourceDuration=(float)row["sourceDuration"],extraPoseEdits=false});
                        checks.Add(new{id=code+"_"+suffix,frames=steps+1,alignmentFrames,maxAlignmentMatrixDelta,maxReceiverXZDelta,maxReceiverYawDelta,maxPlayerYDelta,
                            receiverStartSeconds=pair.receiverStartSeconds,sourceDuration=pair.Duration,playbackDuration,impact=pair.PlaybackSecondsAt(pair.strikeSeconds),
                            getupWallSeconds=getup!=null?getup.duration:0f,passed=maxAlignmentMatrixDelta<.00001f&&maxReceiverXZDelta<.00001f&&maxReceiverYawDelta<.0001f&&maxPlayerYDelta<.00001f});
                    }
                }
                var model=new{nodes=nodes.Select(t=>t.name).ToArray(),nodeActors,meshes,skeletons,blade=new[]{nodes.IndexOf(bladeBase),nodes.IndexOf(bladeTip)},clips,playerMotionUnchanged=true,elitePreviewOnly=false,version="elite-weighted-execution",groundContactY=ElitePreviewGroundY,receiverPoseUnmodified=true};
                File.WriteAllText(Path.Combine(folder,"preview-data.json"),Newtonsoft.Json.JsonConvert.SerializeObject(model));
                EVSave("weighted-execution-web-checks.json",new{version="elite-weighted-execution",checks});
                return "Exported "+meshes.Count+" surfaces, "+nodes.Count+" matrices, "+clips.Count+" paired timelines.";
            }
            finally{foreach(var o in temp.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
