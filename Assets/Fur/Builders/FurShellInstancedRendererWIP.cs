using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class FurShellInstancedRendererWIP : MonoBehaviour
{
     [Header("Mesh Source")]
    public MeshRenderer sourceRenderer;
    public MeshFilter sourceMeshFilter;
    
    [Header("Materials")]
    public Material surfaceMaterial;
    public Material shellMaterial;

    [Header("Basic Settings")]
    public Color color = Color.white;
    public Color specular = Color.white;

    [Range(0.01f, 256f)]
    public float shininess = 32f;

    [Range(0f, 1f)]
    public float occlusionStrength = 1f;

    [Header("Fur Settings")]
    [Range(1, 256)]
    public int furLayerCount = 120;

    [Min(0f)]
    public float furLength = 0.1f;

    [Range(0f, 20f)]
    public float furDensity = 1f;

    [Range(0.01f, 10f)]
    public float furThinness = 1f;
    [Range(0.0f, 1f)]
    public float furShading = 0.25f;

    [Header("Force Settings")]
    public Vector3 forceGlobal = Vector3.zero;
    public Vector3 forceLocal = Vector3.zero;

    [Header("Rim Lighting")]
    public Color rimColor = Color.black;

    [Range(0.0f, 8.0f)]
    public float rimPower = 6.0f;

    public ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;
    public bool receiveShadows = false;

}