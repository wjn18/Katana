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
    /// <summary>Two connected mountain enclosures with the existing stair/court/bridge route retained.</summary>
    public static class SamuraiMountainCompoundSetup
    {
        public const string Root = SamuraiComicStyleSetup.Root + "/Compound";
        public const string ModelPath = Root + "/Models/Courtyard_Compound.fbx";
        public const string RootName = "07 Mountain compound - 合围山院";
        static Transform dressing, compound;
        static MeshCollider[] ground;
        static Mesh cube;
        static int sections, piers;
        static readonly Dictionary<string, string> Palette = new Dictionary<string, string> {
            {"Stone","Warm_sandstone"},{"Slate","Foundation_slate"},{"Plaster","Ivory_plaster"},
            {"Timber","Old_honey_timber"},{"Red","Vermilion_lacquer"},{"Roof","Ink_blue_roof"},
            {"Paper","Warm_lantern_paper"},{"Gold","Quiet_ochre"}
        };
        static Vector3 N(float x, float z, float level = float.NaN) { return new Vector3(x, level, z); }
        public static readonly Vector3[] UpperBoundary = {
            N(-13.286f,-12.62366f,1.28f),N(-14.5f,-14.5f),N(-18.3f,-20.8f),N(-22.5f,-24),
            N(-32,-26.6f),N(-42,-24.3f),N(-46.5f,-18),N(-47.8f,-6),N(-48,3),N(-44.5f,17),
            N(-44,21.5f),N(-38,25.5f),N(-31,29),N(-19,32.5f),N(-11,29),N(-12.6f,17),N(-13.286f,12.02634f,1.28f)
        };
        public static readonly Vector3[] LowerNorthEastBoundary = {
            N(.1f,-.075f,0),N(.1f,10,0),N(1,20),N(8,24.5f),N(17,26),N(26,26.7f),N(32,22.5f),
            N(35,16),N(36.9f,10.5f),N(37.1f,1),N(37.2f,-9),N(33,-20.8f),N(29.2f,-22.5f),N(25.55f,-19.4f,-.8f)
        };
        public static readonly Vector3[] LowerSouthWestBoundary = {
            N(19.25f,-19.4f,-.8f),N(18.2f,-24.5f),N(15,-27),N(8.5f,-27),N(3,-23),N(.1f,-12.2f,0),N(.1f,-3.525f,0)
        };

        [MenuItem("OC Design/Samurai Combat/Enclose Mountain Courtyards")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var d = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Courtyard dressing");
            if (!d) throw new InvalidOperationException("Install the courtyard and mountain environment first.");
            dressing = d.transform;
            if (dressing.Find(RootName)) return "Mountain compound already exists. All edited placements, materials and enabled states retained.";
            ground = dressing.GetComponentsInChildren<MeshCollider>().Where(c => c.name.Contains("Sanctuary_Crag")).ToArray();
            if (ground.Length != 2) throw new InvalidOperationException("Expected the two summit collision meshes.");
            var meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().OrderBy(m => m.name).ToArray();
            foreach (string prefix in new[] { "Wall_", "WindowWall_", "Pier_", "Portal_", "Detail_Basin_", "Detail_LeftDrumStone_", "Detail_RightDrumStone_" })
                if (!meshes.Any(m => m.name.StartsWith(prefix))) throw new InvalidOperationException("Incomplete compound model: " + prefix);
            foreach (var folder in new[] { "Prefabs", "Meshes/Contours", "Meshes/Placed" }) Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            cube = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
            if (!cube) throw new InvalidOperationException("Missing persistent courtyard box mesh.");
            var wall = Module("Wall_", "Compound_Wall", meshes);
            var window = Module("WindowWall_", "Compound_WindowWall", meshes);
            var pier = Module("Pier_", "Compound_Pier", meshes);
            var portal = Module("Portal_", "Compound_Portal", meshes);
            var basin = Module("Detail_Basin_", "Compound_StoneBasin", meshes);
            var leftDrum = Module("Detail_LeftDrumStone_", "Compound_LeftDrumStone", meshes);
            var rightDrum = Module("Detail_RightDrumStone_", "Compound_RightDrumStone", meshes);
            int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Enclose mountain courtyards");
            compound = Child(dressing, RootName); Undo.RegisterCreatedObjectUndo(compound.gameObject, "Add mountain compound");
            sections = piers = 0;
            Boundary(Child(compound, "01 Upper enclosure - 上峰外院墙"), UpperBoundary, wall, window, pier, "Upper");
            Boundary(Child(compound, "02 Lower enclosure - 下峰外院墙"), LowerNorthEastBoundary, wall, window, pier, "LowerNE");
            Boundary(compound.Find("02 Lower enclosure - 下峰外院墙"), LowerSouthWestBoundary, wall, window, pier, "LowerSW");
            var access = Child(compound, "03 Inner court access - 内院通口与石阶");
            OpenWall(access, true, "North Wall (1)", -25.4f, 4.2f, "Upper hall doorway - 上殿通口");
            OpenWall(access, false, "North Wall", 13.6f, 4.2f, "Eastern hall doorway - 中殿通口");
            OpenWall(access, true, "South Wall (2)", -25.4f, 3.8f, "Garden side doorway - 茶庭侧门");
            Stair(access, "Upper hall approach - 上殿石径", new Vector3(-25.4f,1.367f,11.85f),new Vector3(-25.4f,1.4871f,14.8f),3.8f);
            Stair(access, "Eastern hall approach - 中殿石径", new Vector3(13.6f,0,11.85f),new Vector3(13.6f,.12f,14.75f),3.8f);
            Stair(access, "Garden descending steps - 茶庭下行阶",new Vector3(-25.4f,1.367f,-11.85f),new Vector3(-25.4f,.40f,-16.7f),3.2f);
            var gates = Child(compound, "04 Bridge and garden portals - 桥口与侧门");
            foreach(var gate in new[]{Instance(portal,gates,"Upper bridge portal - 上桥口",new Vector3(-13.7f,1.367f,-1.8f),90),
                Instance(portal,gates,"Lower bridge portal - 下桥口",new Vector3(.7f,.02f,-1.8f),90),
                Instance(portal,gates,"Tea garden side gate - 茶庭侧门",new Vector3(-25.4f,1.367f,-12),0)})
            { gate.localScale=new Vector3(1,gate.name.Contains("bridge")?1.5f:1.3f,1);PrefabUtility.RecordPrefabInstancePropertyModifications(gate); }
            var oldGate = dressing.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "07 Upper timber frame");
            if (oldGate) { Undo.RecordObject(oldGate.gameObject,"Retain previous bridge frame"); oldGate.gameObject.SetActive(false); }
            var walks = Child(compound,"05 Garden walks - 外院石径");
            Walk(walks,"Tea pavilion link - 茶亭小径",new[]{new Vector3(-25.4f,.40f,-16.7f),new Vector3(-22.5f,.19f,-16.7f)},1.6f);
            GalleryLanding(walks);
            Stair(walks,"Storehouse descending gallery - 仓房外廊阶",new Vector3(-27.5f,1.21f,-13),new Vector3(-39.9f,-1.85f,-13),1.4f);
            Walk(walks,"Storehouse entrance turn - 仓前转径",new[]{N(-39.9f,-13,-1.85f),N(-39.9f,-11.5f,-1.85f)},1.4f);
            Walk(walks,"Eastern gate landing - 东园门内平台",new[]{N(24.1f,-18.7f,-.80f),N(25.2f,-18.3f,-.80f)},1.0f);
            float eastWalkLevel=Ground(25.2f,-11.5f)+.08f;
            Stair(walks,"Eastern garden descending steps - 东园下行阶",new Vector3(25.2f,-.8f,-18.3f),new Vector3(25.2f,eastWalkLevel,-11.5f),1.0f);
            Walk(walks,"Eastern residence walk - 东廊园径",new[]{N(25.2f,-11.5f,eastWalkLevel),N(26.4f,-9.8f),N(26.4f,-7.2f),N(25.2f,-5.5f),N(25.2f,3.5f),N(28.2f,5,-2.68f),N(29.2f,5,-2.55f)},1.0f);
            Walk(walks,"Entry maple garden walk - 门内枫庭",new[]{N(20.6f,-18.3f,-.80f),N(18,-18.3f),N(13,-18.4f),N(11,-23)},1.5f);
            var details = Child(compound,"06 Arrival details - 入院细部");
            GroundDetail(basin,details,"Carved purification basin - 净手石盆",new Vector2(17.1f,-17.1f),15);
            var ld = Instance(leftDrum,details,"Left entrance drum stone - 门前左抱鼓",new Vector3(19.25f,-.8f,-20.15f),0);
            var rd = Instance(rightDrum,details,"Right entrance drum stone - 门前右抱鼓",new Vector3(25.55f,-.8f,-20.15f),0);
            Support(details,"Left drum stone foundation",ld.position,new Vector2(.72f,1.25f),0);
            Support(details,"Right drum stone foundation",rd.position,new Vector2(.72f,1.25f),0);
            AddLantern(details,"Garden step lantern - 茶庭阶灯",new Vector2(-23,-15.8f),.62f);
            AddLantern(details,"Storehouse lantern - 仓前石灯",new Vector2(-40,-14.6f),.62f);
            AddLantern(details,"Eastern walk lantern - 东径石灯",new Vector2(27.0f,8.0f),.62f);
            AddLantern(details,"Entry basin lantern - 净手石灯",new Vector2(15.2f,-17.2f),.62f);
            SupportExistingLanterns(details);
            int trees = ClearWallTrunks();
            foreach (var r in compound.GetComponentsInChildren<Renderer>())
                if (r.transform.parent && r.transform.parent.GetComponent<Renderer>()) SceneVisibilityManager.instance.DisablePicking(r.gameObject,false);
            if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var screen)) screen.RefreshSources();
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(undo);
            return $"Mountain compound: {sections} wall panels, {piers} piers, three portals, three inner openings, garden paths and arrival details. {trees} obstructing pine trunks repositioned; primary route retained.";
        }

        static Material Mat(string suffix) { return AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root+"/Materials/"+Palette[suffix]+".mat"); }
        static Material Ink(bool thin) { return AssetDatabase.LoadAssetAtPath<Material>(thin ? SamuraiCourtyardArchitectureSetup.Root+"/Materials/Hall_roof_ink.mat" : SamuraiComicStyleSetup.Root+"/Materials/Ink.mat"); }
        static Transform Child(Transform parent,string name) { var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform; }
        static Transform Instance(GameObject prefab,Transform parent,string name,Vector3 position,float yaw)
        {
            var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);g.name=name;g.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
            PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(g);return g.transform;
        }
        static void GroundDetail(GameObject prefab,Transform parent,string name,Vector2 xz,float yaw)
        { Instance(prefab,parent,name,new Vector3(xz.x,Ground(xz.x,xz.y)+.03f,xz.y),yaw); }
        static GameObject Module(string prefix,string file,Mesh[] meshes)
        {
            string path=Root+"/Prefabs/"+file+".prefab";var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing)return existing;
            var preview=EditorSceneManager.NewPreviewScene();var g=new GameObject(file);SceneManager.MoveGameObjectToScene(g,preview);
            try {
                foreach(var mesh in meshes.Where(m=>m.name.StartsWith(prefix))) {
                    string suffix=mesh.name.Substring(mesh.name.LastIndexOf('_')+1);var p=Child(g.transform,mesh.name.Substring(prefix.Length));p.localPosition=mesh.bounds.center;
                    bool fine=mesh.name.Contains("Lattice")||mesh.name.Contains("Binding")||mesh.name.Contains("Capital")||mesh.name.Contains("Plaque")||mesh.name.Contains("Ridge");
                    var s=Surface(p,mesh,Mat(suffix),!fine&&suffix!="Plaster");s.localPosition=-mesh.bounds.center;
                    if(!fine) s.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
                }
                PrefabUtility.SaveAsPrefabAsset(g,path);
            } finally { EditorSceneManager.ClosePreviewScene(preview); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        static Transform Surface(Transform parent,Mesh mesh,Material material,bool outline)
        {
            var s=Child(parent,"Surface - "+mesh.name);s.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=s.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(s.gameObject,StaticEditorFlags.BatchingStatic);
            if(outline) {
                ComicOutlineHullFactory.Diagnostics info;var h=Child(s,"Comic ink contour - 随部件描边");
                h.gameObject.AddComponent<MeshFilter>().sharedMesh=ComicOutlineHullFactory.GetOrCreate(mesh,Root+"/Meshes/Contours",out info);
                var edge=h.gameObject.AddComponent<MeshRenderer>();edge.sharedMaterial=Ink(mesh.name.EndsWith("_Roof")||mesh.bounds.size.y<.45f);
                edge.shadowCastingMode=ShadowCastingMode.Off;edge.receiveShadows=false;edge.lightProbeUsage=LightProbeUsage.Off;edge.reflectionProbeUsage=ReflectionProbeUsage.Off;
            }
            return s;
        }
        static float Ground(float x,float z)
        {
            float y=float.NegativeInfinity;foreach(var c in ground) { RaycastHit h;if(c.Raycast(new Ray(new Vector3(x,30,z),Vector3.down),out h,250))y=Mathf.Max(y,h.point.y); }
            if(float.IsNegativeInfinity(y))throw new InvalidOperationException("Missing mountain support at "+x+","+z);return y;
        }
        static float Level(Vector3 n) { return float.IsNaN(n.y)?Mathf.Round(Ground(n.x,n.z)/.35f)*.35f:n.y; }
        static void Boundary(Transform parent,Vector3[] nodes,GameObject wall,GameObject window,GameObject pier,string key)
        {
            Vector3? last=null;float previous=0;
            for(int i=0;i<nodes.Length-1;i++) {
                var a=nodes[i];var b=nodes[i+1];var horizontal=new Vector3(b.x-a.x,0,b.z-a.z);float length=horizontal.magnitude;int count=Mathf.Max(1,Mathf.CeilToInt(length/5.2f));
                float la=Level(a),lb=Level(b);
                for(int j=0;j<count;j++) {
                    var start=Vector3.Lerp(a,b,(float)j/count);var end=Vector3.Lerp(a,b,(j+1f)/count);float y=Mathf.Round(Mathf.Lerp(la,lb,(j+.5f)/count)/.35f)*.35f;
                    if(i==0&&j==0&&!float.IsNaN(a.y))y=a.y;
                    if(i==nodes.Length-2&&j==count-1&&!float.IsNaN(b.y))y=b.y;
                    var midpoint=new Vector3((start.x+end.x)*.5f,y,(start.z+end.z)*.5f);
                    var panel=Instance(sections%7==3?window:wall,parent,key+" wall "+(++sections).ToString("000"),midpoint,0);
                    panel.rotation=Quaternion.Euler(0,-Mathf.Atan2(horizontal.z,horizontal.x)*Mathf.Rad2Deg,0);panel.localScale=new Vector3(length/count/4,1,1);PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
                    Support(parent,key+" footing "+sections,midpoint,new Vector2(length/count+.02f,.52f),panel.eulerAngles.y);
                    if(last.HasValue)AddPier(parent,pier,start,Mathf.Min(previous,y),Mathf.Abs(previous-y),key);
                    else AddPier(parent,pier,start,y,0,key);
                    last=end;previous=y;
                }
            }
            if(last.HasValue)AddPier(parent,pier,last.Value,previous,0,key);
        }
        static void AddPier(Transform parent,GameObject prefab,Vector3 p,float y,float rise,string key)
        {
            // Keep pier caps outside the original bridge's clear width, including the large enemy capsule.
            if(key=="LowerNE"&&Mathf.Abs(p.z+.075f)<.01f)p.z+=.4f;
            if(key=="LowerSW"&&Mathf.Abs(p.z+3.525f)<.01f)p.z-=.4f;
            p.y=y;var t=Instance(prefab,parent,key+" pier "+(++piers).ToString("000"),p,0);t.localScale=new Vector3(1,1+rise/2.25f,1);PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            Support(parent,key+" pier footing "+piers,p,new Vector2(.68f,.68f),0);
        }
        static Transform Box(Transform parent,string name,Vector3 center,Vector3 size,Material material,bool collision)
        {
            var p=Child(parent,name);p.localPosition=center;p.localScale=size;var s=Surface(p,cube,material,false);if(collision)s.gameObject.AddComponent<BoxCollider>();return p;
        }
        static void Support(Transform parent,string name,Vector3 top,Vector2 size,float yaw)
        {
            var rotation=Quaternion.Euler(0,yaw,0);float bottom=top.y;
            foreach(float t in new[]{-.5f,0,.5f})foreach(float side in new[]{-.5f,.5f}) { var q=top+rotation*new Vector3(size.x*t,0,size.y*side);bottom=Mathf.Min(bottom,Ground(q.x,q.z)-.15f); }
            float height=Mathf.Max(.12f,top.y-bottom);var p=Box(parent,name,new Vector3(top.x,top.y-height*.5f,top.z),new Vector3(size.x,height,size.y),Mat("Slate"),true);p.rotation=rotation;
        }
        static void OpenWall(Transform parent,bool upper,string originalName,float center,float width,string name)
        {
            var court=GameObject.Find("Floor").transform.Find(upper?"01 Upper court - 上层场地":"02 Lower court - 下层场地");
            var old=court.GetComponentsInChildren<Transform>(true).First(t=>t.name==originalName);var renderer=old.GetComponent<MeshRenderer>();var bounds=renderer.bounds;
            var group=Child(parent,name);var area=dressing.Find(upper?"01 Upper courtyard - 上层庭院":"02 Lower courtyard - 下层庭院");
            var cap=area.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==originalName+" - 独立压顶");
            foreach(int sign in new[]{-1,1}) {
                float x0=sign<0?bounds.min.x:center+width*.5f,x1=sign<0?center-width*.5f:bounds.max.x;
                var copy=UnityEngine.Object.Instantiate(old.gameObject,group);copy.name=(sign<0?"Left":"Right")+" retained-style wall";copy.SetActive(true);
                copy.transform.position=new Vector3((x0+x1)*.5f,bounds.center.y,bounds.center.z);copy.transform.rotation=old.rotation;
                copy.transform.localScale=new Vector3(old.lossyScale.x*(x1-x0)/bounds.size.x,old.lossyScale.y,old.lossyScale.z);
                if(cap){var c=UnityEngine.Object.Instantiate(cap.gameObject,group);c.name=(sign<0?"Left":"Right")+" coping";c.SetActive(true);c.transform.position=new Vector3((x0+x1)*.5f,cap.position.y,cap.position.z);c.transform.localScale=new Vector3(cap.lossyScale.x*(x1-x0)/bounds.size.x,cap.lossyScale.y,cap.lossyScale.z);}
            }
            Undo.RecordObject(old.gameObject,"Open inner court access");old.gameObject.SetActive(false);
            if(cap){Undo.RecordObject(cap.gameObject,"Retain old continuous coping");cap.gameObject.SetActive(false);}
        }
        static Mesh Profile(string key,List<Vector2> top,float width,float bottom)
        {
            string fingerprint=width.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+bottom.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+string.Join(";",top.Select(p=>p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            using(var sha=SHA256.Create())key+="_"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint))).Replace("-","").Substring(0,12);
            string path=Root+"/Meshes/Placed/"+key+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing)return existing;
            var polygon=new List<Vector2>{new Vector2(top[0].x,bottom)};polygon.AddRange(top);polygon.Add(new Vector2(top[top.Count-1].x,bottom));
            var vertices=new List<Vector3>();var triangles=new List<int>();
            foreach(float x in new[]{-width*.5f,width*.5f})foreach(var p in polygon)vertices.Add(new Vector3(x,p.y,p.x));
            var ids=Enumerable.Range(0,polygon.Count).ToList();float area=0;for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];area+=a.x*b.y-b.x*a.y;}if(area<0)ids.Reverse();
            var face=new List<int>();int guard=0;
            while(ids.Count>2&&guard++<polygon.Count*polygon.Count){bool found=false;for(int j=0;j<ids.Count;j++){int ia=ids[(j+ids.Count-1)%ids.Count],ib=ids[j],ic=ids[(j+1)%ids.Count];var a=polygon[ia];var b=polygon[ib];var c=polygon[ic];if(Cross(b-a,c-b)<=.000001f)continue;bool inside=false;foreach(int k in ids){if(k==ia||k==ib||k==ic)continue;var p=polygon[k];if(Cross(b-a,p-a)>=-.000001f&&Cross(c-b,p-b)>=-.000001f&&Cross(a-c,p-c)>=-.000001f){inside=true;break;}}if(inside)continue;face.AddRange(new[]{ia,ib,ic});ids.RemoveAt(j);found=true;break;}if(!found)throw new InvalidOperationException("Cannot triangulate stone profile "+key);}
            int n=polygon.Count;for(int i=0;i<face.Count;i+=3){triangles.AddRange(new[]{face[i],face[i+1],face[i+2],face[i]+n,face[i+2]+n,face[i+1]+n});}
            for(int i=0;i<n;i++){int j=(i+1)%n;triangles.AddRange(new[]{i,j,j+n,i,j+n,i+n});}
            // Independent face vertices keep step lighting crisp; logical edges remain closed.
            var expanded=triangles.Select(i=>vertices[i]).ToArray();var mesh=new Mesh{name=key};mesh.vertices=expanded;mesh.triangles=Enumerable.Range(0,expanded.Length).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static float Cross(Vector2 a,Vector2 b){return a.x*b.y-a.y*b.x;}
        static void Stair(Transform parent,string name,Vector3 a,Vector3 b,float width)
        {
            var group=Child(parent,name);group.position=new Vector3(a.x,0,a.z);var direction=new Vector3(b.x-a.x,0,b.z-a.z);group.rotation=Quaternion.Euler(0,Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,0);float length=direction.magnitude;
            int count=Mathf.CeilToInt(Mathf.Abs(b.y-a.y)/.18f)+1;count=Mathf.Max(2,count);var top=new List<Vector2>();
            for(int i=0;i<count;i++){float y=Mathf.Lerp(a.y,b.y,(float)i/(count-1));top.Add(new Vector2(length*i/count,y));top.Add(new Vector2(length*(i+1)/count,y));}
            float bottom=Mathf.Min(a.y,b.y)-.2f;for(int i=0;i<=8;i++){var p=Vector3.Lerp(a,b,i/8f);bottom=Mathf.Min(bottom,Ground(p.x,p.z)-.15f);}
            var mesh=Profile(name.Replace(' ','_'),top,width,bottom);var body=Surface(group,mesh,Mat("Stone"),false);body.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
            for(int i=1;i<count;i++){float previous=Mathf.Lerp(a.y,b.y,(i-1f)/(count-1));float y=Mathf.Lerp(a.y,b.y,i/(count-1f));Box(group,"Riser "+i,new Vector3(0,(previous+y)*.5f,length*i/count),new Vector3(width-.06f,Mathf.Abs(y-previous)*.82f,.012f),Mat("Slate"),false);}
            SamuraiStairRampCollisionSetup.EnsureCompoundStair(group);
        }
        static void Walk(Transform parent,string name,Vector3[] nodes,float width)
        {
            var group=Child(parent,name);
            for(int i=0;i<nodes.Length-1;i++) {
                var a=nodes[i];var b=nodes[i+1];float ya=float.IsNaN(a.y)?Ground(a.x,a.z)+.07f:a.y,yb=float.IsNaN(b.y)?Ground(b.x,b.z)+.07f:b.y;
                var delta=new Vector3(b.x-a.x,0,b.z-a.z);float length=delta.magnitude;var part=Child(group,"Stone run "+(i+1));part.position=new Vector3(a.x,0,a.z);part.rotation=Quaternion.Euler(0,Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,0);
                int count=Mathf.Max(2,Mathf.CeilToInt(length/.7f));var top=new List<Vector2>();float bottom=Mathf.Min(ya,yb)-.15f;
                for(int j=0;j<=count;j++){float t=(float)j/count;var p=Vector3.Lerp(a,b,t);float y=Ground(p.x,p.z)+.07f;
                    if(j==0)y=ya;else if(j==count)y=yb;
                    // Blend endpoint landings into the local terrain over a short distance.
                    else {float nearA=Mathf.Clamp01(1-t*length/2),nearB=Mathf.Clamp01(1-(1-t)*length/2);y=Mathf.Lerp(y,Mathf.Lerp(ya,yb,t),Mathf.Max(nearA,nearB));}
                    top.Add(new Vector2(t*length,y));bottom=Mathf.Min(bottom,y-.16f);}
                var mesh=Profile(name.Replace(' ','_')+"_"+i,top,width,bottom);var s=Surface(part,mesh,Mat("Stone"),false);s.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
        }
        static void AddLantern(Transform parent,string name,Vector2 p,float scale)
        {
            var source=dressing.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Stone lantern 1");var go=UnityEngine.Object.Instantiate(source.gameObject,parent);go.name=name;go.SetActive(true);go.transform.position=new Vector3(p.x,Ground(p.x,p.y)+.03f,p.y);go.transform.rotation=Quaternion.identity;go.transform.localScale=Vector3.one*scale;
        }
        static int ClearWallTrunks()
        {
            var lines=new[]{UpperBoundary,LowerNorthEastBoundary,LowerSouthWestBoundary};int changed=0;
            var targets=dressing.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh&&f.sharedMesh.name.StartsWith("Pine_")&&!f.transform.IsChildOf(compound)&&!(f.transform.parent&&f.transform.parent.GetComponent<Renderer>())).Select(f=>f.transform.parent).Distinct().ToArray();
            foreach(var tree in targets){var p=new Vector2(tree.position.x,tree.position.z);float distance=float.PositiveInfinity;Vector2 closest=Vector2.zero;
                foreach(var line in lines)for(int i=0;i<line.Length-1;i++){var a=new Vector2(line[i].x,line[i].z);var b=new Vector2(line[i+1].x,line[i+1].z);var ab=b-a;var q=a+ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude);if(Vector2.Distance(p,q)<distance){distance=Vector2.Distance(p,q);closest=q;}}
                if(distance>=.85f)continue;var center=tree.position.x<-7?new Vector2(-29,1):new Vector2(17,0);var direction=(center-closest).normalized;var q2=closest+direction*1.25f;
                Undo.RecordObject(tree,"Clear enclosure wall from pine trunk");tree.position=new Vector3(q2.x,Ground(q2.x,q2.y),q2.y);changed++;
            }return changed;
        }
        static void SupportExistingLanterns(Transform parent)
        {
            foreach(var lantern in dressing.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Stone lantern ")&&!t.IsChildOf(compound)))
            {
                float bottom=lantern.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                Support(parent,lantern.name+" - 接地石柱",new Vector3(lantern.position.x,bottom,lantern.position.z),new Vector2(.85f,.85f),0);
            }
        }
        static void GalleryLanding(Transform parent)
        {
            var group=Child(parent,"Storehouse gallery landing - 仓廊平台");
            Support(group,"Slate support",new Vector3(-26.45f,1.14f,-13),new Vector2(2.1f,1.4f),0);
            Box(group,"Stone landing top",new Vector3(-26.45f,1.175f,-13),new Vector3(2.1f,.07f,1.4f),Mat("Stone"),true);
        }
    }
}
