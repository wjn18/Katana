using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OCDesign.LookDev;
using OCDesign.LookDev.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Bridge landmark, key-controlled ridge route, one-way drop and inside-only shortcut.</summary>
    public static class SamuraiLevelFlowSetup
    {
        public const string Root=SamuraiComicStyleSetup.Root+"/LevelFlow";
        public const string RootName="10 Summit route loop - 桥门钥匙与高路回环";
        const string Model=Root+"/Models/LevelRouteProps.fbx";
        static Mesh cube;
        static MeshCollider[] crags;
        static readonly Dictionary<string,string> Palette=new Dictionary<string,string>{
            {"Stone","Warm_sandstone"},{"Slate","Foundation_slate"},{"Plaster","Ivory_plaster"},
            {"Timber","Old_honey_timber"},{"Red","Vermilion_lacquer"},{"Roof","Ink_blue_roof"},
            {"Paper","Warm_lantern_paper"},{"Gold","Quiet_ochre"}
        };
        public static Vector3[] HighPathNodes() => new[]{
            new Vector3(1.9f,.05f,16.4f),new Vector3(1.9f,.65f,18.2f),new Vector3(-1.5f,1.55f,18.2f),
            new Vector3(-1.5f,4.9f,25.5f),new Vector3(-14.6f,5.2f,25.5f),
            new Vector3(-14.6f,5.3f,7.2f),new Vector3(-16.8f,5.3f,7.2f)
        };

        [MenuItem("Katana/Level/Install Summit Key And Inside Gate Loop")]
        public static string Install()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode.");
            var dressing=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Courtyard dressing");
            if(!dressing||!dressing.transform.Find(SamuraiFirstCourtyardSetup.RootName))throw new InvalidOperationException("Install the first courtyard first.");
            if(dressing.transform.Find(RootName))return "Summit route loop exists. All gate, path, spawn, key and user edits retained.";
            var meshes=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Mesh>().ToArray();
            string[] families={"BridgeLeft","BridgeRight","Latch","SideLeft","SideRight","SideFrame","Key","KeyStand"};
            foreach(var p in families)if(!meshes.Any(m=>m.name.StartsWith(p+"_")))throw new InvalidOperationException("Missing route module "+p);
            foreach(var path in new[]{"Prefabs","Materials","Meshes/Contours","Meshes/Route"})Directory.CreateDirectory(Root+"/"+path);
            AssetDatabase.Refresh();
            cube=AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root+"/Meshes/Shapes/Block.asset");
            crags=dressing.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.Contains("Sanctuary_Crag")).ToArray();
            var modules=families.ToDictionary(p=>p,p=>Module(p,meshes));
            int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Design summit key and shortcut loop");
            var root=Child(dressing.transform,RootName);
            var progression=root.gameObject.AddComponent<SamuraiLevelRoute>();
            progression.player=scene.GetRootGameObjects().First(g=>g.name=="SamuraiCombatPlayer").GetComponent<SamuraiCombatant>();
            progression.input=progression.player.GetComponent<SamuraiPlayerInput>();
            progression.promptFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/KatanaDemo/SamuraiCombat/UI/Fonts/NotoSansSC.ttf");

            var gates=Child(root,"01 Gates - 钥匙侧门与内闩桥门");
            var bridge=Child(gates,"Bridge inside gate - 桥口内闩大门");
            bridge.SetPositionAndRotation(new Vector3(-13.7f,1.3671f,-1.8f),Quaternion.Euler(0,-90,0));
            progression.bridgeGateRoot=bridge;
            progression.bridgeGateHinges=new[]{Hinge(bridge,modules["BridgeLeft"],"Left leaf hinge - 左门轴",-1.8f),Hinge(bridge,modules["BridgeRight"],"Right leaf hinge - 右门轴",1.8f)};
            progression.bridgeGateBlocker=GateBlocker(bridge,3.7f,4.25f);
            progression.bridgeLatchVisual=Instance(modules["Latch"],bridge,"Inside sliding bar - 院内横闩",bridge.position,bridge.rotation).gameObject;
            progression.bridgeInsideDirection=Vector3.left;progression.insideThreshold=.65f;
            var side=Child(gates,"Keyed mountain gate - 山路钥匙门");
            side.position=new Vector3(1.9f,.05f,16.4f);
            Instance(modules["SideFrame"],side,"Mountain gate frame - 山路门框",side.position,Quaternion.identity);
            progression.sideGateRoot=side;
            progression.sideGateHinges=new[]{Hinge(side,modules["SideLeft"],"Left leaf hinge - 左门轴",-1.1f),Hinge(side,modules["SideRight"],"Right leaf hinge - 右门轴",1.1f)};
            progression.sideGateBlocker=GateBlocker(side,2.35f,3.35f);
            var lockMark=Box(side,"Keyhole lock plate - 钥匙锁牌",new Vector3(1.9f,1.2f,16.06f),new Vector3(.18f,.28f,.045f),Mat("Gold"),false);
            progression.sideLockVisual=lockMark.gameObject;
            // The keyed doorway joins the existing hall and parapet, with no side bypass.
            var sides=Child(gates,"Mountain gate flank closures - 山路门两侧接挡");
            Box(sides,"West gate return",new Vector3(.60f,1.25f,16.0f),new Vector3(.30f,2.5f,.6f),Mat("Timber"),true);
            Box(sides,"East gate return",new Vector3(3.25f,1.25f,16.0f),new Vector3(.30f,2.5f,.6f),Mat("Timber"),true);
            var parapet=dressing.transform.Find(SamuraiFirstCourtyardSetup.RootName+"/02 Circulation and planted court - 双线动线与植栽院/West side parapet");
            if(parapet) {
                Undo.RecordObject(parapet,"Leave clearance for inward-opening side gate");
                parapet.position=new Vector3(parapet.position.x,parapet.position.y,13.1f);
                parapet.localScale=new Vector3(parapet.localScale.x,parapet.localScale.y,5.8f);
            }

            var keyArea=Child(root,"02 Key search - 正殿内室寻钥匙");
            var keySite=Child(keyArea,"Mountain pass key site - 山路钥匙台");keySite.position=new Vector3(20.7f,.065f,17.2f);
            Instance(modules["KeyStand"],keySite,"Key stand - 钥匙石木台",keySite.position,Quaternion.identity);
            var key=Instance(modules["Key"],keySite,"Mountain pass key - 山路钥匙",keySite.position+Vector3.up*.88f,Quaternion.Euler(0,135,0));
            progression.keyRoot=keySite;progression.keyVisual=key.gameObject;
            var keyAnchor=Child(keySite,"Pickup anchor - 拾取锚点");keyAnchor.position=key.position+Vector3.up*.28f;
            progression.keyAnchor=keyAnchor;

            var route=Child(root,"03 High maintenance path - 高处单向山路");
            BuildHighPath(route,HighPathNodes());
            var drop=Child(root,"04 Drop arrival - 跳落进入精英院");
            Box(drop,"Safe drop landing - 六米落地缓冲区",new Vector3(-21,1.371f,7.2f),new Vector3(6,.008f,4),QuietStone(),false);
            var edge=Child(drop,"Drop edge - 向西走下落点");edge.position=new Vector3(-16.8f,5.3f,7.2f);edge.rotation=Quaternion.Euler(0,-90,0);
            var landing=Child(drop,"Landing centre - 落地中心");landing.position=new Vector3(-21,1.3671f,7.2f);
            // Painted chevrons stay on the high deck; no floating instruction text.
            foreach(float z in new[]{-.22f,.22f}) {
                var arrow=Box(drop,"Drop direction inlay",new Vector3(-16.08f,5.313f,7.2f+z),new Vector3(.55f,.015f,.07f),Mat("Gold"),false);
                arrow.rotation=Quaternion.Euler(0,z<0?35:-35,0);
            }
            var landmarks=Child(root,"05 Bridge landmark - 出生桥景引导");
            BridgePalette(dressing.transform);
            Marker(landmarks,"Initial focus - 桥中点",new Vector3(-6.5f,1.9f,-1.8f));
            Marker(landmarks,"Bridge obstruction encounter - 遇闩门站位",new Vector3(-11.7f,1.2f,-1.8f));
            Marker(landmarks,"Gate inside latch approach - 院内开门站位",new Vector3(-15.3f,1.4f,-1.8f));
            ClearRouteTrees(dressing.transform);
            Undo.RecordObject(progression.player.transform,"Set bridge-facing first spawn");
            float spawnY=scene.GetRootGameObjects().First(g=>g.name=="Floor").GetComponentsInChildren<Collider>(true).Where(c=>c.name=="Training Floor").First().bounds.max.y+.035f;
            progression.player.transform.SetPositionAndRotation(new Vector3(8.8f,spawnY,-1.8f),Quaternion.Euler(0,-90,0));
            PrefabUtility.RecordPrefabInstancePropertyModifications(progression.player.transform);
            if(Camera.main) {
                Undo.RecordObject(Camera.main.transform,"Frame bridge at first spawn");
                var orbit=Camera.main.GetComponent<SamuraiOrbitCamera>();
                if(orbit){Undo.RecordObject(orbit,"Set first view toward bridge landmark");orbit.initialPitch=12f;orbit.ResetView();}
                if(Camera.main.TryGetComponent<ComicScreenOutline>(out var outline))outline.RefreshSources();
            }
            Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);Undo.CollapseUndoOperations(undo);
            return "Summit loop installed: bridge-facing spawn, inside-barred bridge, hall key, keyed ridge gate, high route and one-way drop. B interacts. Scene not automatically saved.";
        }

        static Transform Child(Transform p,string name)
        {
            var g=new GameObject(name);g.transform.SetParent(p,false);Undo.RegisterCreatedObjectUndo(g,"Add summit route part");return g.transform;
        }
        static void Marker(Transform p,string name,Vector3 point){Child(p,name).position=point;}
        static Transform Instance(GameObject prefab,Transform p,string name,Vector3 point,Quaternion rotation)
        {
            var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab,p);g.name=name;g.transform.SetPositionAndRotation(point,rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(g);return g.transform;
        }
        static Transform Hinge(Transform gate,GameObject prefab,string name,float pivot)
        {
            var hinge=Child(gate,name);hinge.localPosition=new Vector3(pivot,0,0);
            var leaf=(GameObject)PrefabUtility.InstantiatePrefab(prefab,hinge);leaf.name="Moving timber leaf - 活动厚木门扇";leaf.transform.localPosition=new Vector3(-pivot,0,0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(leaf.transform);return hinge;
        }
        static BoxCollider GateBlocker(Transform gate,float width,float height)
        {
            var t=Child(gate,"Closed gate obstruction - 关门阻挡");var c=t.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,height*.5f,0);c.size=new Vector3(width,height,.24f);return c;
        }
        static Material Shared(string key)=>AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root+"/Materials/"+Palette[key]+".mat");
        static Material Mat(string key)
        {
            if(key!="Timber"&&key!="Gold")return Shared(key);
            string path=Root+"/Materials/"+(key=="Timber"?"Deep_gate_timber":"Route_key_gold")+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m)return m;
            m=new Material(Shared(key)){name=key=="Timber"?"Deep_gate_timber":"Route_key_gold"};
            m.SetColor("_BaseColor",key=="Timber"?new Color(.27f,.19f,.13f,1):new Color(.96f,.73f,.20f,1));
            m.SetColor("_ShadowColor",key=="Timber"?new Color(.15f,.145f,.14f,1):new Color(.63f,.42f,.13f,1));
            AssetDatabase.CreateAsset(m,path);return m;
        }
        static Material QuietStone()
        {
            string path=Root+"/Materials/Drop_landing_stone.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m)return m;
            m=new Material(Shared("Stone")){name="Drop_landing_stone"};m.SetColor("_BaseColor",new Color(.80f,.78f,.71f,1));m.SetFloat("_PavingStrength",.7f);m.SetFloat("_PavingWobble",0);AssetDatabase.CreateAsset(m,path);return m;
        }
        static GameObject Module(string prefix,Mesh[] meshes)
        {
            string path=Root+"/Prefabs/Route_"+prefix+".prefab";var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing)return existing;
            var preview=EditorSceneManager.NewPreviewScene();var root=new GameObject("Route_"+prefix);SceneManager.MoveGameObjectToScene(root,preview);
            try {
                foreach(var mesh in meshes.Where(m=>m.name.StartsWith(prefix+"_"))) {
                    var p=Child(root.transform,mesh.name.Substring(prefix.Length+1));p.localPosition=mesh.bounds.center;
                    var s=Child(p,"Surface - "+mesh.name);s.localPosition=-mesh.bounds.center;s.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=s.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=Mat(mesh.name.Substring(mesh.name.LastIndexOf('_')+1));renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                    bool body=mesh.name.Contains("ThickPlankBody")||prefix=="SideFrame"&&!mesh.name.Contains("Marks")||prefix=="KeyStand"&&mesh.bounds.max.y<=.89f;
                    if(body)s.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
                    if(body||prefix=="Key") {
                        ComicOutlineHullFactory.Diagnostics info;var ink=Child(s,"Comic ink contour - 随动描边");ink.gameObject.AddComponent<MeshFilter>().sharedMesh=ComicOutlineHullFactory.GetOrCreate(mesh,Root+"/Meshes/Contours",out info);
                        var r=ink.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardArchitectureSetup.Root+"/Materials/Hall_roof_ink.mat");r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
                    }
                }
                return PrefabUtility.SaveAsPrefabAsset(root,path);
            } finally {EditorSceneManager.ClosePreviewScene(preview);}
        }
        static Transform Box(Transform p,string name,Vector3 point,Vector3 size,Material mat,bool collision)
        {
            var t=Child(p,name);t.position=point;t.localScale=size;t.gameObject.AddComponent<MeshFilter>().sharedMesh=cube;
            var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject,StaticEditorFlags.BatchingStatic);if(collision)t.gameObject.AddComponent<BoxCollider>();return t;
        }
        struct Part {public Vector3 p,s;public Quaternion r;public Part(Vector3 p,Vector3 s,Quaternion r){this.p=p;this.s=s;this.r=r;}}
        static Part Beam(Vector3 a,Vector3 b,float width,float height)
        {return new Part((a+b)*.5f,new Vector3(width,height,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a));}
        static void Boxes(Transform p,string name,List<Part> parts,Material mat,bool collision)
        {
            if(parts.Count==0)return;var group=Child(p,name);var mesh=new Mesh{name=name.Replace(' ','_')};
            mesh.CombineMeshes(parts.Select(x=>new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(x.p,x.r,x.s)}).ToArray(),true,true);mesh=Save(mesh);
            var surface=Child(group,"Combined timber - 合并静态木构");surface.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=surface.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;GameObjectUtility.SetStaticEditorFlags(surface.gameObject,StaticEditorFlags.BatchingStatic);
            if(collision)for(int i=0;i<parts.Count;i++) {
                var t=Child(group,"Collision "+i);t.SetPositionAndRotation(parts[i].p,parts[i].r);t.localScale=parts[i].s;t.gameObject.AddComponent<BoxCollider>();
            }
        }
        static void BuildHighPath(Transform parent,Vector3[] nodes)
        {
            const float half=.9f;int n=nodes.Length;var left=new Vector3[n];var right=new Vector3[n];
            for(int i=0;i<n;i++) {
                Vector3 a=(nodes[Mathf.Min(i+1,n-1)]-nodes[Mathf.Max(i-1,0)]);a.y=0;
                Vector3 normal;
                if(i==0||i==n-1)normal=Vector3.Cross(a.normalized,Vector3.up)*(i==0?1.35f:half);
                else {var incoming=nodes[i]-nodes[i-1];incoming.y=0;var outgoing=nodes[i+1]-nodes[i];outgoing.y=0;var n0=Vector3.Cross(incoming.normalized,Vector3.up);var n1=Vector3.Cross(outgoing.normalized,Vector3.up);var bisector=(n0+n1).normalized;normal=bisector*half/Vector3.Dot(bisector,n1);}
                left[i]=nodes[i]+normal;right[i]=nodes[i]-normal;
            }
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<n;i++){vertices.Add(left[i]);vertices.Add(nodes[i]);vertices.Add(right[i]);vertices.Add(left[i]-Vector3.up*.22f);vertices.Add(nodes[i]-Vector3.up*.22f);vertices.Add(right[i]-Vector3.up*.22f);}
            Action<int,int,int,int> quad=(a,b,c,d)=>triangles.AddRange(new[]{a,b,c,a,c,d});
            for(int i=0;i<n-1;i++){int a=i*6,b=a+6;quad(a,b,b+1,a+1);quad(a+1,b+1,b+2,a+2);quad(a+3,a+4,b+4,b+3);quad(a+4,a+5,b+5,b+4);quad(a,a+3,b+3,b);quad(a+2,b+2,b+5,a+5);}
            quad(0,1,4,3);quad(1,2,5,4);int end=(n-1)*6;quad(end,end+3,end+4,end+1);quad(end+1,end+4,end+5,end+2);
            var mesh=new Mesh{name="Suspended_ridge_deck"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(!CourtyardRoadMeshBuilder.Audit(mesh).closedPositiveVolume)throw new InvalidOperationException("Invalid high-path deck topology.");
            mesh=Save(mesh);var deck=Child(parent,"Continuous walking deck - 连续高路行走面");deck.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;deck.gameObject.AddComponent<MeshRenderer>().sharedMaterial=Shared("Timber");deck.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
            var posts=new List<Part>();var rails=new List<Part>();var seams=new List<Part>();var braces=new List<Part>();
            for(int i=0;i<n-1;i++) {
                float distance=Vector3.Distance(nodes[i],nodes[i+1]);int steps=Mathf.CeilToInt(distance/1.4f);
                for(int j=0;j<=steps;j++)foreach(int side in new[]{-1,1}) {
                    var edge=Vector3.Lerp(side<0?left[i]:right[i],side<0?left[i+1]:right[i+1],j/(float)steps);
                    posts.Add(new Part(edge+Vector3.up*.72f,new Vector3(.13f,1.44f,.13f),Quaternion.identity));
                }
                foreach(int side in new[]{-1,1})foreach(float h in new[]{.5f,1.34f})rails.Add(Beam((side<0?left[i]:right[i])+Vector3.up*h,(side<0?left[i+1]:right[i+1])+Vector3.up*h,.11f,.12f));
                int planks=Mathf.CeilToInt(distance/.6f);
                for(int j=1;j<planks;j++){var c=Vector3.Lerp(nodes[i],nodes[i+1],j/(float)planks);var direction=nodes[i+1]-nodes[i];direction.y=0;seams.Add(new Part(c+Vector3.up*.005f,new Vector3(1.72f,.01f,.022f),Quaternion.LookRotation(direction)));}
                if(distance>7)foreach(int side in new[]{-1,1}) {
                    var a=(side<0?left[i]:right[i])-Vector3.up*.32f;var b=(side<0?left[i+1]:right[i+1])-Vector3.up*.32f;var mid=(a+b)*.5f-Vector3.up*.85f;
                    braces.Add(Beam(a,mid,.17f,.19f));braces.Add(Beam(mid,b,.17f,.19f));
                }
            }
            posts=posts.GroupBy(p=>p.p.x.ToString("F4")+"/"+p.p.y.ToString("F4")+"/"+p.p.z.ToString("F4")).Select(g=>g.First()).ToList();
            Boxes(parent,"01 Guard posts - 山路护柱",posts,Shared("Red"),true);
            Boxes(parent,"02 Continuous rails - 连续双道护栏",rails,Shared("Timber"),true);
            Boxes(parent,"03 Plank joints - 紧密木板细缝",seams,Shared("Roof"),false);
            Boxes(parent,"04 Underdeck trusses - 高路承托斜梁",braces,Mat("Timber"),false);
            var supports=new List<Part>();
            foreach(var point in new[]{nodes[2],nodes[3],nodes[4],nodes[5]}) {
                float ground=Ground(point.x,point.z);if(float.IsNaN(ground)||point.y-ground<1)continue;
                supports.Add(new Part(new Vector3(point.x,(ground+point.y-.22f)*.5f,point.z),new Vector3(.3f,point.y-.22f-ground,.3f),Quaternion.identity));
            }
            Boxes(parent,"05 Grounded supports - 接地承柱",supports,Mat("Timber"),true);
        }
        static Mesh Save(Mesh mesh)
        {
            string data=string.Join(",",mesh.vertices.SelectMany(v=>new[]{v.x,v.y,v.z}).Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))+"|"+string.Join(",",mesh.triangles);
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data))).Replace("-","").Substring(0,12);
            string path=Root+"/Meshes/Route/"+mesh.name+"_"+hash+".asset";var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(m){UnityEngine.Object.DestroyImmediate(mesh);return m;}AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static float Ground(float x,float z)
        {
            float y=float.NegativeInfinity;RaycastHit hit;
            foreach(var c in crags)if(c.Raycast(new Ray(new Vector3(x,80,z),Vector3.down),out hit,200))y=Mathf.Max(y,hit.point.y);
            return float.IsNegativeInfinity(y)?float.NaN:y;
        }
        static void ClearRouteTrees(Transform d)
        {
            var tree=d.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Mountain pine 036");
            if(tree&&tree.position.x<4&&tree.position.z>15&&tree.position.z<20) {
                float old=Ground(tree.position.x,tree.position.z),next=Ground(31,20);
                if(!float.IsNaN(next)){Undo.RecordObject(tree,"Clear high-path pine");tree.position=new Vector3(31,next+(float.IsNaN(old)?0:tree.position.y-old),20);}
            }
            var garden=d.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Ink pine garden 2");
            if(garden&&garden.position.z<29){Undo.RecordObject(garden,"Clear high-path corner canopy");garden.position+=new Vector3(0,0,2.5f);}
        }
        static void BridgePalette(Transform d)
        {
            var bridge=d.Find(SamuraiCourtyardBridgeSetup.BridgeName);
            foreach(var r in bridge.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.StartsWith("Surface - Deck_")||r.name.StartsWith("Surface - Rail_"))) {
                bool rail=r.name.Contains("Rail_");string path=Root+"/Materials/"+(rail?"Bridge_landmark_vermilion":"Bridge_landmark_timber")+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(r.sharedMaterial){name=rail?"Bridge_landmark_vermilion":"Bridge_landmark_timber"};mat.SetColor("_BaseColor",rail?new Color(.77f,.31f,.20f,1):new Color(.66f,.44f,.23f,1));AssetDatabase.CreateAsset(mat,path);}
                Undo.RecordObject(r,"Emphasize bridge landmark");r.sharedMaterial=mat;
            }
        }
    }
}
