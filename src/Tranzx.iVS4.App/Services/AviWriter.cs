// ============================================================================
// Tranzx.iVS4.App / Services / AviWriter.cs
//
// Phase 5-9 (B)：純 C# MJPEG AVI Writer
//   - 不需 FFmpeg、不需任何外部程式
//   - 把每一格 Viewport3D 的 RenderTargetBitmap 編成 JPEG 後串成 AVI
//   - 結果是標準 AVI v1.0 format，Windows Media Player / VLC 都能放
//   - 想要 MP4 的話，要客戶自己用 HandBrake / FFmpeg 轉檔
// ============================================================================

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tranzx.iVS4.App.Services;

/// <summary>純 C# 實作的 Motion-JPEG AVI Writer（標準 AVI v1.0）</summary>
public sealed class MjpegAviWriter : IDisposable
{
    private readonly BinaryWriter _bw;
    private readonly int _width, _height, _fps;
    private long _riffSizePos, _moviSizePos, _moviDataStart;
    private int _frameCount;
    private readonly int _quality;
    private readonly System.Collections.Generic.List<(uint offset, uint size)> _index = new();

    public MjpegAviWriter(string path, int width, int height, int fps = 30, int jpegQuality = 80)
    {
        _width = width; _height = height; _fps = fps;
        _quality = Math.Clamp(jpegQuality, 30, 100);
        _bw = new BinaryWriter(File.Create(path));
        WriteAviHeader();
    }

    public void AddFrame(BitmapSource? frame)
    {
        if (frame is null) return;  // 截圖失敗時跳過該幀（呼叫端會記）

        try
        {
            // 強制目標尺寸（如果不一樣就 scale）
            BitmapSource src = frame;
            if (frame.PixelWidth != _width || frame.PixelHeight != _height)
            {
                var scaled = new TransformedBitmap(frame,
                    new ScaleTransform((double)_width / frame.PixelWidth,
                                       (double)_height / frame.PixelHeight));
                scaled.Freeze();
                src = scaled;
            }

            var enc = new JpegBitmapEncoder { QualityLevel = _quality };
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            var jpg = ms.ToArray();

            // 記錄位置（給 idx1 chunk 用，offset 是相對於 movi 起點 + 4）
            uint off = (uint)(_bw.BaseStream.Position - _moviDataStart + 4);
            _index.Add((off, (uint)jpg.Length));

            // 寫入 "00dc" chunk
            _bw.Write(System.Text.Encoding.ASCII.GetBytes("00dc"));
            _bw.Write((uint)jpg.Length);
            _bw.Write(jpg);
            // chunk 必須 word-align
            if ((jpg.Length & 1) == 1) _bw.Write((byte)0);
            _frameCount++;
        }
        catch
        {
            // 個別幀寫入失敗不應該讓整個錄影掛掉
        }
    }

    public void Close()
    {
        if (_bw is null) return;
        // 補 movi size
        long moviEnd = _bw.BaseStream.Position;
        _bw.BaseStream.Position = _moviSizePos;
        _bw.Write((uint)(moviEnd - _moviSizePos - 4));

        // idx1 chunk
        _bw.BaseStream.Position = moviEnd;
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("idx1"));
        long idx1SizeP = _bw.BaseStream.Position;
        _bw.Write((uint)0);
        long idx1Start = _bw.BaseStream.Position;
        foreach (var (off, sz) in _index)
        {
            _bw.Write(System.Text.Encoding.ASCII.GetBytes("00dc"));
            _bw.Write((uint)0x10);  // AVIIF_KEYFRAME
            _bw.Write(off);
            _bw.Write(sz);
        }
        long idx1End = _bw.BaseStream.Position;
        _bw.BaseStream.Position = idx1SizeP;
        _bw.Write((uint)(idx1End - idx1Start));

        // 補 RIFF size + frame count
        long fileEnd = idx1End;
        _bw.BaseStream.Position = _riffSizePos;
        _bw.Write((uint)(fileEnd - 8));

        // avih.dwTotalFrames （offset 32 from "avih" header start）
        _bw.BaseStream.Position = _avihTotalFramesPos;
        _bw.Write((uint)_frameCount);
        // strh.dwLength
        _bw.BaseStream.Position = _strhLengthPos;
        _bw.Write((uint)_frameCount);

        _bw.BaseStream.Position = fileEnd;
        _bw.Flush();
        _bw.Dispose();
    }

    public void Dispose() => Close();

    // ─────────────────────────────────────────────────
    private long _avihTotalFramesPos, _strhLengthPos;

    private void WriteAviHeader()
    {
        // RIFF header
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        _riffSizePos = _bw.BaseStream.Position;
        _bw.Write((uint)0); // RIFF size，最後補
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("AVI "));

        // LIST hdrl
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
        long hdrlSizeP = _bw.BaseStream.Position;
        _bw.Write((uint)0);
        long hdrlStart = _bw.BaseStream.Position;
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("hdrl"));

        // avih (Main AVI Header) - 56 bytes payload
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("avih"));
        _bw.Write((uint)56);
        _bw.Write((uint)(1_000_000 / _fps));  // dwMicroSecPerFrame
        _bw.Write((uint)0);                    // dwMaxBytesPerSec
        _bw.Write((uint)0);                    // dwPaddingGranularity
        _bw.Write((uint)0x10 | 0x800);         // dwFlags AVIF_HASINDEX | AVIF_TRUSTCKTYPE
        _avihTotalFramesPos = _bw.BaseStream.Position;
        _bw.Write((uint)0);                    // dwTotalFrames（最後補）
        _bw.Write((uint)0);                    // dwInitialFrames
        _bw.Write((uint)1);                    // dwStreams
        _bw.Write((uint)0);                    // dwSuggestedBufferSize
        _bw.Write((uint)_width);               // dwWidth
        _bw.Write((uint)_height);              // dwHeight
        _bw.Write((uint)0);                    // reserved x4
        _bw.Write((uint)0);
        _bw.Write((uint)0);
        _bw.Write((uint)0);

        // LIST strl
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
        long strlSizeP = _bw.BaseStream.Position;
        _bw.Write((uint)0);
        long strlStart = _bw.BaseStream.Position;
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("strl"));

        // strh (Stream Header) - 56 bytes
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("strh"));
        _bw.Write((uint)56);
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("vids"));
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("MJPG"));
        _bw.Write((uint)0);                    // dwFlags
        _bw.Write((ushort)0);                  // wPriority
        _bw.Write((ushort)0);                  // wLanguage
        _bw.Write((uint)0);                    // dwInitialFrames
        _bw.Write((uint)1);                    // dwScale
        _bw.Write((uint)_fps);                 // dwRate
        _bw.Write((uint)0);                    // dwStart
        _strhLengthPos = _bw.BaseStream.Position;
        _bw.Write((uint)0);                    // dwLength
        _bw.Write((uint)0);                    // dwSuggestedBufferSize
        _bw.Write((uint)0xFFFFFFFF);           // dwQuality
        _bw.Write((uint)0);                    // dwSampleSize
        _bw.Write((short)0); _bw.Write((short)0); // rcFrame.left, top
        _bw.Write((short)_width); _bw.Write((short)_height);

        // strf (BITMAPINFOHEADER for MJPG) - 40 bytes
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("strf"));
        _bw.Write((uint)40);
        _bw.Write((uint)40);                   // biSize
        _bw.Write((int)_width);
        _bw.Write((int)_height);
        _bw.Write((ushort)1);                  // biPlanes
        _bw.Write((ushort)24);                 // biBitCount
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("MJPG"));
        _bw.Write((uint)(_width * _height * 3));// biSizeImage
        _bw.Write((int)0);                     // biXPelsPerMeter
        _bw.Write((int)0);                     // biYPelsPerMeter
        _bw.Write((uint)0);                    // biClrUsed
        _bw.Write((uint)0);                    // biClrImportant

        // 補 strl size
        long strlEnd = _bw.BaseStream.Position;
        _bw.BaseStream.Position = strlSizeP;
        _bw.Write((uint)(strlEnd - strlStart));
        _bw.BaseStream.Position = strlEnd;

        // 補 hdrl size
        long hdrlEnd = _bw.BaseStream.Position;
        _bw.BaseStream.Position = hdrlSizeP;
        _bw.Write((uint)(hdrlEnd - hdrlStart));
        _bw.BaseStream.Position = hdrlEnd;

        // LIST movi
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
        _moviSizePos = _bw.BaseStream.Position;
        _bw.Write((uint)0);
        _moviDataStart = _bw.BaseStream.Position;
        _bw.Write(System.Text.Encoding.ASCII.GetBytes("movi"));
    }
}

/// <summary>把 UIElement / Visual 渲染成 BitmapSource</summary>
public static class FrameCapture
{
    /// <summary>
    /// 把已 layout 的 UIElement（如 Viewport3D）渲染成 BitmapSource。
    /// 不對原 element 做 Measure / Arrange（會破壞 visible UI）。
    /// 改為包一層 DrawingVisual + VisualBrush 強制完整渲染 3D scene。
    /// 失敗回 null，呼叫端決定要不要重試 / 跳過該幀。
    /// </summary>
    public static RenderTargetBitmap? CaptureElement(UIElement el, int width, int height, double dpi = 96)
    {
        try
        {
            double srcW = el.RenderSize.Width;
            double srcH = el.RenderSize.Height;
            if (srcW < 1 || srcH < 1)
            {
                el.Measure(new Size(width, height));
                el.Arrange(new Rect(0, 0, width, height));
                el.UpdateLayout();
                srcW = el.RenderSize.Width;
                srcH = el.RenderSize.Height;
                if (srcW < 1) srcW = width;
                if (srcH < 1) srcH = height;
            }

            var brush = new VisualBrush(el)
            {
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
                    null, new Rect(0, 0, width, height));
                dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }

            var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // GPU 暫時 race condition，呼叫端可重試
            return null;
        }
        catch (System.OutOfMemoryException)
        {
            // 累積太多沒 GC 的 RenderTargetBitmap
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
