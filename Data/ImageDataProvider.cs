using Microsoft.Data.SqlClient;
using System.Drawing;
using zlib;
using Aspose.CAD.ImageOptions;

namespace auth.Providers;

class ImageDataProvider
{
    private CacheProvider _cache = new CacheProvider();

    private const string Server = "srv-search";
    private const string DBName = "search";
    private const string UserID = "guest";
    private const string Password = "search";
    private const string ConnectionString = "Server={0}; Initial Catalog={1}; encrypt=false; trustServerCertificate=false; User ID={2}; Password={3}";

    private static SqlConnection Connection = new SqlConnection(string.Format(ConnectionString, Server, DBName, UserID, Password));

    ~ImageDataProvider()
    {
        if (Connection.State != System.Data.ConnectionState.Closed) Connection.Close();
    }

    public byte[] GetImage(int art, int ver)
    {
        byte[] bytes = new byte[] { };

        bool cached = _cache.CheckCache(art, ver);
        // bool cached = false; // Testing pre-processing
        if (cached)
        {
            bytes = _cache.GetFromCache(art, ver);
        }
        else
        {
            try
            {
                TimeOnly start = TimeOnly.FromDateTime(DateTime.Now);
                bytes = GetFile(art, ver);
                if (bytes.Length != 0)
                {
                    bytes = Convert(bytes);

                    var bitmap = new Bitmap(Image.FromStream(new MemoryStream(bytes)));
                    RemoveWatermark(ref bitmap);
                    CropWhiteSpace(ref bitmap);

                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        bytes = ms.ToArray();
                    }

                    _cache.SaveToCache(bytes, (art, ver));
                    TimeOnly finish = TimeOnly.FromDateTime(DateTime.Now);
                    System.Console.WriteLine(finish - start);
                }
                else // TODO: Log this outcome somehow differently??
                {
                    bytes = _cache.GetFromCache(0, 0);
                }
            }
            catch (Aspose.CAD.CadExceptions.ImageLoadException e)
            {
                System.Console.WriteLine($"{art}/{ver}: {e.Message}");
                bytes = _cache.GetFromCache(-1, 0);
            }
            catch (Exception e)
            {
                System.Console.WriteLine($"{art}/{ver}: {e.Message}");
                bytes = _cache.GetFromCache(-2, 0);
            }
        }

        return bytes;
    }

    public byte[] GetFile((int, int) par) => GetFile(par.Item1, par.Item2);

    public byte[] GetFile(int art, int ver)
    {
        byte[] rawfile = new byte[] { };

        string query = """
                        declare @artid int,@docverid int,@vartid int
                        set @artid={0}
                        set @docverid={1}

                        select @vartid=VART_ID from V_ARTICLES where ART_ID=@artid and DOC_VER_ID=@docverid

                        declare @prizn as int
                        select @prizn =SECTION_ID from V_ARTICLES where ART_ID=@artid and DOC_VER_ID=@docverid
                        if @prizn =3
                        begin
                        select d4.FILEBODY
                        from V_ARTICLES va
                        join v_pc vp
                        on va.VART_ID=vp.PART_AID
                        and vp.PROJ_AID=@vartid
                        and va.SECTION_ID=1
                        join rc r on r.DOC_ID=va.DOC_ID
                        and r.VERSION_ID=va.DOC_VER_ID
                        join DOCUMS4 d4 on r.FILENAME=d4.FLNAME
                        end
                        else
                        begin
                        select d4.FILEBODY from v_articles a
                        join rc r on a.DOC_ID=r.DOC_ID and a.DOC_VER_ID=r.VERSION_ID
                        join DOCUMS4 d4 on r.FILENAME=d4.FLNAME
                        where a.ART_ID=@artid and a.DOC_VER_ID =@docverid
                        end
                        """;

        query = string.Format(query, art.ToString(), ver.ToString());

        if (Connection.State != System.Data.ConnectionState.Open) Connection.Open();

        SqlCommand cmd = new SqlCommand(query, Connection);
        byte[] file = new byte[] { };
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                rawfile = (byte[])reader.GetValue(0);

                try { Decompress(rawfile, out file); }
                catch (Exception e) { System.Console.WriteLine(e.Message); return new byte[] { }; }
            }
        }
        return file;
    }

    private static void Decompress(byte[] inData, out byte[] outData)
    {
        using (MemoryStream outMemoryStream = new MemoryStream())
        using (ZOutputStream outZStream = new ZOutputStream(outMemoryStream))
        using (Stream inMemoryStream = new MemoryStream(inData))
        {
            CopyStream(inMemoryStream, outZStream);
            outZStream.finish();
            outData = outMemoryStream.ToArray();
        }
    }

    private static void CopyStream(System.IO.Stream input, System.IO.Stream output)
    {
        byte[] buffer = new byte[2000];
        int len;
        while ((len = input.Read(buffer, 0, 2000)) > 0)
        {
            output.Write(buffer, 0, len);
        }
        output.Flush();
    }

    public byte[] Convert(byte[] dwg)
    {
        MemoryStream output = new MemoryStream();
        using (var source = new MemoryStream(dwg, false))
        using (var image = Aspose.CAD.Image.Load(source))
        {
            // Create an instance of CadRasterizationOptions and set its various properties
            Aspose.CAD.ImageOptions.CadRasterizationOptions rasterizationOptions = new Aspose.CAD.ImageOptions.CadRasterizationOptions();
            rasterizationOptions.BackgroundColor = Aspose.CAD.Color.White;
            rasterizationOptions.DrawType = Aspose.CAD.FileFormats.Cad.CadDrawTypeMode.UseObjectColor;
            rasterizationOptions.PageWidth = 4800;
            rasterizationOptions.PageHeight = 4800;

            var options = new Aspose.CAD.ImageOptions.PngOptions();
            options.VectorRasterizationOptions = rasterizationOptions;

            image.Save(output, options);
        }
        return output.GetBuffer();
    }

    public void RemoveWatermark(ref Bitmap bitmap)
    {
        var sampleImage = Image.FromFile(Path.Combine(AppContext.BaseDirectory, @"samples\sample.png"));
        var sampleBitmap = new Bitmap(sampleImage);
        for (int i = 0; i < sampleBitmap.Width; i++)
        {
            for (int j = 0; j < sampleBitmap.Height; j++)
            {
                var pixel = bitmap.GetPixel(i, j);
                var samplePixel = sampleBitmap.GetPixel(i, j);

                if (pixel == samplePixel)
                {
                    bitmap.SetPixel(i, j, Color.White);
                }
            }
        }
    }

    public void CropWhiteSpace(ref Bitmap bitmap)
    {
        var testBorder = FindBorder(in bitmap);

        bitmap = bitmap.Clone(testBorder, System.Drawing.Imaging.PixelFormat.DontCare);
    }

    private Rectangle FindBorder(in Bitmap bitmap)
    {
        int x = 0, y = 0, width = bitmap.Width, height = bitmap.Height;
        (bool left, bool top, bool right, bool bottom) skip = (false, false, false, false);

        while (x < width && y < height)
        {
            if (!skip.top)
            {
                skip.top = !IsLineEmpty(in bitmap, y, x, width, false);
                if (!skip.top)
                {
                    y++;
                }
            }

            if (!skip.right)
            {
                skip.right = !IsLineEmpty(in bitmap, width - 1, y, height, true);
                if (!skip.right)
                {
                    width--;
                }
            }

            if (!skip.bottom)
            {
                skip.bottom = !IsLineEmpty(in bitmap, height - 1, x, width, false);
                if (!skip.bottom)
                {
                    height--;
                }
            }

            if (!skip.left)
            {
                skip.left = !IsLineEmpty(in bitmap, x, y, height, true);
                if (!skip.left)
                {
                    x++;
                }
            }

            if (skip.left && skip.top && skip.right && skip.bottom)
            {
                break;
            }
        }
        return new Rectangle(x, y, width - x, height - y);
    }

    public bool IsLineEmpty(in Bitmap bitmap, int point, int low, int high, bool vertical)
    {
        const int White = -1; // Color.White.ToArgb()
        int nonWhitePixels = 0;
        for (int i = low; i < high; i++)
        {
            if (vertical)
            {
                if (bitmap.GetPixel(point, i).ToArgb() != White)
                {
                    nonWhitePixels++;
                }
            }
            else
            {
                if (bitmap.GetPixel(i, point).ToArgb() != White)
                {
                    nonWhitePixels++;
                }
            }
            if (nonWhitePixels > 2)
            {
                return false;
            }
        }
        return true;
    }

}
