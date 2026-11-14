using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PanzerSim
{
    /// <summary>
    /// Renderer-Feature fuer Hoehennebel/Dunst (Shader Resources/Shaders/HeightFog). Laeuft vor der Nachbearbeitung
    /// (Tonemapping, Bloom). Werte setzt Visuals.cs als globale Shader-Variablen; Visuals.FogOn schaltet ihn ab.
    /// Wird vom Build-Skript (GraphicsSetup) in den PC_Renderer eingetragen.
    /// </summary>
    public class HeightFogFeature : ScriptableRendererFeature
    {
        class FogPass : ScriptableRenderPass
        {
            readonly Material mat;
            class PassData { public TextureHandle src; public Material mat; }

            public FogPass(Material m)
            {
                mat = m;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                TextureHandle src = res.activeColorTexture;
                var desc = rg.GetTextureDesc(src);
                desc.name = "Hoehennebel"; desc.clearBuffer = false; desc.depthBufferBits = 0;
                TextureHandle dst = rg.CreateTexture(desc);
                using (var builder = rg.AddRasterRenderPass<PassData>("Hoehennebel", out var pd))
                {
                    pd.src = src; pd.mat = mat;
                    builder.UseTexture(src, AccessFlags.Read);
                    if (res.cameraDepthTexture.IsValid()) builder.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1f, 1f, 0f, 0f), d.mat, 0));
                }
                res.cameraColor = dst;
            }
        }

        Material mat;
        FogPass pass;

        public override void Create()
        {
            var sh = Resources.Load<Shader>("Shaders/HeightFog");
            if (sh == null) sh = Shader.Find("Hidden/PanzerSim/HeightFog");
            if (sh == null) return;
            mat = CoreUtils.CreateEngineMaterial(sh);
            pass = new FogPass(mat);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || !Visuals.FogOn) return;
            var t = renderingData.cameraData.cameraType;
            if (t != CameraType.Game && t != CameraType.SceneView) return;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(mat);
    }
}
