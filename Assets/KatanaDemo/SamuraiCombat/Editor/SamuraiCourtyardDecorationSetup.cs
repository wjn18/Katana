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
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Function-based editable dressing. Gameplay and collision stay on their original objects.</summary>
    public static class SamuraiCourtyardDecorationSetup
    {
        public const string Root = SamuraiComicStyleSetup.Root + "/Decoration";
        public const string RootName = "Courtyard dressing";
        const string ContentName = "Editable components - 可调部件";
        static Transform dressing, group, current;
        static Dictionary<Material,List<CombineInstance>> parts;
        static Dictionary<string,LegacySurface> legacy;
        static Mesh cube,roof,column,taper,foliage,rock,cloth,ring,ridge,crest,lattice;
        static Material stone,darkStone,plaster,red,timber,roofMat,gold,paper,ink,detailInk;
        static Material leafRed,leafOrange,leafDark,pine,pineLight,moss,mountain;
        static bool groupOutline;
        static int added, pieces, migrated;

        sealed class LegacySurface
        {
            public Material material, outlineMaterial;
            public Matrix4x4 matrix=Matrix4x4.identity;
            public bool enabled=true, active=true, outlineEnabled=true, outlineActive=true;
        }

        [MenuItem("OC Design/Samurai Combat/Decorate Existing Courtyard")]
        [MenuItem("OC Design/Samurai Combat/Organize And Refine Courtyard")]
        public static string Install()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            foreach(string folder in new[]{"Materials","Meshes/Shapes","Meshes/Editable","Meshes/EditableContours"})
                Directory.CreateDirectory(Root+"/"+folder);
            AssetDatabase.Refresh();
            Initialize(); // Validate the Blender meshes before touching the scene.
            var found=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(!found){found=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(found,"Create courtyard dressing");}
            dressing=found.transform;added=pieces=migrated=0;
            var upper=Folder(dressing,"01 Upper courtyard - 上层庭院",CourtPivot(true));
            var lower=Folder(dressing,"02 Lower courtyard - 下层庭院",CourtPivot(false));
            var background=Folder(dressing,"03 Background - 远景",Vector3.zero);
            Group("01 Upper terrace masonry",Folder(upper,"01 Structure - 场地装饰"),new Vector3(-25.42f,0,0),()=>Foundation(true),false);
            Group("02 Lower terrace masonry",Folder(lower,"01 Structure - 场地装饰"),Vector3.zero,()=>Foundation(false),false);
            WallCaps(upper,lower);
            Group("04 Main practice hall",Folder(upper,"02 Architecture - 建筑与门架"),new Vector3(-25.4f,1.3671f,19),()=>Hall(16,6),true);
            Group("05 Eastern pavilion",Folder(lower,"02 Architecture - 建筑与门架"),new Vector3(1.2f,0,18.5f),()=>Hall(10,5.2f),true);
            Group("06 Entry gate",Folder(lower,"02 Architecture - 建筑与门架"),new Vector3(10,-.8f,-19.4f),()=>Gate(7.6f,6.5f),true);
            Group("07 Upper timber frame",Folder(upper,"02 Architecture - 建筑与门架"),new Vector3(-25.4f,1.3671f,-14.2f),()=>Gate(10.4f,6.2f),true);
            Vector3[] lanterns={new Vector3(-38.6f,1.3671f,-8),new Vector3(-38.6f,1.3671f,8),new Vector3(-32,1.3671f,13.3f),new Vector3(-18.8f,1.3671f,13.3f),new Vector3(13.1f,0,-8.5f),new Vector3(13.1f,0,8.5f),new Vector3(-7,0,13.3f),new Vector3(7.5f,0,13.3f)};
            for(int i=0;i<lanterns.Length;i++)Group("Stone lantern "+(i+1),Folder(i<4?upper:lower,"03 Stone lanterns - 石灯"),lanterns[i],StoneLantern,true);
            Vector3[] banners={new Vector3(-38.15f,1.3671f,-4),new Vector3(-38.15f,1.3671f,5),new Vector3(-35.4f,1.3671f,13.2f),new Vector3(-15.5f,1.3671f,13.2f),new Vector3(12.9f,0,-4),new Vector3(12.9f,0,5),new Vector3(-8.8f,0,-12.9f),new Vector3(4.8f,0,13.2f)};
            for(int i=0;i<banners.Length;i++){int index=i;Group("Crest banner "+(i+1),Folder(i<4?upper:lower,"04 Banners - 旗幡"),banners[i],()=>Banner(index),true);}
            Vector3[] maples={new Vector3(-42,-1.7f,-4),new Vector3(-40,-1,15),new Vector3(18,-1.8f,-6),new Vector3(17,-1.4f,14),new Vector3(-5,-2,-22)};
            for(int i=0;i<maples.Length;i++){int index=i;Group("Copper maple garden "+(i+1),Folder(i<2?upper:lower,"05 Gardens - 庭园"),maples[i],()=>TreeGarden(false,index),false);}
            Vector3[] pines={new Vector3(-36,-1,-18),new Vector3(-13,-.8f,27),new Vector3(17,-2,-17)};
            for(int i=0;i<pines.Length;i++){int index=i;Group("Ink pine garden "+(i+1),Folder(i==0?upper:i==1?background:lower,"05 Gardens - 庭园"),pines[i],()=>TreeGarden(true,index),false);}
            Group("Upper stone court crest",Folder(upper,"06 Court marks - 地面纹章"),new Vector3(-25.42f,1.374f,0),CourtCrest,false);
            Group("Lower stone court crest",Folder(lower,"06 Court marks - 地面纹章"),new Vector3(0,.007f,0),CourtCrest,false);
            Group("Quiet northern peaks",background,new Vector3(0,-21,0),Mountains,false);
            OrganizeFloor();
            if(Camera.main&&Camera.main.TryGetComponent<ComicScreenOutline>(out var screen))screen.RefreshSources();
            if(added+pieces+migrated>0)EditorSceneManager.MarkSceneDirty(scene);
            return $"Editable courtyard: {migrated} legacy groups migrated, {added} new groups, {pieces} functional parts. Existing edited components preserved.";
        }

        static Transform Folder(Transform parent,string name,Vector3? worldPivot=null)
        {
            var existing=parent.Find(name);if(existing)return existing;
            var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Group courtyard by function");
            go.transform.SetParent(parent,false);if(worldPivot.HasValue)go.transform.position=worldPivot.Value;
            added++;return go.transform;
        }
        static Transform Find(Transform parent,string name)
        {
            foreach(var t in parent.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;
            return null;
        }
        static Vector3 CourtPivot(bool upper)
        {
            var floor=GameObject.Find("Floor");
            if(floor)foreach(var t in floor.GetComponentsInChildren<Transform>(true))
                if(t.name==(upper?"Training Floor (1)":"Training Floor"))
                    return t.position+Vector3.up*.15f;
            return upper?new Vector3(-25.42f,1.3671f,0):Vector3.zero;
        }
        static Dictionary<string,LegacySurface> Capture(Transform old)
        {
            var result=new Dictionary<string,LegacySurface>();if(!old)return result;
            foreach(Transform t in old)
            {
                var r=t.GetComponent<MeshRenderer>();if(!r)continue;
                var h=t.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x=>x!=r);
                result[t.name.Replace('_',' ')]=new LegacySurface{material=r.sharedMaterial,matrix=Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale),enabled=r.enabled,active=t.gameObject.activeSelf,outlineMaterial=h?h.sharedMaterial:null,outlineEnabled=!h||h.enabled,outlineActive=!h||h.gameObject.activeSelf};
            }
            return result;
        }
        static void Group(string name,Transform parent,Vector3 position,Action build,bool outline)
        {
            var existing=Find(dressing,name);
            if(existing&&existing.Find(ContentName))return;
            legacy=Capture(existing);
            var oldChildren=existing?existing.Cast<Transform>().ToArray():Array.Empty<Transform>();
            if(!existing){var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Add courtyard prop");existing=go.transform;existing.SetParent(dressing,false);existing.position=position;added++;}
            existing.SetParent(parent,true);
            var content=new GameObject(ContentName);Undo.RegisterCreatedObjectUndo(content,"Make editable components");content.transform.SetParent(existing,false);
            Matrix4x4 common=Matrix4x4.identity;
            if(legacy.Count>0)
            {
                common=legacy.Values.First().matrix;
                bool same=legacy.Values.All(x=>MatrixClose(x.matrix,common));
                if(same)
                {
                    var local=existing.parent.worldToLocalMatrix*existing.localToWorldMatrix*common;
                    var rebuilt=Matrix4x4.TRS(local.GetColumn(3),local.rotation,local.lossyScale);
                    if(MatrixClose(local,rebuilt)){existing.localPosition=local.GetColumn(3);existing.localRotation=local.rotation;existing.localScale=local.lossyScale;}
                    else {content.transform.localPosition=common.GetColumn(3);content.transform.localRotation=common.rotation;content.transform.localScale=common.lossyScale;}
                    foreach(var x in legacy.Values)x.matrix=Matrix4x4.identity;
                }
            }
            group=current=content.transform;groupOutline=outline;parts=new Dictionary<Material,List<CombineInstance>>();
            build();Flush(current,parts);
            foreach(var oldChild in oldChildren)Undo.DestroyObjectImmediate(oldChild.gameObject);
            if(oldChildren.Length>0)migrated++;
        }
        static bool MatrixClose(Matrix4x4 a,Matrix4x4 b){for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>.00001f)return false;return true;}
        static void Unit(string name,Vector3 pivot,Action build)
        {
            var previous=current;var previousParts=parts;
            var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Add functional prop part");go.transform.SetParent(previous,false);go.transform.position=group.TransformPoint(pivot);
            current=go.transform;parts=new Dictionary<Material,List<CombineInstance>>();
            build();Flush(current,parts);current=previous;parts=previousParts;pieces++;
        }
        static string Key(List<CombineInstance> instances)
        {
            var value=new StringBuilder();
            foreach(var instance in instances)
            {
                string path=AssetDatabase.GetAssetPath(instance.mesh);
                value.Append(path).Append('|').Append(instance.mesh.name).Append('|').Append(AssetDatabase.GetAssetDependencyHash(path)).Append('|');
                for(int i=0;i<16;i++)value.Append(Mathf.Round(instance.transform[i]*10000f).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            }
            using(var sha=SHA256.Create())
            {
                var bytes=Encoding.UTF8.GetBytes(value.ToString());
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").Substring(0,22);
            }
        }
        static void Flush(Transform unit,Dictionary<Material,List<CombineInstance>> meshes)
        {
            foreach(var entry in meshes)
            {
                string key="Geometry_"+Key(entry.Value);
                string path=Root+"/Meshes/Editable/"+key+".asset";
                bool imported=entry.Value.Count==1&&AssetDatabase.GetAssetPath(entry.Value[0].mesh)==Root+"/Models/Courtyard_Refined.fbx";
                var mesh=imported?entry.Value[0].mesh:AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(!mesh){mesh=new Mesh{name=unit.name+" - "+entry.Key.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(entry.Value.ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);}
                legacy.TryGetValue(entry.Key.name,out var state);
                var surface=new GameObject("Surface - "+entry.Key.name);surface.transform.SetParent(unit,false);
                if(imported){var matrix=entry.Value[0].transform;surface.transform.localPosition=matrix.GetColumn(3);surface.transform.localRotation=matrix.rotation;surface.transform.localScale=matrix.lossyScale;}
                GameObjectUtility.SetStaticEditorFlags(surface,StaticEditorFlags.BatchingStatic);
                surface.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=surface.AddComponent<MeshRenderer>();r.sharedMaterial=state?.material?state.material:entry.Key;r.enabled=state?.enabled??true;
                r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;surface.SetActive(state?.active??true);
                if(groupOutline)
                {
                    var shell=new GameObject("Comic ink contour - 描边随父部件");shell.transform.SetParent(surface.transform,false);
                    GameObjectUtility.SetStaticEditorFlags(shell,StaticEditorFlags.BatchingStatic);
                    ComicOutlineHullFactory.Diagnostics diagnostic;
                    shell.AddComponent<MeshFilter>().sharedMesh=ComicOutlineHullFactory.GetOrCreate(mesh,Root+"/Meshes/EditableContours",out diagnostic);
                    var edge=shell.AddComponent<MeshRenderer>();edge.sharedMaterial=mesh==lattice?detailInk:state?.outlineMaterial?state.outlineMaterial:ink;edge.enabled=state?.outlineEnabled??true;edge.shadowCastingMode=ShadowCastingMode.Off;edge.receiveShadows=false;edge.lightProbeUsage=LightProbeUsage.Off;edge.reflectionProbeUsage=ReflectionProbeUsage.Off;shell.SetActive(state?.outlineActive??true);
                    SceneVisibilityManager.instance.DisablePicking(shell,false);
                }
            }
        }
        static void Piece(Mesh mesh,Vector3 p,Vector3 size,Material m,Quaternion rotation)
        {
            if(!parts.TryGetValue(m,out var list))parts[m]=list=new List<CombineInstance>();
            var matrix=Matrix4x4.TRS(p,rotation,size);
            if(legacy.TryGetValue(m.name,out var state))matrix=state.matrix*matrix;
            list.Add(new CombineInstance{mesh=mesh,transform=current.worldToLocalMatrix*group.localToWorldMatrix*matrix});
        }
        static void Block(Vector3 p,Vector3 s,Material m)=>Piece(cube,p,s,m,Quaternion.identity);
        static void Form(Mesh mesh,Vector3 p,Vector3 s,Material m)=>Piece(mesh,p,s,m,Quaternion.identity);
        static void Beam(Vector3 a,Vector3 b,float diameter,Material m){var d=b-a;Piece(taper,(a+b)*.5f,new Vector3(diameter,d.magnitude,diameter),m,Quaternion.FromToRotation(Vector3.up,d.normalized));}
        static Mesh ModelMesh(string name)
        {
            var mesh=AssetDatabase.LoadAllAssetsAtPath(Root+"/Models/Courtyard_Refined.fbx").OfType<Mesh>().FirstOrDefault(m=>m.name==name);
            if(!mesh)throw new InvalidOperationException("Import the Blender mesh "+name+" first.");return mesh;
        }
        static void Initialize()
        {
            stone=Palette("Warm sandstone","C9C2B2","60788B");
            darkStone=Palette("Foundation slate","788A91","354F65");
            plaster=Palette("Ivory plaster","E8DFC8","668197");
            red=Palette("Vermilion lacquer","A45137","542F32");
            timber=Palette("Old honey timber","88633E","393E43");
            roofMat=Palette("Ink blue roof","3F5363","253D52");
            gold=Palette("Quiet ochre","C7A568","74664E");
            paper=Palette("Warm lantern paper","F2D6A1","D4B985");
            leafRed=Palette("Maple copper","BA694A","704A42");
            leafOrange=Palette("Maple peach","CD9661","90644B");
            leafDark=Palette("Maple brick","97513F","593D3D");
            pine=Palette("Pine dark teal","42665F","2D484B");
            pineLight=Palette("Pine sage","6F8871","425D5C");
            moss=Palette("Garden dry sage","8B946F","516C6E");
            mountain=Palette("Far blue stone","819DA8","667F90");
            ink=AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root+"/Materials/Ink.mat");
            detailInk=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Ink_Detail.mat");
            if(!detailInk){detailInk=new Material(ink){name="Fine detail ink"};detailInk.SetFloat("_OutlineWidth",.0015f);detailInk.SetFloat("_MinOutlinePixels",0);AssetDatabase.CreateAsset(detailInk,Root+"/Materials/Ink_Detail.mat");}
            cube=Shape("Block",()=>{
                var pb=ShapeGenerator.GenerateCube(PivotLocation.Center,Vector3.one);
                var mesh=UnityEngine.Object.Instantiate(pb.GetComponent<MeshFilter>().sharedMesh);
                UnityEngine.Object.DestroyImmediate(pb.gameObject);
                return mesh;
            });
            column=Shape("Column12",()=>Frustum(.5f,.5f,12));
            taper=Shape("Taper12",()=>Frustum(.5f,.26f,12));
            roof=Shape("Lifted eave roof",RoofMesh);
            foliage=Shape("Hand cut foliage",()=>Lump(16,true));
            rock=Shape("Faceted garden stone",()=>Lump(9,false));
            cloth=Shape("Banner cloth",BannerMesh);
            ring=Shape("Open circular crest",RingMesh);
            ridge=Shape("Distant mountain ridge",RidgeMesh);
            crest=ModelMesh("Crest_Continuous");
            lattice=ModelMesh("Window_Lattice_Continuous");
        }

        static Material Palette(string name,string lit,string shade)
        {
            string path=Root+"/Materials/"+name.Replace(' ','_')+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m)return m;
            m=new Material(Shader.Find("OC Design/Comic Lighting/Surface")){name=name,enableInstancing=true};
            m.SetColor("_BaseColor",Hex(lit));m.SetColor("_ShadowColor",Hex(shade));
            m.SetFloat("_Threshold",.16f);m.SetFloat("_Feather",.025f);m.SetFloat("_PaperStrength",.035f);
            m.SetFloat("_Cull",0);
            AssetDatabase.CreateAsset(m,path);return m;
        }

        static Mesh Shape(string name,Func<Mesh> create)
        {
            string path=Root+"/Meshes/Shapes/"+name.Replace(' ','_')+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
            mesh=create();mesh.name=name;AssetDatabase.CreateAsset(mesh,path);return mesh;
        }


        static void Foundation(bool upper)
        {
            float top=upper?1.05f:-.34f;
            Unit("01 Stone body - 石基主体",new Vector3(0,top-1.5f,0),()=>Block(new Vector3(0,top-1.5f,0),new Vector3(24.3f,3,24.3f),darkStone));
            Unit("02 Foot plinth - 底座",new Vector3(0,top-3.1f,0),()=>Block(new Vector3(0,top-3.1f,0),new Vector3(26,.25f,26),stone));
            Unit("03 Top ledge - 顶沿",new Vector3(0,top-.12f,0),()=>Block(new Vector3(0,top-.12f,0),new Vector3(24.6f,.24f,24.6f),stone));
            foreach(int side in new[]{0,1,2,3})Unit("04 Stone courses "+side+" - 分侧石缝",Vector3.zero,()=>{
                for(int level=0;level<3;level++){float y=top-.5f-level*.9f;float s=side%2==0?1:-1;if(side<2)Block(new Vector3(0,y,s*12.17f),new Vector3(24.2f,.06f,.04f),stone);else Block(new Vector3(s*12.17f,y,0),new Vector3(.04f,.06f,24.2f),stone);}});
        }
        static void WallCaps(Transform upper,Transform lower)
        {
            var old=Find(dressing,"03 Cream wall coping");var states=Capture(old);
            var floor=GameObject.Find("Floor");var sources=floor.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.Contains("Wall")||r.name.Contains("Pillar")).ToArray();
            foreach(var r in sources)
            {
                var b=r.bounds;bool pillar=r.name.Contains("Pillar");if(!pillar&&(b.size.y<1.3f||b.size.y>1.5f))continue;
                var area=b.center.x<-13?upper:lower;var folder=Folder(Folder(area,"01 Structure - 场地装饰"),"Wall coping - 逐段墙帽");
                string name=r.name+" - 独立压顶";
                Group(name,folder,b.center,()=>{
                    // This migration intentionally aligns each cap to its own current wall, fixing the old cross-court move.
                    legacy=new Dictionary<string,LegacySurface>();foreach(var pair in states){pair.Value.matrix=Matrix4x4.identity;legacy[pair.Key]=pair.Value;}
                    var p=group.InverseTransformPoint(new Vector3(b.center.x,b.max.y+.065f,b.center.z));
                    Unit("01 Coping stone - 压顶石",p,()=>Block(p,new Vector3(b.size.x+.18f,.13f,b.size.z+.18f),stone));
                    if(pillar){var band=group.InverseTransformPoint(new Vector3(b.center.x,b.max.y-.15f,b.center.z));Unit("02 Pillar band - 柱头饰带",band,()=>Block(band,new Vector3(b.size.x+.08f,.13f,b.size.z+.08f),gold));}
                },true);
            }
            if(old){Undo.DestroyObjectImmediate(old.gameObject);migrated++;}
        }
        static void Hall(float width,float depth)
        {
            Unit("01 Foundation - 殿基",Vector3.zero,()=>{Block(new Vector3(0,-.22f,0),new Vector3(width+1.3f,.65f,depth+1.7f),darkStone);Block(new Vector3(0,.12f,0),new Vector3(width+1.5f,.18f,depth+1.9f),stone);});
            Unit("02 Main walls - 墙身",new Vector3(0,2.1f,.5f),()=>Block(new Vector3(0,2.1f,.5f),new Vector3(width,3.8f,depth-.8f),plaster));
            Unit("03 Front beams - 门楣与槛",new Vector3(0,2.4f,-depth*.5f),()=>{Block(new Vector3(0,.55f,-depth*.5f+.45f),new Vector3(width,.48f,.28f),timber);Block(new Vector3(0,4.35f,-depth*.5f),new Vector3(width+1.1f,.35f,.42f),red);});
            int bays=width>12?6:4;
            for(int i=0;i<=bays;i++)
            {
                float x=-width*.47f+i*width*.94f/bays;
                Unit("Column "+(i+1)+" - 檐柱",new Vector3(x,2.23f,-depth*.5f),()=>{Form(column,new Vector3(x,2.23f,-depth*.5f),new Vector3(.3f,4.25f,.3f),red);Block(new Vector3(x,.35f,-depth*.5f),new Vector3(.52f,.32f,.52f),stone);Block(new Vector3(x,4.13f,-depth*.5f),new Vector3(.62f,.12f,.55f),gold);});
                if(i<bays)
                {
                    float bx=x+width*.47f/bays;
                    Unit("Window "+(i+1)+" - 纸窗",new Vector3(bx,2.25f,-depth*.5f+.4f),()=>{
                        Unit("01 Back frame - 窗框",new Vector3(bx,2.2f,-depth*.5f+.5f),()=>Block(new Vector3(bx,2.2f,-depth*.5f+.5f),new Vector3(width*.83f/bays,2.65f,.14f),roofMat));
                        Unit("02 Paper - 窗纸",new Vector3(bx,2.25f,-depth*.5f+.4f),()=>Block(new Vector3(bx,2.25f,-depth*.5f+.4f),new Vector3(width*.75f/bays,2.35f,.08f),paper));
                        Unit("03 Continuous lattice - 一体窗棂",new Vector3(bx,2.25f,-depth*.5f+.32f),()=>Form(lattice,new Vector3(bx,2.25f,-depth*.5f+.32f),new Vector3(width*.77f/bays/1.8f,1,1),timber));
                    });
                    Unit("Bracket "+(i+1)+" - 斜撑",new Vector3(x+.325f,3.95f,-depth*.5f),()=>Beam(new Vector3(x,3.6f,-depth*.5f),new Vector3(x+.65f,4.3f,-depth*.5f),.12f,gold));
                }
            }
            Unit("04 Roof - 屋顶",new Vector3(0,4.25f,0),()=>Form(roof,new Vector3(0,4.25f,0),new Vector3(width+3.5f,2.35f,depth+3.5f),roofMat));
            Unit("05 Ridge - 屋脊",new Vector3(0,6.62f,0),()=>Block(new Vector3(0,6.62f,0),new Vector3(width+2.2f,.16f,.22f),gold));
            foreach(int sign in new[]{-1,1})
            {
                Unit((sign<0?"Left":"Right")+" ridge end - 脊饰",new Vector3(sign*(width*.5f+.25f),6.75f,0),()=>Block(new Vector3(sign*(width*.5f+.25f),6.75f,0),new Vector3(.18f,.42f,.22f),red));
                var p=new Vector3(sign*width*.30f,3.55f,-depth*.5f-.15f);Unit((sign<0?"Left":"Right")+" hanging lantern - 吊灯",p,()=>HangingLantern(p,.85f));
            }
        }
        static void Gate(float width,float height)
        {
            foreach(int sign in new[]{-1,1})
            {
                float x=sign*(width*.5f-.65f);
                Unit((sign<0?"01 Left":"02 Right")+" post - 门柱",new Vector3(x,height*.5f,0),()=>{Form(taper,new Vector3(x,height*.5f,0),new Vector3(.57f,height,.57f),red);Block(new Vector3(x,.24f,0),new Vector3(.9f,.48f,.95f),stone);Block(new Vector3(x,1f,0),new Vector3(.62f,.16f,.64f),roofMat);Block(new Vector3(x,height-.75f,0),new Vector3(.75f,.2f,.75f),gold);});
                var p=new Vector3(sign*width*.27f,height-1.5f,0);Unit((sign<0?"Left":"Right")+" hanging lantern - 吊灯",p,()=>HangingLantern(p,1));
            }
            Unit("03 Lintel - 横梁",new Vector3(0,height-.65f,0),()=>Block(new Vector3(0,height-.65f,0),new Vector3(width,.3f,.46f),red));
            Unit("04 Eave beam - 檐梁",new Vector3(0,height+.02f,0),()=>Block(new Vector3(0,height+.02f,0),new Vector3(width+1,.3f,.6f),timber));
            Unit("05 Roof - 门檐",new Vector3(0,height,0),()=>Form(roof,new Vector3(0,height,0),new Vector3(width+1.6f,.5f,1.6f),roofMat));
            Unit("06 Plaque - 门额",new Vector3(0,height-.9f,-.3f),()=>Block(new Vector3(0,height-.9f,-.3f),new Vector3(.7f,.85f,.14f),gold));
            Unit("07 Crest - 一体纹章",new Vector3(0,height-.9f,-.39f),()=>Crest(new Vector3(0,height-.9f,-.39f),.3f,paper));
        }
        static void HangingLantern(Vector3 p,float scale)
        {
            Unit("01 Paper body - 纸筒",p,()=>Form(column,p,new Vector3(.62f*scale,.8f*scale,.62f*scale),paper));
            Unit("02 Rims and ribs - 筒缘与骨架",p,()=>{foreach(float y in new[]{-.4f,.4f})Form(column,p+Vector3.up*y*scale,new Vector3(.7f*scale,.08f*scale,.7f*scale),red);for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Block(p+new Vector3(Mathf.Cos(a)*.31f*scale,0,Mathf.Sin(a)*.31f*scale),new Vector3(.025f,.77f*scale,.025f),gold);}});
            Unit("03 Hanger - 吊杆",p+Vector3.up*.62f*scale,()=>Block(p+Vector3.up*.62f*scale,new Vector3(.035f,.43f*scale,.035f),timber));
        }
        static void StoneLantern()
        {
            Unit("01 Foot - 石座",new Vector3(0,.2f,0),()=>{Block(new Vector3(0,.12f,0),new Vector3(1.1f,.24f,1.1f),darkStone);Block(new Vector3(0,.32f,0),new Vector3(.88f,.16f,.88f),stone);});
            Unit("02 Column - 灯柱",new Vector3(0,.9f,0),()=>Form(taper,new Vector3(0,.9f,0),new Vector3(.48f,1.05f,.48f),stone));
            Unit("03 Chamber floor - 灯室底托",new Vector3(0,1.45f,0),()=>Block(new Vector3(0,1.45f,0),new Vector3(1,.15f,1),darkStone));
            Unit("04 Paper chamber - 灯室",new Vector3(0,1.83f,0),()=>Block(new Vector3(0,1.83f,0),new Vector3(.61f,.65f,.61f),paper));
            Unit("05 Chamber frame - 灯室框",new Vector3(0,1.83f,0),()=>{foreach(float x in new[]{-.36f,.36f})foreach(float z in new[]{-.36f,.36f})Block(new Vector3(x,1.83f,z),new Vector3(.1f,.76f,.1f),stone);});
            Unit("06 Roof - 灯檐",new Vector3(0,2.13f,0),()=>Form(roof,new Vector3(0,2.13f,0),new Vector3(1.45f,.47f,1.45f),roofMat));
            Unit("07 Finial - 顶饰",new Vector3(0,2.7f,0),()=>Form(taper,new Vector3(0,2.7f,0),new Vector3(.22f,.28f,.22f),gold));
        }
        static void Banner(int index)
        {
            Unit("01 Pole and foot - 旗杆与座",new Vector3(0,2.95f,0),()=>{Form(column,new Vector3(0,2.95f,0),new Vector3(.10f,5.9f,.10f),timber);Form(rock,new Vector3(0,.15f,0),new Vector3(.55f,.3f,.55f),stone);});
            Unit("02 Crossbar - 横杆",new Vector3(.52f,5.7f,0),()=>Block(new Vector3(.52f,5.7f,0),new Vector3(1.4f,.09f,.09f),gold));
            Unit("03 Cloth - 旗面",new Vector3(.58f,4.18f,0),()=>Form(cloth,new Vector3(.58f,4.18f,0),new Vector3(1.02f,2.8f,1),index%2==0?red:roofMat));
            Unit("04 Crest - 一体纹章",new Vector3(.58f,4.55f,-.13f),()=>Crest(new Vector3(.58f,4.55f,-.13f),.31f,paper));
            Unit("05 Border - 旗边",new Vector3(.58f,4.3f,-.04f),()=>{foreach(float s in new[]{-1f,1f})Block(new Vector3(.58f+s*.46f,4.3f,-.04f),new Vector3(.035f,2.4f,.025f),gold);});
        }
        static void Crest(Vector3 p,float radius,Material m)
        {
            Unit("01 Outer ring - 外环",p,()=>Form(ring,p,Vector3.one*radius,m));
            Unit("02 Continuous symbol - 连续三叉",p,()=>Form(crest,p,Vector3.one*radius,m));
        }
        static void TreeGarden(bool evergreen,int seed)
        {
            Unit("01 Rock bed - 山石地基",new Vector3(0,-.65f,0),()=>Form(rock,new Vector3(0,-.65f,0),new Vector3(7.4f,2.1f,6.2f),darkStone));
            Unit("02 Ground cover - 地被",Vector3.zero,()=>Form(foliage,new Vector3(0,.02f,0),new Vector3(6.6f,.48f,5.4f),moss));
            for(int i=0;i<4;i++){int n=i;float a=(i*1.7f+seed)*1.8f;var p=new Vector3(Mathf.Sin(a)*2.3f,.3f,Mathf.Cos(a)*1.8f);Unit("Stone "+(i+1)+" - 散石",p,()=>Form(rock,p,new Vector3(1.3f+n*.15f,1+n*.1f,1.1f),stone));}
            Unit("03 Trunk - 树干",new Vector3(.15f,1.975f,0),()=>Beam(new Vector3(0,.25f,0),new Vector3(.3f,3.7f,0),.52f,timber));
            for(int i=0;i<6;i++)
            {
                float a=i*2.4f+seed*.8f;float h=3.8f+(i%3)*.7f;var end=new Vector3(Mathf.Sin(a)*(i%2==0?1.7f:1.1f),h,Mathf.Cos(a)*1.4f);
                var m=evergreen?(i%2==0?pine:pineLight):(i%3==0?leafOrange:i%3==1?leafRed:leafDark);
                Unit("Branch "+(i+1)+" - 树枝",(new Vector3(.2f,2.2f,0)+end)*.5f,()=>Beam(new Vector3(.2f,2.2f,0),end,.24f,timber));
                Unit("Canopy "+(i+1)+" - 叶簇",end+Vector3.up*.3f,()=>Form(foliage,end+Vector3.up*.3f,new Vector3(evergreen?3.7f:3.3f,evergreen?.95f:1.4f,2.9f),m));
            }
            Unit("Crown - 树冠",new Vector3(.2f,6,0),()=>Form(foliage,new Vector3(.2f,6,0),new Vector3(2.7f,1.25f,2.5f),evergreen?pine:leafOrange));
        }
        static void CourtCrest()
        {
            Unit("01 Inlaid ring - 地面环纹",Vector3.zero,()=>Piece(ring,Vector3.zero,Vector3.one*3.3f,gold,Quaternion.Euler(90,0,0)));
            Unit("02 Alignment marks - 刻度",Vector3.zero,()=>{for(int i=0;i<8;i++){float a=i*45f*Mathf.Deg2Rad;Piece(cube,new Vector3(Mathf.Sin(a)*3.7f,0,Mathf.Cos(a)*3.7f),new Vector3(.065f,.008f,.3f),gold,Quaternion.Euler(0,i*45,0));}});
        }
        static void Mountains()
        {
            Unit("01 Northern ridge - 北侧山脊",new Vector3(-15,-36,110),()=>Form(ridge,new Vector3(-15,-36,110),new Vector3(300,73,48),mountain));
            Unit("02 Western ridge - 西侧山脊",new Vector3(-117,-40,52),()=>Piece(ridge,new Vector3(-117,-40,52),new Vector3(190,71,40),mountain,Quaternion.Euler(0,-58,0)));
            Unit("03 Eastern ridge - 东侧山脊",new Vector3(95,-41,57),()=>Piece(ridge,new Vector3(95,-41,57),new Vector3(190,68,40),mountain,Quaternion.Euler(0,55,0)));
        }
        static void OrganizeFloor()
        {
            var floor=GameObject.Find("Floor");var original=floor.transform.Cast<Transform>().Where(t=>t.GetComponent<Renderer>()).ToArray();
            foreach(var t in original)
            {
                bool bridge=t.name=="South Wall (3)"||t.name=="South Wall (4)";
                var area=Folder(floor.transform,bridge?"03 Connecting passage - 连接通道":t.position.x<-13?"01 Upper court - 上层场地":"02 Lower court - 下层场地");
                string category=t.name.StartsWith("Grid ")?"04 Legacy grid - 原细网格":t.name.Contains("Pillar")?"03 Pillars - 练习柱":t.name.StartsWith("Training Floor")?"01 Ground - 地面":"02 Walls and ramps - 围墙与坡面";
                var parent=bridge?area:Folder(area,category);Undo.SetTransformParent(t,parent,"Group original court geometry");
            }
            if(original.Length>0)
            {
                var areas=floor.transform.Cast<Transform>().OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
                for(int i=0;i<areas.Length;i++)
                {
                    areas[i].SetSiblingIndex(i);
                    var categories=areas[i].Cast<Transform>().OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
                    for(int j=0;j<categories.Length;j++)categories[j].SetSiblingIndex(j);
                }
            }
        }
        static Mesh RidgeMesh()
        {
            float[] heights={.35f,.48f,.41f,.76f,.55f,.61f,.9f,.73f,.47f,.66f,.51f,.82f,.56f,.42f,.54f};
            var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<heights.Length-1;i++)
            {
                float x0=-.5f+i/(float)(heights.Length-1),x1=-.5f+(i+1)/(float)(heights.Length-1);
                var a=new Vector3(x0,heights[i],.13f+Mathf.Sin(i*2.4f)*.1f);
                var b=new Vector3(x1,heights[i+1],.13f+Mathf.Sin((i+1)*2.4f)*.1f);
                var m0=new Vector3(x0,heights[i]*.41f,-.3f);
                var m1=new Vector3(x1,heights[i+1]*.41f,-.3f);
                Quad(v,t,new Vector3(x0,0,-.5f),new Vector3(x1,0,-.5f),m1,m0);
                Quad(v,t,m0,m1,b,a);
                Quad(v,t,a,b,new Vector3(x1,0,.5f),new Vector3(x0,0,.5f));
            }
            return MeshOf(v,t);
        }

        static Mesh Frustum(float bottom,float top,int sides)
        {
            var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<sides;i++)
            {
                float a=i*2*Mathf.PI/sides,b=(i+1)*2*Mathf.PI/sides;
                Quad(v,t,new Vector3(Mathf.Sin(a)*bottom,-.5f,Mathf.Cos(a)*bottom),new Vector3(Mathf.Sin(b)*bottom,-.5f,Mathf.Cos(b)*bottom),new Vector3(Mathf.Sin(b)*top,.5f,Mathf.Cos(b)*top),new Vector3(Mathf.Sin(a)*top,.5f,Mathf.Cos(a)*top));
                Tri(v,t,new Vector3(0,-.5f,0),new Vector3(Mathf.Sin(b)*bottom,-.5f,Mathf.Cos(b)*bottom),new Vector3(Mathf.Sin(a)*bottom,-.5f,Mathf.Cos(a)*bottom));
                Tri(v,t,new Vector3(0,.5f,0),new Vector3(Mathf.Sin(a)*top,.5f,Mathf.Cos(a)*top),new Vector3(Mathf.Sin(b)*top,.5f,Mathf.Cos(b)*top));
            }
            return MeshOf(v,t);
        }

        static Mesh RoofMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();
            Func<float,float,Vector3> point=(x,z)=>new Vector3(x,1-.82f*Mathf.Pow(Mathf.Abs(z*2),.75f)+.12f*Mathf.Pow(Mathf.Abs(z*2),8)+.07f*Mathf.Pow(Mathf.Abs(x*2),6)*Mathf.Abs(z*2),z);
            const int nx=8,nz=10;
            for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
            {
                float x0=-.5f+x/(float)nx,x1=-.5f+(x+1)/(float)nx,z0=-.5f+z/(float)nz,z1=-.5f+(z+1)/(float)nz;
                var a=point(x0,z0);var b=point(x0,z1);var c=point(x1,z1);var d=point(x1,z0);
                Quad(v,t,a,b,c,d);
                Quad(v,t,d-Vector3.up*.055f,c-Vector3.up*.055f,b-Vector3.up*.055f,a-Vector3.up*.055f);
                if(x==0)Quad(v,t,a-Vector3.up*.055f,b-Vector3.up*.055f,b,a);
                if(x==nx-1)Quad(v,t,d,c,c-Vector3.up*.055f,d-Vector3.up*.055f);
                if(z==0)Quad(v,t,a,d,d-Vector3.up*.055f,a-Vector3.up*.055f);
                if(z==nz-1)Quad(v,t,b-Vector3.up*.055f,c-Vector3.up*.055f,c,b);
            }
            return MeshOf(v,t);
        }

        static Mesh Lump(int sides,bool leaves)
        {
            var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<sides;i++)
            {
                Func<int,float,Vector3> p=(k,level)=>{
                    float a=k*2*Mathf.PI/sides;
                    float radius=(k%2==0?1:.86f)*(.46f+.04f*Mathf.Sin(k*3.7f));
                    return new Vector3(Mathf.Sin(a)*radius,level+Mathf.Sin(k*2.1f)*.07f,Mathf.Cos(a)*radius);
                };
                var a0=p(i,0);var b0=p(i+1,0);var a1=Vector3.Scale(p(i,.34f),new Vector3(.65f,1,.65f));var b1=Vector3.Scale(p(i+1,.34f),new Vector3(.65f,1,.65f));
                Quad(v,t,a0,b0,b1,a1);
                Tri(v,t,a1,b1,new Vector3(.04f,.54f,-.03f));
                Tri(v,t,b0,a0,new Vector3(0,leaves?-.18f:-.45f,0));
            }
            return MeshOf(v,t);
        }

        static Mesh BannerMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();
            Func<float,float,Vector3> p=(x,y)=>new Vector3(x,y,.08f*Mathf.Sin(y*7+x*3));
            for(int i=0;i<6;i++)
            {
                float y0=-.4f+i*.9f/6,y1=-.4f+(i+1)*.9f/6;
                Quad(v,t,p(-.5f,y0),p(.5f,y0),p(.5f,y1),p(-.5f,y1));
            }
            Tri(v,t,p(-.5f,-.4f),p(0,-.3f),p(-.5f,-.5f));
            Tri(v,t,p(0,-.3f),p(.5f,-.4f),p(.5f,-.5f));
            return MeshOf(v,t);
        }

        static Mesh RingMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<64;i++)
            {
                float a=i*Mathf.PI/32,b=(i+1)*Mathf.PI/32;
                Quad(v,t,new Vector3(Mathf.Sin(a),Mathf.Cos(a),0),new Vector3(Mathf.Sin(b),Mathf.Cos(b),0),new Vector3(Mathf.Sin(b)*.97f,Mathf.Cos(b)*.97f,0),new Vector3(Mathf.Sin(a)*.97f,Mathf.Cos(a)*.97f,0));
            }
            return MeshOf(v,t);
        }

        static void Tri(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c)
        {
            int n=v.Count;v.Add(a);v.Add(b);v.Add(c);t.Add(n);t.Add(n+1);t.Add(n+2);
        }
        static void Quad(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            Tri(v,t,a,b,c);Tri(v,t,a,c,d);
        }
        static Mesh MeshOf(List<Vector3> v,List<int> t)
        {
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static Color Hex(string h){ColorUtility.TryParseHtmlString("#"+h,out var c);return c;}
    }
}
