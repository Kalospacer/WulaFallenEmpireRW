using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// Captures the current screen and returns it as a multimodal image so the model can actually see it,
    /// unlike <c>rimworld/take_screenshot</c> which only yields a file path. The image is written to disk
    /// via <see cref="AIImageStore"/> (history stores just the reference); the base64 fed to the model is
    /// transient for this turn only.
    /// </summary>
    public class Tool_Screenshot : AITool
    {
        public override string Name => "take_screenshot";

        public override string Description =>
            "Capture the current game screen and SEE it. Returns the screenshot as an image you can view. "
            + "Use this when you need to look at what is on screen (UI state, colony, map).";

        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["note"] = SchemaString("Optional note about why the screenshot is taken.", nullable: true)
            };
            return SchemaObject(properties, RequiredList());
        }

        private string _capturedFileName;

        public override Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _capturedFileName = null;
                byte[] jpgBytes = ScreenCaptureUtility.CaptureScreenAsBytes();
                if (jpgBytes == null || jpgBytes.Length == 0)
                {
                    return Task.FromResult("Error: 截图失败，无法获取画面。");
                }
                string fileName = AIImageStore.SaveImage(jpgBytes);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return Task.FromResult("Error: 截图保存失败。");
                }
                _capturedFileName = fileName;
                return Task.FromResult($"已截图，图片已作为多模态结果返回（你现在能看到画面）。image_ref={AIImageStore.BuildImageRef(fileName, 0, 0)}");
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult("Error: 截图被取消或超时。");
            }
            catch (Exception ex)
            {
                WulaLog.Debug("[Tool_Screenshot] Error: " + ex);
                return Task.FromResult("Error: 截图异常: " + ex.Message);
            }
        }

        public override Task<List<AIContentPart>> GetResultPartsAsync(string argsJson, string textResult, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_capturedFileName))
            {
                return Task.FromResult<List<AIContentPart>>(null);
            }
            byte[] bytes = AIImageStore.LoadImageBytes(_capturedFileName);
            if (bytes == null || bytes.Length == 0)
            {
                return Task.FromResult<List<AIContentPart>>(null);
            }
            var parts = new List<AIContentPart>
            {
                AIContentPart.ImagePart("image/jpeg", Convert.ToBase64String(bytes))
            };
            return Task.FromResult(parts);
        }
    }
}
