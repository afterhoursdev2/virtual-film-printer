using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;

namespace VirtualFilmPrinter
{
    /// <summary>
    /// Turns what the XPS driver sends into pictures of the sheets. Over a port the in-box "Microsoft XPS Document
    /// Writer v4" driver sends OpenXPS (ECMA-388), which WPF cannot open; the two formats describe pages the same
    /// way under different namespaces, so the package is rewritten to Microsoft XPS first and then rendered by WPF.
    /// Everything here must run on an STA thread.
    /// </summary>
    public static class XpsPages
    {
        private const string OpenXpsNamespace = "http://schemas.openxps.org/oxps/v1.0";
        private const string MicrosoftXpsNamespace = "http://schemas.microsoft.com/xps/2005/06";

        /// <summary>The film sizes AMSI Printer maps DICOM film sizes to, and common paper, in inches.</summary>
        private static readonly (string Name, double W, double H)[] KnownSizes =
        {
            ("8INx10IN", 8, 10), ("8INx12IN", 8, 12), ("10INx12IN", 10, 12), ("10INx14IN", 10, 14),
            ("11INx14IN", 11, 14), ("14INx14IN", 14, 14), ("14INx17IN", 14, 17), ("13INx17IN", 13, 17),
            ("A3", 11.69, 16.54), ("A4", 8.27, 11.69), ("Letter", 8.5, 11), ("Legal", 8.5, 14)
        };

        /// <summary>The same package with every OpenXPS namespace replaced by its Microsoft XPS twin.</summary>
        public static byte[] ToMicrosoftXps(byte[] package)
        {
            using (var input = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
            using (var output = new MemoryStream())
            {
                using (var converted = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var entry in input.Entries)
                    {
                        byte[] bytes;
                        using (var stream = entry.Open())
                        using (var buffer = new MemoryStream())
                        {
                            stream.CopyTo(buffer);
                            bytes = buffer.ToArray();
                        }

                        if (IsMarkup(entry.FullName))
                        {
                            var text = Encoding.UTF8.GetString(bytes);
                            bytes = Encoding.UTF8.GetBytes(text.Replace(OpenXpsNamespace, MicrosoftXpsNamespace));
                        }

                        using (var target = converted.CreateEntry(entry.FullName).Open())
                        {
                            target.Write(bytes, 0, bytes.Length);
                        }
                    }
                }

                return output.ToArray();
            }
        }

        private static bool IsMarkup(string name)
        {
            var extension = Path.GetExtension(name).ToLowerInvariant();
            return extension == ".rels" || extension == ".fdseq" || extension == ".fdoc" || extension == ".fpage";
        }

        /// <summary>
        /// Renders every page of a Microsoft XPS file to a PNG in <paramref name="folder"/> (page-1.png, ...) at
        /// <paramref name="dpi"/>, on white, as the sheet would look.
        /// </summary>
        public static List<PrintedPage> Render(string xpsPath, string folder, int dpi)
        {
            var pages = new List<PrintedPage>();
            using (var document = new XpsDocument(xpsPath, FileAccess.Read))
            {
                var sequence = document.GetFixedDocumentSequence()
                               ?? throw new InvalidDataException("The job holds no fixed document sequence.");
                var paginator = sequence.DocumentPaginator;
                for (var i = 0; i < paginator.PageCount; i++)
                {
                    var page = paginator.GetPage(i);
                    var size = page.Size;
                    var pixelWidth = Math.Max(1, (int)Math.Round(size.Width / 96.0 * dpi));
                    var pixelHeight = Math.Max(1, (int)Math.Round(size.Height / 96.0 * dpi));

                    var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
                    var paper = new DrawingVisual();
                    using (var context = paper.RenderOpen())
                    {
                        context.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(size));
                    }

                    bitmap.Render(paper);
                    bitmap.Render(page.Visual);
                    bitmap.Freeze();

                    var pngPath = Path.Combine(folder, "page-" + (i + 1) + ".png");
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(pngPath))
                    {
                        encoder.Save(file);
                    }

                    var widthInches = size.Width / 96.0;
                    var heightInches = size.Height / 96.0;
                    pages.Add(new PrintedPage
                    {
                        Number = i + 1,
                        WidthInches = widthInches,
                        HeightInches = heightInches,
                        Paper = PaperName(widthInches, heightInches),
                        PngPath = pngPath,
                        Hash = PixelHash(bitmap)
                    });
                }
            }

            return pages;
        }

        /// <summary>
        /// The known size a page matches, within a twentieth of an inch (Windows keeps forms in whole millimetres),
        /// with "landscape" when it lies on its side; otherwise its size in inches.
        /// </summary>
        public static string PaperName(double widthInches, double heightInches)
        {
            const double tolerance = 0.05;
            foreach (var known in KnownSizes)
            {
                if (Math.Abs(widthInches - known.W) <= tolerance && Math.Abs(heightInches - known.H) <= tolerance)
                {
                    return known.Name;
                }

                if (Math.Abs(widthInches - known.H) <= tolerance && Math.Abs(heightInches - known.W) <= tolerance)
                {
                    return known.Name + " landscape";
                }
            }

            return string.Format("{0:0.##} x {1:0.##} in", widthInches, heightInches);
        }

        private static string PixelHash(BitmapSource bitmap)
        {
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(pixels)).Replace("-", "");
            }
        }
    }
}
