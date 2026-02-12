using System.Drawing;
using dwg2img.Providers;

namespace dwg2img.Data;

public class LoadImageService
{
    private readonly ImageDataProvider _provider = new();

    public byte[] GetBytes(int art, int ver)
    {
        return _provider.GetImage(art, ver);
    }

    private static readonly Dictionary<float, Font> _fontCache = new();
    private static readonly Lock _fontCacheLock = new();

    public byte[] GetWatermarkedBytes(byte[] src, string[] watermark)
    {
        var brush = new SolidBrush(Color.FromArgb(25, 215, 215, 215));

        int imgWidth, imgHeight;
        using (var img = Image.FromStream(new MemoryStream(src)))
        {
            imgWidth = img.Width;
            imgHeight = img.Height;
            using (var g = Graphics.FromImage(img))
            {
                float textWidth = 0;
                float textHeight = 0;
                int totalStrings = watermark.Length * 2 + 1;
                string longestString = watermark.OrderByDescending(s => s.Length).First();

                Font? font = null;
                for (int fontSize = 100; fontSize <= 300; fontSize += 2)
                {
                    // Check font cache first
                    lock (_fontCacheLock)
                    {
                        if (!_fontCache.TryGetValue(fontSize, out font))
                        {
                            font = new Font("Arial", fontSize, FontStyle.Bold);
                            _fontCache[fontSize] = font;
                        }
                    }

                    textWidth = g.MeasureString(longestString, font).Width;
                    textHeight = g.MeasureString(longestString, font).Height * 0.8f; // Correction for measuring
                    if (textWidth > imgWidth - 100 || textHeight * totalStrings > imgHeight) break;
                }

                int c = 1;
                foreach (var line in watermark.Concat(watermark).Append(watermark[0]))
                {
                    textWidth = g.MeasureString(line, font!).Width;
                    textHeight = g.MeasureString(line, font!).Height;

                    float textX = (imgWidth - textWidth) / 2;
                    float textY = (imgHeight / (totalStrings + 1) * c) - textHeight / 2;
                    g.DrawString(line, font!, brush, textX, textY);
                    c++;
                }
            }
            using MemoryStream ms = new();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // Dispose cached fonts when service is no longer needed
    public void Dispose()
    {
        lock (_fontCacheLock)
        {
            foreach (var font in _fontCache.Values)
            {
                font.Dispose();
            }
            _fontCache.Clear();
        }
    }

    public string GetBase64(byte[] src) => Convert.ToBase64String(src);

    public async Task<string> LoadImage(int art, int ver, string[] watermark)
    {
        return await Task.Run(() =>
        {
            var bytes = GetBytes(art, ver);
            bytes = GetWatermarkedBytes(bytes, watermark);
            return GetBase64(bytes);
        });
    }
}
