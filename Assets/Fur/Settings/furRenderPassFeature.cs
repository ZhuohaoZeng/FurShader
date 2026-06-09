using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

public class furRenderPassFeature : ScriptableRendererFeature
{

    [Header("【渲染时机】")]
    [Space(20)]
    public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingSkybox;//毛发渲染发生的时机

    [Header("【过滤】")]
    [Space(20)]
    public RenderQueueType queue = RenderQueueType.Opaque;//场景物体的渲染队列在该范围内时渲染
    public enum RenderQueueType
    {
        Opaque,
        Transparent,
    };
    public LayerMask layerMask;//只对此层内的物体附加毛发渲染
    public string[] lightModeTags;//只对Shader Tag在该列表中的材质渲染

    [Header("【材质】")]
    [Space(20)]
    public Material furMat;
    public int passId;//使用材质对应shader中的第passId个Pass进行渲染

    [Header("【材质参数】")]
    [Range(2, 100)] public int step = 10;//层数

    class FurPass : ScriptableRenderPass
    {
        
        //成员变量
        public FilteringSettings filteringSettings;
        public RenderStateBlock renderStateBlock;
        private RenderQueueType renderQueueType;
        private List<ShaderTagId> tagIds;
        private Material overrideMaterial;
        private int overrideMaterialPassIndex;
        private int step;

        public FurPass(RenderPassEvent renderPassEvent, RenderQueueType renderQueueType, int layerMask, string[] lightModeTags, Material overrideMaterial, int overrideMaterialPassIndex, int step)
        {
            this.renderPassEvent = renderPassEvent;
            this.renderQueueType = renderQueueType;
            this.overrideMaterial = overrideMaterial;
            this.overrideMaterialPassIndex = overrideMaterialPassIndex;
            this.step = step;

            //渲染队列过滤配置
            RenderQueueRange renderQueueRange = (renderQueueType == RenderQueueType.Transparent)
                ? RenderQueueRange.transparent
                : RenderQueueRange.opaque;
            filteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            //Shader Tag过滤配置
            tagIds = new List<ShaderTagId>();
            if (lightModeTags != null && lightModeTags.Length > 0)
            {
                for (int i = 0; i < lightModeTags.Length; i++)
                {
                    tagIds.Add(new ShaderTagId(lightModeTags[i]));
                }
            }
            else
            {
                //管线默认的Tag
                tagIds.Add(new ShaderTagId("SRPDefaultUnlit"));
                tagIds.Add(new ShaderTagId("UniversalForward"));
                tagIds.Add(new ShaderTagId("UniversalForwardOnly"));

            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
        //排序层配置
        SortingCriteria sortingCriteria = (renderQueueType == RenderQueueType.Transparent)
            ? SortingCriteria.CommonTransparent
            : renderingData.cameraData.defaultOpaqueSortFlags;

        //材质配置
        DrawingSettings drawingSettings = CreateDrawingSettings(tagIds, ref renderingData, sortingCriteria);
        drawingSettings.overrideMaterial = overrideMaterial;
        drawingSettings.overrideMaterialPassIndex = overrideMaterialPassIndex;

        //绘制
        CommandBuffer cmd = CommandBufferPool.Get();

        for (int i = 1; i <= step; i++)
        {
            cmd.SetGlobalFloat("_FurStep", i / (float)step);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
        }
        CommandBufferPool.Release(cmd);
    }

    }

    FurPass furPass;

    /// <inheritdoc/>
    public override void Create()
    {
        furPass = new FurPass(passEvent, queue, layerMask, lightModeTags, furMat, passId, step);
        

        // Configures where the render pass should be injected.
        furPass.renderPassEvent = passEvent;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(furPass);
    }
}


