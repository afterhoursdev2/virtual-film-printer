using System.IO;
using Xunit;

namespace VirtualFilmPrinter.Tests
{
    /// <summary>The two jobs captured from the real driver: OpenXPS in, the sheet as a picture out, at its true size.</summary>
    public class XpsPagesTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "vfp-tests-" + Guid.NewGuid().ToString("N"));

        public XpsPagesTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); } catch (IOException) { }
        }

        internal static string Fixture(string name) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);

        /// <summary>WPF renders on an STA thread only.</summary>
        internal static T OnSta<T>(Func<T> work)
        {
            T result = default;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { result = work(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw new Exception("rendering failed", error);
            }

            return result;
        }

        private List<PrintedPage> RenderFixture(string name, string subfolder)
        {
            var folder = Path.Combine(_folder, subfolder);
            Directory.CreateDirectory(folder);
            var xps = Path.Combine(folder, "converted.xps");
            File.WriteAllBytes(xps, XpsPages.ToMicrosoftXps(File.ReadAllBytes(Fixture(name))));
            return OnSta(() => XpsPages.Render(xps, folder, 50));
        }

        [Fact]
        public void A_14x17_job_from_the_driver_becomes_one_14x17_picture()
        {
            var pages = RenderFixture("text-14x17.oxps", "a");
            var page = Assert.Single(pages);
            Assert.Equal("14INx17IN", page.Paper);
            Assert.InRange(page.WidthInches, 13.95, 14.05);
            Assert.InRange(page.HeightInches, 16.95, 17.05);
            Assert.True(File.Exists(page.PngPath));

            var image = OnSta(() => new System.Windows.Media.Imaging.PngBitmapDecoder(
                new Uri(page.PngPath), System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0].PixelWidth);
            Assert.Equal(701, image); // 14.02 in at 50 dpi
        }

        [Fact]
        public void An_image_on_8x10_keeps_its_size()
        {
            var page = Assert.Single(RenderFixture("image-8x10.oxps", "b"));
            Assert.Equal("8INx10IN", page.Paper);
        }

        [Fact]
        public void The_same_sheet_twice_has_the_same_hash_and_another_sheet_does_not()
        {
            var first = Assert.Single(RenderFixture("text-14x17.oxps", "c1"));
            var again = Assert.Single(RenderFixture("text-14x17.oxps", "c2"));
            var other = Assert.Single(RenderFixture("image-8x10.oxps", "c3"));
            Assert.Equal(first.Hash, again.Hash);
            Assert.NotEqual(first.Hash, other.Hash);
        }

        [Theory]
        [InlineData(14.02, 17.01, "14INx17IN")]
        [InlineData(17.01, 14.02, "14INx17IN landscape")]
        [InlineData(7.99, 10.0, "8INx10IN")]
        [InlineData(8.27, 11.69, "A4")]
        [InlineData(5, 7, "5 x 7 in")]
        public void Paper_is_named_by_size(double w, double h, string expected)
        {
            Assert.Equal(expected, XpsPages.PaperName(w, h));
        }
    }
}
