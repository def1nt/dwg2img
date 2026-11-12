using Microsoft.Data.SqlClient;
using System.Drawing;
using System.IO.Compression;
using zlib;
using Aspose.CAD.ImageOptions;
using Aspose.CAD.FileFormats.Cad;
using Aspose.CAD.FileFormats.Cad.CadObjects;
using Aspose.CAD.FileFormats.Cad.CadObjects.Polylines;
using Aspose.CAD.FileFormats.Cad.CadObjects.Vertices;
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

    public byte[] GetImage(int art, int ver, bool stripDimensions = false, bool refresh = false, bool cleanCache = false)
    {
        byte[] bytes;

        var cache = (stripDimensions || cleanCache) ? _cleanCache : _defaultCache;

        bool cached = !refresh && cache.CheckCache(art, ver);
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
                    bytes = Convert(bytes, out Rectangle[] borders, stripDimensions);
                    var bitmap = new Bitmap(Image.FromStream(new MemoryStream(bytes))); // Stream conversion
                    try
                    {
                        RemoveWatermark(ref bitmap);
                        Rectangle crop = CropWhiteSpace(ref bitmap);
                        if (stripDimensions)
                        {
                            Rectangle[] parts = MapBorders(borders, crop);
                            if (parts.Length == 0)
                            {
                                Console.WriteLine($"{art}/{ver}: No BORDER geometry found; drawing is considered malformed.");
                                bytes = Array.Empty<byte>();
                            }
                            else
                            {
                                bytes = BuildStripZip(bitmap, parts);
                            }
                        }
                        else
                        {
                            using var ms = new MemoryStream();
                            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            bytes = ms.ToArray(); // Stream conversion
                        }

                        if (bytes.Length > 0) cache.SaveToCache(bytes, (art, ver));
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

    public byte[] Convert(byte[] dwg, out Rectangle[] borders, bool stripDimensions = false)
    {
        using var source = new MemoryStream(dwg, writable: false);
        using var image = (CadImage)Aspose.CAD.Image.Load(source);

        if (stripDimensions)
            RemoveDimensions(image);

        // Locate the actual border frames while the vector data is still
        // available. Each BORDER* layer is expected to contain one frame; the
        // returned rectangles are in model coordinates and are mapped onto the
        // rasterized/cropped bitmap later. Border detection must never prevent
        // the normal (non-sliced) image from being produced.
        try
        {
            borders = ToModelRectangles(FindBorderBounds(image));
        }
        catch (Exception e)
        {
            Console.WriteLine($"Border detection failed: {e.Message}");
            borders = Array.Empty<Rectangle>();
        }

        using var output = new MemoryStream();
        var rasterizationOptions = new CadRasterizationOptions
        {
            BackgroundColor = Aspose.CAD.Color.White,
            DrawType = CadDrawTypeMode.UseObjectColor,
            PageWidth = 4800,
            PageHeight = 4800,
        };
        if (stripDimensions) rasterizationOptions.Layers = ["0", "POZ", "ASS", "0_DIM", "BORDER", "BORDER1", "BORDER2", "BORDER3"]; // Главный слой, сноски и рамки, допустим четыре

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

    // Modifies bitmap in place, returns crop rectangle for border coordinates mapping
    public Rectangle CropWhiteSpace(ref Bitmap bitmap)
    {
        var testBorder = FindBorder(in bitmap);

        bitmap = bitmap.Clone(testBorder, System.Drawing.Imaging.PixelFormat.DontCare);
        return testBorder;
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
    /// Slices the bitmap into the supplied rectangles, encodes each slice as a PNG
    /// in memory and packs them into an in-memory ZIP archive whose entries are
    /// named part_0.png .. part_{partCount-1}.png.
    /// </summary>
    private static byte[] BuildStripZip(Bitmap bitmap, Rectangle[] parts)
    {
        if (parts.Length == 0) return Array.Empty<byte>();

        Bitmap[] slices = Split(in bitmap, parts);
        if (slices.Length == 0) return Array.Empty<byte>();

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
    /// Crops <paramref name="bitmap"/> to each rectangle. Rectangles are already
    /// expressed in the bitmap's coordinate system and define the actual page
    /// borders, so parts with different widths are preserved exactly.
    /// </summary>
    private static Bitmap[] Split(in Bitmap bitmap, Rectangle[] parts)
    {
        var imageBounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var result = new List<Bitmap>(parts.Length);

        foreach (Rectangle part in parts)
        {
            Rectangle slice = Rectangle.Intersect(part, imageBounds);
            if (slice.Width <= 0 || slice.Height <= 0)
                continue;

            result.Add(bitmap.Clone(slice, System.Drawing.Imaging.PixelFormat.DontCare));
        }

        return result.ToArray();
    }

    /// <summary>
    /// Converts the model-space border bounds into integer rectangles. The values
    /// are relative to the model origin; <see cref="MapBorders"/> fits their union
    /// onto the actual raster ink bounds later.
    /// </summary>
    private static Rectangle[] ToModelRectangles(Dictionary<string, ModelBounds> borderBounds)
    {
        return borderBounds.Values
            .Where(b => b.IsValid)
            .Select(b => Rectangle.FromLTRB(
                (int)Math.Floor(b.MinX),
                (int)Math.Floor(b.MinY),
                (int)Math.Ceiling(b.MaxX),
                (int)Math.Ceiling(b.MaxY)))
            .Where(r => r.Width > 0 && r.Height > 0)
            .OrderBy(r => r.Left)
            .ThenBy(r => r.Top)
            .ToArray();
    }

    /// <summary>
    /// Maps model-space border rectangles onto the cropped raster. The union of
    /// all borders is fitted to the raster's ink bounds, which preserves the
    /// relative widths of unequal BORDER layers.
    /// </summary>
    private static Rectangle[] MapBorders(Rectangle[] modelRects, Rectangle crop)
    {
        if (modelRects.Length == 0 || crop.Width <= 0 || crop.Height <= 0)
            return Array.Empty<Rectangle>();

        Rectangle union = UnionBounds(modelRects);
        if (union.Width <= 0 || union.Height <= 0)
            return Array.Empty<Rectangle>();

        var result = new List<Rectangle>(modelRects.Length);
        var cropBounds = new Rectangle(0, 0, crop.Width, crop.Height);

        foreach (Rectangle rect in modelRects)
        {
            int left = (int)Math.Round(((double)rect.Left - union.Left) / union.Width * crop.Width);
            int right = (int)Math.Round(((double)rect.Right - union.Left) / union.Width * crop.Width);
            int top = (int)Math.Round(((double)union.Bottom - rect.Bottom) / union.Height * crop.Height);
            int bottom = (int)Math.Round(((double)union.Bottom - rect.Top) / union.Height * crop.Height);

            Rectangle part = Rectangle.FromLTRB(left, top, right, bottom);
            part = Rectangle.Intersect(part, cropBounds);
            if (part.Width > 0 && part.Height > 0)
                result.Add(part);
        }

        return result.ToArray();
    }

    private static Rectangle UnionBounds(IEnumerable<Rectangle> rectangles)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (Rectangle rect in rectangles)
        {
            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return right >= left && bottom >= top
            ? Rectangle.FromLTRB(left, top, right, bottom)
            : Rectangle.Empty;
    }

    /// <summary>
    /// Walks the model space (including nested block inserts) and computes the
    /// union of curve geometry for every layer whose name contains "BORDER".
    /// </summary>
    private static Dictionary<string, ModelBounds> FindBorderBounds(CadImage image)
    {
        var unions = new Dictionary<string, ModelBounds>(StringComparer.OrdinalIgnoreCase);
        var largestClosed = new Dictionary<string, (ModelBounds Bounds, double Area)>(StringComparer.OrdinalIgnoreCase);

        var borderLayers = image.Layers?.GetLayersNames()
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && name.Contains("BORDER", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (borderLayers == null || borderLayers.Count == 0 || image.Entities == null)
            return new Dictionary<string, ModelBounds>(StringComparer.OrdinalIgnoreCase);

        var blockStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(image.Entities, Affine.Identity, null, 0);

        // A border frame is normally the largest closed polyline on its layer
        // (outer + inner frame lines are both closed; the outer one wins). If a
        // file draws the frame from separate LINE entities, fall back to the
        // union of all curve geometry on that layer.
        var result = new Dictionary<string, ModelBounds>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in unions)
        {
            result[pair.Key] = largestClosed.TryGetValue(pair.Key, out var closed)
                ? closed.Bounds
                : pair.Value;
        }
        return result;

        void Walk(IEnumerable<CadBaseEntity>? entities, Affine transform, string? inheritedLayer, int depth)
        {
            if (entities == null || depth > 16)
                return;

            foreach (var entity in entities)
            {
                if (entity == null)
                    continue;

                if (entity is CadInsertObject insert)
                {
                    string? insertLayer = EffectiveLayer(insert.LayerName, inheritedLayer);
                    string? blockName = !string.IsNullOrWhiteSpace(insert.Name)
                        ? insert.Name
                        : insert.OriginalBlockName;

                    if (string.IsNullOrWhiteSpace(blockName) || !blockStack.Add(blockName))
                        continue;

                    try
                    {
                        CadBlockEntity? block = null;
                        if (image.BlockEntities.ContainsKey(blockName))
                            block = image.BlockEntities[blockName];
                        else
                        {
                            string? key = image.BlockEntities.KeysTyped
                                .FirstOrDefault(k => string.Equals(k, blockName, StringComparison.OrdinalIgnoreCase));
                            if (key != null)
                                block = image.BlockEntities[key];
                        }

                        if (block?.Entities == null)
                            continue;

                        var basePoint = block.BasePoint ?? new Cad3DPoint();
                        Affine insertTransform = BuildInsertTransform(insert, basePoint).Then(transform);
                        Walk(block.Entities, insertTransform, insertLayer, depth + 1);
                    }
                    finally
                    {
                        blockStack.Remove(blockName);
                    }

                    continue;
                }

                string? layer = EffectiveLayer(entity.LayerName, inheritedLayer);
                if (layer != null
                    && borderLayers.Contains(layer)
                    && TryGetCurveBounds(entity, out ModelBounds local))
                {
                    ModelBounds world = TransformBounds(local, transform);

                    // Ignore degenerate geometry (points/zero-length lines): it
                    // would otherwise pull the layer bounds towards the origin.
                    if (world.Width == 0 && world.Height == 0)
                        continue;

                    unions[layer] = unions.TryGetValue(layer, out ModelBounds existing)
                        ? existing.Union(world)
                        : world;

                    if (IsClosedPolyline(entity))
                    {
                        double area = world.Width * world.Height;
                        if (!largestClosed.TryGetValue(layer, out var best) || area > best.Area)
                            largestClosed[layer] = (world, area);
                    }
                }

                // Some proxy/complex entities expose their geometry as children.
                if (entity.ChildObjects is { Count: > 0 })
                    Walk(entity.ChildObjects, transform, layer, depth + 1);
            }
        }
    }

    private static string? EffectiveLayer(string? layer, string? inheritedLayer)
    {
        if (string.IsNullOrWhiteSpace(layer) || string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase))
            return inheritedLayer ?? layer;
        return layer;
    }

    private static bool IsClosedPolyline(CadBaseEntity entity)
    {
        switch (entity)
        {
            case CadLwPolyline polyline:
                if ((polyline.Flag & CadLwPolylineFlag.Closed) == CadLwPolylineFlag.Closed)
                    return true;

                if (polyline.Coordinates is { Count: > 1 })
                {
                    var first = polyline.Coordinates[0];
                    var last = polyline.Coordinates[^1];
                    return Math.Abs(first.X - last.X) < 1e-6
                        && Math.Abs(first.Y - last.Y) < 1e-6;
                }
                return false;

            case CadPolylineBase polyline:
                return (polyline.Flag & CadPolylineFlag.CLOSED_POLY) == CadPolylineFlag.CLOSED_POLY;

            default:
                return false;
        }
    }

    private static bool TryGetCurveBounds(CadBaseEntity entity, out ModelBounds bounds)
    {
        switch (entity)
        {
            case CadLine line when line.FirstPoint != null && line.SecondPoint != null:
                bounds = ModelBounds.FromPoints(line.FirstPoint, line.SecondPoint);
                return true;

            case CadLwPolyline polyline when polyline.Coordinates is { Count: > 0 }:
                bounds = ModelBounds.FromPoints(polyline.Coordinates
                    .Where(p => p != null)
                    .Select(p => (p.X, p.Y)));
                return true;

            case CadPolylineBase polyline when polyline.ChildObjects is { Count: > 0 }:
                var vertices = polyline.ChildObjects
                    .OfType<CadVertexBase>()
                    .Select(vertex => vertex.LocationPoint)
                    .Where(point => point != null)
                    .ToList();
                if (vertices.Count > 0)
                {
                    bounds = ModelBounds.FromPoints(vertices);
                    return true;
                }
                break;

            case CadCircle circle when circle.CenterPoint != null:
                bounds = ModelBounds.FromCenterRadius(circle.CenterPoint, circle.Radius);
                return true;

            case CadArc arc when arc.CenterPoint != null:
                bounds = ModelBounds.FromCenterRadius(arc.CenterPoint, arc.Radius);
                return true;

            case CadEllipse ellipse when ellipse.CenterPoint != null:
                double radius = Math.Max(Length(ellipse.MajorEndPoint), Length(ellipse.MinorEndPoint));
                bounds = ModelBounds.FromCenterRadius(ellipse.CenterPoint, radius);
                return true;

            case CadSpline spline:
                var points = new List<Cad3DPoint>();
                if (spline.ControlPoints != null)
                    points.AddRange(spline.ControlPoints.Where(p => p != null));
                if (spline.FitPoints != null)
                    points.AddRange(spline.FitPoints.Where(p => p != null));
                if (points.Count > 0)
                {
                    bounds = ModelBounds.FromPoints(points);
                    return true;
                }
                break;
        }

        bounds = default;
        return false;
    }

    private static ModelBounds TransformBounds(ModelBounds bounds, Affine transform)
    {
        var p1 = transform.Apply(bounds.MinX, bounds.MinY);
        var p2 = transform.Apply(bounds.MinX, bounds.MaxY);
        var p3 = transform.Apply(bounds.MaxX, bounds.MinY);
        var p4 = transform.Apply(bounds.MaxX, bounds.MaxY);
        return ModelBounds.FromPoints(new[] { p1, p2, p3, p4 });
    }

    private static Affine BuildInsertTransform(CadInsertObject insert, Cad3DPoint basePoint)
    {
        double scaleX = insert.ScaleX;
        double scaleY = insert.ScaleY;
        double angle = NormalizeAngle(insert.RotationAngle);
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        double m11 = scaleX * cos;
        double m12 = -scaleY * sin;
        double m21 = scaleX * sin;
        double m22 = scaleY * cos;

        double insertionX = insert.InsertionPoint?.X ?? 0;
        double insertionY = insert.InsertionPoint?.Y ?? 0;
        double dx = insertionX - (m11 * basePoint.X + m12 * basePoint.Y);
        double dy = insertionY - (m21 * basePoint.X + m22 * basePoint.Y);
        return new Affine(m11, m12, m21, m22, dx, dy);
    }

    private static double NormalizeAngle(double angle)
    {
        // Aspose exposes block rotation in radians, but a few third-party files
        // store degrees. Treat values outside a full turn as degrees.
        if (Math.Abs(angle) > Math.PI * 2)
            return angle * Math.PI / 180.0;
        return angle;
    }

    private static double Length(Cad3DPoint? point) =>
        point == null ? 0 : Math.Sqrt(point.X * point.X + point.Y * point.Y);

    private readonly record struct ModelBounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public bool IsValid =>
            double.IsFinite(MinX) && double.IsFinite(MinY)
            && double.IsFinite(MaxX) && double.IsFinite(MaxY)
            && MaxX >= MinX && MaxY >= MinY;

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public ModelBounds Union(ModelBounds other) => new(
            Math.Min(MinX, other.MinX),
            Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX),
            Math.Max(MaxY, other.MaxY));

        public static ModelBounds FromPoints(params Cad3DPoint[] points) =>
            FromPoints(points.Where(p => p != null).Select(p => (p.X, p.Y)));

        public static ModelBounds FromPoints(IEnumerable<Cad3DPoint> points) =>
            FromPoints(points.Where(p => p != null).Select(p => (p.X, p.Y)));

        public static ModelBounds FromPoints(IEnumerable<(double X, double Y)> points)
        {
            bool any = false;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;

            foreach (var (x, y) in points)
            {
                if (!double.IsFinite(x) || !double.IsFinite(y))
                    continue;

                if (!any)
                {
                    minX = maxX = x;
                    minY = maxY = y;
                    any = true;
                }
                else
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            return any ? new ModelBounds(minX, minY, maxX, maxY) : default;
        }

        public static ModelBounds FromCenterRadius(Cad3DPoint center, double radius)
        {
            radius = Math.Abs(radius);
            return new ModelBounds(
                center.X - radius,
                center.Y - radius,
                center.X + radius,
                center.Y + radius);
        }
    }

    private readonly record struct Affine(
        double M11, double M12, double M21, double M22, double DX, double DY)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) =>
            (M11 * x + M12 * y + DX, M21 * x + M22 * y + DY);

        /// <summary>Applies this transform first, then <paramref name="parent"/>.</summary>
        public Affine Then(Affine parent) => new(
            parent.M11 * M11 + parent.M12 * M21,
            parent.M11 * M12 + parent.M12 * M22,
            parent.M21 * M11 + parent.M22 * M21,
            parent.M21 * M12 + parent.M22 * M22,
            parent.M11 * DX + parent.M12 * DY + parent.DX,
            parent.M21 * DX + parent.M22 * DY + parent.DY);
    }
}
