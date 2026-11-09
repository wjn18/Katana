using System;
using System.IO;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OCDesign.EditorTools
{
    /// <summary>定向装配原场景的可编辑 HUD；再次调用只绑定已有对象，不重置用户布局。</summary>
    public static class SamuraiHUDSceneBuilder
    {
        public const string RootPath = "Assets/KatanaDemo/SamuraiCombat/UI";
        const string ScenePath = "Assets/KatanaDemo/SamuraiCombat/Scenes/SamuraiCombatDemo.unity";
        static readonly Color Gold = new Color(.82f,.67f,.38f,1);
        static readonly Color Ink = new Color(.025f,.043f,.052f,.88f);
        static readonly Color Ivory = new Color(.96f,.91f,.78f,1);
        static Sprite solid, outline, ring, disc, gear;
        static Font font;

        [MenuItem("Katana/Samurai/HUD/Install Editable HUD In Current Scene")]
        public static void InstallInCurrentScene()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("请先退出 Play。");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=ScenePath) throw new InvalidOperationException("此工具只修改原 SamuraiCombatDemo 场景。");
            var input=UnityEngine.Object.FindAnyObjectByType<SamuraiPlayerInput>();
            if(!input) throw new InvalidOperationException("原场景中未找到玩家。");
            var enemy=input.trainingEnemy ? input.trainingEnemy : UnityEngine.Object.FindAnyObjectByType<SamuraiTrainingEnemy>();
            var actor=input.actor ? input.actor : input.GetComponent<SamuraiCombatant>();
            Build(actor,enemy,input,Camera.main);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static SamuraiHUDView Build(SamuraiCombatant actor,SamuraiTrainingEnemy enemy,SamuraiPlayerInput input,Camera camera)
        {
            var current=UnityEngine.Object.FindAnyObjectByType<SamuraiHUDView>(FindObjectsInactive.Include);
            if(current) { current.player=actor;current.enemy=enemy;current.input=input;current.worldCamera=camera;EnsureVitalFeedback(current);SamuraiToughnessSetup.EnsureHud(current);SamuraiEliteHUDSetup.EnsureHud(current);return current; }
            PrepareArt();
            // 仅删除被替换的旧 HUD 对象，既有角色、相机和环境不重建。
            var old=GameObject.Find("Combat HUD");
            if(old) Undo.DestroyObjectImmediate(old);
            var root=new GameObject("Samurai HUD Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root,"Install editable combat HUD");
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=50;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var view=root.AddComponent<SamuraiHUDView>();view.player=actor;view.enemy=enemy;view.input=input;view.worldCamera=camera;view.canvas=canvas;
            var combat=Stretch("Combat",root.transform);view.combatRoot=combat.gameObject;

            var vitals=Corner("VitalsTopLeft",combat,Vector2.up,new Vector2(36,-28),new Vector2(760,154));
            Picture("FamilyEmblem",vitals,Icon("FamilyEmblem"),0,0,142,142,Color.white);
            var hp=Rect("Health",vitals,146,32,540,24);MeterBack(hp,540,24);
            view.healthFill=Fill("HealthFill",hp,5,5,530,14,new Color(.8f,.19f,.14f));
            var ki=Rect("StaminaAndKiPulse",vitals,146,77,540,22);MeterBack(ki,540,22);
            view.kiPendingFill=Fill("KiPending",ki,5,5,530,12,new Color(.55f,.62f,.65f));
            view.kiRecoverableFill=Fill("KiRecoverable",ki,5,5,530,12,new Color(.32f,.8f,.85f));
            view.staminaFill=Fill("StaminaFill",ki,5,5,530,12,new Color(.25f,.69f,.47f));
            view.kiPendingFill.fillAmount=.86f;view.kiRecoverableFill.fillAmount=.73f;view.staminaFill.fillAmount=.62f;
            var key=Rect("KiPulseKey",vitals,702,69,44,38);view.kiKeyGlow=Picture("KeyFrame",key,outline,0,0,44,38,new Color(.5f,.85f,.9f));
            Label("Q",key,"Q",0,0,44,38,25,Ivory);
            var charge=Rect("ChargeWindow",vitals,146,117,540,6);view.chargeRoot=charge.gameObject;
            Picture("Track",charge,solid,0,0,540,6,new Color(.1f,.17f,.18f,.75f));view.chargeFill=Fill("ChargeFill",charge,0,0,540,6,Gold);charge.gameObject.SetActive(false);

            var bottom=Corner("StanceAndArtsBottomRight",combat,new Vector2(1,0),new Vector2(-36,36),new Vector2(520,220));
            var arts=Rect("Arts",bottom,0,14,184,200);view.artsRoot=arts.gameObject;
            Picture("ArtsBackplate",arts,disc,0,0,176,176,Ink);
            Picture("ArtsEngravedFrame",arts,Icon("CircleFrame"),0,0,176,176,new Color(.38f,.37f,.32f,.8f));
            view.artsRing=Picture("ArtsProgress",arts,ring,0,0,176,176,new Color(.36f,.7f,.93f));
            view.artsRing.type=Image.Type.Filled;view.artsRing.fillMethod=Image.FillMethod.Radial360;view.artsRing.fillOrigin=2;view.artsRing.fillClockwise=true;view.artsRing.fillAmount=.48f;
            view.artsSymbol=Picture("ArtsSigil",arts,Icon("ArtsSigil"),36,24,106,106,Ivory);
            view.artsTimer=Label("RemainingSeconds",arts,"",10,166,156,34,28,Ivory);
            BuildStance(bottom,view,true,216);BuildStance(bottom,view,false,366);

            var target=Rect("TargetWorldAnchor",combat,0,0,200,76);target.anchorMin=target.anchorMax=new Vector2(.5f,.5f);target.pivot=new Vector2(.5f,.5f);target.anchoredPosition=Vector2.zero;view.targetRoot=target;
            var targetBar=Rect("TargetHealth",target,10,0,180,10);MeterBack(targetBar,180,10);view.targetHealthFill=Fill("TargetHealthFill",targetBar,2,2,176,6,new Color(.8f,.19f,.14f));
            view.lockMarker=Picture("TargetLockDiamond",target,Icon("DiamondFrame"),74,26,52,52,Gold);
            var warn=Rect("ThreatWorldAnchor",combat,0,0,90,52);warn.anchorMin=warn.anchorMax=new Vector2(.5f,.5f);warn.pivot=new Vector2(.5f,.5f);view.warningRoot=warn;
            Picture("ThreatDiamond",warn,Icon("DiamondFrame"),24,0,42,42,Gold);Label("ThreatPip",warn,"!",24,0,42,42,22,Gold);
            view.warningFill=Fill("ThreatProgress",warn,15,46,60,3,Gold);warn.gameObject.SetActive(false);
            var feedback=Corner("BriefFeedback",combat,new Vector2(.5f,0),new Vector2(0,125),new Vector2(280,40));
            view.briefFeedback=Label("Feedback",feedback,"",0,0,280,40,24,new Color(.6f,.9f,.95f));
            var capture=Corner("CaptureHint",combat,new Vector2(.5f,0),new Vector2(0,40),new Vector2(340,36));view.captureHint=capture.gameObject;
            Label("CaptureText",capture,"单击画面恢复操控",0,0,340,36,20,new Color(.85f,.85f,.8f));capture.gameObject.SetActive(false);
            var death=Corner("DeathPanel",combat,new Vector2(.5f,.5f),Vector2.zero,new Vector2(360,140));view.deathPanel=death.gameObject;
            Picture("Backdrop",death,solid,0,0,360,140,Ink);view.deathText=Label("DeathText",death,"战败\nR 重试",0,0,360,140,30,Ivory);death.gameObject.SetActive(false);

            var settings=SamuraiSettingsUIBuilder.Build(root.transform,combat.gameObject,actor,input,enemy,font,null,outline);
            var esc=Corner("SettingsShortcutTopRight",combat,Vector2.one,new Vector2(-36,-34),new Vector2(116,46));
            var escImage=Picture("Background",esc,outline,0,0,116,46,Gold);
            var escButton=escImage.gameObject.AddComponent<Button>();escImage.raycastTarget=true;escButton.targetGraphic=escImage;
            Label("EscLabel",escImage.transform,"Esc",8,0,64,46,25,Ivory);
            Picture("SettingsGear",escImage.transform,gear,78,10,26,26,Ivory);
            UnityEventTools.AddPersistentListener(escButton.onClick,settings.OpenFromButton);
            if(!UnityEngine.Object.FindAnyObjectByType<EventSystem>())
            {
                var events=new GameObject("HUD Event System",typeof(EventSystem),typeof(InputSystemUIInputModule));events.transform.SetParent(root.transform,false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            EnsureVitalFeedback(view);
            SamuraiToughnessSetup.EnsureHud(view);
            SamuraiEliteHUDSetup.EnsureHud(view);
            Selection.activeGameObject=root;
            return view;
        }

        public static void InstallVitalFeedbackInCurrentScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出 Play。");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("此工具只修改原 SamuraiCombatDemo 场景。");
            var view = UnityEngine.Object.FindAnyObjectByType<SamuraiHUDView>(FindObjectsInactive.Include);
            if (!view) throw new InvalidOperationException("原场景中未找到 HUD。");
            Undo.RecordObject(view, "Install vital HUD feedback");
            EnsureVitalFeedback(view);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>只补生命残影层和体力边框引用，沿用当前图片、尺寸与用户布局。</summary>
        public static void EnsureVitalFeedback(SamuraiHUDView view)
        {
            if (!view.healthDamageFill)
                view.healthDamageFill = DamageLayer(view.healthFill, "HealthDamageTrail", view.healthDamageColor);
            if (!view.targetHealthDamageFill)
                view.targetHealthDamageFill = DamageLayer(view.targetHealthFill, "TargetHealthDamageTrail", view.healthDamageColor);
            if (!view.staminaBorder && view.staminaFill)
            {
                var border = view.staminaFill.transform.parent.Find("Border");
                if (border) view.staminaBorder = border.GetComponent<Image>();
            }
        }

        static Image DamageLayer(Image fill, string name, Color color)
        {
            if (!fill) return null;
            var existing = fill.transform.parent.Find(name);
            if (existing) return existing.GetComponent<Image>();
            var trail = UnityEngine.Object.Instantiate(fill, fill.transform.parent);
            trail.name = name;
            Undo.RegisterCreatedObjectUndo(trail.gameObject, "Add health damage trail");
            trail.transform.SetSiblingIndex(fill.transform.GetSiblingIndex());
            trail.color = color;
            trail.raycastTarget = false;
            trail.fillAmount = 0f;
            return trail;
        }

        static void BuildStance(RectTransform parent,SamuraiHUDView view,bool high,float x)
        {
            var node=Rect(high?"HighStance":"MiddleStance",parent,x,24,148,190);
            var frame=Picture("StanceFrame",node,Icon("DiamondFrame"),0,0,148,148,high?new Color(.4f,.4f,.36f):Color.white);
            var icon=Picture("StanceSilhouette",node,Icon(high?"HighStance":"MiddleStance"),26,high?21:6,96,104,high?new Color(.7f,.7f,.65f):Ivory);
            var marker=Picture("SelectedMarker",node,solid,66,133,13,13,Gold);marker.rectTransform.localRotation=Quaternion.Euler(0,0,45);marker.gameObject.SetActive(!high);
            Picture("KeyFrame",node,outline,52,155,44,38,Gold);Label("Key",node,high?"1":"2",52,155,44,38,24,Ivory);
            if(high){view.highStanceFrame=frame;view.highStanceIcon=icon;view.highStanceMarker=marker.gameObject;}
            else{view.midStanceFrame=frame;view.midStanceIcon=icon;view.midStanceMarker=marker.gameObject;}
        }

        static void PrepareArt()
        {
            Directory.CreateDirectory(RootPath+"/Art");
            string atlas=RootPath+"/Art/SamuraiHUD_Icons.png";AssetDatabase.ImportAsset(atlas,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(atlas);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;importer.filterMode=FilterMode.Bilinear;importer.spritePixelsPerUnit=100;
            string[] names={"HighStance","MiddleStance","ArtsSigil","DiamondFrame","CircleFrame","FamilyEmblem"};
            var rects=new SpriteMetaData[6];for(int i=0;i<6;i++)rects[i]=new SpriteMetaData{name=names[i],rect=new Rect((i%3)*512,(1-i/3)*512,512,512),alignment=9,pivot=new Vector2(.5f,.5f)};
#pragma warning disable 618
            importer.spritesheet=rects;
#pragma warning restore 618
            importer.SaveAndReimport();
            solid=MakeShape("UI_Solid",8,(x,y)=>1f,Vector4.zero);
            outline=MakeShape("UI_Outline",64,(x,y)=>x<2||y<2||x>=62||y>=62?1:0,new Vector4(8,8,8,8));
            ring=MakeShape("UI_Ring",256,(x,y)=>Mathf.Clamp01(4.5f-Mathf.Abs(Mathf.Sqrt((x-127.5f)*(x-127.5f)+(y-127.5f)*(y-127.5f))-107f)),Vector4.zero);
            disc=MakeShape("UI_Disc",256,(x,y)=>Mathf.Clamp01(108f-Mathf.Sqrt((x-127.5f)*(x-127.5f)+(y-127.5f)*(y-127.5f))),Vector4.zero);
            gear=MakeShape("UI_Gear",128,(x,y)=>{float dx=x-63.5f,dy=y-63.5f;float r=Mathf.Sqrt(dx*dx+dy*dy);float outer=Mathf.Cos(Mathf.Atan2(dy,dx)*8f)>.1f?57f:46f;return Mathf.Clamp01(outer-r)*Mathf.Clamp01(r-20f);},Vector4.zero);
            AssetDatabase.ImportAsset(RootPath+"/Fonts/NotoSansSC.ttf",ImportAssetOptions.ForceSynchronousImport);
            font=AssetDatabase.LoadAssetAtPath<Font>(RootPath+"/Fonts/NotoSansSC.ttf");
            if(!font)throw new InvalidOperationException("缺少中文字体。");
        }
        static Sprite MakeShape(string name,int size,Func<int,int,float> alpha,Vector4 border)
        {
            string path=RootPath+"/Art/"+name+".png";
            if(!File.Exists(path))
            {
                var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);var pixels=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)pixels[y*size+x]=new Color(1,1,1,alpha(x,y));
                tex.SetPixels(pixels);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var imp=(TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType=TextureImporterType.Sprite;imp.spriteImportMode=SpriteImportMode.Single;imp.alphaIsTransparency=true;imp.mipmapEnabled=false;imp.spriteBorder=border;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static Sprite Icon(string name)=>AssetDatabase.LoadAllAssetsAtPath(RootPath+"/Art/SamuraiHUD_Icons.png").OfType<Sprite>().First(s=>s.name==name);
        static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=Vector2.up;r.pivot=Vector2.up;r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
        }
        static RectTransform Stretch(string name,Transform parent){var r=Rect(name,parent,0,0,0,0);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,.5f);r.offsetMin=r.offsetMax=Vector2.zero;return r;}
        static RectTransform Corner(string name,Transform parent,Vector2 anchor,Vector2 offset,Vector2 size){var r=Rect(name,parent,0,0,size.x,size.y);r.anchorMin=r.anchorMax=r.pivot=anchor;r.anchoredPosition=offset;return r;}
        static Image Picture(string name,Transform parent,Sprite sprite,float x,float y,float w,float h,Color color)
        {
            var r=Rect(name,parent,x,y,w,h);var im=r.gameObject.AddComponent<Image>();im.sprite=sprite;im.color=color;im.raycastTarget=false;
            if(sprite==outline)im.type=Image.Type.Sliced;return im;
        }
        static Image Fill(string name,Transform parent,float x,float y,float w,float h,Color color){var im=Picture(name,parent,solid,x,y,w,h,color);im.type=Image.Type.Filled;im.fillMethod=Image.FillMethod.Horizontal;im.fillOrigin=0;return im;}
        static void MeterBack(Transform parent,float w,float h){Picture("Track",parent,solid,0,0,w,h,Ink);Picture("Border",parent,outline,0,0,w,h,Gold);}
        static Text Label(string name,Transform parent,string value,float x,float y,float w,float h,int size,Color color){var r=Rect(name,parent,x,y,w,h);var t=r.gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;t.color=color;t.text=value;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;}
    }
}
