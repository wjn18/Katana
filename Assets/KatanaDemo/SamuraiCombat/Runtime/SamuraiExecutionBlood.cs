using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>Short directional ribbons, ballistic drops and ground splashes; no global effects or materials.</summary>
    public sealed class SamuraiExecutionBlood : MonoBehaviour
    {
        struct Drop { public Vector3 velocity,offset;public float width,life;public Color color; }
        SamuraiCombatant owner,victim;
        Mesh mesh;Drop[] drops;Vector3 origin,forward;float elapsed,groundY;
        Vector3[] vertices;Color[] colors;Vector2[] uv;int[] triangles;
        public static int BurstCount { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetCount(){BurstCount=0;}
        public static void Spawn(SamuraiCombatant owner,SamuraiCombatant victim,Vector3 point,Vector3 facing,SamuraiExecutionPair pair)
        {
            if(pair.bloodMaterial==null||pair.bloodIntensity<=0)return;
            var go=new GameObject("Execution blood burst");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,owner.gameObject.scene);
            var effect=go.AddComponent<SamuraiExecutionBlood>();effect.Initialize(owner,victim,point,facing,pair);BurstCount++;
        }
        void Initialize(SamuraiCombatant source,SamuraiCombatant target,Vector3 point,Vector3 facing,SamuraiExecutionPair pair)
        {
            owner=source;victim=target;origin=point;forward=facing;groundY=target.transform.position.y+.012f;
            owner.CombatReset+=Clear;victim.CombatReset+=Clear;
            var random=new System.Random(unchecked(Time.frameCount*397+BurstCount));int count=Mathf.Clamp(Mathf.RoundToInt(140*pair.bloodIntensity),40,360);
            drops=new Drop[count];vertices=new Vector3[count*4];colors=new Color[count*4];uv=new Vector2[count*4];triangles=new int[count*6];
            Vector3 side=Vector3.Cross(Vector3.up,forward);
            for(int i=0;i<count;i++)
            {
                float a=(float)random.NextDouble()*Mathf.PI*2;float power=1+(float)random.NextDouble()*5.5f;
                Vector3 direction=(forward*((float)random.NextDouble()*.7f-.2f)+side*Mathf.Cos(a)*1.15f+Vector3.up*(.3f+Mathf.Abs(Mathf.Sin(a))*.9f)).normalized;
                bool jet=i%5==0;
                drops[i]=new Drop {velocity=direction*power*(jet?1.35f:1),offset=side*((float)random.NextDouble()-.5f)*.16f,
                    width=(jet?.065f:.012f+(float)random.NextDouble()*.04f)*Mathf.Sqrt(pair.bloodIntensity),life=.35f+(float)random.NextDouble()*.7f,
                    color=Color.Lerp(new Color(.07f,.001f,.004f,.98f),new Color(.48f,.004f,.012f,.96f),(float)random.NextDouble())};
                int v=i*4,k=i*6;triangles[k]=v;triangles[k+1]=v+1;triangles[k+2]=v+2;triangles[k+3]=v;triangles[k+4]=v+2;triangles[k+5]=v+3;
                uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;
            }
            mesh=new Mesh {name="Execution blood transient mesh"};mesh.MarkDynamic();
            var filter=gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;var renderer=gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.bloodMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            Draw(0);
        }
        void Update()
        {
            if(owner==null||victim==null||!owner.isActiveAndEnabled||!victim.gameObject.activeInHierarchy){Clear();return;}
            if(Time.timeScale<=0)return;elapsed+=Time.deltaTime;if(elapsed>2.2f){Clear();return;}Draw(elapsed);
        }
        Vector3 Position(Drop d,float t)=>origin+d.offset+d.velocity*t+Vector3.down*(5.5f*t*t);
        void Draw(float t)
        {
            var camera=Camera.main;Vector3 view=camera!=null?camera.transform.forward:forward;
            for(int i=0;i<drops.Length;i++)
            {
                var d=drops[i];Vector3 p=Position(d,t),q=Position(d,Mathf.Max(0,t-(i%5==0?.075f:.016f)));
                float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.1f,2.2f,t));Vector3 a,b,c,e;
                if(p.y<=groundY)
                {
                    // Solve first floor crossing so stains do not slide across the floor.
                    float hit=(d.velocity.y+Mathf.Sqrt(d.velocity.y*d.velocity.y+22*Mathf.Max(0,origin.y+d.offset.y-groundY)))/11;
                    p=Position(d,hit);p.y=groundY+(i%3)*.0003f;
                    float w=d.width*(i%5==0?2:1.4f);Vector3 x=new Vector3(Mathf.Cos(i*2.4f),0,Mathf.Sin(i*2.4f))*w;
                    Vector3 z=Vector3.Cross(Vector3.up,x)*2.1f;a=p-x-z;b=p+x-z;c=p+x+z;e=p-x+z;
                }
                else
                {
                    fade*=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(d.life*.7f,d.life+.25f,t));
                    Vector3 width=Vector3.Cross(view,p-q).normalized*d.width*(1-Mathf.Clamp01(t/1.4f)*.6f);
                    if(width.sqrMagnitude<.000001f)width=Vector3.right*d.width;
                    a=q-width;b=q+width;c=p+width*.45f;e=p-width*.45f;
                }
                int v=i*4;vertices[v]=a;vertices[v+1]=b;vertices[v+2]=c;vertices[v+3]=e;var color=d.color;color.a*=fade;
                colors[v]=colors[v+1]=colors[v+2]=colors[v+3]=color;
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();
        }
        void Clear(){if(gameObject!=null)Destroy(gameObject);}
        void OnDestroy(){if(owner!=null)owner.CombatReset-=Clear;if(victim!=null)victim.CombatReset-=Clear;if(mesh!=null)Destroy(mesh);}
    }
}
