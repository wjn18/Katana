using UnityEngine;

namespace OCDesign.LookDev
{
    /// <summary>Keeps an outline-only renderer in step with the visible source, without changing animation.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ComicContourFollower : MonoBehaviour
    {
        public Renderer source;
        public Renderer contour;

        void OnEnable() => Synchronize();
        void LateUpdate() => Synchronize();

        public void Synchronize()
        {
            if (!contour) return;
            contour.enabled = source && source.enabled;
            if (!source) return;
            contour.forceRenderingOff = source.forceRenderingOff;
            gameObject.layer = source.gameObject.layer;
            var skin = source as SkinnedMeshRenderer;
            var shell = contour as SkinnedMeshRenderer;
            if (!skin || !shell || !skin.sharedMesh || !shell.sharedMesh) return;
            shell.localBounds = skin.localBounds;
            int count = Mathf.Min(skin.sharedMesh.blendShapeCount, shell.sharedMesh.blendShapeCount);
            for (int i = 0; i < count; i++) shell.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
        }

        void OnDisable()
        {
            if (contour) contour.enabled = false;
        }
    }
}
