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
    public byte[] GetFromCache((int art, int ver) par) => File.ReadAllBytes($"{cache}{par.art}-{par.ver}.png");

    public void SaveToCache(byte[] data, (int art, int ver) par)
    {
        using var fo = new FileStream($"{cache}{par.art}-{par.ver}.png", FileMode.Create, FileAccess.Write);
        fo.Write(data, 0, data.Length);
    }

    public void InitCache()
    {
        if (!Directory.Exists(cache)) Directory.CreateDirectory(cache);
    }
}
