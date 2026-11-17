using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrashuTiles.Device;

/// <summary>
/// Builds the ZIP the dock expects for SetButtons / PartialUpdateButtons:
///   manifest.json      slot "col_row" -> { State, ViewParam: [ { Font, Icon, Text } ] }
///   Images/&lt;id&gt;.png  the images the manifest points to
/// Slot "3_2" is always the small screen and always carries SmallViewMode.
/// </summary>
internal static class ButtonZip
{
    // Required by the firmware on every slot, even though we draw our own text.
    static readonly Dictionary<string, object> Font = new()
    {
        ["Align"] = "bottom", ["Color"] = 0xFFFFFF, ["FontName"] = "Source Han Sans SC",
        ["ShowTitle"] = true, ["Size"] = 10, ["Weight"] = 80,
    };

    /// <param name="keys">Key index (0..12) to PNG bytes, only for the slots being sent.</param>
    /// <param name="smallMode">Small-screen mode written into slot 3_2.</param>
    /// <param name="smallImage">458x196 PNG; used (and required) when smallMode is Background.</param>
    public static byte[] Build(IReadOnlyDictionary<int, byte[]> keys, D200Protocol.SmallMode smallMode, byte[]? smallImage)
    {
        var images = new Dictionary<string, byte[]>();
        var manifest = new SortedDictionary<string, object>(StringComparer.Ordinal);

        foreach (var (index, png) in keys)
        {
            if (index < 0 || index >= D200Protocol.KeyCount) continue;
            string id = Guid.NewGuid().ToString();
            images[id] = png;
            manifest[$"{index % D200Protocol.Columns}_{index / D200Protocol.Columns}"] = Slot(id);
        }

        var small = new Dictionary<string, object> { ["State"] = 0, ["SmallViewMode"] = (int)smallMode };
        var view = new Dictionary<string, object> { ["Font"] = Font, ["Text"] = "" };
        if (smallMode == D200Protocol.SmallMode.Background && smallImage != null)
        {
            string id = Guid.NewGuid().ToString();
            images[id] = smallImage;
            view["Icon"] = $"Images/{id}.png";
        }
        small["ViewParam"] = new[] { view };
        manifest["3_2"] = small;

        byte[] manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest);

        // Pad with a growing dummy file until the firmware's bad offsets are clear.
        for (int attempt = 0; attempt < 64; attempt++)
        {
            byte[] zip = Compress(manifestJson, images, attempt == 0 ? null : Dummy(8 * attempt));
            if (D200Protocol.IsPayloadSafe(zip)) return zip;
        }
        throw new InvalidOperationException("Could not build a ZIP the dock firmware accepts");
    }

    static Dictionary<string, object> Slot(string imageId) => new()
    {
        ["State"] = 0,
        ["ViewParam"] = new[] { new Dictionary<string, object> { ["Font"] = Font, ["Icon"] = $"Images/{imageId}.png", ["Text"] = "" } },
    };

    static byte[] Compress(byte[] manifest, Dictionary<string, byte[]> images, byte[]? dummy)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "manifest.json", manifest);
            if (dummy != null) Add(zip, "dummy.txt", dummy);
            foreach (var (id, png) in images) Add(zip, $"Images/{id}.png", png);
        }
        return ms.ToArray();
    }

    static void Add(ZipArchive zip, string name, byte[] data)
    {
        // PNGs are already compressed; Fastest keeps the CPU cost low.
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var s = entry.Open();
        s.Write(data);
    }

    static byte[] Dummy(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
