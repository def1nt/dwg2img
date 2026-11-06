using Microsoft.Data.SqlClient;
using System.Drawing;
using System.IO.Compression;
using zlib;
using Aspose.CAD.ImageOptions;
using Aspose.CAD.FileFormats.Cad;
using Aspose.CAD.FileFormats.Cad.CadObjects;
using Aspose.CAD.FileFormats.Cad.CadConsts;

namespace dwg2img.Providers;

sealed class ImageDataProvider
{
    private readonly CacheProvider _defaultCache = new();
    private readonly CacheProvider _cleanCache = new("cache-clean");

    private const string Server = "srv-search";
    private const string DBName = "search";
    private const string UserID = "searchtopdf";
    private const string Password = " SQLsearch231";
    private const string ConnectionString = "Server={0}; Initial Catalog={1}; encrypt=false; trustServerCertificate=false; User ID={2}; Password={3}";

    private static readonly SqlConnection Connection = new(string.Format(ConnectionString, Server, DBName, UserID, Password));

    private Bitmap? sampleBitmap;

    ~ImageDataProvider()
    {
        if (Connection.State != System.Data.ConnectionState.Closed) Connection.Close();
    }

    public byte[] GetImage(int art, int ver, bool stripDimensions = false, bool useCache = true, bool cleanCache = false)
    {
        byte[] bytes;

        var cache = (stripDimensions || cleanCache) ? _cleanCache : _defaultCache;

        bool cached = useCache && cache.CheckCache(art, ver);
        // bool cached = false; // Testing pre-processing
        if (cached)
        {
            bytes = cache.GetFromCache(art, ver);
        }
        else
        {
            try
            {
                TimeOnly start = TimeOnly.FromDateTime(DateTime.Now);
                bytes = GetFile(art, ver);
                if (bytes.Length != 0)
                {
                    bytes = Convert(bytes, out int borders, stripDimensions);
                    // System.Console.WriteLine($"Border layers: {borders}");

                    var bitmap = new Bitmap(Image.FromStream(new MemoryStream(bytes))); // Stream conversion
                    try
                    {
                        RemoveWatermark(ref bitmap);
                        CropWhiteSpace(ref bitmap);

                        if (stripDimensions)
                        {
                            bytes = BuildStripZip(bitmap, borders);
                        }
                        else
                        {
                            using var ms = new MemoryStream();
                            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            bytes = ms.ToArray(); // Stream conversion
                        }

                        if (useCache) cache.SaveToCache(bytes, (art, ver));
                        TimeOnly finish = TimeOnly.FromDateTime(DateTime.Now);
                        // Console.WriteLine($"Image {art}/{ver} generated in {finish - start}");
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }
                else // TODO: Log this outcome somehow differently??
                {
                    bytes = !cleanCache ? cache.GetFromCache(0, 0) : Array.Empty<byte>();
                }
            }
            catch (Aspose.CAD.CadExceptions.ImageLoadException e)
            {
                Console.WriteLine($"{art}/{ver}: ImageLoadException: {e.Message}");
                bytes = !cleanCache ? cache.GetFromCache(-1, 0) : Array.Empty<byte>();
            }
            catch (Exception e)
            {
                Console.WriteLine($"{art}/{ver}: Exception: {e.Message}\n{e.StackTrace}");
                bytes = !cleanCache ? cache.GetFromCache(-2, 0) : Array.Empty<byte>();
            }
        }

        return bytes;
    }

    public byte[] GetFile((int, int) par) => GetFile(par.Item1, par.Item2);

    public byte[] GetFile(int art, int ver)
    {
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
                        where a.ART_ID=@artid and a.DOC_VER_ID =@docverid and d4.VERSION_ID = @docverid and a.DOC_ID=d4.FILE_ID
                        end
                        """;

        query = string.Format(query, art.ToString(), ver.ToString());

        if (Connection.State != System.Data.ConnectionState.Open) Connection.Open();

        SqlCommand cmd = new SqlCommand(query, Connection);
        byte[] rawfile;
        byte[] file = Array.Empty<byte>();
        using (SqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection))
        {
            if (reader.Read())
            {
                rawfile = (byte[])reader.GetValue(0);

                try { Decompress(rawfile, out file); }
                catch (Exception e) { System.Console.WriteLine("Decompress: " + e.Message); return Array.Empty<byte>(); }
            }
        }
        return file;
    }

    private static void Decompress(byte[] inData, out byte[] outData)
    {
        using MemoryStream outMemoryStream = new();
        using ZOutputStream outZStream = new(outMemoryStream);
        using Stream inMemoryStream = new MemoryStream(inData);
        CopyStream(inMemoryStream, outZStream);
        outZStream.finish();
        outData = outMemoryStream.ToArray();
    }

    private static void CopyStream(Stream input, Stream output)
    {
        byte[] buffer = new byte[2000];
        int len;
        while ((len = input.Read(buffer, 0, 2000)) > 0)
        {
            output.Write(buffer, 0, len);
        }
        output.Flush();
    }

    public byte[] Convert(byte[] dwg, out int borders, bool stripDimensions = false)
    {
        using var source = new MemoryStream(dwg, writable: false);
        using var image = (CadImage)Aspose.CAD.Image.Load(source);

        if (stripDimensions)
            RemoveDimensions(image);

        borders = image.Layers.GetLayersNames().Count(s => s.Contains("BORDER"));

        using var output = new MemoryStream();
        var rasterizationOptions = new CadRasterizationOptions
        {
            BackgroundColor = Aspose.CAD.Color.White,
            DrawType = CadDrawTypeMode.UseObjectColor,
            PageWidth = 4800,
            PageHeight = 4800,
        };
        if (stripDimensions) rasterizationOptions.Layers = ["0", "POZ", "ASS"]; // Главный слой и сноски

        var options = new PngOptions
        {
            VectorRasterizationOptions = rasterizationOptions
        };

        image.Save(output, options);
        return output.ToArray();

        // A dimension is a DIMENSION entity (any CadDimensionBase subclass: rotated,
        // aligned, radial, diametric, angular, ordinate, R12, ...). Some third-party
        // CADs wrap their own dimension markers in ACAD_PROXY_ENTITY objects and put
        // the real class name into SubClassName, so check that too.
        static bool IsDimension(CadBaseEntity entity) =>
            entity is CadDimensionBase
            || entity.TypeName == CadEntityTypeName.DIMENSION
            || (entity is CadAcadProxyEntity proxy
                && proxy.SubClassName?.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) >= 0);

        static void RemoveDimensions(CadImage cad)
        {
            // Model/paper space. RemoveEntity keeps Aspose's internal model-space
            // bookkeeping in sync with the public Entities array.
            if (cad.Entities != null)
            {
                foreach (var entity in cad.Entities.Where(IsDimension).ToList())
                    cad.RemoveEntity(entity);
            }

            // Model space is also exposed as the *MODEL_SPACE block, and every nested
            // block (including layout blocks and blocks inserted on layer "0") has its
            // own entity list. Dimensions must be removed from the block definitions as
            // well, otherwise they would still be drawn through their INSERT.
            foreach (var block in cad.BlockEntities.ValuesTyped)
            {
                var entities = block.Entities;
                if (entities == null)
                    continue;

                var kept = entities.Where(entity => !IsDimension(entity)).ToArray();
                if (kept.Length != entities.Length)
                    block.Entities = kept;
            }

            // RemoveEntity already changed this array, but assigning the filtered array
            // also covers files where the model-space block and Entities are not shared.
            if (cad.Entities != null)
                cad.Entities = cad.Entities.Where(entity => !IsDimension(entity)).ToArray();
        }
    }

    public void RemoveWatermark(ref Bitmap bitmap)
    {
        sampleBitmap ??= new Bitmap(Image.FromFile(Path.Combine(AppContext.BaseDirectory, @"samples\sample.png")));
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

    /// <summary>
    /// Slices the bitmap into <paramref name="borders"/> vertical strips, encodes
    /// each strip as a PNG in memory and packs them into an in-memory ZIP archive
    /// whose entries are named part_0.png .. part_{partCount-1}.png.
    /// </summary>
    private static byte[] BuildStripZip(Bitmap bitmap, int borders)
    {
        // If no BORDER layers we kinda assume it's 1
        int partCount = borders > 0 ? borders : 1;
        if (borders <= 0) Console.WriteLine($"Border layers: {borders}, falling back to a single strip.");

        // If in some fantastic event image is a couple pixels wide, slice just them
        // and return so OBVIOUSLY an error is APPARENT
        if (partCount > bitmap.Width) partCount = bitmap.Width;

        Bitmap[] slices = Split(in bitmap, partCount);
        try
        {
            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                for (int i = 0; i < slices.Length; i++)
                {
                    using var pngStream = new MemoryStream();
                    slices[i].Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] png = pngStream.ToArray();

                    // Every created entry gets its slice data written below, so the
                    // archive never contains empty or trailing entries.
                    ZipArchiveEntry entry = zip.CreateEntry($"part_{i}.png", CompressionLevel.Optimal);
                    using Stream entryStream = entry.Open();
                    entryStream.Write(png, 0, png.Length);
                }
                // Disposing the archive flushes all entry streams and writes the
                // central directory into zipStream before its bytes are read back.
            }
            return zipStream.ToArray();
        }
        finally
        {
            foreach (Bitmap slice in slices) slice.Dispose();
        }
    }

    /// <summary>
    /// Splits <paramref name="bitmap"/> into <paramref name="parts"/> vertical slices
    /// (equal-width columns) that preserve the full image height. The remainder of
    /// Width / parts is distributed one extra pixel at a time across the leading
    /// (left-most) slices, the rest use Math.Floor(Width / parts) widths, so no slice
    /// differs from the others by more than 1 px.
    /// </summary>
    private static Bitmap[] Split(in Bitmap bitmap, int parts)
    {
        if (parts < 1) throw new ArgumentOutOfRangeException(nameof(parts), parts, "Parts must be at least 1.");

        var result = new Bitmap[parts];
        int baseWidth = bitmap.Width / parts; // floor; remainder goes to leading slices
        int extra = bitmap.Width % parts;

        int x = 0;
        for (int i = 0; i < parts; i++)
        {
            int width = baseWidth + (i < extra ? 1 : 0);
            result[i] = bitmap.Clone(new Rectangle(x, 0, width, bitmap.Height), System.Drawing.Imaging.PixelFormat.DontCare);
            x += width;
        }
        return result;
    }
}
