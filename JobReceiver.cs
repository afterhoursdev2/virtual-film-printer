using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace VirtualFilmPrinter
{
    /// <summary>
    /// The printer's other end. The Standard TCP/IP port that Install-Printer.ps1 creates sends each Windows print job
    /// as one connection to 127.0.0.1:&lt;port&gt;, so one connection is one job: it is read whole, kept, rendered to
    /// pictures and announced through <see cref="JobReady"/>. Rendering runs on one STA thread, in arrival order,
    /// which also keeps job numbers and repeat marks in order.
    /// </summary>
    public sealed class JobReceiver : IDisposable
    {
        public const int DefaultDpi = 100;

        private readonly JobStore _store;
        private readonly int _dpi;
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        private readonly Thread _renderer;
        private readonly object _lock = new object();
        private readonly Dictionary<string, int> _seen = new Dictionary<string, int>(StringComparer.Ordinal);
        private TcpListener _listener;
        private volatile bool _stopping;

        public JobReceiver(JobStore store, int port, int dpi = DefaultDpi)
        {
            _store = store;
            Port = port;
            _dpi = dpi;
            _renderer = new Thread(RenderLoop) { IsBackground = true, Name = "Render" };
            _renderer.SetApartmentState(ApartmentState.STA);
            _renderer.Start();
        }

        public int Port { get; }

        /// <summary>Raised on the render thread once a job is kept and drawn (or failed to draw).</summary>
        public event Action<PrintedJob> JobReady;

        /// <summary>Raised when a connection could not be read or kept at all.</summary>
        public event Action<string> Problem;

        /// <summary>Starts listening on 127.0.0.1 only; throws when the port is taken.</summary>
        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            Task.Run(AcceptLoop);
        }

        /// <summary>
        /// Takes in jobs loaded from disk, oldest first: marks their own repeats again and remembers their sheets, so
        /// a repeat of them arriving now is still caught.
        /// </summary>
        public void Remember(IEnumerable<PrintedJob> jobs)
        {
            lock (_lock)
            {
                foreach (var job in jobs)
                {
                    MarkRepeats(job);
                }
            }
        }

        private void MarkRepeats(PrintedJob job)
        {
            foreach (var page in job.Pages)
            {
                if (_seen.TryGetValue(page.Hash, out var earlier))
                {
                    job.RepeatOf = job.RepeatOf ?? earlier;
                }
                else
                {
                    _seen[page.Hash] = job.Number;
                }
            }
        }

        /// <summary>Forgets every sheet seen, so the next job is compared with nothing.</summary>
        public void ForgetSheets()
        {
            lock (_lock)
            {
                _seen.Clear();
            }
        }

        /// <summary>Keeps and renders one job; public so a test can hand it bytes without a spooler.</summary>
        public void Accept(byte[] bytes, DateTime received, string documentName)
        {
            _work.Add(() => Process(bytes, received, documentName));
        }

        private async Task AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception) when (_stopping)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Problem?.Invoke("Listening failed: " + ex.Message);
                    return;
                }

                var received = DateTime.Now;
                _ = Task.Run(() => Receive(client, received));
            }
        }

        private void Receive(TcpClient client, DateTime received)
        {
            try
            {
                using (client)
                {
                    // Asked while the spooler is still sending, when the job is still in the queue.
                    var name = Spooler.CurrentDocument(Port);
                    client.ReceiveTimeout = 120000;
                    using (var stream = client.GetStream())
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        if (buffer.Length == 0)
                        {
                            return; // a port probe, not a job
                        }

                        Accept(buffer.ToArray(), received, name);
                    }
                }
            }
            catch (Exception ex)
            {
                Problem?.Invoke("A job could not be received: " + ex.Message);
            }
        }

        private void RenderLoop()
        {
            foreach (var work in _work.GetConsumingEnumerable())
            {
                work();
            }
        }

        private void Process(byte[] bytes, DateTime received, string documentName)
        {
            PrintedJob job;
            try
            {
                var folder = _store.NewJobFolder(received, out var number);
                job = new PrintedJob
                {
                    Number = number,
                    Received = received,
                    DocumentName = documentName,
                    Bytes = bytes.Length,
                    Folder = folder
                };
                File.WriteAllBytes(Path.Combine(folder, "received.oxps"), bytes);
            }
            catch (Exception ex)
            {
                Problem?.Invoke("A job could not be kept: " + ex.Message);
                return;
            }

            try
            {
                var xps = Path.Combine(job.Folder, "converted.xps");
                File.WriteAllBytes(xps, XpsPages.ToMicrosoftXps(bytes));
                job.Pages.AddRange(XpsPages.Render(xps, job.Folder, _dpi));
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
            }

            lock (_lock)
            {
                MarkRepeats(job);
            }

            try
            {
                _store.Save(job);
            }
            catch (Exception ex)
            {
                Problem?.Invoke("Job " + job.Number + " was drawn but job.txt could not be written: " + ex.Message);
            }

            JobReady?.Invoke(job);
        }

        public void Dispose()
        {
            _stopping = true;
            try
            {
                _listener?.Stop();
            }
            catch (Exception)
            {
                // closing anyway
            }

            _work.CompleteAdding();
        }
    }
}
