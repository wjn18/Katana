using UnityEngine;

namespace OCDesign.Samurai
{
    [DisallowMultipleComponent,DefaultExecutionOrder(30)]
    public sealed class Elite1LocomotionPlayback:MonoBehaviour
    {
        public SamuraiCombatant actor;
        public float[] walkReferenceSpeeds=new float[8];
        public float[] runReferenceSpeeds=new float[8];
        static readonly string[] Directions={"","ForwardRight","Right","BackwardRight","Backward","BackwardLeft","Left","ForwardLeft"};
        static readonly int Rate=Animator.StringToHash("LocomotionRate");
        Vector3 previous;bool initialized;
        void OnEnable(){previous=transform.position;initialized=true;if(actor!=null)actor.CombatReset+=ResetPosition;}
        void OnDisable(){if(actor!=null)actor.CombatReset-=ResetPosition;initialized=false;}
        void ResetPosition(){previous=transform.position;initialized=true;if(actor!=null&&actor.Animator!=null)actor.Animator.SetFloat(Rate,1);}
        void LateUpdate()=>UpdatePlayback(Time.deltaTime);
        public void UpdatePlayback(float deltaTime)
        {
            if(actor==null||actor.Animator==null)return;var delta=transform.position-previous;previous=transform.position;
            if(!initialized){initialized=true;return;}if(deltaTime<=0||actor.IsHitStopped)return;
            string state=actor.StateName;bool run=state.StartsWith("Run");if(!run&&!state.StartsWith("Walk"))return;
            int index=System.Array.IndexOf(Directions,state.Substring(run?3:4));if(index<0)index=0;var speeds=run?runReferenceSpeeds:walkReferenceSpeeds;float reference=speeds!=null&&speeds.Length>index?speeds[index]:0;
            delta.y=0;actor.Animator.SetFloat(Rate,reference>.01f?Mathf.Clamp(delta.magnitude/deltaTime/reference,0,1.6f):1);
        }
    }
}
