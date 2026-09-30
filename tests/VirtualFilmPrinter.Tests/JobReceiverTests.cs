using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Net.Sockets;
using System.Printing;
using Xunit;
using Xunit.Abstractions;

namespace VirtualFilmPrinter.Tests
{
    public class JobReceiverTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vfp-store-" + Guid.NewGuid().ToString("N"));
        private readonly BlockingCollection<PrintedJob> _ready = new BlockingCollection<PrintedJob>();

        public JobReceiverTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch (IOException) { }
        }

        private JobReceiver Start(int port)
        {
            var receiver = new JobReceiver(new JobStore(_root), port, dpi: 30);
            receiver.JobReady += job => _ready.Add(job);
            receiver.Problem += text => _output.WriteLine("problem: " + text);
            receiver.Start();
            return receiver;
        }

        private PrintedJob Next()
        {
            Assert.True(_ready.TryTake(out var job, TimeSpan.FromSeconds(60)), "no job arrived within 60 s");
            _output.WriteLine("#{0} '{1}' {2} sheet(s) {3} repeat {4} {5}", job.Number, job.DocumentName, job.Pages.Count,
                job.PaperSummary, job.RepeatOf, job.Error);
            return job;
        }

        private static void Send(int port, byte[] bytes)
        {
            using (var client = new TcpClient("127.0.0.1", port))
            using (var stream = client.GetStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        [Fact]
        public void A_job_sent_to_the_port_is_kept_drawn_and_a_repeat_is_marked()
        {
            using (Start(19107))
            {
                var bytes = File.ReadAllBytes(XpsPagesTests.Fixture("text-14x17.oxps"));
                Send(19107, bytes);
                var first = Next();
                Assert.Null(first.Error);
                Assert.Equal("14INx17IN", Assert.Single(first.Pages).Paper);
                Assert.Null(first.RepeatOf);
                Assert.True(File.Exists(Path.Combine(first.Folder, "received.oxps")));
                Assert.True(File.Exists(Path.Combine(first.Folder, "job.txt")));

                Send(19107, bytes);
                var repeat = Next();
                Assert.Equal(first.Number, repeat.RepeatOf);

                Send(19107, File.ReadAllBytes(XpsPagesTests.Fixture("image-8x10.oxps")));
                Assert.Null(Next().RepeatOf);

                Send(19107, new byte[] { 1, 2, 3 });
                Assert.NotNull(Next().Error); // kept, shown as not readable, never lost
            }

            // What was kept reads back, repeats included once remembered.
            var loaded = new JobStore(_root).Load(DateTime.Today);
            Assert.Equal(4, loaded.Count);
            using (var again = new JobReceiver(new JobStore(_root), 19108))
            {
                again.Remember(loaded);
            }

            Assert.Equal(loaded[0].Number, loaded[1].RepeatOf);
        }

        /// <summary>
        /// Through Windows itself: a printer on one of our ports (Install-Printer.ps1, or the feasibility printer)
        /// prints three sheets as three jobs on 14x17 film paper; the second is the same sheet as the first.
        /// </summary>
        [Fact]
        public void Three_jobs_through_a_real_Windows_print_queue()
        {
            var printer = Spooler.PrintersFor(9100).FirstOrDefault();
            Assert.False(printer == null, "no printer sends to 127.0.0.1:9100; run scripts\\Install-Printer.ps1 as administrator");

            using (Start(9100))
            {
                PrintSheet(printer, "VFP test F1", "sheet A");
                PrintSheet(printer, "VFP test F2", "sheet A");
                PrintSheet(printer, "VFP test F3", "sheet B");

                var jobs = new[] { Next(), Next(), Next() }.OrderBy(j => j.Number).ToArray();
                Assert.Equal(new[] { "VFP test F1", "VFP test F2", "VFP test F3" }, jobs.Select(j => j.DocumentName).ToArray());
                Assert.All(jobs, j => Assert.Equal("14INx17IN", Assert.Single(j.Pages).Paper));
                Assert.Null(jobs[0].RepeatOf);
                Assert.Equal(jobs[0].Number, jobs[1].RepeatOf);
                Assert.Null(jobs[2].RepeatOf);
            }
        }

        private static void PrintSheet(string printer, string documentName, string text)
        {
            using (var document = new PrintDocument())
            {
                document.PrinterSettings.PrinterName = printer;
                document.DocumentName = documentName;
                document.PrintController = new StandardPrintController();
                document.DefaultPageSettings.PaperSize = document.PrinterSettings.PaperSizes.Cast<PaperSize>()
                    .First(p => p.PaperName == "14INx17IN");
                document.PrintPage += (s, e) =>
                {
                    e.Graphics.FillRectangle(Brushes.Black, e.PageBounds);
                    using (var font = new Font("Arial", 60))
                    {
                        e.Graphics.DrawString(text, font, Brushes.White, 100, 100);
                    }

                    e.HasMorePages = false;
                };
                document.Print();
            }

            // One job at a time, as AMSI Printer sends them: wait until the spooler has let this one go.
            WaitForEmptyQueue(printer);
        }

        private static void WaitForEmptyQueue(string printer)
        {
            var until = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < until)
            {
                using (var server = new LocalPrintServer())
                using (var queue = server.GetPrintQueue(printer))
                {
                    queue.Refresh();
                    if (queue.NumberOfJobs == 0)
                    {
                        return;
                    }
                }

                Thread.Sleep(200);
            }
        }
    }
}
