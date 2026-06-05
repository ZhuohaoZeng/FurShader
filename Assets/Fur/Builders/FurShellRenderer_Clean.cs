using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Draws a normal surface material through the source MeshRenderer, then draws fur shell layers
/// with one GPU-instanced procedural call. Surface-only parameters are kept separate from shell-only
/// parameters so artists can tune the two materials independently.
/// </summary>
[ExecuteAlways]
public class FurShellRenderer_Clean : MonoBehaviour
{
    [Header("01 - Mesh Source")]
    [Tooltip("Renderer that draws the real surface/base mesh.")]
    public MeshRenderer sourceRenderer;

    [Tooltip("Mesh source used for both surface and instanced fur shells.")]
    public MeshFilter sourceMeshFilter;

    [Header("02 - Materials")]
    [Tooltip("Opaque/ZWrite surface material. Uses Custom/FurShaderSurface.")]
    public Material surfaceMaterial;

    [Tooltip("Transparent instanced shell material. Uses Custom/FurShaderShell.")]
    public Material shellMaterial;

    [Header("03 - Shared Base Color")]
    [Tooltip("Global tint shared by surface and shell.")]
    public Color baseTint = Color.white;

    [Header("04 - Surface Lighting Only")]
    [Tooltip("Only used by the surface shader. The shell uses Kajiya-Kay strand highlights instead.")]
    public Color surfaceSpecularColor = Color.black;

    [Tooltip("Only used by the surface shader.")]
    [Range(0.01f, 256f)]
    public float surfaceSpecularPower = 32f;

    [Header("05 - Shell Shape / Density")]
    [Tooltip("Number of instanced shell layers. 80-150 is a common test range.")]
    [Range(1, 256)]
    public int shellCount = 120;

    [Tooltip("Total shell extrusion distance along vertex normals.")]
    [Min(0f)]
    public float furLength = 0.1f;

    [Tooltip("Higher values remove more outer shell pixels and make the fur thinner/sparser toward the tips.")]
    [Range(0f, 20f)]
    public float outerLayerDensityFade = 1f;

    [Tooltip("Darkens inner/root shell layers without changing the base texture color.")]
    [Range(0f, 1f)]
    public float rootDarkening = 0.25f;

    [Header("06 - Fur Noise / Alpha Mask")]
    [Tooltip("Noise tiling. Higher values create smaller/denser fur clumps.")]
    [Range(0.01f, 10f)]
    public float noiseTiling = 5f;

    [Tooltip("Weight of the G channel noise. Usually medium/coarse strand groups.")]
    [Range(0f, 2f)]
    public float midNoiseWeight = 0.9f;

    [Tooltip("Weight of the B channel noise. Usually fine strand breakup.")]
    [Range(0f, 2f)]
    public float fineNoiseWeight = 0.8f;

    [Tooltip("Higher values make the fur mask sharper and less fog-like.")]
    [Range(0.1f, 20f)]
    public float alphaSharpness = 1f;

    [Header("07 - Layer UV Offset / Strand Direction")]
    [Tooltip("Direction of per-layer UV drift. This gives shell fur a slanted/flowing strand direction.")]
    public Vector2 layerUVOffset = new Vector2(0f, -1f);

    [Tooltip("Strength of the per-layer UV drift.")]
    [Range(0f, 1f)]
    public float layerUVOffsetStrength = 0.1f;

    [Header("08 - Fur Ambient Occlusion / Rim")]
    [Tooltip("Color used for occluded inner/root shell ambient light.")]
    public Color rootOcclusionColor = new Color(0.15f, 0.12f, 0.10f, 1f);

    [Tooltip("Strength of the environment-driven fur rim/fresnel term.")]
    [Range(0f, 5f)]
    public float furRimStrength = 1f;

    [Header("09 - Fur Direct Light")]
    [Tooltip("Overall direct light intensity on shell fur.")]
    [Range(0f, 5f)]
    public float directLightExposure = 1f;

    [Tooltip("Controls how much direct light reaches back-facing or inner shell pixels. Lower values preserve AO more strongly.")]
    [Range(-1f, 1f)]
    public float directLightPenetration = 0.1f;

    [Header("10 - Kajiya-Kay Strand Highlights")]
    [Tooltip("Usually the narrow/white primary strand highlight.")]
    public Color primaryStrandSpecColor = Color.white;

    [Tooltip("Usually the wider/tinted secondary strand highlight.")]
    public Color secondaryStrandSpecColor = new Color(1f, 0.85f, 0.65f, 1f);

    [Range(1f, 256f)]
    public float primaryStrandSpecPower = 64f;

    [Range(1f, 256f)]
    public float secondaryStrandSpecPower = 32f;

    [Tooltip("Offsets the first strand highlight along the normal.")]
    [Range(-1f, 1f)]
    public float primarySpecShift = 0.1f;

    [Tooltip("Offsets the second strand highlight along the normal.")]
    [Range(-1f, 1f)]
    public float secondarySpecShift = -0.2f;

    [Range(0f, 5f)]
    public float strandSpecularStrength = 1f;

    [Header("11 - Force / Wind")]
    [Tooltip("World-space force direction, converted to object space in the shader.")]
    public Vector3 globalForce = Vector3.zero;

    [Tooltip("Object-space force direction.")]
    public Vector3 localForce = Vector3.zero;

    [Tooltip("Higher values keep roots more stable and bend mostly the tips. 1 = linear, 3 = tip-heavy.")]
    [Range(1f, 5f)]
    public float forceFalloffPower = 3f;

    [Header("12 - Render Settings")]
    public int surfaceRenderQueue = (int)RenderQueue.Geometry;
    public int shellRenderQueue = (int)RenderQueue.Transparent;
    public ShadowCastingMode shellShadowCastingMode = ShadowCastingMode.Off;
    public bool shellReceiveShadows = false;

    [Tooltip("Extra expansion for procedural draw bounds. Increase this if fur disappears near screen edges.")]
    [Min(0f)]
    public float extraBoundsPadding = 0.25f;

    [Header("13 - Runtime")]
    public bool autoRender = true;
    public bool assignSurfaceMaterial = true;

    private const string OldShellRootName = "__Generated_Fur_Shells";

    private static readonly int FurStepId = Shader.PropertyToID("_FurStep");
    private static readonly int FurLayerCountId = Shader.PropertyToID("_FurLayerCount");
    private static readonly int FurLengthId = Shader.PropertyToID("_FurLength");
    private static readonly int FurDensityId = Shader.PropertyToID("_FurDensity");
    private static readonly int FurThinnessId = Shader.PropertyToID("_FurThinness");
    private static readonly int FurShadingId = Shader.PropertyToID("_FurShading");
    private static readonly int MidNoiseWeightId = Shader.PropertyToID("_MidNoiseWeight");
    private static readonly int FineNoiseWeightId = Shader.PropertyToID("_FineNoiseWeight");
    private static readonly int FurAlphaSharpnessId = Shader.PropertyToID("_FurAlphaSharpness");
    private static readonly int UVOffsetId = Shader.PropertyToID("_UVOffset");
    private static readonly int UVOffsetStrengthId = Shader.PropertyToID("_UVOffsetStrength");

    private static readonly int FurDirLightExposureId = Shader.PropertyToID("_FurDirLightExposure");
    private static readonly int LightFilterId = Shader.PropertyToID("_LightFilter");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SpecularId = Shader.PropertyToID("_Specular");
    private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
    private static readonly int ForceGlobalId = Shader.PropertyToID("_ForceGlobal");
    private static readonly int ForceLocalId = Shader.PropertyToID("_ForceLocal");
    private static readonly int ForcePowerId = Shader.PropertyToID("_ForcePower");
    private static readonly int OcclusionColorId = Shader.PropertyToID("_OcclusionColor");
    private static readonly int FresnelLVId = Shader.PropertyToID("_FresnelLV");
    private static readonly int StrandSpecColor1Id = Shader.PropertyToID("_StrandSpecColor1");
    private static readonly int StrandSpecColor2Id = Shader.PropertyToID("_StrandSpecColor2");
    private static readonly int StrandSpecPower1Id = Shader.PropertyToID("_StrandSpecPower1");
    private static readonly int StrandSpecPower2Id = Shader.PropertyToID("_StrandSpecPower2");
    private static readonly int SpecShift1Id = Shader.PropertyToID("_SpecShift1");
    private static readonly int SpecShift2Id = Shader.PropertyToID("_SpecShift2");
    private static readonly int StrandSpecStrengthId = Shader.PropertyToID("_StrandSpecStrength");

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
        ConfigureMaterials();
        ApplySurfaceSettings();
    }

    private void OnValidate()
    {
        shellCount = Mathf.Max(1, shellCount);
        furLength = Mathf.Max(0f, furLength);
        surfaceSpecularPower = Mathf.Max(0.01f, surfaceSpecularPower);
        noiseTiling = Mathf.Max(0.01f, noiseTiling);
        rootDarkening = Mathf.Clamp01(rootDarkening);
        alphaSharpness = Mathf.Clamp(alphaSharpness, 0.1f, 20f);
        layerUVOffsetStrength = Mathf.Clamp01(layerUVOffsetStrength);
        directLightExposure = Mathf.Clamp(directLightExposure, 0f, 5f);
        directLightPenetration = Mathf.Clamp(directLightPenetration, -1f, 1f);
        furRimStrength = Mathf.Clamp(furRimStrength, 0f, 5f);
        primaryStrandSpecPower = Mathf.Clamp(primaryStrandSpecPower, 1f, 256f);
        secondaryStrandSpecPower = Mathf.Clamp(secondaryStrandSpecPower, 1f, 256f);
        primarySpecShift = Mathf.Clamp(primarySpecShift, -1f, 1f);
        secondarySpecShift = Mathf.Clamp(secondarySpecShift, -1f, 1f);
        strandSpecularStrength = Mathf.Clamp(strandSpecularStrength, 0f, 5f);
        forceFalloffPower = Mathf.Clamp(forceFalloffPower, 1f, 5f);

        EnsureRuntimeData();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    private void Update()
    {
        if (autoRender)
            RenderFur();
    }

    /// <summary>
    /// Call manually when autoRender is disabled.
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

        // Surface keeps AO texture/strength on the material itself.
        SetMaterialColorIfExists(mat, ColorId, baseTint);
        SetMaterialColorIfExists(mat, SpecularId, surfaceSpecularColor);
        SetMaterialFloatIfExists(mat, ShininessId, surfaceSpecularPower);
        mat.renderQueue = renderQueue;

#if UNITY_EDITOR
        EditorUtility.SetDirty(mat);
#endif
    }

    private void ConfigureShellMaterial(Material mat, int renderQueue)
    {
        if (mat == null)
            return;

        WriteShellMaterialDefaults(mat);
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

        RenderParams renderParams = new RenderParams(shellMaterial)
        {
            matProps = shellBlock,
            layer = gameObject.layer,
            shadowCastingMode = shellShadowCastingMode,
            receiveShadows = shellReceiveShadows,
            worldBounds = ComputeExpandedWorldBounds(mesh)
        };

        Graphics.RenderMeshInstanced(renderParams, mesh, 0, instanceMatrices, shellCount);
    }

    private void WriteSurfaceProperties(MaterialPropertyBlock block)
    {
        // Surface only. Do not write shell AO/rim/spec/force controls here.
        block.SetColor(ColorId, baseTint);
        block.SetColor(SpecularId, surfaceSpecularColor);
        block.SetFloat(ShininessId, surfaceSpecularPower);
    }

    private void WriteShellMaterialDefaults(Material mat)
    {
        SetMaterialColorIfExists(mat, ColorId, baseTint);
        SetMaterialFloatIfExists(mat, FurLayerCountId, shellCount);
        SetMaterialFloatIfExists(mat, FurLengthId, furLength);
        SetMaterialFloatIfExists(mat, FurDensityId, outerLayerDensityFade);
        SetMaterialFloatIfExists(mat, FurThinnessId, noiseTiling);
        SetMaterialFloatIfExists(mat, FurShadingId, rootDarkening);
        SetMaterialFloatIfExists(mat, MidNoiseWeightId, midNoiseWeight);
        SetMaterialFloatIfExists(mat, FineNoiseWeightId, fineNoiseWeight);
        SetMaterialFloatIfExists(mat, FurAlphaSharpnessId, alphaSharpness);
        SetMaterialVectorIfExists(mat, UVOffsetId, new Vector4(layerUVOffset.x, layerUVOffset.y, 0f, 0f));
        SetMaterialFloatIfExists(mat, UVOffsetStrengthId, layerUVOffsetStrength);
        SetMaterialColorIfExists(mat, OcclusionColorId, rootOcclusionColor);
        SetMaterialFloatIfExists(mat, FresnelLVId, furRimStrength);
        SetMaterialFloatIfExists(mat, FurDirLightExposureId, directLightExposure);
        SetMaterialFloatIfExists(mat, LightFilterId, directLightPenetration);
        SetMaterialColorIfExists(mat, StrandSpecColor1Id, primaryStrandSpecColor);
        SetMaterialColorIfExists(mat, StrandSpecColor2Id, secondaryStrandSpecColor);
        SetMaterialFloatIfExists(mat, StrandSpecPower1Id, primaryStrandSpecPower);
        SetMaterialFloatIfExists(mat, StrandSpecPower2Id, secondaryStrandSpecPower);
        SetMaterialFloatIfExists(mat, SpecShift1Id, primarySpecShift);
        SetMaterialFloatIfExists(mat, SpecShift2Id, secondarySpecShift);
        SetMaterialFloatIfExists(mat, StrandSpecStrengthId, strandSpecularStrength);
        SetMaterialVectorIfExists(mat, ForceGlobalId, globalForce);
        SetMaterialVectorIfExists(mat, ForceLocalId, localForce);
        SetMaterialFloatIfExists(mat, ForcePowerId, forceFalloffPower);
    }

    private void WriteShellProperties(MaterialPropertyBlock block)
    {
        block.SetColor(ColorId, baseTint);
        block.SetFloat(FurLayerCountId, shellCount);
        block.SetFloat(FurLengthId, furLength);
        block.SetFloat(FurDensityId, outerLayerDensityFade);
        block.SetFloat(FurThinnessId, noiseTiling);
        block.SetFloat(FurShadingId, rootDarkening);
        block.SetFloat(MidNoiseWeightId, midNoiseWeight);
        block.SetFloat(FineNoiseWeightId, fineNoiseWeight);
        block.SetFloat(FurAlphaSharpnessId, alphaSharpness);
        block.SetVector(UVOffsetId, new Vector4(layerUVOffset.x, layerUVOffset.y, 0f, 0f));
        block.SetFloat(UVOffsetStrengthId, layerUVOffsetStrength);
        block.SetColor(OcclusionColorId, rootOcclusionColor);
        block.SetFloat(FresnelLVId, furRimStrength);
        block.SetFloat(FurDirLightExposureId, directLightExposure);
        block.SetFloat(LightFilterId, directLightPenetration);
        block.SetColor(StrandSpecColor1Id, primaryStrandSpecColor);
        block.SetColor(StrandSpecColor2Id, secondaryStrandSpecColor);
        block.SetFloat(StrandSpecPower1Id, primaryStrandSpecPower);
        block.SetFloat(StrandSpecPower2Id, secondaryStrandSpecPower);
        block.SetFloat(SpecShift1Id, primarySpecShift);
        block.SetFloat(SpecShift2Id, secondarySpecShift);
        block.SetFloat(StrandSpecStrengthId, strandSpecularStrength);
        block.SetVector(ForceGlobalId, globalForce);
        block.SetVector(ForceLocalId, localForce);
        block.SetFloat(ForcePowerId, forceFalloffPower);
    }

    private Bounds ComputeExpandedWorldBounds(Mesh mesh)
    {
        Bounds worldBounds = TransformBounds(transform.localToWorldMatrix, mesh.bounds);
        float forcePadding = Mathf.Max(globalForce.magnitude, localForce.magnitude) * furLength;
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
