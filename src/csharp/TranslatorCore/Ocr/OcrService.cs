//=============================================================
// Ocr/OcrService.cs - Windows 内置 OCR（Windows.Media.Ocr）包装（优化 5）
// net48 消费 WinRT：Windows Kits 的 Windows.winmd（编译期引用，Private=false
// 不部署）+ GAC System.Runtime.WindowsRuntime（AsTask / byte[].AsBuffer），
// 运行时零部署依赖（Win10+ 自带 OCR 引擎，免费离线）。
// 进程内调用安全性（与 UIA 子进程隔离决策的差异）：UIA 连到不可信第三方
// 进程、目标挂死会拖死宿主；OCR 走系统 RuntimeBroker 服务、输入是自己的
// 位图，不存在"目标挂死拖死宿主"面 → 进程内 + 后台线程 + 超时放弃足够。
// 坑：CopyFromScreen 快照的 alpha 通道未定义，转 Bgra8（premultiplied 语义）
// 时不置 0xFF 会被当全透明 → 识别为空；故逐行拷贝时强制 alpha=255。
//=============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Translator.Core.Ocr
{
    public sealed class OcrOutcome
    {
        public bool Ok;
        public string Text;
        public string LangTag;     // 实际使用的 OCR 识别语言
        public string FailReason;  // no-language(指引) / no-engine / timeout(n) / error:类型名
        public string Preprocess;  // 预处理动作记录（宿主日志用）："invert" / "2x" / "invert+2x" / "-"
    }

    public static class OcrService
    {
        /// <summary>同步识别（内部异步转同步等待，超时放弃；残留任务自灭无副作用）。
        /// 在后台线程调用（宿主 Task.Run），image 由调用方负责线程安全传递。</summary>
        public static OcrOutcome Recognize(Bitmap image, string srcLang, int timeoutMs)
        {
            var oc = new OcrOutcome();
            OcrEngine engine = null;
            SoftwareBitmap software = null;
            Bitmap work = image;
            bool ownsWork = false;
            try
            {
                // ① 引擎选择：跟随翻译源语言，取不到时系统语言兜底
                string profileFirst = null;
                try
                {
                    OcrEngine pe = OcrEngine.TryCreateFromUserProfileLanguages();
                    if (pe != null) profileFirst = pe.RecognizerLanguage.LanguageTag;
                }
                catch (Exception) { }
                var avail = new List<string>();
                try
                {
                    foreach (Language l in OcrEngine.AvailableRecognizerLanguages)
                        avail.Add(l.LanguageTag);
                }
                catch (Exception) { }
                string tag = OcrText.PickLanguage(srcLang, avail, profileFirst);
                if (tag == null)
                {
                    oc.FailReason = "no-language（本机未安装 OCR 语言包：设置 → 时间和语言 → 语言和区域 → 添加语言 → 勾选「光学字符识别」可选功能）";
                    return oc;
                }
                try { engine = OcrEngine.TryCreateFromLanguage(new Language(tag)); }
                catch (Exception) { engine = null; }
                if (engine == null && profileFirst != null && profileFirst != tag)
                {
                    try { engine = OcrEngine.TryCreateFromLanguage(new Language(profileFirst)); tag = profileFirst; }
                    catch (Exception) { engine = null; }
                }
                if (engine == null) { oc.FailReason = "no-engine（" + tag + " 引擎创建失败）"; return oc; }
                oc.LangTag = engine.RecognizerLanguage.LanguageTag;

                // ② 尺寸适配（MaxImageDimension 硬限）
                int nw, nh;
                OcrText.FitDimension(image.Width, image.Height, OcrEngine.MaxImageDimension, out nw, out nh);
                if (nw != image.Width || nh != image.Height)
                {
                    work = Resize(image, nw, nh);
                    ownsWork = true;
                }

                // ②' 预处理（提高准确率，零成本本地）：
                //    暗底浅字反色（引擎为白底黑字文档优化，深色 IDE/终端截图
                //    是开发场景主流量）+ 小区域 2x 放大（字高 ~20px 以下识别率骤降）
                var marks = new List<string>();
                bool invert = OcrText.ShouldInvert(SampleLuma(work));
                if (invert) marks.Add("invert");
                if (OcrText.ShouldUpscale(work.Width, work.Height))
                {
                    Bitmap up = Resize(work, work.Width * 2, work.Height * 2);
                    if (ownsWork) work.Dispose();
                    work = up;
                    ownsWork = true;
                    marks.Add("2x");
                }
                oc.Preprocess = marks.Count > 0 ? string.Join("+", marks) : "-";

                // ③ 快照 → SoftwareBitmap（Bgra8，alpha 强制不透明；按需反色）
                software = ToSoftwareBitmap(work, invert);

                // ④ 识别
                Task<OcrResult> task = engine.RecognizeAsync(software).AsTask();
                if (!task.Wait(timeoutMs)) { oc.FailReason = "timeout(" + timeoutMs + "ms)"; return oc; }
                OcrResult result = task.Result;
                var lines = new List<string>();
                foreach (OcrLine line in result.Lines) lines.Add(line.Text);
                oc.Text = OcrText.JoinLines(lines);
                oc.Ok = true;
                return oc;
            }
            catch (Exception ex)
            {
                oc.FailReason = "error:" + ex.GetType().Name;
                return oc;
            }
            finally
            {
                if (software != null) software.Dispose();
                if (ownsWork && work != null) work.Dispose();
                // OcrEngine 未实现 IClosable（无状态引擎），无需 dispose
            }
        }

        /// <summary>selftest 探针：引擎能力注记（环境事实，不计 PASS/FAIL）。</summary>
        public static string Probe()
        {
            try
            {
                var tags = new List<string>();
                foreach (Language l in OcrEngine.AvailableRecognizerLanguages) tags.Add(l.LanguageTag);
                string profile = "-";
                try
                {
                    OcrEngine pe = OcrEngine.TryCreateFromUserProfileLanguages();
                    if (pe != null) profile = pe.RecognizerLanguage.LanguageTag;
                }
                catch (Exception) { }
                return tags.Count + " langs [" + string.Join(",", tags) + "] profile=" + profile
                    + " maxDim=" + OcrEngine.MaxImageDimension;
            }
            catch (Exception ex) { return "error:" + ex.GetType().Name; }
        }

        private static Bitmap Resize(Bitmap src, int w, int h)
        {
            var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return dst;
        }

        /// <summary>区域平均亮度（Rec.601 luma；每像素间隔抽样约 5 万点足够）。</summary>
        private static double SampleLuma(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int step = Math.Max(1, (w * h) / 50000);
                long sum = 0;
                int count = 0;
                var row = new byte[w * 4];
                for (int y = 0; y < h; y += step)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w * 4);
                    for (int x = 0; x < w; x += step)
                    {
                        int off = x * 4;             // 小端 BGRA
                        sum += 299 * row[off + 2] + 587 * row[off + 1] + 114 * row[off];
                        count++;
                    }
                }
                return count == 0 ? -1 : sum / 1000.0 / count;
            }
            finally { bmp.UnlockBits(data); }
        }

        private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp, bool invert)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = w * 4;
                var row = new byte[rowBytes];
                var all = new byte[rowBytes * h];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowBytes);
                    for (int x = 0; x < rowBytes; x += 4)
                    {
                        if (invert)
                        {
                            row[x] = (byte)(255 - row[x]);         // B
                            row[x + 1] = (byte)(255 - row[x + 1]); // G
                            row[x + 2] = (byte)(255 - row[x + 2]); // R
                        }
                        row[x + 3] = 0xFF; // premultiplied 下 255=原值
                    }
                    Buffer.BlockCopy(row, 0, all, y * rowBytes, rowBytes);
                }
                return SoftwareBitmap.CreateCopyFromBuffer(all.AsBuffer(), BitmapPixelFormat.Bgra8, w, h);
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
