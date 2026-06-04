using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class FurShellBuilder : MonoBehaviour
{
    [Header("Source")]
    public MeshRenderer sourceRenderer;
    public MeshFilter sourceMeshFilter;

    [Header("Materials")]
    public Material surfaceMaterial;
    public Material shellMaterial;

    [Header("Fur Ambient Occlusion")]
    public Color occlusionColor = new Color(0.15f, 0.12f, 0.10f, 1f);

    [Range(0f, 5f)]
    public float fresnelLV = 1f;

    [Header("Fur Settings")]
    [Range(1, 64)]
    public int shellCount = 20;

    public float furLength = 0.1f;

    [Range(0f, 2f)]
    public float furDensity = 1f;

    public Color color = Color.white;
    public Color specular = Color.white;

    [Range(1f, 256f)]
    public float shininess = 32f;

    [Header("Auto Update")]
    public bool autoUpdate = true;

    private bool updateQueued;

    private const string ShellRootName = "__Generated_Fur_Shells";

    private static readonly int FurStepId = Shader.PropertyToID("_FurStep");
    private static readonly int FurLengthId = Shader.PropertyToID("_FurLength");
    private static readonly int FurDensityId = Shader.PropertyToID("_FurDensity");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SpecularId = Shader.PropertyToID("_Specular");
    private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
    private static readonly int FurSurfaceId = Shader.PropertyToID("_FurSurface");
    private static readonly int OcclusionColorId = Shader.PropertyToID("_OcclusionColor");
    private static readonly int FresnelLVId = Shader.PropertyToID("_FresnelLV");

    private void Reset()
    {
        sourceRenderer = GetComponent<MeshRenderer>();
        sourceMeshFilter = GetComponent<MeshFilter>();
    }

    [ContextMenu("Rebuild Fur Shells")]
    public void RebuildFurShells()
    {
        if (sourceRenderer == null)
            sourceRenderer = GetComponent<MeshRenderer>();

        if (sourceMeshFilter == null)
            sourceMeshFilter = GetComponent<MeshFilter>();

        if (sourceRenderer == null || sourceMeshFilter == null)
        {
            Debug.LogError("FurShellBuilder requires a MeshRenderer and MeshFilter on the source object.");
            return;
        }

        if (surfaceMaterial == null || shellMaterial == null)
        {
            Debug.LogError("Please assign both surfaceMaterial and shellMaterial.");
            return;
        }

        ConfigureMaterial(surfaceMaterial, isSurface: true);
        ConfigureMaterial(shellMaterial, isSurface: false);

        sourceRenderer.sharedMaterial = surfaceMaterial;
        ApplyPropertyBlock(sourceRenderer, 0f);

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
            shellRenderer.sharedMaterial = shellMaterial;

            CopyRendererSettings(sourceRenderer, shellRenderer);
            ApplyPropertyBlock(shellRenderer, step);
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(gameObject);
#endif
    }

    [ContextMenu("Update Fur Parameters Only")]
    public void UpdateFurParametersOnly()
    {
        if (sourceRenderer != null)
            ApplyPropertyBlock(sourceRenderer, 0f);

        Transform root = transform.Find(ShellRootName);
        if (root == null)
            return;

        int index = 1;
        foreach (Transform child in root)
        {
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;

            float step = index / (float)shellCount;
            ApplyPropertyBlock(renderer, step);
            index++;
        }
    }

    private void ConfigureMaterial(Material mat, bool isSurface)
    {
        if (mat == null)
            return;

        if (mat.HasProperty(FurSurfaceId))
            mat.SetFloat(FurSurfaceId, isSurface ? 1f : 0f);

        if (isSurface)
            mat.EnableKeyword("_FUR_SURFACE");
        else
            mat.DisableKeyword("_FUR_SURFACE");

        if (mat.HasProperty(FurLengthId))
            mat.SetFloat(FurLengthId, furLength);

        if (mat.HasProperty(FurDensityId))
            mat.SetFloat(FurDensityId, furDensity);

        if (mat.HasProperty(ColorId))
            mat.SetColor(ColorId, color);

        if (mat.HasProperty(SpecularId))
            mat.SetColor(SpecularId, specular);

        if (mat.HasProperty(ShininessId))
            mat.SetFloat(ShininessId, shininess);

        mat.renderQueue = (int)RenderQueue.Transparent;

#if UNITY_EDITOR
        EditorUtility.SetDirty(mat);
#endif
    }

    private void ApplyPropertyBlock(Renderer renderer, float furStep)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);

        block.SetFloat(FurStepId, furStep);
        block.SetFloat(FurLengthId, furLength);
        block.SetFloat(FurDensityId, furDensity);
        block.SetColor(ColorId, color);
        block.SetColor(SpecularId, specular);
        block.SetFloat(ShininessId, shininess);

        renderer.SetPropertyBlock(block);
    }

    private void CopyRendererSettings(Renderer source, Renderer target)
    {
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
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
    private void OnValidate()
    {
        if (!autoUpdate)
            return;

        shellCount = Mathf.Max(1, shellCount);
        furLength = Mathf.Max(0f, furLength);

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

                AutoRefresh();
            };

            return;
        }
    #endif

        AutoRefresh();
    }

    private void AutoRefresh()
    {
        if (sourceRenderer == null)
            sourceRenderer = GetComponent<MeshRenderer>();

        if (sourceMeshFilter == null)
            sourceMeshFilter = GetComponent<MeshFilter>();

        if (sourceRenderer == null || sourceMeshFilter == null)
            return;

        if (surfaceMaterial == null || shellMaterial == null)
            return;

        Transform root = transform.Find(ShellRootName);

        bool needRebuild =
            root == null ||
            root.childCount != shellCount;

        if (needRebuild)
        {
            RebuildFurShells();
        }
        else
        {
            ConfigureMaterial(surfaceMaterial, isSurface: true);
            ConfigureMaterial(shellMaterial, isSurface: false);
            sourceRenderer.sharedMaterial = surfaceMaterial;
            ApplyPropertyBlock(sourceRenderer, 0f);
            UpdateFurParametersOnly();
        }
    }

}
