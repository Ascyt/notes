using ImageMagick;

namespace notes.Features.ImageConversion;

/// <summary>
/// The images browsers can't show themselves, which are converted for them when shown (see <see cref="ImageForBrowser"/>), by extension.
/// Only formats listed here are ever read: the file is read as the format its extension names, never as what its contents claim to be,
/// so that none of ImageMagick's other readers (scripts like MSL or MVG, SVG, URLs) can be reached with a file of the notes.
/// </summary>
public static class ConvertedImageFormats
{
    private static readonly Dictionary<string, MagickFormat> FormatsByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // Photos of phones and cameras
        [".heic"] = MagickFormat.Heic,
        [".heif"] = MagickFormat.Heif,
        [".hif"] = MagickFormat.Heif,
        [".jxl"] = MagickFormat.Jxl,

        // Camera RAW
        [".3fr"] = MagickFormat.ThreeFr,
        [".arw"] = MagickFormat.Arw,
        [".cr2"] = MagickFormat.Cr2,
        [".cr3"] = MagickFormat.Cr3,
        [".crw"] = MagickFormat.Crw,
        [".dcr"] = MagickFormat.Dcr,
        [".dng"] = MagickFormat.Dng,
        [".erf"] = MagickFormat.Erf,
        [".fff"] = MagickFormat.Fff,
        [".iiq"] = MagickFormat.Iiq,
        [".k25"] = MagickFormat.K25,
        [".kdc"] = MagickFormat.Kdc,
        [".mdc"] = MagickFormat.Mdc,
        [".mef"] = MagickFormat.Mef,
        [".mos"] = MagickFormat.Mos,
        [".mrw"] = MagickFormat.Mrw,
        [".nef"] = MagickFormat.Nef,
        [".nrw"] = MagickFormat.Nrw,
        [".orf"] = MagickFormat.Orf,
        [".pef"] = MagickFormat.Pef,
        [".raf"] = MagickFormat.Raf,
        [".rmf"] = MagickFormat.Rmf,
        [".rw2"] = MagickFormat.Rw2,
        [".rwl"] = MagickFormat.Rwl,
        [".sr2"] = MagickFormat.Sr2,
        [".srf"] = MagickFormat.Srf,
        [".srw"] = MagickFormat.Srw,
        [".sti"] = MagickFormat.Sti,
        [".x3f"] = MagickFormat.X3f,

        // Image editors and print
        [".tif"] = MagickFormat.Tiff,
        [".tiff"] = MagickFormat.Tiff,
        [".psd"] = MagickFormat.Psd,
        [".psb"] = MagickFormat.Psb,
        [".xcf"] = MagickFormat.Xcf,
        [".ora"] = MagickFormat.Ora,
        [".jp2"] = MagickFormat.Jp2,
        [".j2k"] = MagickFormat.J2k,
        [".j2c"] = MagickFormat.J2c,
        [".jpc"] = MagickFormat.Jpc,
        [".jpm"] = MagickFormat.Jpm,
        [".dpx"] = MagickFormat.Dpx,
        [".cin"] = MagickFormat.Cin,
        [".exr"] = MagickFormat.Exr,
        [".hdr"] = MagickFormat.Hdr,

        // Older and simpler formats
        [".tga"] = MagickFormat.Tga,
        [".dds"] = MagickFormat.Dds,
        [".pcx"] = MagickFormat.Pcx,
        [".pict"] = MagickFormat.Pict,
        [".pct"] = MagickFormat.Pct,
        [".sgi"] = MagickFormat.Sgi,
        [".ras"] = MagickFormat.Sun,
        [".pbm"] = MagickFormat.Pbm,
        [".pgm"] = MagickFormat.Pgm,
        [".ppm"] = MagickFormat.Ppm,
        [".pnm"] = MagickFormat.Pnm,
        [".pam"] = MagickFormat.Pam,
        [".pfm"] = MagickFormat.Pfm,
        [".qoi"] = MagickFormat.Qoi,
        [".xpm"] = MagickFormat.Xpm,
        [".xbm"] = MagickFormat.Xbm,
        [".wbmp"] = MagickFormat.Wbmp,
        [".cur"] = MagickFormat.Cur,
        [".mng"] = MagickFormat.Mng,
        [".jng"] = MagickFormat.Jng,

        // Science and medicine
        [".fits"] = MagickFormat.Fits,
        [".fts"] = MagickFormat.Fts,
        [".dcm"] = MagickFormat.Dcm,
    };

    /// <summary>The format a file of that name is read as, or false if it isn't converted (the browser shows it itself, or it isn't an image).</summary>
    public static bool TryGet(string path, out MagickFormat format)
        => FormatsByExtension.TryGetValue(Path.GetExtension(path), out format);
}
