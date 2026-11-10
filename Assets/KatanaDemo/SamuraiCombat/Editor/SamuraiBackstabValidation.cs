using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>
    /// Explicit paused-Play driver. Every simulated frame runs the real E gateway and exactly one
    /// Combatant.Tick per actor. AI remains enabled, including its real perception and patrol arbitration.
    /// Nothing here saves scenes, modifies source assets, or reads/writes game progress.
    /// </summary>
    public static class SamuraiBackstabValidation
    {
        const string Base = "Assets/KatanaDemo/SamuraiCombat/";
        const string AttackSource = "Assets/ARPGPack/ARPGWarrior/Additional_Animations/Humanoid/ARPG_Warrior_Execute_B2.anim";
        public static readonly Vector3 Origin = new Vector3(1000, 0, 1000);
        public static SamuraiCombatant Player, Enemy, Elite;
        public static SamuraiTrainingEnemy EnemyAI;
        public static SamuraiPatrolAgent Patrol;
        public static SamuraiCombatCameraFeedback Feedback;
        public static SamuraiOrbitCamera Orbit;
        public static Camera View;
        public static float SimulatedSeconds { get; private set; }
        public static int PlayerTickCount { get; private set; }
        public static int EnemyTickCount { get; private set; }

        static GameObject root, floor, routeObject;
        static SamuraiPatrolRoute route;
        static readonly List<GameObject> obstacles = new List<GameObject>();
        static readonly List<ScriptableObject> clones = new List<ScriptableObject>();
        static float savedTimeScale;
        static SamuraiCombatProfile playerProfile;
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
        static SamuraiActionController Actions(SamuraiCombatant actor) => Field<SamuraiActionController>(actor, "actions");
        static SamuraiVitals Vitals(SamuraiCombatant actor) => Field<SamuraiVitals>(actor, "vitals");

        [Serializable] sealed class Check
        {
            public string name;
            public bool passed;
            public object observed;
        }
        [Serializable] sealed class Case
        {
            public string name;
            public List<Check> checks = new List<Check>();
            public string exception;
            public bool passed => exception == null && checks.All(c => c.passed);
            public void Is(bool condition, string description, object observed = null)
                => checks.Add(new Check { name = description, passed = condition, observed = observed });
        }

        public static string Setup()
        {
            if (!Application.isPlaying || !EditorApplication.isPaused)
                throw new InvalidOperationException("Run Setup only in paused Play; the fixture never changes Play state.");
            Cleanup();
            savedTimeScale = Time.timeScale;
            if (Time.timeScale <= 0) throw new InvalidOperationException("Paused Play must have a positive game TimeScale before Setup.");
            root = new GameObject("Backstab validation temporary - isolated at 1km");
            floor = Cube("Ground", Origin + Vector3.down * .5f, new Vector3(24, 1, 24), false);
            Player = Clone(Base + "Prefabs/SamuraiCombatPlayer.prefab", "Backstab test player", true);
            Enemy = Clone("Assets/KatanaDemo/Characters/Enemy1/Prefabs/Enemy1.prefab", "Backstab test ordinary enemy", false);
            Elite = Clone("Assets/KatanaDemo/Characters/Elite1/Prefabs/Elite1.prefab", "Backstab test Elite", false);
            playerProfile = Player.Profile;
            EnemyAI = Enemy.GetComponent<SamuraiTrainingEnemy>();
            if (EnemyAI == null) throw new InvalidOperationException("The real ordinary-enemy prefab requires SamuraiTrainingEnemy.");
            EnemyAI.enabled = true;
            EnemyAI.activeTraining = true;
            EnemyAI.trainingMode = SamuraiTrainingMode.Normal;
            EnemyAI.waitForPlayerCapture = false;
            EnemyAI.Configure(Enemy, Player, Player.GetComponent<SamuraiPlayerInput>());
            Patrol = Enemy.GetComponent<SamuraiPatrolAgent>() ?? Enemy.gameObject.AddComponent<SamuraiPatrolAgent>();
            routeObject = new GameObject("Test patrol route"); routeObject.transform.SetParent(root.transform);
            route = routeObject.AddComponent<SamuraiPatrolRoute>();
            route.points.Add(new SamuraiPatrolPoint()); route.points.Add(new SamuraiPatrolPoint());
            Patrol.route = route; Patrol.enabled = true;
            var cameraObject = new GameObject("Backstab test camera"); cameraObject.transform.SetParent(root.transform);
            View = cameraObject.AddComponent<Camera>(); View.enabled = true; View.fieldOfView = 55;
            View.clearFlags = CameraClearFlags.SolidColor; View.backgroundColor = new Color(.12f, .15f, .18f);
            Orbit = cameraObject.AddComponent<SamuraiOrbitCamera>(); Orbit.Configure(Player.transform);
            Feedback = cameraObject.AddComponent<SamuraiCombatCameraFeedback>(); Feedback.Configure(Orbit, Player);
            Prepare();
            return "Ready: paused Play, AI enabled, capture requirement disabled, source profile clones, real E gateway, one Tick per actor/frame.";
        }

        static SamuraiCombatant Clone(string assetPath, string name, bool player)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null) throw new InvalidOperationException("Missing prefab: " + assetPath);
            var go = UnityEngine.Object.Instantiate(source, root.transform); go.name = name;
            foreach (var component in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(component is SamuraiCombatant)) component.enabled = false;
            var actor = go.GetComponent<SamuraiCombatant>();
            var profile = UnityEngine.Object.Instantiate(actor.Profile); profile.name += " (validation clone)";
            clones.Add(profile);
            actor.Configure(actor.Animator, actor.Motor, profile, Field<Transform>(actor, "weaponBase"),
                Field<Transform>(actor, "weaponTip"), actor.Team);
            // The real PlayerInput component is intentionally present but its Update is disabled.
            // Combatant.Tick still executes the identical E arbitration used by that component.
            if (player && go.GetComponent<SamuraiPlayerInput>() == null)
                throw new InvalidOperationException("The real player prefab has no input component.");
            return actor;
        }

        static GameObject Cube(string name, Vector3 position, Vector3 scale, bool obstacle = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(root.transform); go.transform.SetPositionAndRotation(position, Quaternion.identity);
            go.transform.localScale = scale;
            if (obstacle) obstacles.Add(go);
            return go;
        }

        static void MoveAndGround(SamuraiCombatant actor, Vector3 position, Quaternion rotation)
        {
            actor.Motor.enabled = false;
            actor.transform.SetPositionAndRotation(position + Vector3.up * .04f, rotation);
            actor.Motor.enabled = true;
            Physics.SyncTransforms();
            // Teleport alone does not refresh CharacterController.isGrounded. This is a genuine CC move.
            actor.Motor.Move(Vector3.down * .12f);
            Physics.SyncTransforms();
            actor.Movement.CaptureSpawn();
            actor.Animator.Update(0);
        }

        /// <summary>Returns to an untracked rear approach at the approved authored anchor; yaw rotates the entire pair.</summary>
        public static string Prepare(float yaw = 0)
        {
            RequireSetup();
            ClearBlood();
            foreach (var go in obstacles) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            obstacles.Clear();
            floor.transform.SetPositionAndRotation(Origin + Vector3.down * .5f, Quaternion.identity);
            floor.transform.localScale = new Vector3(24, 1, 24);
            Enemy.gameObject.SetActive(true); Player.gameObject.SetActive(true); Elite.gameObject.SetActive(true);
            Player.ResetCombat(); Enemy.ResetCombat(); Elite.ResetCombat(); Feedback.ClearFeedback();
            playerProfile.backstabEnabled = true;
            EnemyAI.enabled = true; EnemyAI.activeTraining = true; EnemyAI.waitForPlayerCapture = false;
            EnemyAI.trainingMode = SamuraiTrainingMode.Normal;
            EnemyAI.Configure(Enemy, Player, Player.GetComponent<SamuraiPlayerInput>());
            Patrol.patrolEnabled = false; Patrol.ResetPatrol();
            var pair = Enemy.Profile.backstabPair;
            if (pair == null || !pair.isBackstab) throw new InvalidOperationException("Install the formal B2 pair before validation.");
            var facing = Quaternion.Euler(0, yaw, 0);
            MoveAndGround(Player, Origin, facing);
            MoveAndGround(Enemy, Origin + facing * new Vector3(pair.receiverOffset.x, 0, pair.receiverOffset.z),
                facing * Quaternion.Euler(0, pair.receiverYaw, 0));
            MoveAndGround(Elite, Origin + Vector3.right * 8, Quaternion.identity);
            route.transform.position = Enemy.transform.position;
            route.points[0].localPosition = Vector3.zero; route.points[1].localPosition = facing * Vector3.forward * 2;
            SimulatedSeconds = 0; PlayerTickCount = EnemyTickCount = 0;
            Orbit.ResetView(); Orbit.TickCamera(0); Feedback.SampleNow(0, 0);
            return "yaw=" + yaw + " grounded=" + Player.Motor.isGrounded + "/" + Enemy.Motor.isGrounded
                + " unaware=" + EnemyAI.IsUnawareForBackstab(Player) + " eligible=" + Player.CanBackstabTarget(Enemy);
        }

        static void RequireSetup()
        {
            if (root == null || Player == null || Enemy == null || !Application.isPlaying || !EditorApplication.isPaused)
                throw new InvalidOperationException("Call Setup in paused Play first.");
        }

        /// <summary>One and only one real player Tick and AI BuildFrame/enemy Tick; no pair-clock shortcut.</summary>
        public static void Step(float dt, bool pressE = false)
        {
            RequireSetup();
            Player.Tick(new SamuraiInputFrame { executionPressed = pressE }, dt); PlayerTickCount++;
            Enemy.Tick(EnemyAI.BuildFrame(dt), dt); EnemyTickCount++;
            if (Player.isActiveAndEnabled && !Player.IsExecuting) Player.Animator.Update(Mathf.Max(0, dt));
            if (Enemy.isActiveAndEnabled && !Enemy.IsBeingExecuted) Enemy.Animator.Update(Mathf.Max(0, dt));
            if (Player.isActiveAndEnabled) Player.EvaluateExecutionPose();
            if (Enemy.isActiveAndEnabled) Enemy.EvaluateExecutionPose();
            Physics.SyncTransforms();
            if (Time.timeScale > 0 && dt > 0)
            {
                Player.EvaluateWeaponContacts();
                if (Enemy.isActiveAndEnabled) Enemy.EvaluateWeaponContacts();
            }
            Orbit.TickCamera(Mathf.Max(0, dt));
            Feedback.SampleNow(Mathf.Max(0, dt), Mathf.Max(0, dt));
            SimulatedSeconds += Mathf.Max(0, dt);
        }

        public static bool PressE(float dt = 1f / 120f)
        {
            Step(dt, true); return Player.IsBackstabbing;
        }

        static void UntilContact(float dt = 1f / 120f)
        {
            for (int i = 0; i < 1000 && Player.IsBackstabbing && !Enemy.IsDead; i++) Step(dt);
        }
        static Transform Hips(SamuraiCombatant actor) => actor.Animator.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "Hips" || t.name.EndsWith(":Hips", StringComparison.Ordinal));

        /// <summary>Runs independent cases, preserving every assertion and exception in a reviewable JSON report.</summary>
        public static string RunMatrix(string outputPath)
        {
            RequireSetup();
            var cases = new List<Case>();
            Action<string, Action<Case>> run = (name, test) =>
            {
                var item = new Case { name = name }; cases.Add(item);
                try { test(item); } catch (Exception e) { item.exception = e.ToString(); }
            };
            float timeScale = Time.timeScale;
            try
            {
                run("Installed B2 source and feedback contract", c =>
                {
                    Prepare(); var pair = Enemy.Profile.backstabPair;
                    c.Is(pair.isBackstab && !Enemy.Profile.executionLethalPair.isBackstab, "standing and old down pairs are separate");
                    c.Is(Mathf.Abs(pair.attackerClip.length - 2f) < .012f, "attacker returns through the full 2s source", pair.attackerClip.length);
                    c.Is(Mathf.Abs(pair.receiverClip.length - 1.9f) < .012f, "receiver retains full 1.9s reaction", pair.receiverClip.length);
                    c.Is(Mathf.Abs(pair.receiverDelay - .1f) < .001f, "receiver retains .10s delayed start", pair.receiverDelay);
                    c.Is(Mathf.Abs(pair.impactHoldSeconds - .12f) < .001f, "both share .12s contact hold", pair.impactHoldSeconds);
                    c.Is(pair.bloodMaterial != null && Mathf.Abs(pair.bloodIntensity - 1.8f) < .001f, "approved blood material/intensity");
                    c.Is(Mathf.Abs(pair.shakeDegrees - 5) < .001f && Mathf.Abs(pair.shakeSeconds - .16f) < .001f, "approved 5 degree/.16s shake");
                    c.Is(Mathf.Abs(pair.cameraPushFov - 6) < .001f && Mathf.Abs(pair.cameraRecoilFov - 2) < .001f, "approved zoom and recoil");
                    int muscles; float difference = IdleTailDifference(pair.attackerClip, Player.Profile.idle, out muscles);
                    c.Is(muscles > 0 && difference < .003f, "2s attacker tail matches every muscle curve shared with current moving idle", new { muscles, maximumMuscleDifference = difference });
                });
                foreach (int hz in new[] { 15, 30, 120 }) foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                {
                    int rate = hz; float heading = yaw;
                    run(rate + "Hz rear E, yaw " + heading, c => Positive(c, rate, heading));
                }
                run("Natural straight-behind approach accepts the authored B2 yaw correction", c =>
                {
                    Prepare();
                    MoveAndGround(Player, Enemy.transform.position - Enemy.transform.forward * 1.5f, Enemy.transform.rotation);
                    Vector3 anchor; Quaternion facing;
                    bool planned = Player.TryGetBackstabApproach(Enemy, out anchor, out facing);
                    c.Is(planned, "straight behind at 1.5m has a valid standing anchor", new { approachRange = playerProfile.backstabApproachRange,
                        planarAlignmentDistance = new Vector2(anchor.x - Player.transform.position.x, anchor.z - Player.transform.position.z).magnitude });
                    c.Is(PressE(), "real E starts from naturally straight behind");
                });
                run("Front, side, wrong facing and anchor range reject before a pair", c =>
                {
                    Prepare(); var target = Enemy.transform.position; var rot = Enemy.transform.rotation;
                    MoveAndGround(Player, target + Enemy.transform.forward * 1.3f, Quaternion.LookRotation(-Enemy.transform.forward));
                    c.Is(!Player.CanBackstabTarget(Enemy) && !PressE(), "frontal E cannot beat same-frame discovery");
                    Prepare(); MoveAndGround(Player, Enemy.transform.position + Enemy.transform.right * 1.3f, Quaternion.LookRotation(-Enemy.transform.right));
                    c.Is(!Player.CanBackstabTarget(Enemy) && !PressE(), "side E rejected");
                    Prepare(); Player.transform.rotation *= Quaternion.Euler(0, 180, 0); Physics.SyncTransforms();
                    c.Is(!PressE(), "back-facing player cannot start");
                    Prepare(); MoveAndGround(Player, Player.transform.position - Player.transform.forward * (playerProfile.backstabApproachRange + .10f), Player.transform.rotation);
                    c.Is(!PressE(), "outside authored approach radius rejected");
                    Prepare(); MoveAndGround(Player, Player.transform.position - Player.transform.forward * Mathf.Max(.05f, playerProfile.backstabApproachRange - .08f), Player.transform.rotation);
                    c.Is(PressE(), "inside approach radius still starts");
                });
                run("Visibility, narrow corridor, receiver trajectory and edge clearance", c =>
                {
                    Prepare(); Cube("Intervening wall", (Player.transform.position + Enemy.transform.position) * .5f + Vector3.up,
                        new Vector3(4, 2, .15f)); Physics.SyncTransforms(); c.Is(!PressE(), "wall blocks E and cue eligibility");
                    Prepare(); float radius = Player.Motor.radius * Player.transform.lossyScale.x;
                    Cube("Narrow right corridor", Player.transform.position + Vector3.up + Vector3.right * (radius * .65f), new Vector3(.12f, 2, 2));
                    Physics.SyncTransforms(); c.Is(!PressE(), "player capsule corridor clearance is required");
                    Prepare(); var pair = Enemy.Profile.backstabPair;
                    Vector3 end = Enemy.transform.position + Enemy.transform.rotation * pair.ReceiverRootAt(pair.receiverClip.length);
                    Vector3 displacement = end - Enemy.transform.position;
                    c.Is(displacement.magnitude > .15f, "fixture uses the receiver's authored forward travel", displacement.magnitude);
                    Cube("Obstacle on receiver future route", end + Vector3.up * .85f, new Vector3(.12f, 1.5f, .12f));
                    Physics.SyncTransforms(); c.Is(!PressE(), "receiver travel is checked before starting");
                    Prepare(); end = Enemy.transform.position + Enemy.transform.rotation * pair.ReceiverRootAt(pair.receiverClip.length);
                    float edge = (Enemy.transform.position.z + end.z) * .5f;
                    floor.transform.position = Origin + new Vector3(0, -.5f, (edge - Origin.z - .7f) * .5f);
                    floor.transform.localScale = new Vector3(8, 1, edge - Origin.z + .7f);
                    Physics.SyncTransforms(); c.Is(!PressE(), "no floor beneath the complete receiver route rejects E");
                });
                run("Grounded high step rejects", c =>
                {
                    Prepare(); var at = Enemy.transform.position;
                    Cube("Raised receiver platform", at + Vector3.up * .15f, new Vector3(1.1f, .4f, 1.1f));
                    MoveAndGround(Enemy, new Vector3(at.x, .35f, at.z), Enemy.transform.rotation);
                    c.Is(Enemy.Motor.isGrounded && Mathf.Abs(Enemy.transform.position.y - Player.transform.position.y) > .25f, "fixture has a genuinely grounded height difference");
                    c.Is(!PressE(), "different floor elevation rejects pair");
                });
                run("Tracked, returning, disabled option and Elite gates", c =>
                {
                    Prepare(); EnemyAI.NoticeInterruptedBackstab(Player);
                    c.Is(EnemyAI.HasTrackedTarget && !PressE(), "tracked target cannot be backstabbed even while facing away");
                    Prepare(); Patrol.patrolEnabled = true; Patrol.SetEngaged(Enemy, true); Patrol.SetEngaged(Enemy, false);
                    c.Is(Patrol.IsReturning && !EnemyAI.HasTrackedTarget, "fixture is returning with no currently tracked target");
                    c.Is(!PressE(), "returning enemy remains ineligible");
                    Prepare(); playerProfile.backstabEnabled = false; c.Is(!PressE(), "player feature switch rejects E");
                    Prepare(); EnemyAI.activeTraining = false; c.Is(!PressE(), "inactive practice dummy is not an unaware guard");
                    Prepare(); c.Is(!Elite.HasBackstabReceiver && !Player.TryBackstab(Elite), "Elite remains outside ordinary B2 feature");
                });
                foreach (bool after in new[] { false, true })
                {
                    bool struck = after;
                    run("Player R reset " + (struck ? "after" : "before") + " contact", c =>
                    {
                        Prepare(); c.Is(PressE(), "E starts"); if (struck) UntilContact();
                        float health = Enemy.Health;
                        Player.Tick(new SamuraiInputFrame { resetPressed = true }, 0);
                        c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Player.Animator.speed == 1 && Enemy.Animator.speed == 1,
                            "R releases both owners and restores animator speeds");
                        c.Is(Enemy.Health == health, "R does not apply extra damage");
                        if (!struck) c.Is(Enemy.CanAct && !Enemy.IsToughnessBroken && !Actions(Enemy).IsGettingUp, "standing victim returns to a standing action");
                        else c.Is(Enemy.IsDead && !Enemy.Motor.enabled, "lethal corpse remains dead with collision disabled");
                        Enemy.Tick(new SamuraiInputFrame { resetPressed = true }, 0); EnemyAI.ResetTraining();
                        c.Is(Player.Motor.isGrounded || !Player.IsExecuting, "reset does not leave player locked in a pair");
                        Prepare(); c.Is(PressE(), "a fresh E can backstab again after resetting both actors");
                    });
                }
                run("Receiver disable and reset release the attacker", c =>
                {
                    Prepare(); PressE(); Enemy.gameObject.SetActive(false); Step(.01f);
                    c.Is(!Player.IsExecuting && Player.Animator.speed == 1, "pre-contact disable releases owner");
                    Prepare(); PressE(); UntilContact(); Enemy.ResetCombat(); Step(.01f);
                    c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && !Enemy.IsDead && Enemy.Motor.enabled,
                        "post-contact receiver reset restores a live unpaired receiver");
                });
                run("New obstacle interrupts without teleporting or creating a down recovery", c =>
                {
                    Prepare(); PressE(); float hp = Enemy.Health;
                    Cube("Door closed during entry", Player.transform.position + Vector3.up, new Vector3(2, 2, .1f));
                    Physics.SyncTransforms(); Step(.01f);
                    c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Enemy.Health == hp, "door abort before strike causes no damage");
                    c.Is(Enemy.CanAct && !Actions(Enemy).IsGettingUp && EnemyAI.HasTrackedTarget, "interrupted standing enemy notices the player");
                    Prepare(); PressE(); UntilContact(); var hips = Hips(Enemy); Vector3 before = hips != null ? hips.position : Enemy.transform.position;
                    Cube("Door closed after contact", Player.transform.position + Vector3.up, new Vector3(2, 2, .1f));
                    Physics.SyncTransforms(); Step(.01f);
                    Vector3 frozen = hips != null ? hips.position : Enemy.transform.position;
                    for (int i = 0; i < 20; i++) Step(.01f);
                    c.Is(!Player.IsExecuting && Enemy.IsDead, "door abort after strike leaves a dead released receiver");
                    c.Is(Vector3.Distance(frozen, hips != null ? hips.position : Enemy.transform.position) < .003f,
                        "dead receiver stays at the last collision-safe authored pose");
                    c.Is(Vector3.Distance(before, frozen) < .1f, "abort does not teleport corpse to the clip end", Vector3.Distance(before, frozen));
                });
                run("Synchronous state and damage callback resets are reentrant", c =>
                {
                    Prepare(); Action<string> resetOnEntry = _ => { if (Player.IsBackstabbing) Player.ResetCombat(); };
                    Player.StateChanged += resetOnEntry;
                    try { PressE(); } finally { Player.StateChanged -= resetOnEntry; }
                    c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Enemy.Health == Enemy.MaxHealth, "entry callback cannot start an orphan receiver half");
                    Prepare(); int blood = SamuraiExecutionBlood.BurstCount, shakes = Feedback.ExecutionShakeCount;
                    Action<SamuraiDamageFeedback> resetVictim = _ => Enemy.ResetCombat(); Enemy.DamageReceived += resetVictim;
                    try { PressE(); UntilContact(); } finally { Enemy.DamageReceived -= resetVictim; }
                    c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Enemy.Health == Enemy.MaxHealth, "damage callback resets both halves without stale sampling");
                    c.Is(SamuraiExecutionBlood.BurstCount == blood && Feedback.ExecutionShakeCount == shakes,
                        "cancelled contact callback produces no stale blood or camera event");
                    Prepare(); Action deathReset = () => Player.ResetCombat(); Enemy.Died += deathReset;
                    try { PressE(); UntilContact(); } finally { Enemy.Died -= deathReset; }
                    c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Enemy.IsDead, "death callback safely resets owner");
                });
                run("Pause, zero dt, and E edges do not cache a later trigger", c =>
                {
                    Prepare(); Time.timeScale = 0; Step(0, true); Time.timeScale = timeScale;
                    Step(.01f); c.Is(!Player.IsExecuting, "E while globally paused is not buffered");
                    Prepare(); Player.ApplyHitStop(.10f); Step(0, true); Player.ClearHitStop(); Step(.01f);
                    c.Is(!Player.IsExecuting, "E during local hitstop is not buffered");
                    Prepare(); PressE(); float elapsed = Player.ExecutionElapsed; Vector3 p = Player.transform.position;
                    for (int i = 0; i < 12; i++) Step(0);
                    c.Is(Player.ExecutionElapsed == elapsed && Player.transform.position == p && !Enemy.IsDead,
                        "zero dt freezes both pair clocks and roots");
                    Time.timeScale = 0; for (int i = 0; i < 12; i++) Step(0); Time.timeScale = timeScale;
                    c.Is(Player.ExecutionElapsed == elapsed && Player.transform.position == p, "global pause preserves the active pair");
                    UntilContact(); for (int i = 0; i < 400 && Player.IsExecuting; i++) Step(.01f, true);
                    c.Is(!Player.IsExecuting && Enemy.IsDead, "repeated E edges during a pair cannot queue a second execution");
                });
                run("Old prone execution retains surviving and lethal branches", c =>
                {
                    foreach (bool lethal in new[] { false, true })
                    {
                        Prepare(); var old = lethal ? Enemy.Profile.executionLethalPair : Enemy.Profile.executionSurvivingPair;
                        float health = lethal ? 20 : Enemy.MaxHealth;
                        if (Enemy.Health > health) Vitals(Enemy).Damage(Enemy.Health - health);
                        Actions(Enemy).ReactToToughnessBreak();
                        MoveAndGround(Enemy, Origin + new Vector3(0, 0, 1.10f), Quaternion.Euler(0, 180, 0));
                        c.Is(Player.CanExecuteTarget(Enemy), "old prone target remains eligible, lethal=" + lethal);
                        Step(.01f, true); c.Is(Player.IsExecuting && !Player.IsBackstabbing && Player.ActiveExecutionPair == old,
                            "E selects the original prone pair, lethal=" + lethal);
                        for (int i = 0; i < 950; i++) Step(.01f);
                        c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Enemy.IsDead == lethal,
                            "old pair completes with original health result, lethal=" + lethal);
                        if (!lethal) c.Is(Enemy.Health > 0 && !Actions(Enemy).IsGettingUp, "surviving old receiver completes its recovery");
                    }
                });
            }
            finally { Time.timeScale = timeScale; }
            var report = new
            {
                kind = "B2 formal backstab paused-Play validation", utc = DateTime.UtcNow.ToString("o"),
                originalSceneActorsDriven = false, sourceAssetsModified = false,
                aiEnabled = EnemyAI.enabled, waitForPlayerCapture = EnemyAI.waitForPlayerCapture,
                assertions = cases.Sum(c => c.checks.Count), passed = cases.All(c => c.passed), cases
            };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            File.WriteAllText(outputPath, json); return json;
        }

        static void Positive(Case c, int hz, float yaw)
        {
            Prepare(yaw); float dt = 1f / hz;
            c.Is(EnemyAI.enabled && EnemyAI.activeTraining && !EnemyAI.waitForPlayerCapture,
                "real enemy AI is enabled and independent of mouse capture");
            c.Is(Player.Motor.isGrounded && Enemy.Motor.isGrounded, "both real CCs are grounded by a downward Move");
            c.Is(Enemy.Health == Enemy.MaxHealth && !EnemyAI.HasTrackedTarget && EnemyAI.IsUnawareForBackstab(Player),
                "full-health enemy is genuinely untracked and unaware");
            int damage = 0, deaths = 0, blood = SamuraiExecutionBlood.BurstCount, shakes = Feedback.ExecutionShakeCount;
            float firstDamage = -1, completedAt = -1, maximumSource = 0, maxZoom = 0, maxShake = 0;
            var pair = Enemy.Profile.backstabPair; var receiverHome = Enemy.transform.position;
            float receiverAnchorDriftDuringPair = 0;
            float holdStart = -1, holdEnd = -1; Vector3 heldPosition = Vector3.zero; bool holdRootStable = true;
            Action<SamuraiDamageFeedback> onDamage = _ => { damage++; if (firstDamage < 0) firstDamage = SimulatedSeconds + dt; };
            Action onDeath = () => deaths++;
            Enemy.DamageReceived += onDamage; Enemy.Died += onDeath;
            float playerClock = Player.Actions.Clock, enemyClock = Enemy.Actions.Clock;
            try
            {
                c.Is(PressE(dt), "real player E gateway starts B2");
                c.Is(Player.ActiveExecutionPair == pair && Enemy.IsBeingExecuted, "both halves select the same formal B2 pair");
                c.Is(Mathf.Abs(Player.Actions.Clock - playerClock - dt) < .002f && Mathf.Abs(Enemy.Actions.Clock - enemyClock - dt) < .002f,
                    "one frame advances each actor clock once");
                for (int frame = 0; frame < hz * 4 && Player.IsExecuting; frame++)
                {
                    // After the completion frame Enemy.Tick legitimately runs MoveCorpse and settles its Y.
                    // While the owner still owns the pair, its receiver logical root must remain fully fixed.
                    if (Enemy.IsBeingExecuted) receiverAnchorDriftDuringPair = Mathf.Max(receiverAnchorDriftDuringPair,
                        Vector3.Distance(receiverHome, Enemy.transform.position));
                    float source = Player.ExecutionElapsed; maximumSource = Mathf.Max(maximumSource, source);
                    maxZoom = Mathf.Max(maxZoom, Mathf.Abs(Feedback.ExecutionFovOffset));
                    maxShake = Mathf.Max(maxShake, Feedback.ShakeEuler.magnitude);
                    if (Mathf.Abs(source - pair.strikeSeconds) < .00005f)
                    {
                        if (holdStart < 0) { holdStart = SimulatedSeconds; heldPosition = Player.transform.position; }
                        holdRootStable &= Vector3.Distance(Player.transform.position, heldPosition) < .0001f;
                        holdEnd = SimulatedSeconds;
                    }
                    if (frame == 0) c.Is(Enemy.ReceiveHit(5, 0, Player.transform.position, Player) == SamuraiHitResult.Ignored,
                        "third-party hit cannot split the committed pair");
                    Step(dt);
                }
                completedAt = SimulatedSeconds;
                c.Is(damage == 1 && deaths == 1 && Enemy.Health == 0, "full health is killed exactly once", new { damage, deaths, health = Enemy.Health });
                c.Is(SamuraiExecutionBlood.BurstCount - blood == 1 && Feedback.ExecutionShakeCount - shakes == 1,
                    "exactly one blood burst and one execution shake", new { blood = SamuraiExecutionBlood.BurstCount - blood, shakes = Feedback.ExecutionShakeCount - shakes });
                float expectedContact = pair.alignSeconds + pair.PreparationSeconds + pair.PlaybackSecondsAt(pair.strikeSeconds);
                c.Is(firstDamage >= expectedContact - .001f && firstDamage <= expectedContact + dt + .002f,
                    "damage occurs at the authored contact clock", new { firstDamage, expectedContact });
                c.Is(holdStart >= 0 && holdRootStable && holdEnd - holdStart + dt >= pair.impactHoldSeconds - dt - .002f,
                    "shared contact hold freezes root movement for the retained duration", new { holdStart, holdEnd });
                c.Is(maxZoom > .5f && maxShake > .05f, "zoom and shake actually sample during the formal pair", new { maxZoom, maxShake });
                c.Is(completedAt >= pair.TotalDuration - .002f && completedAt <= pair.TotalDuration + dt + .003f,
                    "complete delayed receiver and attacker tail play before release", new { completedAt, pair.TotalDuration, maximumSource });
                c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Player.CanAct && Enemy.IsDead && !Enemy.Motor.enabled,
                    "player is actionable and receiver is a released corpse");
                c.Is(Player.Animator.speed == 1 && Enemy.Animator.speed == 1, "both animator speeds restored");
                c.Is(receiverAnchorDriftDuringPair < .0001f,
                    "receiver logical anchor stays fixed throughout the owned pair", receiverAnchorDriftDuringPair);
                Vector3 afterRelease = Enemy.transform.position - receiverHome;
                c.Is(new Vector2(afterRelease.x, afterRelease.z).sqrMagnitude < .00000001f,
                    "released corpse never drifts horizontally", new[]{afterRelease.x,afterRelease.y,afterRelease.z});
                c.Is(Enemy.transform.position.y <= receiverHome.y + .0001f && Enemy.transform.position.y >= Origin.y - .001f,
                    "released corpse may settle vertically to the real floor", new { initialY = receiverHome.y, releasedY = Enemy.transform.position.y });
                c.Is(PlayerTickCount == EnemyTickCount, "AI and player have a single matched Tick owner", new { PlayerTickCount, EnemyTickCount });
            }
            finally { Enemy.DamageReceived -= onDamage; Enemy.Died -= onDeath; }
        }

        static float IdleTailDifference(AnimationClip attack, AnimationClip idle, out int count)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackSource);
            if (source == null || idle == null) { count = 0; return float.PositiveInfinity; }
            float time = Mathf.Repeat(attack.length - source.length, idle.length);
            var muscleNames = new HashSet<string>(HumanTrait.MuscleName); float maximum = 0; count = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(attack))
            {
                if (binding.type != typeof(Animator) || !muscleNames.Contains(binding.propertyName)) continue;
                var expected = AnimationUtility.GetEditorCurve(idle, binding); if (expected == null) continue;
                var actual = AnimationUtility.GetEditorCurve(attack, binding); count++;
                maximum = Mathf.Max(maximum, Mathf.Abs(actual.Evaluate(attack.length) - expected.Evaluate(time)));
            }
            return maximum;
        }

        static void ClearBlood()
        {
            if (Player == null && Enemy == null) return;
            foreach (var effect in UnityEngine.Object.FindObjectsByType<SamuraiExecutionBlood>())
            {
                var owner = Field<SamuraiCombatant>(effect, "owner");
                if (owner == Player || owner == Enemy) UnityEngine.Object.DestroyImmediate(effect.gameObject);
            }
        }

        /// <summary>Leaves the isolated fixture prepared for parent-driven image capture; camera is never made the main camera.</summary>
        public static string Render(string outputPath, float bloodAge = .06f, float yaw = 0)
        {
            RequireSetup();
            foreach (var effect in UnityEngine.Object.FindObjectsByType<SamuraiExecutionBlood>())
                if (Field<SamuraiCombatant>(effect, "owner") == Player) effect.SendMessage("Draw", bloodAge);
            var facing = Quaternion.Euler(0, yaw, 0);
            View.transform.position = Origin + facing * new Vector3(3.6f, 2.3f, 3.5f);
            View.transform.LookAt(Origin + facing * new Vector3(0, .85f, .9f));
            var rt = new RenderTexture(1200, 900, 24); var previous = RenderTexture.active; var previousTarget = View.targetTexture;
            Texture2D image = null;
            try
            {
                rt.Create(); View.targetTexture = rt; View.Render(); RenderTexture.active = rt;
                image = new Texture2D(1200, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; View.targetTexture = previousTarget; rt.Release();
                UnityEngine.Object.DestroyImmediate(rt); if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
            return outputPath;
        }

        public static string Cleanup()
        {
            ClearBlood();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            foreach (var item in clones) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            clones.Clear(); obstacles.Clear();
            if (savedTimeScale > 0 && Application.isPlaying) Time.timeScale = savedTimeScale;
            root = floor = routeObject = null; Player = Enemy = Elite = null; EnemyAI = null; Patrol = null;
            View = null; Orbit = null; Feedback = null; route = null; playerProfile = null;
            return "Temporary B2 validation actors and cloned profiles removed; original actors/assets/progress untouched.";
        }
    }
}
