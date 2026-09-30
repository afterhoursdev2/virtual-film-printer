using System.Printing;

namespace VirtualFilmPrinter
{
    /// <summary>
    /// What Windows knows about the printers that send here: the ones whose port is one of ours (Install-Printer.ps1
    /// names it VFP_127.0.0.1_&lt;port&gt;), and the document name of the job they are sending right now, which is not
    /// inside the XPS package itself.
    /// </summary>
    public static class Spooler
    {
        public const string PortPrefix = "VFP_";

        /// <summary>The printers whose port sends to <paramref name="port"/> on this PC.</summary>
        public static List<string> PrintersFor(int port)
        {
            var names = new List<string>();
            try
            {
                using (var server = new LocalPrintServer())
                {
                    foreach (var queue in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local }))
                    {
                        using (queue)
                        {
                            if (IsOurs(queue, port))
                            {
                                names.Add(queue.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The printer list is a convenience; the listener works without it.
            }

            return names;
        }

        /// <summary>
        /// The document name of the job one of our printers is sending now: the one printing, else the oldest
        /// waiting. "(unknown)" when the spooler cannot say.
        /// </summary>
        public static string CurrentDocument(int port)
        {
            try
            {
                using (var server = new LocalPrintServer())
                {
                    PrintSystemJobInfo best = null;
                    var jobs = new List<PrintSystemJobInfo>();
                    foreach (var queue in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local }))
                    {
                        if (!IsOurs(queue, port))
                        {
                            queue.Dispose();
                            continue;
                        }

                        queue.Refresh();
                        foreach (var job in queue.GetPrintJobInfoCollection())
                        {
                            jobs.Add(job);
                            if (best == null
                                || (job.IsPrinting && !best.IsPrinting)
                                || (job.IsPrinting == best.IsPrinting && job.TimeJobSubmitted < best.TimeJobSubmitted))
                            {
                                best = job;
                            }
                        }
                    }

                    var name = best?.Name;
                    foreach (var job in jobs)
                    {
                        job.Dispose();
                    }

                    return string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
                }
            }
            catch (Exception)
            {
                return "(unknown)";
            }
        }

        private static bool IsOurs(PrintQueue queue, int port)
        {
            var portName = queue.QueuePort?.Name ?? string.Empty;
            return portName.StartsWith(PortPrefix, StringComparison.OrdinalIgnoreCase)
                   && portName.EndsWith("_" + port, StringComparison.Ordinal);
        }
    }
}
