using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Connected road surfaces. No runtime scripts or changes to actor/architecture placement.</summary>
    public static class SamuraiCourtyardRoadSetup
    {
        public const string Root = SamuraiComicStyleSetup.Root + "/Roads";
        public const string RootName = "09 Continuous roads - 连续规整路面";
        public static readonly string[] CourtNames = {
            "Arrival turn - 入院折线","West fighting loop - 西侧主线","East exploration loop - 东侧绕行",
            "Bridge connection - 连桥通路","Prayer room connection - 存档支路","West store connection"
        };
        public static readonly string[] GardenNames = {
            "Tea pavilion link - 茶亭小径","Storehouse entrance turn - 仓前转径","Eastern gate landing - 东园门内平台",
            "Eastern residence walk - 东廊园径","Entry maple garden walk - 门内枫庭"
        };
        static Vector3 P(float x,float z) => new Vector3(x,0,z);
        public static Vector3[][] CourtRoutes() => new[] {
            new[]{P(22.386f,-12),P(22.386f,-8),P(13,-8)},
            new[]{P(13,-8),P(9.5f,-8),P(9.5f,8),P(13,8),P(13,10.1f)},
            new[]{P(13,-8),P(16.1f,-8),P(16.1f,8),P(13,8)},
            new[]{P(.4f,-1.8f),P(9.5f,-1.8f)},
            new[]{P(9.5f,-6),P(5.8f,-6)},new[]{P(9.5f,5),P(5.8f,5)}
        };
        public static float[] CourtWidths() => new[]{2f,2f,2f,1.6f,1.5f,1.5f};

        public static string Install()
        {
            int count=EnsureMissing();
            return count==0 ? "Continuous roads already present. Existing road edits and scene dirty state retained."
                : "Installed "+count+" connected road surfaces. Straight court network and five continuous garden paths; scene not automatically saved.";
        }

        /// <summary>Used by both scene builders; only missing sections are installed.</summary>
        public static int EnsureMissing()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Courtyard dressing");
            if(!dressing)return 0;
            var d=dressing.transform;
            var court=d.Find(SamuraiFirstCourtyardSetup.RootName+"/02 Circulation and planted court - 双线动线与植栽院");
            var gardens=d.Find(SamuraiMountainCompoundSetup.RootName+"/05 Garden walks - 外院石径");
            var root=d.Find(RootName);
            bool needCourt=court&&CourtNames.All(n=>court.Find(n))&&(!root||!root.Find("01 First courtyard network - 第一庭院连续路网"));
            var neededGardens=gardens?GardenNames.Where(n=>gardens.Find(n)&&(!root||!root.Find("02 Garden paths - 接地连续园径/"+n))).ToArray():new string[0];
            if(!needCourt&&neededGardens.Length==0)return 0;
            foreach(string folder in new[]{"Meshes","Materials"})Directory.CreateDirectory(Root+"/"+folder);
            AssetDatabase.Refresh();
            int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Connect and straighten courtyard roads");
            if(!root)root=Child(d,RootName);
            int count=0;
            if(needCourt) {
                var mesh=SaveMesh(CourtyardRoadMeshBuilder.FlatNetwork("First_court_connected_paving",CourtRoutes(),CourtWidths(),.018f,.001f));
                var network=Child(root,"01 First courtyard network - 第一庭院连续路网");
                Surface(network,mesh,Material(false),false);
                foreach(string name in CourtNames)RetainDisabled(court.Find(name));
                count++;
            }
            Transform gardenRoot=root.Find("02 Garden paths - 接地连续园径");
            if(neededGardens.Length>0&&!gardenRoot)gardenRoot=Child(root,"02 Garden paths - 接地连续园径");
            foreach(string name in neededGardens) {
                var old=gardens.Find(name);
                var runs=Enumerable.Range(0,old.childCount).Select(i=>old.GetChild(i)).Where(t=>t.name.StartsWith("Stone run ")).OrderBy(t=>int.Parse(t.name.Substring(10))).ToArray();
                if(runs.Length==0)throw new InvalidOperationException("No original stone runs: "+name);
                float width,bottom;
                var centers=ReadCenterline(runs,out width,out bottom);
                var mesh=SaveMesh(CourtyardRoadMeshBuilder.Ribbon("Garden_"+name.Split(' ')[0]+"_"+GardenNames.ToList().IndexOf(name),centers,width,bottom));
                var group=Child(gardenRoot,name);Surface(group,mesh,Material(true),true);
                foreach(var run in runs)RetainDisabled(run);
                count++;
            }
            if(count>0) {
                Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            }
            Undo.CollapseUndoOperations(undo);return count;
        }

        public static Vector3[] ReadCenterline(Transform[] runs,out float width,out float bottom)
        {
            var result=new List<Vector3>();width=0;bottom=float.PositiveInfinity;
            foreach(var run in runs) {
                var f=run.GetComponentsInChildren<MeshFilter>(true).First(x=>x.sharedMesh&&!x.name.Contains("contour"));
                var vertices=f.sharedMesh.vertices;
                width=Mathf.Max(width,f.sharedMesh.bounds.size.x*Mathf.Abs(f.transform.lossyScale.x));
                bottom=Mathf.Min(bottom,vertices.Min(v=>f.transform.TransformPoint(v).y));
                foreach(var section in vertices.GroupBy(v=>Mathf.RoundToInt(v.z*100000)).OrderBy(g=>g.Key)) {
                    var z=section.Average(v=>v.z);var y=section.Max(v=>v.y);
                    var point=f.transform.TransformPoint(new Vector3(0,y,z));
                    if(result.Count>0) {
                        var previous=result[result.Count-1];
                        if(new Vector2(previous.x-point.x,previous.z-point.z).sqrMagnitude<.00000001f) {
                            // Older separately placed runs can disagree in height at
                            // the same corner. Share one averaged join instead of a lip.
                            result[result.Count-1]=new Vector3((previous.x+point.x)*.5f,(previous.y+point.y)*.5f,(previous.z+point.z)*.5f);
                            continue;
                        }
                    }
                    result.Add(point);
                }
            }
            if(result.Count<2||width<.1f)throw new InvalidOperationException("Invalid garden centreline.");
            bottom=Mathf.Min(bottom,result.Min(p=>p.y)-.16f);
            return result.ToArray();
        }
        static Transform Child(Transform p,string name)
        {
            var g=new GameObject(name);g.transform.SetParent(p,false);
            Undo.RegisterCreatedObjectUndo(g,"Add continuous road");return g.transform;
        }
        static void RetainDisabled(Transform t)
        {
            if(!t||!t.gameObject.activeSelf)return;
            Undo.RecordObject(t.gameObject,"Retain previous road parts");t.gameObject.SetActive(false);
        }
        static Material Material(bool garden)
        {
            string path=Root+"/Materials/"+(garden?"Garden_straight_stone":"Court_continuous_stone")+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing)return existing;
            var source=AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root+"/Materials/Warm_sandstone.mat");
            var mat=new Material(source){name=garden?"Garden_straight_stone":"Court_continuous_stone"};
            if(!garden) {
                mat.SetColor("_BaseColor",new Color(.70f,.71f,.675f,1));
                mat.SetColor("_ShadowColor",new Color(.34f,.43f,.49f,1));
            }
            mat.SetFloat("_PavingStrength",garden?.5f:.75f);mat.SetFloat("_PavingWobble",0);
            AssetDatabase.CreateAsset(mat,path);return mat;
        }
        static Mesh SaveMesh(Mesh mesh)
        {
            var values=mesh.vertices.SelectMany(v=>new[]{v.x,v.y,v.z}).Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            string fingerprint=string.Join(",",values)+"|"+string.Join(",",mesh.triangles);
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint))).Replace("-","").Substring(0,12);
            string path=Root+"/Meshes/"+mesh.name+"_"+hash+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){UnityEngine.Object.DestroyImmediate(mesh);return existing;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static void Surface(Transform p,Mesh mesh,Material mat,bool collision)
        {
            var t=Child(p,"Continuous stone surface - 共享接头石面");
            t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;
            r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject,StaticEditorFlags.BatchingStatic);
            if(collision)t.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
    }
}
