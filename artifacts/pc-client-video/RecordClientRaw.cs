using System;
using System.Collections;
using System.IO;
using UnityEngine;
using InhaExpress.Client.Presentation;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        new GameObject("Temporary PC video capture").AddComponent<ClientRawCapture>();
        result.Log("Recording 400 actual Game View frames at 20 fps");
    }
    public sealed class ClientRawCapture : MonoBehaviour
    {
        private FileStream output;
        private RenderTexture full, small;
        private Texture2D pixels;
        private PcOperatorControls controls;
        private int frames;
        private IEnumerator Start()
        {
            string folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"artifacts/pc-client-video");
            controls=UnityEngine.Object.FindFirstObjectByType<WebClientHud>().OperatorControls;
            Application.runInBackground=true;
            controls.DeselectVehicle();
            full=new RenderTexture(Screen.width,Screen.height,0,RenderTextureFormat.ARGB32);
            small=new RenderTexture(1280,720,0,RenderTextureFormat.ARGB32);
            pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            output=new FileStream(Path.Combine(folder,"frames.rgb"),FileMode.Create,FileAccess.Write,FileShare.Read,1048576);
            double started=Time.realtimeSinceStartupAsDouble;
            int previous=-1;
            while(frames<400)
            {
                double elapsed=Time.realtimeSinceStartupAsDouble-started;
                int segment=Math.Min(4,(int)(elapsed/4));
                if(segment!=previous)
                {
                    previous=segment;
                    if(segment==0||segment==4) controls.DeselectVehicle();
                    else controls.SelectVehicle("V0"+segment);
                }
                yield return new WaitForEndOfFrame();
                int wanted=Math.Min(400,1+(int)((Time.realtimeSinceStartupAsDouble-started)*20));
                if(wanted<=frames)continue;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                Graphics.Blit(full,small);
                var old=RenderTexture.active;RenderTexture.active=small;
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0,false);pixels.Apply(false,false);
                RenderTexture.active=old;
                byte[] bytes=pixels.GetRawTextureData();
                while(frames<wanted){output.Write(bytes,0,bytes.Length);frames++;}
            }
            output.Dispose();output=null;
            File.WriteAllText(Path.Combine(folder,"capture-result.json"),"{\"frames\":400,\"fps\":20,\"duration_s\":20,\"width\":1280,\"height\":720,\"wall_s\":"+(Time.realtimeSinceStartupAsDouble-started).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"}");
            controls.DeselectVehicle();Destroy(gameObject);
        }
        private void OnDestroy()
        {
            if(output!=null)output.Dispose();
            if(full!=null){full.Release();Destroy(full);}if(small!=null){small.Release();Destroy(small);}
            if(pixels!=null)Destroy(pixels);
        }
    }
}
