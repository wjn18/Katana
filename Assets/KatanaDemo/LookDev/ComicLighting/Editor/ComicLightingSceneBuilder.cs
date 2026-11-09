using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OCDesign.LookDev.Editor
{
    /// <summary>Creates the user-requested standalone art-direction sample. Never edits source prefabs.</summary>
    public static class ComicLightingSceneBuilder
    {
        public const string Root = "Assets/KatanaDemo/LookDev/ComicLighting";
        public const string ScenePath = Root + "/Scenes/ComicLightingCourtyard.unity";
        static Material ink, stone, blue, red, timber, dark, brass;
        static Mesh cube, cubeHull;
        static Transform architecture;
        static readonly Dictionary<Material, Material> characterMaterials = new Dictionary<Material, Material>();

        [MenuItem("Katana/LookDev/Open Comic Lighting Courtyard")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before opening the sample.");
            if (!File.Exists(ScenePath)) { Build(); return; }
            AssertSavedScenes();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Frame();
        }

        static void AssertSavedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) throw new InvalidOperationException("Existing unsaved scene preserved; save a recovery copy before switching: " + scene.path);
            }
        }

        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Build in Edit mode.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Sample already exists; open it without rebuilding user edits.");
            AssertSavedScenes();
            if (!Shader.Find("OC Design/Comic Lighting/Surface")) throw new InvalidOperationException("Comic shader has not imported.");
            foreach (string folder in new[] { "Scenes", "Materials", "Meshes", "Animation", "Textures" })
                Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            var sample = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            characterMaterials.Clear();
            ink = new Material(Shader.Find("OC Design/Comic Lighting/Outline")) { name = "Ink outline" };
            ink.SetColor("_OutlineColor", Hex("302923")); ink.SetFloat("_OutlineWidth", .022f);
            ink.SetFloat("_MinOutlinePixels",3f);
            AssetDatabase.CreateAsset(ink, Root + "/Materials/Ink.mat");
            stone = Palette("Warm limestone", "EBE3DD", "365777", .025f);
            blue = Palette("Indigo plaster", "426783", "2C455F", .035f);
            red = Palette("Vermilion timber", "9E5033", "54352F", .035f);
            timber = Palette("Honey wood", "B48A52", "69513E", .055f);
            dark = Palette("Dark roof", "4C5660", "283A4D", .02f);
            brass = Palette("Ochre accent", "C69D52", "6C684B", .015f);
            var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, Vector3.one);
            cube = UnityEngine.Object.Instantiate(pb.GetComponent<MeshFilter>().sharedMesh); cube.name = "Editable block source";
            AssetDatabase.CreateAsset(cube, Root + "/Meshes/Block.asset");
            UnityEngine.Object.DestroyImmediate(pb.gameObject);
            cubeHull = UnityEngine.Object.Instantiate(cube); cubeHull.name = "Block smooth outline hull";
            cubeHull.normals = cubeHull.vertices.Select(v => v.normalized).ToArray();
            AssetDatabase.CreateAsset(cubeHull, Root + "/Meshes/BlockOutline.asset");
            architecture = new GameObject("01  Courtyard architecture").transform;

            // One ground surface with filtered broad paving seams avoids thin coplanar tile artifacts.
            var paving=new Material(stone){name="Courtyard paving"};
            paving.SetColor("_BaseColor",Hex("E7DDDA")); paving.SetFloat("_PaperStrength",.035f);
            paving.SetFloat("_PavingStrength",1); paving.SetFloat("_PavingWobble",.008f);
            AssetDatabase.CreateAsset(paving,Root+"/Materials/Courtyard_paving.mat");
            Block("Foundation", new Vector3(0,-.191f,3), new Vector3(120,.4f,120), paving, false);
            Block("Rear indigo wall", new Vector3(0,2.1f,11), new Vector3(20,4.2f,.5f), blue);
            Block("Rear stone base", new Vector3(0,.3f,10.65f), new Vector3(20,.6f,.35f), stone);
            Block("Rear cream cornice", new Vector3(0,4.2f,11), new Vector3(20.4f,.22f,.9f), stone);
            for (int x=-9;x<=9;x+=3)
            {
                Block("Rear pilaster",new Vector3(x,2.1f,10.65f),new Vector3(.22f,4.2f,.25f),stone);
                if(x!=0) Block("Inset dark shutter",new Vector3(x,2.7f,10.37f),new Vector3(.95f,1.25f,.12f),dark);
            }
            for (int y=1;y<=5;y++)
                Block("Rear masonry seam",new Vector3(0,y*.64f,10.735f),new Vector3(19.8f,.018f,.018f),blue,false);
            // Right return creates a large readable cool plane, left gate creates long diagonal shadows.
            Block("Right return",new Vector3(8.6f,1.8f,6.6f),new Vector3(.55f,3.6f,8.8f),blue);
            Block("Right coping",new Vector3(8.6f,3.66f,6.6f),new Vector3(.9f,.18f,9.1f),stone);
            Block("Right low trim",new Vector3(8.28f,.35f,6.6f),new Vector3(.18f,.65f,8.8f),stone);
            for(int z=3;z<=10;z+=3)
                Block("Right trim post",new Vector3(8.27f,1.85f,z),new Vector3(.16f,3.5f,.21f),stone);

            for(int i=0;i<8;i++)
            {
                Block("Stair " + (i+1),new Vector3(4.5f,.14f*(i+1),5.0f+i*.48f),new Vector3(3.5f,.28f*(i+1),.48f),stone);
                Block("Stair edge " + (i+1),new Vector3(4.5f,.28f*(i+1)-.012f,4.755f+i*.48f),new Vector3(3.5f,.026f,.025f),dark,false);
            }
            Block("Upper landing",new Vector3(4.5f,1.05f,9.4f),new Vector3(3.6f,2.1f,2.0f),stone);
            foreach(float x in new[]{2.6f,6.4f})
            {
                for(int i=0;i<4;i++) Block("Stair rail upright",new Vector3(x,1f+i*.48f,5.1f+i*.98f),new Vector3(.12f,1.35f,.12f),red);
                var rail=Block("Stair hand rail",new Vector3(x,2.35f,6.6f),new Vector3(.15f,.15f,4.15f),red);
                rail.transform.rotation=Quaternion.Euler(-29,0,0);
            }
            foreach(float z in new[]{-.3f,4.1f})
            {
                foreach(float x in new[]{-7.0f,-3.8f})
                {
                    Block("Gate stone foot",new Vector3(x,.22f,z),new Vector3(.85f,.44f,.85f),stone);
                    Block("Vermilion column",new Vector3(x,2.25f,z),new Vector3(.36f,4.15f,.36f),red);
                    Block("Column dark collar",new Vector3(x,.7f,z),new Vector3(.43f,.15f,.43f),dark);
                }
                Block("Gate lintel",new Vector3(-5.4f,4.33f,z),new Vector3(4.4f,.32f,.48f),red);
                Block("Gate roof edge",new Vector3(-5.4f,4.57f,z),new Vector3(4.8f,.15f,.65f),dark);
            }
            foreach(float x in new[]{-7f,-3.8f})
                Block("Pergola side beam",new Vector3(x,4.05f,1.9f),new Vector3(.25f,.25f,5.3f),red);
            for(int i=0;i<6;i++) Block("Pergola rafter",new Vector3(-5.4f,4.43f,-.4f+i*.9f),new Vector3(3.65f,.1f,.13f),timber);
            // A low bench and timber boxes provide several differently oriented surfaces.
            Block("Bench seat",new Vector3(-5.5f,.65f,4.5f),new Vector3(2.8f,.18f,.8f),timber);
            foreach(float x in new[]{-6.5f,-4.5f}) Block("Bench support",new Vector3(x,.29f,4.5f),new Vector3(.22f,.58f,.6f),red);
            Crate(new Vector3(5.8f,.52f,.3f),1.04f);
            Crate(new Vector3(6.8f,.36f,1.5f),.72f);
            Crate(new Vector3(-7.4f,.45f,7f),.9f);
            // Pale distant silhouettes keep the horizon quiet.
            Block("Distant cream tower",new Vector3(-10,3,18),new Vector3(3,6,3),stone);
            Block("Distant blue tower",new Vector3(8,3.7f,20),new Vector3(2.3f,7.4f,3.2f),blue);
            Block("Tower cornice",new Vector3(8,7.4f,20),new Vector3(2.7f,.22f,3.6f),stone);
            BuildHorizon();

            var characters=new GameObject("02  Existing characters - preview material overrides").transform;
            Character("Samurai", "Assets/KatanaDemo/SamuraiCombat/Prefabs/SamuraiCombatPlayer.prefab", "Assets/KatanaDemo/SamuraiCombat/Animations/Samurai_Idle.anim",new Vector3(-.9f,0,1.2f),155,characters);
            Character("Enemy1", "Assets/KatanaDemo/Characters/Enemy1/Prefabs/Enemy1.prefab", "Assets/KatanaDemo/Characters/Enemy1/Animations/Generic/Combat/Enemy1_Idle.anim",new Vector3(2f,0,2.4f),165,characters);
            Character("Elite1", "Assets/KatanaDemo/Characters/Elite1/Prefabs/Elite1.prefab", "Assets/KatanaDemo/Characters/Elite1/Animations/Humanoid/Combat/Elite1_Idle.anim",new Vector3(-1.7f,0,6.5f),150,characters);

            var lightGO=new GameObject("03  Sun - warm directional / blue palette shadows");
            var sun=lightGO.AddComponent<Light>(); sun.type=LightType.Directional; sun.color=Color.white; sun.intensity=1;
            sun.shadows=LightShadows.Soft; sun.shadowStrength=1; sun.shadowBias=.03f; sun.shadowNormalBias=.18f;
            sun.transform.rotation=Quaternion.Euler(35,-65,0);
            lightGO.AddComponent<UniversalAdditionalLightData>().softShadowQuality=SoftShadowQuality.Low;
            RenderSettings.sun=sun; RenderSettings.skybox=null; RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=Hex("738395"); RenderSettings.ambientIntensity=.3f; RenderSettings.fog=false; RenderSettings.reflectionIntensity=0;
            var cameraGO=new GameObject("04  Preview Camera"); cameraGO.tag="MainCamera";
            var camera=cameraGO.AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.Skybox; camera.backgroundColor=Hex("746B80");
            camera.fieldOfView=55; camera.nearClipPlane=.08f; camera.farClipPlane=180; camera.allowHDR=true;
            camera.transform.position=new Vector3(8.2f,3.3f,-7.5f); camera.transform.LookAt(new Vector3(-.5f,1.7f,4));
            cameraGO.AddComponent<AudioListener>();
            var data=cameraGO.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality=AntialiasingQuality.High;
            var viewer=cameraGO.AddComponent<ComicLightingViewer>(); viewer.viewCamera=camera; viewer.sun=sun.transform;
            viewer.focusPoint=new Vector3(-.5f,1.7f,4); viewer.closePosition=new Vector3(5,2.6f,-4.5f); viewer.closeFocus=new Vector3(.1f,1.2f,2);
            viewer.paletteMaterials=new[]{stone,blue,red,timber,dark,brass};
            // Local profile overrides the project defaults without editing the shared profile or pipeline.
            var profile=ScriptableObject.CreateInstance<VolumeProfile>(); profile.name="Comic neutral grading";
            var tone=profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.None);
            var bloom=profile.Add<Bloom>(true); bloom.intensity.Override(0);
            var vignette=profile.Add<Vignette>(true); vignette.intensity.Override(0);
            var dof=profile.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Off);
            var motion=profile.Add<MotionBlur>(true); motion.intensity.Override(0);
            var colors=profile.Add<ColorAdjustments>(true); colors.postExposure.Override(0); colors.contrast.Override(0); colors.saturation.Override(0); colors.colorFilter.Override(Color.white);
            AssetDatabase.CreateAsset(profile,Root+"/Materials/ComicVolume.asset");
            foreach(var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            var vol=new GameObject("05  Local neutral color grading").AddComponent<Volume>(); vol.isGlobal=true; vol.priority=100; vol.sharedProfile=profile;
            EditorSceneManager.SaveScene(sample,ScenePath);
            ComicCloudVolumeSetup.Install();
            ComicOutlineRepairSetup.Install();
            EditorSceneManager.SaveScene(sample,ScenePath); Frame();
            return "Created " + ScenePath + " with " + sample.GetRootGameObjects().Length + " roots.";
        }

        static Material StormSkyMaterial()
        {
            string path=Root+"/Materials/ComicStormSky.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material) return material; // Preserve subsequent art-direction tweaks.
            var shader=Shader.Find("OC Design/Comic Lighting/Storm Sky");
            if(!shader) throw new InvalidOperationException("Comic storm sky shader has not imported.");
            material=new Material(shader){name="Comic Storm Sky"};
            var panorama=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/ComicStormSky.png");
            if(!panorama) throw new InvalidOperationException("Comic storm sky panorama has not imported.");
            material.SetTexture("_SkyMap",panorama);
            AssetDatabase.CreateAsset(material,path);
            return material;
        }

        public static void ApplyStormSky()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=ScenePath)
                throw new InvalidOperationException("Open the comic sample in Edit mode first.");
            var camera=GameObject.Find("04  Preview Camera").GetComponent<Camera>();
            var material=StormSkyMaterial();
            Undo.RecordObject(camera,"Apply comic storm sky");
            camera.clearFlags=CameraClearFlags.Skybox;
            RenderSettings.skybox=material;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // Explicit one-off update of the sample. Open() never reapplies these values.
        public static void ApplySkyGroundSeparation()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=ScenePath)
                throw new InvalidOperationException("Open the saved comic sample in Edit mode first.");
            var camera=GameObject.Find("04  Preview Camera").GetComponent<Camera>();
            Undo.RecordObject(camera,"Separate sky and ground"); camera.backgroundColor=Hex("EADDCC");
            var paving=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Courtyard_paving.mat");
            Undo.RecordObject(paving,"Neutral paving highlights");
            paving.SetColor("_BaseColor",Hex("E7DDDA")); paving.SetFloat("_PaperStrength",.035f);
            EditorUtility.SetDirty(paving); AssetDatabase.SaveAssetIfDirty(paving);
            stone=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Warm_limestone.mat");
            Undo.RecordObject(stone,"Pale limestone highlights"); stone.SetColor("_BaseColor",Hex("EBE3DD"));
            EditorUtility.SetDirty(stone); AssetDatabase.SaveAssetIfDirty(stone);
            var foundation=GameObject.Find("Foundation").transform;
            Undo.RecordObject(foundation,"Extend distant ground"); foundation.localScale=new Vector3(120,.4f,120);
            architecture=GameObject.Find("01  Courtyard architecture").transform;
            cube=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Block.asset");
            cubeHull=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/BlockOutline.asset");
            ink=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Ink.mat");
            BuildHorizon(); EditorSceneManager.MarkSceneDirty(scene);
        }

        static void BuildHorizon()
        {
            if(GameObject.Find("06  Pale distant horizon")) return;
            var originalParent=architecture;
            var root=new GameObject("06  Pale distant horizon");
            Undo.RegisterCreatedObjectUndo(root,"Add distant horizon"); architecture=root.transform;
            var far=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Distant_limestone.mat");
            if(!far) far=Palette("Distant limestone","EBE3DF","CEC3C7",.012f);
            far.SetFloat("_Threshold",.5f);
            EditorUtility.SetDirty(far); AssetDatabase.SaveAssetIfDirty(far);
            var originalInk=ink;
            var farInk=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Distant_ink.mat");
            if(!farInk)
            {
                farInk=new Material(ink){name="Distant ink"};
                farInk.SetColor("_OutlineColor",Hex("675956")); farInk.SetFloat("_OutlineWidth",.12f);
                farInk.SetFloat("_MinOutlinePixels",3f);
                AssetDatabase.CreateAsset(farInk,Root+"/Materials/Distant_ink.mat");
            }
            ink=farInk;
            // A low continuous border and sparse faceted mounds give the pale sky
            // a drawn silhouette, without fog or a dark mountain backdrop.
            Block("Far rear ridge",new Vector3(0,.65f,51),new Vector3(106,1.3f,3),far);
            Block("Far left ridge",new Vector3(-51,.65f,0),new Vector3(3,1.3f,106),far);
            Block("Far right ridge",new Vector3(51,.65f,0),new Vector3(3,1.3f,106),far);
            Block("Far front ridge",new Vector3(0,.65f,-51),new Vector3(106,1.3f,3),far);
            var peak=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/DistantPeak.asset");
            if(!peak)
            {
                var pb=ShapeGenerator.GenerateCone(PivotLocation.Center,1,1,4);
                peak=UnityEngine.Object.Instantiate(pb.GetComponent<MeshFilter>().sharedMesh);
                peak.name="Four-sided distant mound"; AssetDatabase.CreateAsset(peak,Root+"/Meshes/DistantPeak.asset");
                UnityEngine.Object.DestroyImmediate(pb.gameObject);
            }
            var locations=new[]{new Vector3(-30,0,22),new Vector3(-18,0,40),new Vector3(18,0,42),new Vector3(-40,0,-20),new Vector3(40,0,-16),new Vector3(9,0,-42)};
            var sizes=new[]{new Vector3(11,5,9),new Vector3(9,3.8f,8),new Vector3(12,4.5f,10),new Vector3(13,4,10),new Vector3(10,4.2f,9),new Vector3(12,4,8)};
            for(int i=0;i<locations.Length;i++)
            {
                var go=new GameObject("Pale faceted mound "+(i+1)); go.transform.SetParent(architecture,false);
                go.transform.localScale=sizes[i]; go.transform.localPosition=locations[i]+Vector3.up*(-peak.bounds.min.y*sizes[i].y+.015f);
                go.transform.localRotation=Quaternion.Euler(0,15+i*17,0);
                go.AddComponent<MeshFilter>().sharedMesh=peak; var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=far;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                var shell=new GameObject("Distant ink contour"); shell.transform.SetParent(go.transform,false);
                shell.AddComponent<MeshFilter>().sharedMesh=peak; var contour=shell.AddComponent<MeshRenderer>(); contour.sharedMaterial=ink;
                contour.shadowCastingMode=ShadowCastingMode.Off; contour.receiveShadows=false;
            }
            architecture=originalParent; ink=originalInk;
        }

        static Color Hex(string text) { ColorUtility.TryParseHtmlString("#"+text,out var c); return c; }
        static Material ArchitecturalInk(Material source)
        {
            string sourceName=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source));
            string path=Root+"/Materials/"+sourceName+"_Architecture.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)
            {
                material=new Material(source){name=sourceName+" Architecture"};
                material.SetFloat("_BoxOutline",1);
                AssetDatabase.CreateAsset(material,path);
            }
            return material;
        }

        public static int ApplyArchitecturalEdgeWidths()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=ScenePath)
                throw new InvalidOperationException("Open the comic sample in Edit mode first.");
            var hull=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/BlockOutline.asset");
            var near=ArchitecturalInk(AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Ink.mat"));
            var far=ArchitecturalInk(AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Distant_ink.mat"));
            int changed=0;
            foreach(string rootName in new[]{"01  Courtyard architecture","06  Pale distant horizon"})
            {
                var root=GameObject.Find(rootName); if(!root)continue;
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter=renderer.GetComponent<MeshFilter>();
                    if(!filter || filter.sharedMesh!=hull)continue;
                    var target=rootName.StartsWith("06")?far:near;
                    if(renderer.sharedMaterial==target)continue;
                    Undo.RecordObject(renderer,"Use edge-aware architectural outlines");
                    renderer.sharedMaterial=target; changed++;
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);return changed;
        }

        static Material Palette(string name,string lit,string shade,float variation)
        {
            var m=new Material(Shader.Find("OC Design/Comic Lighting/Surface")){name=name};
            m.SetColor("_BaseColor",Hex(lit)); m.SetColor("_ShadowColor",Hex(shade)); m.SetFloat("_TextureStrength",0);
            m.SetFloat("_Threshold",.16f); m.SetFloat("_Feather",.018f); m.SetFloat("_PaperStrength",variation);
            AssetDatabase.CreateAsset(m,Root+"/Materials/"+name.Replace(" ","_")+".mat"); return m;
        }
        static GameObject Block(string name,Vector3 position,Vector3 size,Material material,bool outline=true)
        {
            var go=new GameObject(name); go.transform.SetParent(architecture,false); go.transform.localPosition=position; go.transform.localScale=size;
            go.AddComponent<MeshFilter>().sharedMesh=cube; var r=go.AddComponent<MeshRenderer>(); r.sharedMaterial=material;
            if(outline)
            {
                var shell=new GameObject("Ink contour"); shell.transform.SetParent(go.transform,false);
                shell.AddComponent<MeshFilter>().sharedMesh=cubeHull; var edge=shell.AddComponent<MeshRenderer>(); edge.sharedMaterial=ArchitecturalInk(ink);
                edge.shadowCastingMode=ShadowCastingMode.Off; edge.receiveShadows=false;
            }
            return go;
        }
        static void Crate(Vector3 p,float size)
        {
            Block("Timber crate",p,Vector3.one*size,timber);
            foreach(float d in new[]{-.35f,.35f})
            {
                Block("Crate strap front",p+new Vector3(d*size,0,-size*.51f),new Vector3(.07f,size,.025f),red);
                Block("Crate strap top",p+new Vector3(d*size,size*.51f,0),new Vector3(.07f,.025f,size),red);
            }
        }
        static void Character(string name,string prefabPath,string clipPath,Vector3 position,float yaw,Transform parent)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if(!asset) throw new InvalidOperationException("Missing source prefab: "+prefabPath);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset); go.name=name+" - Comic preview"; go.transform.SetParent(parent,false);
            go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
            foreach(var b in go.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled=false;
            foreach(var c in go.GetComponentsInChildren<Collider>(true)) c.enabled=false;
            foreach(var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); ps.gameObject.SetActive(false); }
            foreach(var line in go.GetComponentsInChildren<LineRenderer>(true)) line.enabled=false;
            foreach(var trail in go.GetComponentsInChildren<TrailRenderer>(true)) trail.enabled=false;
            var anim=go.GetComponentInChildren<Animator>(true); var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if(anim && clip)
            {
                var ctrl=AnimatorController.CreateAnimatorControllerAtPath(Root+"/Animation/"+name+"Preview.controller");
                var state=ctrl.layers[0].stateMachine.AddState("Idle preview"); state.motion=clip; ctrl.layers[0].stateMachine.defaultState=state;
                AssetDatabase.SaveAssetIfDirty(ctrl); anim.runtimeAnimatorController=ctrl; anim.applyRootMotion=false; anim.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                anim.Rebind(); anim.Update(.2f);
            }
            var renderers=go.GetComponentsInChildren<Renderer>(true).Where(r=>r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            foreach(var r in renderers)
            {
                if(!r.enabled || !r.gameObject.activeInHierarchy) continue;
                r.sharedMaterials=r.sharedMaterials.Select(m=>CharacterPalette(m,name)).ToArray();
                r.shadowCastingMode=ShadowCastingMode.On; r.receiveShadows=true;
                var shell=new GameObject("Comic ink contour"); shell.transform.SetParent(r.transform,false);
                Renderer edge;
                if(r is SkinnedMeshRenderer skin)
                {
                    var s=shell.AddComponent<SkinnedMeshRenderer>(); s.sharedMesh=ComicOutlineHullFactory.GetOrCreate(skin.sharedMesh); s.bones=skin.bones; s.rootBone=skin.rootBone; s.localBounds=skin.localBounds; s.updateWhenOffscreen=true; edge=s;
                }
                else { shell.AddComponent<MeshFilter>().sharedMesh=ComicOutlineHullFactory.GetOrCreate(r.GetComponent<MeshFilter>().sharedMesh); edge=shell.AddComponent<MeshRenderer>(); }
                edge.sharedMaterials=Enumerable.Repeat(ink,r.sharedMaterials.Length).ToArray(); edge.shadowCastingMode=ShadowCastingMode.Off; edge.receiveShadows=false;
            }
            float lowest=float.PositiveInfinity;
            foreach(var skin in renderers.OfType<SkinnedMeshRenderer>())
            {
                if(!skin.enabled || !skin.gameObject.activeInHierarchy)continue;
                var mesh=new Mesh(); skin.BakeMesh(mesh);
                foreach(var vertex in mesh.vertices) lowest=Mathf.Min(lowest,skin.transform.TransformPoint(vertex).y);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            if(!float.IsInfinity(lowest)) go.transform.position+=Vector3.up*(.025f-lowest);
        }
        static Material CharacterPalette(Material source,string actor)
        {
            if(!source) return stone;
            if(characterMaterials.TryGetValue(source,out var existing)) return existing;
            var m=new Material(Shader.Find("OC Design/Comic Lighting/Surface")){name=actor+"_"+source.name+"_Comic"};
            Texture tex=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.mainTexture;
            Color tint=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):Color.white;
            m.SetTexture("_BaseMap",tex); m.SetColor("_BaseColor",tint*Hex("FFF3DC")); m.SetColor("_ShadowColor",tint*Hex("718EA8"));
            m.SetFloat("_TextureStrength",tex?1:0); m.SetFloat("_PaperStrength",0); m.SetFloat("_Threshold",.12f); m.SetFloat("_Feather",.028f);
            if(source.HasProperty("_Cull")) m.SetFloat("_Cull",source.GetFloat("_Cull"));
            if(source.IsKeywordEnabled("_ALPHATEST_ON")) { m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_AlphaClip",1); if(source.HasProperty("_Cutoff"))m.SetFloat("_Cutoff",source.GetFloat("_Cutoff")); }
            string path=AssetDatabase.GenerateUniqueAssetPath(Root+"/Materials/"+m.name.Replace("/","_")+".mat"); AssetDatabase.CreateAsset(m,path); characterMaterials[source]=m; return m;
        }
        public static void Frame()
        {
            var camera=UnityEngine.Object.FindObjectsByType<Camera>().FirstOrDefault(c=>c.name=="04  Preview Camera");
            if(!camera)return;
            if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(new Vector3(-.2f,1.8f,3.2f),camera.transform.rotation,18,false,true);
            Selection.activeGameObject=camera.gameObject;
        }
    }
}
