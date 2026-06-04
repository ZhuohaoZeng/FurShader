using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class FurShellRenderer : MonoBehaviour
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
    public int shellCount = 120;

    [Min(0f)]
    public float furLength = 0.1f;

    [Range(0f, 20f)]
    public float furDensity = 1f;

    [Range(0.01f, 10f)]
    public float furThinness = 1f;

    [Range(0.0f, 1f)]
    public float furShading = 0.25f;
    [Header("Fur Ambient Occlusion")]
    public Color occlusionColor = new Color(0.15f, 0.12f, 0.10f, 1f);

    [Range(0f, 5f)]
    public float fresnelLV = 1f;

    [Header("Force Settings")]
    public Vector3 forceGlobal = Vector3.zero;
    public Vector3 forceLocal = Vector3.zero;

    [Header("Rim Lighting")]
    public Color rimColor = Color.black;

    [Range(0.0f, 8.0f)]
    public float rimPower = 6.0f;

    [Header("Render Settings")]
    public int surfaceRenderQueue = (int)RenderQueue.Geometry;
    public int shellRenderQueue = (int)RenderQueue.Transparent;
    public ShadowCastingMode shellShadowCastingMode = ShadowCastingMode.Off;
    public bool shellReceiveShadows = false;

    [Tooltip("Extra expansion for procedural draw bounds. Increase this if fur disappears near screen edges.")]
    [Min(0f)]
    public float extraBoundsPadding = 0.25f;

    [Header("Auto Render")]
    public bool autoRender = true;
    public bool assignSurfaceMaterial = true;

    private const string OldShellRootName = "__Generated_Fur_Shells";

    private static readonly int FurStepId = Shader.PropertyToID("_FurStep");
    private static readonly int FurLayerCountId = Shader.PropertyToID("_FurLayerCount");
    private static readonly int FurLengthId = Shader.PropertyToID("_FurLength");
    private static readonly int FurDensityId = Shader.PropertyToID("_FurDensity");
    private static readonly int FurThinnessId = Shader.PropertyToID("_FurThinness");
    private static readonly int FurShadingId = Shader.PropertyToID("_FurShading");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SpecularId = Shader.PropertyToID("_Specular");
    private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
    private static readonly int OcclusionStrengthId = Shader.PropertyToID("_OcclusionStrength");
    private static readonly int ForceGlobalId = Shader.PropertyToID("_ForceGlobal");
    private static readonly int ForceLocalId = Shader.PropertyToID("_ForceLocal");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int OcclusionColorId = Shader.PropertyToID("_OcclusionColor");
    private static readonly int FresnelLVId = Shader.PropertyToID("_FresnelLV");

    private MaterialPropertyBlock surfaceBlock;
    private MaterialPropertyBlock shellBlock;
    private Matrix4x4[] instanceMatrices;

    private void Reset()
    {
        sourceRenderer = GetComponent<MeshRenderer>();
        sourceMeshFilter = GetComponent<MeshFilter>();
    }

    private void OnEnable()
    {
        EnsureSourceReferences();
        EnsureRuntimeData();
        ApplySurfaceSettings();
        ConfigureMaterials();
    }

    private void OnValidate()
    {
        shellCount = Mathf.Max(1, shellCount);
        furLength = Mathf.Max(0f, furLength);
        shininess = Mathf.Max(0.01f, shininess);
        furThinness = Mathf.Max(0.01f, furThinness);
        furShading = Mathf.Clamp01(furShading);
        rimPower = Mathf.Clamp(rimPower, 0.0f, 8.0f);

        EnsureRuntimeData();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    private void Update()
    {
        if (!autoRender)
            return;

        RenderFur();
    }

    /// <summary>
    /// Call this manually if autoRender is disabled.
    /// </summary>
    public void RenderFur()
    {
        EnsureSourceReferences();

        if (!IsReady())
            return;

        EnsureRuntimeData();
        ConfigureMaterials();
        ApplySurfaceSettings();
        DrawInstancedShells();
    }

    [ContextMenu("Clear Old Generated Shell Objects")]
    public void ClearOldGeneratedShellObjects()
    {
        Transform oldRoot = transform.Find(OldShellRootName);
        if (oldRoot == null)
            return;

        if (Application.isPlaying)
            Destroy(oldRoot.gameObject);
        else
            DestroyImmediate(oldRoot.gameObject);
    }

    private void EnsureSourceReferences()
    {
        if (sourceRenderer == null)
            sourceRenderer = GetComponent<MeshRenderer>();

        if (sourceMeshFilter == null)
            sourceMeshFilter = GetComponent<MeshFilter>();
    }

    private bool IsReady()
    {
        return sourceRenderer != null &&
               sourceMeshFilter != null &&
               sourceMeshFilter.sharedMesh != null &&
               shellMaterial != null;
    }

    private void EnsureRuntimeData()
    {
        surfaceBlock ??= new MaterialPropertyBlock();
        shellBlock ??= new MaterialPropertyBlock();

        if (instanceMatrices == null || instanceMatrices.Length != shellCount)
            instanceMatrices = new Matrix4x4[shellCount];
    }

    private void ConfigureMaterials()
    {
        if (surfaceMaterial != null)
            ConfigureSurfaceMaterial(surfaceMaterial, surfaceRenderQueue);

        if (shellMaterial != null)
        {
            ConfigureShellMaterial(shellMaterial, shellRenderQueue);
            shellMaterial.enableInstancing = true;
        }
    }

    private void ConfigureSurfaceMaterial(Material mat, int renderQueue)
    {
        if (mat == null)
            return;

        // Surface keeps its own AO settings from the Material inspector.
        // Do NOT override _OcclusionStrength / _OcclusionColor / _FresnelLV here.
        SetMaterialColorIfExists(mat, ColorId, color);
        SetMaterialColorIfExists(mat, SpecularId, specular);
        SetMaterialFloatIfExists(mat, ShininessId, shininess);

        mat.renderQueue = renderQueue;

#if UNITY_EDITOR
        EditorUtility.SetDirty(mat);
#endif
    }

    private void ConfigureShellMaterial(Material mat, int renderQueue)
    {
        if (mat == null)
            return;

        // Shell/fur-only parameters.
        SetMaterialFloatIfExists(mat, FurLayerCountId, shellCount);
        SetMaterialFloatIfExists(mat, FurLengthId, furLength);
        SetMaterialFloatIfExists(mat, FurDensityId, furDensity);
        SetMaterialFloatIfExists(mat, FurThinnessId, furThinness);
        SetMaterialFloatIfExists(mat, FurShadingId, furShading);
        SetMaterialColorIfExists(mat, ColorId, color);
        SetMaterialColorIfExists(mat, SpecularId, specular);
        SetMaterialFloatIfExists(mat, ShininessId, shininess);
        SetMaterialVectorIfExists(mat, ForceGlobalId, forceGlobal);
        SetMaterialVectorIfExists(mat, ForceLocalId, forceLocal);
        SetMaterialColorIfExists(mat, RimColorId, rimColor);
        SetMaterialFloatIfExists(mat, RimPowerId, rimPower);
        SetMaterialColorIfExists(mat, OcclusionColorId, occlusionColor);
        SetMaterialFloatIfExists(mat, FresnelLVId, fresnelLV);

        mat.renderQueue = renderQueue;

#if UNITY_EDITOR
        EditorUtility.SetDirty(mat);
#endif
    }

    private void ApplySurfaceSettings()
    {
        if (sourceRenderer == null)
            return;

        if (assignSurfaceMaterial && surfaceMaterial != null)
            sourceRenderer.sharedMaterial = surfaceMaterial;

        sourceRenderer.GetPropertyBlock(surfaceBlock);
        WriteSurfaceProperties(surfaceBlock);

        // Surface layer is not a shell, but setting this keeps compatibility
        // with surface shaders that still expose _FurStep.
        surfaceBlock.SetFloat(FurStepId, 0f);

        sourceRenderer.SetPropertyBlock(surfaceBlock);
    }

    private void DrawInstancedShells()
    {
        Mesh mesh = sourceMeshFilter.sharedMesh;

        Matrix4x4 objectToWorld = transform.localToWorldMatrix;
        for (int i = 0; i < shellCount; i++)
            instanceMatrices[i] = objectToWorld;

        shellBlock.Clear();
        WriteShellProperties(shellBlock);

        // The shell shader should use this with SV_InstanceID:
        // furStep = (instanceID + 1.0) / _FurLayerCount.
        shellBlock.SetFloat(FurLayerCountId, shellCount);

        RenderParams renderParams = new RenderParams(shellMaterial)
        {
            matProps = shellBlock,
            layer = gameObject.layer,
            shadowCastingMode = shellShadowCastingMode,
            receiveShadows = shellReceiveShadows,
            worldBounds = ComputeExpandedWorldBounds(mesh)
        };

        Graphics.RenderMeshInstanced(
            renderParams,
            mesh,
            0,
            instanceMatrices,
            shellCount
        );
    }

    private void WriteSurfaceProperties(MaterialPropertyBlock block)
    {
        // Surface uses its own/default occlusion settings from the material.
        // Do not set _OcclusionStrength, _OcclusionColor, or _FresnelLV here.
        block.SetColor(ColorId, color);
        block.SetColor(SpecularId, specular);
        block.SetFloat(ShininessId, shininess);
    }

    private void WriteShellProperties(MaterialPropertyBlock block)
    {
        block.SetFloat(FurLayerCountId, shellCount);
        block.SetFloat(FurLengthId, furLength);
        block.SetFloat(FurDensityId, furDensity);
        block.SetFloat(FurThinnessId, furThinness);
        block.SetFloat(FurShadingId, furShading);
        block.SetColor(ColorId, color);
        block.SetColor(SpecularId, specular);
        block.SetFloat(ShininessId, shininess);
        block.SetVector(ForceGlobalId, forceGlobal);
        block.SetVector(ForceLocalId, forceLocal);
        block.SetColor(RimColorId, rimColor);
        block.SetFloat(RimPowerId, rimPower);
        block.SetColor(OcclusionColorId, occlusionColor);
        block.SetFloat(FresnelLVId, fresnelLV);
    }

    private Bounds ComputeExpandedWorldBounds(Mesh mesh)
    {
        Bounds localBounds = mesh.bounds;
        Bounds worldBounds = TransformBounds(transform.localToWorldMatrix, localBounds);

        float forcePadding = Mathf.Max(forceGlobal.magnitude, forceLocal.magnitude);
        worldBounds.Expand(furLength * 2.0f + forcePadding + extraBoundsPadding);

        return worldBounds;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
    {
        Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
        Vector3 extents = localBounds.extents;

        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, extents * 2f);
    }

    private static void SetMaterialFloatIfExists(Material mat, int propertyId, float value)
    {
        if (mat.HasProperty(propertyId))
            mat.SetFloat(propertyId, value);
    }

    private static void SetMaterialVectorIfExists(Material mat, int propertyId, Vector4 value)
    {
        if (mat.HasProperty(propertyId))
            mat.SetVector(propertyId, value);
    }

    private static void SetMaterialColorIfExists(Material mat, int propertyId, Color value)
    {
        if (mat.HasProperty(propertyId))
            mat.SetColor(propertyId, value);
    }
}
