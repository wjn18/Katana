using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Paused-Play, isolated real actors. Cursor and TimeScale are restored; no scene or asset is saved.</summary>
    public static class SamuraiBackstabCueValidation
    {
        const string EnemyPrefab = "Assets/KatanaDemo/Characters/Enemy1/Prefabs/Enemy1.prefab";
        const string ElitePrefab = "Assets/KatanaDemo/Characters/Elite1/Prefabs/Elite1.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static SamuraiExecutionCue cue;
        static SamuraiPlayerInput input;
        static CursorLockMode savedLock;
        static bool savedCursorVisible, ownsFixture;
        static float savedScale;
        static GameObject wall;
        static SamuraiCombatant Player => SamuraiBackstabValidation.Player;
        static SamuraiCombatant Enemy => SamuraiBackstabValidation.Enemy;
        static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
        static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Private).SetValue(instance, value);
        sealed class Check { public string name; public bool passed; public object observed; }
        sealed class Case
        {
            public string name, exception;
            public List<Check> checks = new List<Check>();
            public bool passed => exception == null && checks.All(c => c.passed);
            public void Is(bool condition, string name, object observed = null)
                => checks.Add(new Check { passed = condition, name = name, observed = observed });
        }

        public static string Setup()
        {
            if (!Application.isPlaying || !EditorApplication.isPaused || Time.timeScale <= 0)
                throw new InvalidOperationException("Use paused Play with positive TimeScale.");
            Cleanup(); savedLock = Cursor.lockState; savedCursorVisible = Cursor.visible; savedScale = Time.timeScale;
            SamuraiBackstabValidation.Setup(); ownsFixture = true;
            UseActualElitePresentation();
            input = Player.GetComponent<SamuraiPlayerInput>();
            input.captureOnStart = false; input.enabled = true;
            input.Configure(Player, SamuraiBackstabValidation.View.transform, SamuraiBackstabValidation.Orbit,
                SamuraiBackstabValidation.EnemyAI);
            cue = Enemy.GetComponent<SamuraiExecutionCue>();
            if (cue == null) throw new InvalidOperationException("Install the ordinary-enemy backstab cue on its prefab first.");
            cue.enabled = true;
            Prepare();
            return "Ready: actual enemy/player prefabs, actual readiness planner, isolated camera and legitimate captured-input state.";
        }
        static void UseActualElitePresentation()
        {
            // The scene presentation adds contour attachment transforms required by the old
            // execution pose bindings; the bare Elite prefab intentionally omits those transforms.
            var old = SamuraiBackstabValidation.Elite;
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>("Assets/KatanaDemo/SamuraiCombat/Config/Elite1/Elite1.asset");
            var source = UnityEngine.Object.FindObjectsByType<SamuraiCombatant>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a != old && a != Player && a != Enemy && a.Profile == profile
                    && a.gameObject.scene == Player.gameObject.scene);
            if (source == null) throw new InvalidOperationException("The actual scene Elite presentation is required for its old pose-binding regression.");
            var clone = UnityEngine.Object.Instantiate(source.gameObject, old.transform.parent);
            clone.name = "Cue test actual Elite presentation";
            foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(component is SamuraiCombatant)) component.enabled = false;
            var actor = clone.GetComponent<SamuraiCombatant>();
            actor.Configure(actor.Animator, actor.Motor, old.Profile, Get<Transform>(actor, "weaponBase"),
                Get<Transform>(actor, "weaponTip"), actor.Team);
            SamuraiBackstabValidation.Elite = actor;
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        static void Capture()
        {
            // Simulate application-focus state on the isolated input; exercise its public cursor capture gate.
            Set(input, "hasFocus", true); Set(input, "paused", false); Set(input, "suspended", false);
            input.CaptureCursor();
        }
        static void PointCamera(SamuraiCombatant target)
        {
            var view = SamuraiBackstabValidation.View;
            view.transform.position = Player.transform.position - Player.transform.forward * 3 + Vector3.up * 1.4f;
            view.transform.LookAt(target.transform.position + Vector3.up * (target == Enemy ? 1.1f : 1.5f));
        }
        static void Bind(SamuraiExecutionCue targetCue, SamuraiCombatant actor)
        {
            targetCue.enabled = true; targetCue.Configure(actor, Player);
            Set(targetCue, "viewingInput", input); Set(targetCue, "viewCamera", SamuraiBackstabValidation.View);
        }
        static void Prepare()
        {
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall); wall = null;
            Time.timeScale = savedScale;
            input.enabled = true; SamuraiBackstabValidation.Prepare(); Capture(); Bind(cue, Enemy); PointCamera(Enemy);
        }
        static void Refresh() { PointCamera(Enemy); cue.RefreshCue(1f / 60f); }
        static bool GroundVisible(SamuraiExecutionCue targetCue)
        {
            var ground = Get<GameObject>(targetCue, "groundRoot"); return ground != null && ground.activeInHierarchy;
        }
        static MaterialPropertyBlock BodyProperties(SamuraiExecutionCue targetCue)
        {
            var block = new MaterialPropertyBlock(); var renderer = Get<MeshRenderer>(targetCue, "bodyRenderer");
            if (renderer != null) renderer.GetPropertyBlock(block); return block;
        }
        static void Ground(SamuraiCombatant actor, Vector3 position, Quaternion rotation)
        {
            actor.Motor.enabled = false; actor.transform.SetPositionAndRotation(position + Vector3.up * .04f, rotation);
            actor.Motor.enabled = true; Physics.SyncTransforms(); actor.Motor.Move(Vector3.down * .12f); Physics.SyncTransforms();
            actor.Animator.Update(0);
        }

        public static string RunMatrix(string outputPath)
        {
            if (!ownsFixture || cue == null) throw new InvalidOperationException("Call Setup in paused Play first.");
            var cases = new List<Case>(); var errors = new List<string>();
            Application.LogCallback log = (message, stack, type) =>
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); };
            Action<string, Action<Case>> run = (name, action) =>
            {
                var item = new Case { name = name }; cases.Add(item);
                try { action(item); } catch (Exception e) { item.exception = e.ToString(); }
                finally { Time.timeScale = savedScale; }
            };
            Application.logMessageReceived += log;
            try
            {
                run("Prefab opt-in preserves the old Elite branch", c =>
                {
                    var ordinary = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab).GetComponents<SamuraiExecutionCue>();
                    var elite = AssetDatabase.LoadAssetAtPath<GameObject>(ElitePrefab).GetComponents<SamuraiExecutionCue>();
                    c.Is(ordinary.Length == 1 && ordinary[0].backstabOnly, "ordinary prefab has exactly one backstab-only cue", ordinary.Length);
                    c.Is(elite.Length == 1 && !elite[0].backstabOnly, "Elite keeps its original non-backstab-only cue", elite.Length);
                    c.Is(!SamuraiBackstabValidation.Elite.HasBackstabReceiver, "Elite remains outside standing B2 opt-in");
                });
                run("Only the actual ready opportunity produces a white heart point", c =>
                {
                    Prepare(); Refresh();
                    c.Is(input.HasCursorCapture && Player.CanBackstabTarget(Enemy) && Player.FindBackstabTarget() == Enemy,
                        "input capture and the real unique E target are valid");
                    c.Is(cue.Ready && cue.CueVisible && cue.IsBackstabOpportunity && cue.PromptText == "[E 背刺]", "only-ready white marker is active");
                    var block = BodyProperties(cue); Color color = block.GetColor(Shader.PropertyToID("_Tint"));
                    c.Is(color.r > 1 && Mathf.Abs(color.r - color.g) < .0001f && Mathf.Abs(color.g - color.b) < .0001f,
                        "marker tint is bright neutral white", new { color.r, color.g, color.b, color.a });
                    c.Is(Mathf.Abs(block.GetFloat(Shader.PropertyToID("_Shape")) - 2) < .001f, "white point uses shape 2");
                    c.Is(!GroundVisible(cue), "backstab marker has no standing circle or direction arrow");
                    var bone = Enemy.Animator.GetComponentsInChildren<Transform>(true).First(t => t.name == "Spine1");
                    float scale = Mathf.Clamp(Enemy.Motor.height * Mathf.Abs(Enemy.transform.lossyScale.y) * .5f, .7f, 2.5f);
                    Vector3 expected = bone.position - Enemy.transform.right * cue.backstabHeartOffset.x * scale
                        + Enemy.transform.up * cue.backstabHeartOffset.y * scale - Enemy.transform.forward * cue.surfaceOffset * scale;
                    c.Is(Vector3.Distance(cue.BodyPoint, expected) < .001f, "point sits at the left/up chest-heart offset on Spine1",
                        new { point = new[] { cue.BodyPoint.x, cue.BodyPoint.y, cue.BodyPoint.z }, expected = new[] { expected.x, expected.y, expected.z } });
                    Vector3 old = bone.localPosition, before = cue.BodyPoint;
                    try
                    {
                        bone.position += Vector3.up * .075f; Vector3 moved = bone.localPosition; Refresh();
                        c.Is(Vector3.Distance(cue.BodyPoint - before, Vector3.up * .075f) < .001f, "heart point follows chest bone motion");
                        c.Is(bone.localPosition == moved, "cue refresh leaves the actor skeleton untouched");
                    }
                    finally { bone.localPosition = old; }
                });
                run("Front, alert, range and wall hide instead of advertising an approach", c =>
                {
                    Prepare(); Ground(Player, Enemy.transform.position + Enemy.transform.forward * 1.5f, Quaternion.LookRotation(-Enemy.transform.forward)); Refresh();
                    c.Is(!Player.CanBackstabTarget(Enemy) && !cue.CueVisible, "front hides");
                    Prepare(); SamuraiBackstabValidation.EnemyAI.NoticeInterruptedBackstab(Player); Refresh();
                    c.Is(!Player.CanBackstabTarget(Enemy) && !cue.CueVisible, "alert guard hides");
                    Prepare(); Ground(Player, Player.transform.position - Player.transform.forward, Player.transform.rotation); Refresh();
                    c.Is(!Player.CanBackstabTarget(Enemy) && !cue.CueVisible, "outside approach range hides");
                    Prepare(); wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = (Player.transform.position + Enemy.transform.position) * .5f + Vector3.up;
                    wall.transform.localScale = new Vector3(4, 2, .15f); Physics.SyncTransforms(); Refresh();
                    c.Is(!Player.CanBackstabTarget(Enemy) && !cue.CueVisible, "wall hides");
                });
                run("Paused, released cursor, focus loss and disabled input hide", c =>
                {
                    Prepare(); Time.timeScale = 0; Refresh(); c.Is(!cue.CueVisible, "paused game hides");
                    Prepare(); input.ReleaseCursor(); Refresh(); c.Is(!input.HasCursorCapture && !cue.CueVisible, "released cursor hides");
                    Prepare(); Set(input, "hasFocus", false); Refresh(); c.Is(!input.HasCursorCapture && !cue.CueVisible, "lost focus hides");
                    Prepare(); input.enabled = false; Refresh(); c.Is(!cue.CueVisible, "disabled input hides");
                });
                run("Committed action, death and reset hide immediately", c =>
                {
                    Prepare(); Refresh(); SamuraiBackstabValidation.PressE();
                    c.Is(Player.IsBackstabbing && !cue.CueVisible, "committed pair hides via actor state event");
                    for (int i = 0; i < 100 && !Enemy.IsDead; i++) SamuraiBackstabValidation.Step(.01f);
                    Refresh(); c.Is(Enemy.IsDead && !cue.CueVisible, "dead receiver stays hidden");
                    Prepare(); Refresh(); c.Is(cue.Ready, "ready before resetting");
                    Enemy.ResetCombat(); c.Is(!cue.CueVisible, "reset event hides immediately");
                    Prepare(); Refresh(); c.Is(cue.Ready && !GroundVisible(cue), "a valid fresh opportunity returns after reset");
                });
                run("Elite keeps its golden prone marker and ground guide", c =>
                {
                    Prepare(); var elite = SamuraiBackstabValidation.Elite; var oldCue = elite.GetComponent<SamuraiExecutionCue>();
                    Ground(Enemy, SamuraiBackstabValidation.Origin + Vector3.left * 8, Quaternion.identity);
                    Ground(elite, SamuraiBackstabValidation.Origin + Vector3.forward * 3, Quaternion.Euler(0, 180, 0));
                    Get<SamuraiActionController>(elite, "actions").ReactToToughnessBreak();
                    Vector3 anchor; Quaternion facing;
                    bool approach = Player.TryGetExecutionApproach(elite, out anchor, out facing);
                    if (approach) Ground(Player, anchor, facing);
                    Bind(oldCue, elite); PointCamera(elite); oldCue.RefreshCue(1f / 60f);
                    c.Is(approach && Player.CanExecuteTarget(elite), "old Elite prone execution remains actually eligible");
                    c.Is(oldCue.Ready && !oldCue.IsBackstabOpportunity && oldCue.PromptText == "[E 处决]", "old Elite ready state and prompt remain");
                    var block = BodyProperties(oldCue); Color tint = block.GetColor(Shader.PropertyToID("_Tint"));
                    c.Is(block.GetFloat(Shader.PropertyToID("_Shape")) == 1 && tint.r > tint.g && tint.g > tint.b,
                        "Elite retains shape 1 and golden tint", new { tint.r, tint.g, tint.b });
                    c.Is(GroundVisible(oldCue), "Elite retains its standing circle and facing arrow");
                });
            }
            finally { Application.logMessageReceived -= log; Time.timeScale = savedScale; }
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(new { assertions = cases.Sum(c => c.checks.Count),
                passed = cases.All(c => c.passed) && errors.Count == 0, errors, cases }, Newtonsoft.Json.Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))); File.WriteAllText(outputPath, json); return json;
        }

        public static string Cleanup()
        {
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall); wall = null;
            if (ownsFixture)
            {
                if (input != null) input.ReleaseCursor();
                SamuraiBackstabValidation.Cleanup();
                Cursor.lockState = savedLock; Cursor.visible = savedCursorVisible; Time.timeScale = savedScale;
            }
            ownsFixture = false; cue = null; input = null;
            return "Isolated cue validation actors removed; original Cursor/TimeScale restored, source assets untouched.";
        }
    }
}
