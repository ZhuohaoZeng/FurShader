using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class FurShellSeparateBuilder : MonoBehaviour
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
    [Range(1, 128)]
    public int shellCount = 20;

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

    [Header("Render Order")]
    public int surfaceRenderQueue = (int)RenderQueue.Geometry;
    public int shellRenderQueue = (int)RenderQueue.Transparent;
    public bool forceSortingOrder = true;
    public int surfaceSortingOrder = 0;
    public int shellSortingOrderStart = 1;

    [Header("Auto Update")]
    public bool autoUpdate = true;

    private const string ShellRootName = "__Generated_Fur_Shells";

    private static readonly int FurStepId = Shader.PropertyToID("_FurStep");
    private static readonly int FurLengthId = Shader.PropertyToID("_FurLength");
    private static readonly int FurDensityId = Shader.PropertyToID("_FurDensity");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SpecularId = Shader.PropertyToID("_Specular");
    private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
    private static readonly int OcclusionStrengthId = Shader.PropertyToID("_OcclusionStrength");
    private static readonly int FurThinnessId = Shader.PropertyToID("_FurThinness");
    private static readonly int FurShadingId = Shader.PropertyToID("_FurShading");
    private static readonly int ForceGlobalId = Shader.PropertyToID("_ForceGlobal");
    private static readonly int ForceLocalId = Shader.PropertyToID("_ForceLocal");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");

    private bool updateQueued;

    private int lastShellCount;
    private float lastFurLength;
    private float lastFurDensity;
    private Color lastColor;
    private Color lastSpecular;
    private float lastShininess;
    private float lastOcclusionStrength;
    private Material lastSurfaceMaterial;
    private Material lastShellMaterial;
    private Mesh lastMesh;
    private int lastSurfaceRenderQueue;
    private int lastShellRenderQueue;
    private bool lastForceSortingOrder;
    private int lastSurfaceSortingOrder;
    private int lastShellSortingOrderStart;
    private float lastFurThinness;
    private float lastFurShading;
    private Vector3 lastForceGlobal;
    private Vector3 lastForceLocal;
    private Color lastRimColor;
    private float lastRimPower;

    private void Reset()
    {
        sourceRenderer = GetComponent<MeshRenderer>();
        sourceMeshFilter = GetComponent<MeshFilter>();
    }

    private void OnEnable()
    {
        EnsureSourceReferences();

        if (autoUpdate)
            QueueAutoRefresh();
    }

    private void Update()
    {
        if (!autoUpdate)
            return;

        EnsureSourceReferences();

        if (!IsReady())
            return;

        if (!HasSettingsChanged())
            return;

        AutoRefresh();
        CacheSettings();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    private void OnValidate()
    {
        shellCount = Mathf.Max(1, shellCount);
        furLength = Mathf.Max(0f, furLength);
        shininess = Mathf.Max(0.01f, shininess);
        furThinness = Mathf.Max(0.01f, furThinness);
        furShading = Mathf.Clamp01(furShading);
        rimPower = Mathf.Clamp(rimPower, 0.0f, 8.0f);

        if (!autoUpdate)
            return;

        QueueAutoRefresh();
    }

    [ContextMenu("Rebuild Fur Shells")]
    public void RebuildFurShells()
    {
        EnsureSourceReferences();

        if (!IsReady())
        {
            Debug.LogError("FurShellSeparateBuilder requires Source Renderer, Source Mesh Filter, Surface Material, and Shell Material.");
            return;
        }

        ConfigureMaterials();

        sourceRenderer.sharedMaterial = surfaceMaterial;
        ApplyPropertyBlock(sourceRenderer, 0f);
        ApplySourceRenderSettings();

        ClearOldShells();

        GameObject root = new GameObject(ShellRootName);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        for (int i = 1; i <= shellCount; i++)
        {
            float step = i / (float)shellCount;

            GameObject shell = new GameObject($"FurShell_{i:00}_Step_{step:0.00}");
            shell.transform.SetParent(root.transform, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale = Vector3.one;

            MeshFilter shellFilter = shell.AddComponent<MeshFilter>();
            shellFilter.sharedMesh = sourceMeshFilter.sharedMesh;

            MeshRenderer shellRenderer = shell.AddComponent<MeshRenderer>();
            CopyRendererSettings(sourceRenderer, shellRenderer);
            shellRenderer.sharedMaterial = shellMaterial;

            ApplyShellRenderSettings(shellRenderer, i);
            ApplyPropertyBlock(shellRenderer, step);
        }

        CacheSettings();

#if UNITY_EDITOR
        EditorUtility.SetDirty(gameObject);
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    [ContextMenu("Update Fur Parameters Only")]
    public void UpdateFurParametersOnly()
    {
        EnsureSourceReferences();

        if (!IsReady())
            return;

        ConfigureMaterials();

        sourceRenderer.sharedMaterial = surfaceMaterial;
        ApplySourceRenderSettings();
        ApplyPropertyBlock(sourceRenderer, 0f);

        Transform root = transform.Find(ShellRootName);
        if (root == null)
            return;

        int index = 1;
        foreach (Transform child in root)
        {
            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = sourceMeshFilter.sharedMesh;

            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;

            renderer.sharedMaterial = shellMaterial;
            ApplyShellRenderSettings(renderer, index);

            float step = index / (float)shellCount;
            ApplyPropertyBlock(renderer, step);
            index++;
        }

        CacheSettings();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    private void AutoRefresh()
    {
        EnsureSourceReferences();

        if (!IsReady())
            return;

        Transform root = transform.Find(ShellRootName);

        bool needRebuild =
            root == null ||
            root.childCount != shellCount ||
            lastMesh != sourceMeshFilter.sharedMesh;

        if (needRebuild)
            RebuildFurShells();
        else
            UpdateFurParametersOnly();
    }

    private void ConfigureMaterials()
    {
        ConfigureMaterial(surfaceMaterial, surfaceRenderQueue);
        ConfigureMaterial(shellMaterial, shellRenderQueue);
    }

    private void ConfigureMaterial(Material mat, int renderQueue)
    {
        if (mat == null)
            return;

        SetMaterialFloatIfExists(mat, FurLengthId, furLength);
        SetMaterialFloatIfExists(mat, FurDensityId, furDensity);
        SetMaterialColorIfExists(mat, ColorId, color);
        SetMaterialColorIfExists(mat, SpecularId, specular);
        SetMaterialFloatIfExists(mat, ShininessId, shininess);
        SetMaterialFloatIfExists(mat, OcclusionStrengthId, occlusionStrength);
        SetMaterialFloatIfExists(mat, FurThinnessId, furThinness);
        SetMaterialFloatIfExists(mat, FurShadingId, furShading);
        SetMaterialVectorIfExists(mat, ForceGlobalId, forceGlobal);
        SetMaterialVectorIfExists(mat, ForceLocalId, forceLocal);
        SetMaterialColorIfExists(mat, RimColorId, rimColor);
        SetMaterialFloatIfExists(mat, RimPowerId, rimPower);

        mat.renderQueue = renderQueue;

#if UNITY_EDITOR
        EditorUtility.SetDirty(mat);
#endif
    }

    private void ApplyPropertyBlock(Renderer renderer, float furStep)
    {
        if (renderer == null)
            return;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);

        block.SetFloat(FurStepId, furStep);
        block.SetFloat(FurLengthId, furLength);
        block.SetFloat(FurDensityId, furDensity);
        block.SetColor(ColorId, color);
        block.SetColor(SpecularId, specular);
        block.SetFloat(ShininessId, shininess);
        block.SetFloat(OcclusionStrengthId, occlusionStrength);
        block.SetFloat(FurThinnessId, furThinness);
        block.SetFloat(FurShadingId, furShading);
        block.SetVector(ForceGlobalId, forceGlobal);
        block.SetVector(ForceLocalId, forceLocal);
        block.SetColor(RimColorId, rimColor);
        block.SetFloat(RimPowerId, rimPower);
        renderer.SetPropertyBlock(block);
    }

    private void ApplySourceRenderSettings()
    {
        if (!forceSortingOrder || sourceRenderer == null)
            return;

        sourceRenderer.sortingOrder = surfaceSortingOrder;
    }

    private void ApplyShellRenderSettings(MeshRenderer shellRenderer, int shellIndex)
    {
        if (shellRenderer == null)
            return;

        if (forceSortingOrder)
        {
            shellRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            shellRenderer.sortingOrder = shellSortingOrderStart + shellIndex - 1;
        }
    }

    private void CopyRendererSettings(Renderer source, Renderer target)
    {
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.sortingLayerID = source.sortingLayerID;
    }

    private void ClearOldShells()
    {
        Transform oldRoot = transform.Find(ShellRootName);
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
               surfaceMaterial != null &&
               shellMaterial != null;
    }

    private void QueueAutoRefresh()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (updateQueued)
                return;

            updateQueued = true;
            EditorApplication.delayCall += () =>
            {
                updateQueued = false;

                if (this == null)
                    return;

                if (!autoUpdate)
                    return;

                AutoRefresh();
                CacheSettings();
            };
            return;
        }
#endif
        AutoRefresh();
        CacheSettings();
    }

    private bool HasSettingsChanged()
    {
        Mesh currentMesh = sourceMeshFilter != null ? sourceMeshFilter.sharedMesh : null;

        return lastShellCount != shellCount ||
               !Mathf.Approximately(lastFurLength, furLength) ||
               !Mathf.Approximately(lastFurDensity, furDensity) ||
               !Mathf.Approximately(lastFurThinness, furThinness) ||
               !Mathf.Approximately(lastOcclusionStrength, occlusionStrength)||
               !Mathf.Approximately(lastFurShading, furShading) ||
               lastForceGlobal != forceGlobal ||
               lastForceLocal != forceLocal ||
               lastRimColor != rimColor ||
               !Mathf.Approximately(lastRimPower, rimPower) ||
               lastColor != color ||
               lastSpecular != specular ||
               !Mathf.Approximately(lastShininess, shininess) ||
               lastSurfaceMaterial != surfaceMaterial ||
               lastShellMaterial != shellMaterial ||
               lastMesh != currentMesh ||
               lastSurfaceRenderQueue != surfaceRenderQueue ||
               lastShellRenderQueue != shellRenderQueue ||
               lastForceSortingOrder != forceSortingOrder ||
               lastSurfaceSortingOrder != surfaceSortingOrder ||
               lastShellSortingOrderStart != shellSortingOrderStart;
    }

    private void CacheSettings()
    {
        lastShellCount = shellCount;
        lastFurLength = furLength;
        lastFurDensity = furDensity;
        lastFurThinness = furThinness;
        lastFurShading = furShading;
        lastForceGlobal = forceGlobal;
        lastForceLocal = forceLocal;
        lastRimColor = rimColor;
        lastRimPower = rimPower;
        lastColor = color;
        lastSpecular = specular;
        lastShininess = shininess;
        lastOcclusionStrength = occlusionStrength;
        lastSurfaceMaterial = surfaceMaterial;
        lastShellMaterial = shellMaterial;
        lastMesh = sourceMeshFilter != null ? sourceMeshFilter.sharedMesh : null;
        lastSurfaceRenderQueue = surfaceRenderQueue;
        lastShellRenderQueue = shellRenderQueue;
        lastForceSortingOrder = forceSortingOrder;
        lastSurfaceSortingOrder = surfaceSortingOrder;
        lastShellSortingOrderStart = shellSortingOrderStart;
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