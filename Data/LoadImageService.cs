using System.Drawing;
using auth.Providers;

namespace auth.Data;

public class LoadImageService
{
    ImageDataProvider _provider = new ImageDataProvider();

    public byte[] GetBytes(int art, int ver)
    {
        return _provider.GetImage(art, ver);
    }

    public byte[] GetWatermarkedBytes(byte[] src, string[] watermark)
    {
        var font = new Font("Arial", 200, FontStyle.Bold);
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
                for (int fontSize = 100; fontSize <= 300; fontSize += 2)
                {
                    font = new Font("Arial", fontSize, FontStyle.Bold);
                    textWidth = g.MeasureString(longestString, font).Width;
                    textHeight = g.MeasureString(longestString, font).Height * 0.8f; // Correction for measuring
                    if (textWidth > imgWidth - 100 || textHeight * totalStrings > imgHeight) break;
                }

                int c = 1;
                foreach (var line in watermark.Concat(watermark).Append(watermark[0]))
                {
                    textWidth = g.MeasureString(line, font).Width;
                    textHeight = g.MeasureString(line, font).Height;

                    float textX = (imgWidth - textWidth) / 2;
                    float textY = (imgHeight / (totalStrings + 1) * c) - textHeight / 2;
                    g.DrawString(line, font, brush, textX, textY);
                    c++;
                }
            }
            using (var ms = new MemoryStream())
            {
                img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
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
