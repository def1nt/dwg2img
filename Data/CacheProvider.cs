namespace dwg2img.Providers;

public sealed class CacheProvider
{
    private readonly string cache = Path.Join(AppContext.BaseDirectory, "cache") + Path.DirectorySeparatorChar;

    public CacheProvider()
    {
        InitCache();
    }

    public bool CheckCache(int art, int ver)
    {
        return File.Exists($"{cache}{art}-{ver}.png");
    }

    public byte[] GetFromCache(int art, int ver) => GetFromCache((art, ver));
    public byte[] GetFromCache((int art, int ver) par)
    {
        using (var fi = new FileStream($"{cache}{par.art}-{par.ver}.png", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            byte[] t = new byte[1000];
            var m = new MemoryStream(0);
            int readbytes = 0;
            while ((readbytes = fi.Read(t, 0, 1000)) > 0) m.Write(t, 0, readbytes);
            return m.GetBuffer();
        }
    }

    public void SaveToCache(byte[] data, (int art, int ver) par)
    {
        using var fo = new FileStream($"{cache}{par.art}-{par.ver}.png", FileMode.Create, FileAccess.Write);
        fo.Write(data, 0, data.Length);
    }

    private bool DoesCacheExist()
    {
        return Directory.Exists(cache);
    }

    public void InitCache()
    {
        if (!DoesCacheExist()) Directory.CreateDirectory(cache);
    }
}
