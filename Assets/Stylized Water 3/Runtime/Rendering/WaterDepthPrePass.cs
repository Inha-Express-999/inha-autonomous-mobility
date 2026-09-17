// Stylized Water 3 by Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//    • Copying or referencing source code for the production of new asset store, or public, content is strictly prohibited!
//    • Uploading this file to a public repository will subject it to an automated DMCA takedown request.

#if URP
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace StylizedWater3
{
    public class WaterDepthPrePass : ScriptableRenderPass
    {
        private const string profilerTag = "Water Depth Prepass";
        private static readonly ProfilingSampler profilerSampler = new ProfilingSampler(profilerTag);
        
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_RenderStateBlock;
        private readonly List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>()
        {
            new ("TransparentDepthOnly")
        };
        
        private RendererListParams rendererListParams;
        private RendererList rendererList;
        
        public WaterDepthPrePass()
        {
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.transparent, LayerMask.GetMask("Water"));
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }
        
        public sealed class RenderTargetDebugContext : RenderTargetDebugger.RenderTarget
        {
            public RenderTargetDebugContext()
            {
                this.name = "Water Depth Texture";
                this.description = "Water geometry depth.";
                this.textureName = ShaderParams.Textures.WaterDepthBufferName;
                this.propertyID = ShaderParams.Textures._WaterDepthTexture;
                this.order = 0;
            }
        }
        
        public void Setup()
        {

        }
        
        public class FrameData : ContextItem
        {
            public TextureHandle _WaterDepthTexture;

            public override void Reset()
            {
                _WaterDepthTexture = TextureHandle.nullHandle;
            }
        }
        
        private class PassData
        {
            public RendererListHandle rendererListHandle;
            public TextureHandle renderTarget;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            var renderingData = frameContext.Get<UniversalRenderingData>();
            var cameraData = frameContext.Get<UniversalCameraData>();
            var lightData = frameContext.Get<UniversalLightData>();
            var resourceData = frameContext.Get<UniversalResourceData>();

            DrawingSettings drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, renderingData, cameraData,
                lightData,
                SortingCriteria.RenderQueue | SortingCriteria.SortingLayer | SortingCriteria.CommonTransparent);
            drawingSettings.perObjectData = PerObjectData.None;

            rendererListParams.cullingResults = renderingData.cullResults;
            rendererListParams.drawSettings = drawingSettings;
            rendererListParams.filteringSettings = m_FilteringSettings;

            //TextureHandle depthTexture = resourceData.activeDepthTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(resourceData.activeDepthTexture);
            desc.depthBufferBits = DepthBits.None;
            //If you're seeing an error here you are not using a compatible Unity version!
            //desc.colorFormat = GraphicsFormat.R16_UNorm;
            desc.colorFormat = GraphicsFormat.R32_SFloat;
            desc.name = ShaderParams.Textures.WaterDepthBufferName;
            desc.clearColor = Color.clear;
            desc.clearBuffer = true;
            
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Water Depth Pre-pass", out var passData))
            {
                //Render target
                passData.renderTarget = renderGraph.CreateTexture(desc);
                //Store render target in RG, so it can be retrieved in other passes
                FrameData frameData = frameContext.GetOrCreate<FrameData>();
                frameData._WaterDepthTexture = passData.renderTarget;

#if DEBUG
                if (RenderTargetDebugger.InspectedProperty == ShaderParams.Textures._WaterDepthTexture)
                {
                    StylizedWaterRenderFeature.DebugData debugData =
                        frameContext.Get<StylizedWaterRenderFeature.DebugData>();
                    debugData.currentHandle = passData.renderTarget;
                }
#endif

                passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParams);
                
                //Set render target and bind to global property
                builder.SetRenderAttachment(passData.renderTarget, 0, AccessFlags.Write);
                //builder.CreateTransientTexture(passData.renderTarget);
                builder.SetGlobalTextureAfterPass(passData.renderTarget, ShaderParams.Textures._WaterDepthTexture);
                
                builder.UseRendererList(passData.rendererListHandle);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Execute(context, data);
                });
            }
        }
        
        private void Execute(RasterGraphContext context, PassData data)
        {
            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilerSampler))
            {
                //cmd.ClearRenderTarget(false, true, Color.clear);
                
                cmd.SetGlobalInt(ShaderParams.GlobalProperties._WaterDepthTextureAvailable, 1);

                cmd.DrawRendererList(data.rendererListHandle);
            }
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.SetGlobalInt(ShaderParams.GlobalProperties._WaterDepthTextureAvailable, 0);
        }

        public void Dispose()
        {
            
        }
    }
}
#endif