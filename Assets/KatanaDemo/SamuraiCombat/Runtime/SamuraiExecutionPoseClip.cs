using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>Target-rig curves copied from an approved pose; no second Humanoid retarget or IK.</summary>
    public sealed class SamuraiExecutionPoseClip : ScriptableObject
    {
        [Serializable] public sealed class Track
        {
            public string path;
            // Local position XYZ, quaternion XYZW, local scale XYZ.
            public AnimationCurve[] curves;
        }
        public float duration;
        public float referenceVisualScale=1;
        public float groundContactY=-.015f;
        public AnimationCurve groundLift;
        public Track[] tracks;

        public sealed class Binding
        {
            readonly SamuraiExecutionPoseClip clip;
            readonly Transform[] bones;
            public bool Valid {get;private set;}
            public Binding(SamuraiExecutionPoseClip source,Animator animator)
            {
                clip=source;Valid=source!=null&&animator!=null&&source.tracks!=null&&source.tracks.Length>0;
                if(!Valid){bones=new Transform[0];return;}
                bones=new Transform[source.tracks.Length];
                for(int i=0;i<bones.Length;i++)
                {
                    var track=source.tracks[i];bones[i]=animator.transform.Find(track.path);
                    if(bones[i]==null||track.curves==null||track.curves.Length!=10){Valid=false;continue;}
                    foreach(var curve in track.curves)if(curve==null||curve.length==0)Valid=false;
                }
            }
            public void Apply(float seconds)
            {
                if(!Valid)return;float t=Mathf.Clamp(seconds,0,clip.duration);
                for(int i=0;i<bones.Length;i++)
                {
                    var c=clip.tracks[i].curves;
                    bones[i].localPosition=new Vector3(c[0].Evaluate(t),c[1].Evaluate(t),c[2].Evaluate(t));
                    bones[i].localRotation=new Quaternion(c[3].Evaluate(t),c[4].Evaluate(t),c[5].Evaluate(t),c[6].Evaluate(t)).normalized;
                    bones[i].localScale=new Vector3(c[7].Evaluate(t),c[8].Evaluate(t),c[9].Evaluate(t));
                }
            }
        }
    }
}
