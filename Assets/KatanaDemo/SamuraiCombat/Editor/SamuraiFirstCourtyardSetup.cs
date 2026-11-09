using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCDesign.LookDev;
using OCDesign.LookDev.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>First court exploration layout. Existing installation and later hand edits are retained.</summary>
    public static class SamuraiFirstCourtyardSetup
    {
        public const string Root = SamuraiComicStyleSetup.Root + "/FirstCourtyard";
        public const string RootName = "08 First courtyard - 回廊房院";
        const string Model = Root + "/Models/FirstCourtyard.fbx";
        static Mesh cube;
        static readonly Dictionary<string,string> Palette = new Dictionary<string,string> {
            {"Stone","Warm_sandstone"},{"Slate","Foundation_slate"},{"Plaster","Ivory_plaster"},
            {"Timber","Old_honey_timber"},{"Red","Vermilion_lacquer"},{"Roof","Ink_blue_roof"},
            {"Paper","Warm_lantern_paper"},{"Gold","Quiet_ochre"}
        };

        [MenuItem("Katana/Level/Build First Courtyard Exploration")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Courtyard dressing");
            if (!dressing) throw new InvalidOperationException("Existing mountain courtyard is required.");
            if (dressing.transform.Find(RootName)) {
                SamuraiCourtyardRoadSetup.EnsureMissing();
                return "First courtyard exists; all placements, encounters and checkpoint settings retained.";
            }
            var meshes = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Mesh>().OrderBy(m => m.name).ToArray();
            foreach (string prefix in new[] {"Hall_","Room_","Gallery_","Shrine_","Chest_"})
                if (!meshes.Any(m => m.name.StartsWith(prefix))) throw new InvalidOperationException("Missing model prefix " + prefix);
            foreach (string folder in new[] {"Prefabs","Meshes/Contours","Materials"}) Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            cube = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
            var prefabs = new Dictionary<string,GameObject>();
            foreach (string key in new[] {"Hall","Room","Gallery","Shrine","Chest"}) prefabs[key] = Module(key, meshes);
            int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Design first courtyard exploration");
            var root = Child(dressing.transform, RootName); Undo.RegisterCreatedObjectUndo(root.gameObject,"Add first courtyard");
            var arch = Child(root,"01 Rooms and great hall - 房间与可进入大殿");
            Instance(prefabs["Hall"],arch,"Great hall - 可进入大殿",new Vector3(13,0,17),0);
            Instance(prefabs["Room"],arch,"West prayer room - 西南祈愿室",new Vector3(3.4f,0,-6),-90);
            Instance(prefabs["Room"],arch,"West store room - 西北藏物室",new Vector3(3.4f,0,5),-90);
            Instance(prefabs["Room"],arch,"East guard room - 东南守卫房",new Vector3(21.4f,0,0),90);
            Instance(prefabs["Room"],arch,"East study room - 东北书房",new Vector3(21.4f,0,7),90);
            foreach (float z in new[] {-6f,5f}) Instance(prefabs["Gallery"],arch,"West covered walk " + z,new Vector3(7,0,z),90);
            foreach (float z in new[] {0f,7f}) Instance(prefabs["Gallery"],arch,"East covered walk " + z,new Vector3(17.8f,0,z),90);

            var routes = Child(root,"02 Circulation and planted court - 双线动线与植栽院");
            // The great hall replaces the old closed pavilion. Its continuous plinth rests on the crag.
            Box(routes,"Great hall retaining plinth - 大殿接地石台",new Vector3(13,-2.08f,17),new Vector3(20.6f,4.0f,11.6f),"Slate",true);
            Box(routes,"West hall side access - 西侧回路石台",new Vector3(2.05f,-2.08f,13.4f),new Vector3(2.5f,4.16f,6.8f),"Stone",true);
            Box(routes,"East hall side access - 东侧回路石台",new Vector3(23.85f,-2.08f,13.4f),new Vector3(2.5f,4.16f,6.8f),"Stone",true);
            Box(routes,"Hall front apron - 殿前连续平台",new Vector3(13,-.10f,10.9f),new Vector3(24,.2f,2.0f),"Stone",true);
            // Side parapets leave both side doors and front access open.
            Box(routes,"West side parapet",new Vector3(.95f,.48f,13.5f),new Vector3(.22f,.96f,6.6f),"Slate",true);
            Box(routes,"East side parapet",new Vector3(25,.48f,13.5f),new Vector3(.22f,.96f,6.6f),"Slate",true);
            Path(routes,"Arrival turn - 入院折线",2.0f,new[]{new Vector3(22.386f,0,-10.8f),new Vector3(13,0,-8)});
            Path(routes,"West fighting loop - 西侧主线",1.8f,new[]{new Vector3(13,0,-8),new Vector3(9.5f,0,-4),new Vector3(9.5f,0,3),new Vector3(13,0,8),new Vector3(13,0,11)});
            Path(routes,"East exploration loop - 东侧绕行",1.65f,new[]{new Vector3(13,0,-8),new Vector3(16.1f,0,-4),new Vector3(16.1f,0,4),new Vector3(13,0,8)});
            Path(routes,"Bridge connection - 连桥通路",1.6f,new[]{new Vector3(.4f,0,-1.8f),new Vector3(8,0,-1.8f)});
            Path(routes,"Prayer room connection - 存档支路",1.5f,new[]{new Vector3(9.5f,0,-6),new Vector3(5.8f,0,-6)});
            Path(routes,"West store connection",1.5f,new[]{new Vector3(9.5f,0,5),new Vector3(5.8f,0,5)});
            Planter(routes);
            Box(routes,"Arrival sight screen - 入院遮景矮墙",new Vector3(19,.85f,-6.2f),new Vector3(3.3f,1.7f,.26f),"Plaster",true);
            Box(routes,"Arrival sight screen timber cap",new Vector3(19,1.73f,-6.2f),new Vector3(3.5f,.16f,.38f),"Timber",false);
            // Short gate wings close off the south without narrowing the existing stair landing.
            Box(routes,"South room enclosure",new Vector3(10,.95f,-10.7f),new Vector3(16,1.9f,.22f),"Plaster",true);
            Box(routes,"South enclosure cap",new Vector3(10,1.98f,-10.7f),new Vector3(16.2f,.18f,.38f),"Roof",false);

            var loot = Child(root,"03 Treasure reservations - 宝箱预留区域");
            Treasure(prefabs["Chest"],loot,"01 West store treasure - 西藏物室宝箱预留",new Vector3(2.05f,.065f,5),-90);
            Treasure(prefabs["Chest"],loot,"02 Great hall treasury - 大殿东内室宝箱预留",new Vector3(20.7f,.065f,20.9f),0);
            var checkpointRoot = Child(root,"04 Single checkpoint - 唯一存档点");
            var shrine = Instance(prefabs["Shrine"],checkpointRoot,"Prayer shrine - 祈愿存档神龛",new Vector3(1.9f,.065f,-6),-90);
            var anchor = Child(checkpointRoot,"Respawn anchor - 安全复活朝向"); anchor.SetPositionAndRotation(new Vector3(4.4f,.07f,-5.3f),Quaternion.Euler(0,90,0));
            var cp = shrine.gameObject.AddComponent<SamuraiCourtyardCheckpoint>();
            cp.player = scene.GetRootGameObjects().First(g=>g.name=="SamuraiCombatPlayer").GetComponent<SamuraiCombatant>();
            cp.input = cp.player.GetComponent<SamuraiPlayerInput>(); cp.respawnAnchor = anchor;
            cp.promptFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/KatanaDemo/SamuraiCombat/UI/Fonts/NotoSansSC.ttf");
            cp.interactionRadius = 2.6f;
            var encounters = Child(root,"05 Encounter design - 分段怪物布点");
            Encounter(scene,encounters,"Enemy1","01 Arrival sentry - 入院单哨",new Vector3(15.5f,.02f,-5),150,6.5f,110);
            Encounter(scene,encounters,"Enemy1 (1)","02 Court guard - 中院侧卫",new Vector3(10,.02f,6),165,7f,120);
            Encounter(scene,encounters,"Enemy1 (2)","03 Hall defender - 大殿守卫",new Vector3(13,.08f,18),180,7.5f,120);
            var extra=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Enemy1 - East study guard");
            if(!extra) {
                extra=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KatanaDemo/Characters/Enemy1/Prefabs/Enemy1.prefab"),scene);
                extra.name="Enemy1 - East study guard"; Undo.RegisterCreatedObjectUndo(extra,"Place east study defender");
                ConfigureEnemy(extra,new Vector3(21.4f,.08f,7),270,5.5f,100);
            }
            Marker(encounters,"04 Optional east study guard - 可选书房守卫",extra.transform.position,270);
            RetireOldLayout(dressing.transform);
            SamuraiCourtyardRoadSetup.EnsureMissing();
            if(Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var screen))screen.RefreshSources();
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); Undo.CollapseUndoOperations(undo);
            return "First courtyard: four enterable side rooms, great hall with side chambers, two circulation loops, two treasure reservations, four staged enemies, one functional checkpoint. Scene is not automatically saved.";
        }

        static Material Mat(string key) => AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root + "/Materials/" + Palette[key] + ".mat");
        // The shared toon shader uses a painted shadow palette. Warm local variants retain
        // the wood/plaster distinctions under a roof without changing global lighting.
        public static Material RoomMaterial(string key)
        {
            if(key=="Roof")return Mat(key);
            string path=Root+"/Materials/Interior_"+key+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material)return material;
            Directory.CreateDirectory(Root+"/Materials");
            material=new Material(Mat(key)){name="Interior_"+key};
            Color color=material.GetColor("_BaseColor");
            float strength=key=="Plaster"?.67f:key=="Paper"?.76f:key=="Gold"?.65f:.63f;
            var shadow=new Color(color.r*strength,color.g*strength,color.b*strength,1);
            material.SetColor("_ShadowColor",shadow);AssetDatabase.CreateAsset(material,path);return material;
        }
        static Transform Child(Transform p,string name) { var g=new GameObject(name);g.transform.SetParent(p,false);return g.transform; }
        static Transform Instance(GameObject prefab,Transform p,string name,Vector3 position,float yaw)
        {
            var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab,p);g.name=name;g.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
            PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(g);return g.transform;
        }
        static string Category(string n)
        {
            if(n.Contains("Floor")||n.Contains("Foundation"))return "01 Ground and floor - 地板与地基";
            if(n.Contains("Roof")||n.Contains("Ceiling")||n.Contains("Ridge")||n.Contains("Eave"))return "05 Roof and ceiling - 屋顶与室内梁顶";
            if(n.Contains("Wall")||n.Contains("Partition"))return "02 Walls and true openings - 墙体与真实开口";
            if(n.Contains("Window")||n.Contains("Lattice")||n.Contains("Door"))return "04 Window and doorway frames - 窗棂与门框";
            if(n.Contains("Furniture")||n.Contains("Light")||n.Contains("Lantern"))return "06 Furniture and lamps - 家具与灯具";
            return "03 Structure and detail - 木构与细部";
        }
        static GameObject Module(string prefix,Mesh[] meshes)
        {
            string path=Root+"/Prefabs/FirstCourt_"+prefix+".prefab";
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing)return existing;
            var preview=EditorSceneManager.NewPreviewScene();var g=new GameObject("FirstCourt_"+prefix);SceneManager.MoveGameObjectToScene(g,preview);
            try {
                foreach(var mesh in meshes.Where(m=>m.name.StartsWith(prefix+"_"))) {
                    string name=mesh.name.Substring(prefix.Length+1);string suffix=name.Substring(name.LastIndexOf('_')+1);
                    var category=g.transform.Find(Category(name));if(!category)category=Child(g.transform,Category(name));
                    var part=Child(category,name);part.localPosition=mesh.bounds.center;
                    bool fine=name.Contains("Lattice")||name.Contains("Paper")||name.Contains("Light")||name.Contains("Lantern")||name.Contains("Trim")||name.Contains("Bracket");
                    var surface=Surface(part,mesh,RoomMaterial(suffix),!fine);surface.localPosition=-mesh.bounds.center;
                    if(name.Contains("Floor")||name.Contains("Foundation")||name.Contains("Wall")||name.Contains("Partition")||name.Contains("Column")||name.Contains("Roof")||name.Contains("Ceiling")||name.Contains("Furniture")||prefix=="Chest"||prefix=="Shrine")
                        surface.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
                }
                return PrefabUtility.SaveAsPrefabAsset(g,path);
            } finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static Transform Surface(Transform p,Mesh mesh,Material mat,bool outline)
        {
            var t=Child(p,"Surface - "+mesh.name);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject,StaticEditorFlags.BatchingStatic);
            if(outline) {
                ComicOutlineHullFactory.Diagnostics diagnostics;
                var h=Child(t,"Comic ink contour - 独立闭合描边");h.gameObject.AddComponent<MeshFilter>().sharedMesh=ComicOutlineHullFactory.GetOrCreate(mesh,Root+"/Meshes/Contours",out diagnostics);
                var ink=h.gameObject.AddComponent<MeshRenderer>();ink.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardArchitectureSetup.Root+"/Materials/Hall_roof_ink.mat");
                ink.shadowCastingMode=ShadowCastingMode.Off;ink.receiveShadows=false;ink.lightProbeUsage=LightProbeUsage.Off;ink.reflectionProbeUsage=ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(h.gameObject,StaticEditorFlags.BatchingStatic);
            }
            return t;
        }
        static Transform Box(Transform p,string name,Vector3 pos,Vector3 size,string mat,bool collide)
        {
            var t=Child(p,name);t.position=pos;t.localScale=size;var s=Surface(t,cube,Mat(mat),false);if(collide)s.gameObject.AddComponent<BoxCollider>();return t;
        }
        static void Path(Transform p,string name,float width,Vector3[] points)
        {
            var group=Child(p,name);
            for(int i=0;i<points.Length-1;i++) {
                var a=points[i];var b=points[i+1];var delta=b-a;float length=delta.magnitude;int count=Mathf.CeilToInt(length/.85f);
                for(int j=0;j<count;j++) {
                    var pos=Vector3.Lerp(a,b,(j+.5f)/count)+Vector3.up*.009f;
                    var t=Box(group,"Inlaid stone "+i+"-"+j,pos,new Vector3(width,.018f,length/count-.045f),j%3==1?"Slate":"Stone",false);
                    t.rotation=Quaternion.LookRotation(delta);
                }
            }
        }
        static void Planter(Transform p)
        {
            var group=Child(p,"Central planted island - 分流石庭");
            Box(group,"Island retaining stone",new Vector3(12.8f,.25f,-1),new Vector3(3.2f,.5f,3.4f),"Slate",true);
            var green=AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root+"/Materials/Garden_dry_sage.mat");
            var top=Box(group,"Moss bed",new Vector3(12.8f,.51f,-1),new Vector3(2.85f,.08f,3.05f),"Stone",false);
            if(green)top.GetComponentInChildren<MeshRenderer>().sharedMaterial=green;
            var stone=Child(group,"Garden stone");stone.position=new Vector3(12.3f,.63f,-.8f);stone.localScale=new Vector3(1.5f,.65f,1.25f);
            Surface(stone,AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root+"/Meshes/Shapes/Faceted_garden_stone.asset"),Mat("Slate"),true);
            var tree=Child(group,"Court pine - 中庭孤松");tree.position=new Vector3(13.4f,.55f,-.5f);
            var trunk=Box(tree,"Small pine trunk",new Vector3(13.4f,1.35f,-.5f),new Vector3(.20f,1.6f,.20f),"Timber",false);
            var foliage=AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root+"/Meshes/Shapes/Hand_cut_foliage.asset");
            var pine=AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root+"/Materials/Pine_dark_teal.mat");
            for(int i=0;i<3;i++) {
                var crown=Child(tree,"Pine bough "+i);crown.position=new Vector3(13.4f+(i==1?.4f:-.1f),1.9f+i*.4f,-.5f);crown.localScale=new Vector3(1.6f-i*.25f,.3f,1.2f-i*.16f);Surface(crown,foliage,pine?pine:Mat("Timber"),false);
            }
        }
        static void Treasure(GameObject prefab,Transform p,string name,Vector3 pos,float yaw)
        {
            var g=Child(p,name);var zone=Child(g,"Reserved clear space - 净空3x3米");zone.position=pos+Quaternion.Euler(0,yaw,0)*new Vector3(0,0,-1.1f);
            Instance(prefab,g,"Reserved treasure chest - 静态预留箱体",pos,yaw);
            Marker(g,"Approach anchor - 接近站位",pos+Quaternion.Euler(0,yaw,0)*new Vector3(0,0,-1.5f),yaw);
        }
        static void Marker(Transform p,string name,Vector3 pos,float yaw) {var t=Child(p,name);t.SetPositionAndRotation(pos,Quaternion.Euler(0,yaw,0));}
        static void Encounter(Scene scene,Transform p,string oldName,string name,Vector3 pos,float yaw,float distance,float angle)
        {
            var g=scene.GetRootGameObjects().First(x=>x.name==oldName);Undo.RecordObject(g.transform,"Redesign enemy placement");
            ConfigureEnemy(g,pos,yaw,distance,angle);Marker(p,name,pos,yaw);
        }
        static void ConfigureEnemy(GameObject g,Vector3 pos,float yaw,float distance,float angle)
        {
            g.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,yaw,0));var ai=g.GetComponent<SamuraiTrainingEnemy>();Undo.RecordObject(ai,"Configure encounter sight");
            ai.detectionDistance=distance;ai.horizontalViewAngle=angle;ai.activeTraining=true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
        }
        static void RetireOldLayout(Transform dressing)
        {
            string lower="02 Lower courtyard - 下层庭院/";
            foreach(string path in new[]{lower+"02 Architecture - 建筑与门架/05 Eastern pavilion",lower+"06 Court marks - 地面纹章",
                lower+"04 Banners - 旗幡/Crest banner 8",lower+"03 Stone lanterns - 石灯/Stone lantern 7",lower+"03 Stone lanterns - 石灯/Stone lantern 8",
                "06 Architecture sites - 建筑落地石基/05 Eastern pavilion - 地形石基",
                "07 Mountain compound - 合围山院/03 Inner court access - 内院通口与石阶/Eastern hall doorway - 中殿通口",
                "07 Mountain compound - 合围山院/03 Inner court access - 内院通口与石阶/Eastern hall approach - 中殿石径"}) {
                var t=dressing.Find(path);if(t){Undo.RecordObject(t.gameObject,"Retain superseded first-court layout");t.gameObject.SetActive(false);}
            }
            var floor=GameObject.Find("Floor").transform.Find("02 Lower court - 下层场地");
            foreach(var t in floor.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Grid ")||t.name=="Collision Practice Pillar")) {
                Undo.RecordObject(t.gameObject,"Retain training markers");t.gameObject.SetActive(false);
            }
        }
    }
}
