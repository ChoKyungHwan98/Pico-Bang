using System.Collections.Generic;
using UnityEngine;

/// <summary>Runtime-generated exit: torus frame, rotating light arcs and a shader surface. No imported art assets.</summary>
public sealed class ProceduralExitPortal : MonoBehaviour
{
    public bool IsUnlocked { get; private set; }
    private Portal portal;
    private BoxCollider trigger;
    private Material frameMaterial, lightMaterial, surfaceMaterial;
    private Mesh ringMesh, surfaceMesh;
    private Transform arcRoot;
    private readonly List<Material> ownedMaterials = new List<Material>();

    public static ProceduralExitPortal Create(Vector3 position, Quaternion rotation)
    {
        var root = new GameObject("ExitPortal_Procedural");
        root.transform.SetPositionAndRotation(position, rotation);
        var result = root.AddComponent<ProceduralExitPortal>(); result.Build(); return result;
    }

    private Material Material(string shader, Color color)
    {
        Shader found = Shader.Find(shader);
        if (found == null) { found = Shader.Find("Universal Render Pipeline/Unlit"); }
        var material = new Material(found);
        if (material.HasProperty("_BaseColor")) { material.SetColor("_BaseColor", color); }
        if (material.HasProperty("_Color")) { material.SetColor("_Color", color); }
        ownedMaterials.Add(material); return material;
    }

    private void Build()
    {
        frameMaterial = Material("Universal Render Pipeline/Lit", new Color(.075f, .105f, .14f));
        frameMaterial.SetFloat("_Metallic", .65f); frameMaterial.SetFloat("_Smoothness", .65f);
        lightMaterial = Material("Universal Render Pipeline/Unlit", new Color(.15f,.7f,.85f));
        surfaceMaterial = Material("PicoBang/ExitPortal", Color.cyan);
        ringMesh = Torus(1.65f, .15f, 72, 10);
        MeshObject("Frame", ringMesh, frameMaterial, new Vector3(0,2.3f,0), new Vector3(1,1.3f,1));
        MeshObject("InnerLight", ringMesh, lightMaterial, new Vector3(0,2.3f,-.015f), new Vector3(.93f,1.21f,.38f));
        var baseRing = MeshObject("GroundRing", ringMesh, frameMaterial, new Vector3(0,.12f,0), new Vector3(1.25f,1.25f,.6f));
        baseRing.localRotation = Quaternion.Euler(90,0,0);
        var floorLight = MeshObject("GroundLight", ringMesh, lightMaterial, new Vector3(0,.16f,0), new Vector3(1.11f,1.11f,.2f));
        floorLight.localRotation = Quaternion.Euler(90,0,0);

        surfaceMesh = new Mesh { name = "PortalSurface" };
        surfaceMesh.vertices = new[] {new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        surfaceMesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
        surfaceMesh.triangles = new[] {0,2,1,0,3,2}; surfaceMesh.RecalculateNormals(); surfaceMesh.RecalculateBounds();
        MeshObject("EnergySurface", surfaceMesh, surfaceMaterial, new Vector3(0,2.3f,0), new Vector3(1.51f,1.98f,1));

        arcRoot = new GameObject("OrbitingArcs").transform; arcRoot.SetParent(transform,false); arcRoot.localPosition = new Vector3(0,2.3f,-.2f);
        for (int a = 0; a < 3; a++)
        {
            var go = new GameObject("Arc" + a); go.transform.SetParent(arcRoot,false);
            var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.sharedMaterial = lightMaterial;
            line.widthMultiplier = .06f; line.positionCount = 25;
            for (int i = 0; i < 25; i++)
            {
                float angle = (a * 120f + i * 2.8f) * Mathf.Deg2Rad;
                line.SetPosition(i,new Vector3(Mathf.Cos(angle)*1.85f,Mathf.Sin(angle)*1.85f,0));
            }
        }
        arcRoot.localScale = new Vector3(1,1.3f,1);
        trigger = gameObject.AddComponent<BoxCollider>(); trigger.isTrigger = true;
        trigger.center = new Vector3(0,2.1f,0); trigger.size = new Vector3(2.7f,4.2f,1.5f);
        portal = gameObject.AddComponent<Portal>();
        SetUnlocked(false);
    }

    public void SetUnlocked(bool value)
    {
        IsUnlocked = value;
        if (portal != null) { portal.IsUnlocked = value; }
        if (trigger != null) { trigger.enabled = value; }
        if (surfaceMaterial != null) { surfaceMaterial.SetFloat("_Unlocked", value ? 1 : 0); }
        if (lightMaterial != null) { lightMaterial.SetColor("_BaseColor", value ? new Color(.12f,1f,1.5f) : new Color(.22f,.32f,.36f)); }
        if (arcRoot != null) { arcRoot.gameObject.SetActive(value); }
        // The exit appears only after the target goal, including its frame and floor ring.
        gameObject.SetActive(value);
    }
    private void Update()
    {
        if (IsUnlocked && arcRoot != null)
        {
            foreach (Transform arc in arcRoot) { arc.localRotation = Quaternion.Euler(0,0,Time.time*24f); }
        }
    }
    private Transform MeshObject(string objectName, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(objectName); go.transform.SetParent(transform,false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return go.transform;
    }
    private static Mesh Torus(float radius, float tube, int segments, int sides)
    {
        var vertices = new Vector3[(segments+1)*(sides+1)]; var indices = new int[segments*sides*6]; int k=0;
        for (int s=0;s<=segments;s++) for (int t=0;t<=sides;t++)
        {
            float a=s*2*Mathf.PI/segments,b=t*2*Mathf.PI/sides;
            vertices[s*(sides+1)+t]=new Vector3(Mathf.Cos(a)*(radius+Mathf.Cos(b)*tube),Mathf.Sin(a)*(radius+Mathf.Cos(b)*tube),Mathf.Sin(b)*tube);
            if(s==segments||t==sides) continue;
            int v=s*(sides+1)+t;
            indices[k++]=v;indices[k++]=v+sides+1;indices[k++]=v+1;
            indices[k++]=v+1;indices[k++]=v+sides+1;indices[k++]=v+sides+2;
        }
        var mesh=new Mesh{name="PortalTorus",vertices=vertices,triangles=indices}; mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private void OnDestroy()
    {
        foreach(var m in ownedMaterials) if(m!=null) Destroy(m);
        if(ringMesh!=null) Destroy(ringMesh); if(surfaceMesh!=null) Destroy(surfaceMesh);
    }
}
