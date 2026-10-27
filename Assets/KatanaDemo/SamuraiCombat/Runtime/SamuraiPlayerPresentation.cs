using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 独立玩家的可选镜头/HUD 装配与生命周期。场景已注入镜头或已有主相机时直接复用；
    /// 只有完全没有镜头时才创建默认展示对象。不会接管输入或驱动战斗。
    /// </summary>
    public sealed class SamuraiPlayerPresentation : IDisposable
    {
        private GameObject ownedRuntimeCamera;

        public void EnsureView(SamuraiCombatant actor, SamuraiPlayerInput input, SamuraiTrainingEnemy enemy,
            ref Transform cameraTransform, ref SamuraiOrbitCamera orbitCamera)
        {
            if (cameraTransform != null || actor == null) return;
            Camera existing = Camera.main;
            if (existing != null)
            {
                cameraTransform = existing.transform;
                return;
            }
            ownedRuntimeCamera = new GameObject("Samurai Runtime Camera");
            Camera view = ownedRuntimeCamera.AddComponent<Camera>();
            view.tag = "MainCamera";
            view.fieldOfView = 48f;
            view.nearClipPlane = 0.08f;
            view.farClipPlane = 100f;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.10f, 0.14f, 0.18f);
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
                ownedRuntimeCamera.AddComponent<AudioListener>();
            orbitCamera = ownedRuntimeCamera.AddComponent<SamuraiOrbitCamera>();
            cameraTransform = view.transform;
            orbitCamera.Configure(actor.transform, input);
            // HUD 由场景内可编辑的 Samurai HUD Canvas 提供，不再生成 IMGUI 叠层。
        }

        public void Dispose()
        {
            if (ownedRuntimeCamera != null) UnityEngine.Object.Destroy(ownedRuntimeCamera);
            ownedRuntimeCamera = null;
        }
    }
}
