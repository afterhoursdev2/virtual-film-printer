using System.Globalization;
using System.IO;

namespace VirtualFilmPrinter
{
    /// <summary>
    /// Where received jobs are kept: Documents\VirtualFilmPrinter\Jobs\&lt;yyyy-MM-dd&gt;\&lt;NNNN_HHmmss&gt;\ holding the
    /// job as it arrived (received.oxps), the converted package (converted.xps), a PNG per page and job.txt.
    /// </summary>
    public sealed class JobStore
    {
        public JobStore(string root = null)
        {
            Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VirtualFilmPrinter", "Jobs");
        }

        public string Root { get; }

        public string DayFolder(DateTime day) => Path.Combine(Root, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        /// <summary>A new, empty folder for a job received at <paramref name="received"/>, and its number for that day.</summary>
        public string NewJobFolder(DateTime received, out int number)
        {
            var day = DayFolder(received);
            Directory.CreateDirectory(day);
            number = Directory.GetDirectories(day).Length + 1;
            string folder;
            while (Directory.Exists(folder = Path.Combine(day, number.ToString("0000") + "_" + received.ToString("HHmmss", CultureInfo.InvariantCulture))))
            {
                number++;
            }

            Directory.CreateDirectory(folder);
            return folder;
        }

        public void Save(PrintedJob job)
        {
            var lines = new List<string>
            {
                "Number=" + job.Number,
                "Received=" + job.Received.ToString("o", CultureInfo.InvariantCulture),
                "Document=" + job.DocumentName,
                "Bytes=" + job.Bytes
            };
            if (job.Error != null)
            {
                lines.Add("Error=" + job.Error.Replace('\r', ' ').Replace('\n', ' '));
            }

            foreach (var page in job.Pages)
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture, "Page={0}|{1:0.###}|{2:0.###}|{3}|{4}|{5}",
                    page.Number, page.WidthInches, page.HeightInches, page.Paper, Path.GetFileName(page.PngPath), page.Hash));
            }

            File.WriteAllLines(Path.Combine(job.Folder, "job.txt"), lines);
        }

        /// <summary>The jobs kept for <paramref name="day"/>, oldest first. Folders without a readable job.txt are skipped.</summary>
        public List<PrintedJob> Load(DateTime day)
        {
            var jobs = new List<PrintedJob>();
            var folder = DayFolder(day);
            if (!Directory.Exists(folder))
            {
                return jobs;
            }

            foreach (var jobFolder in Directory.GetDirectories(folder).OrderBy(f => f, StringComparer.Ordinal))
            {
                var info = Path.Combine(jobFolder, "job.txt");
                if (!File.Exists(info))
                {
                    continue;
                }

                try
                {
                    var job = new PrintedJob { Folder = jobFolder };
                    foreach (var line in File.ReadAllLines(info))
                    {
                        var at = line.IndexOf('=');
                        if (at <= 0)
                        {
                            continue;
                        }

                        var key = line.Substring(0, at);
                        var value = line.Substring(at + 1);
                        switch (key)
                        {
                            case "Number": job.Number = int.Parse(value, CultureInfo.InvariantCulture); break;
                            case "Received": job.Received = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                            case "Document": job.DocumentName = value; break;
                            case "Bytes": job.Bytes = long.Parse(value, CultureInfo.InvariantCulture); break;
                            case "Error": job.Error = value; break;
                            case "Page":
                                var parts = value.Split('|');
                                job.Pages.Add(new PrintedPage
                                {
                                    Number = int.Parse(parts[0], CultureInfo.InvariantCulture),
                                    WidthInches = double.Parse(parts[1], CultureInfo.InvariantCulture),
                                    HeightInches = double.Parse(parts[2], CultureInfo.InvariantCulture),
                                    Paper = parts[3],
                                    PngPath = Path.Combine(jobFolder, parts[4]),
                                    Hash = parts[5]
                                });
                                break;
                        }
                    }

                    jobs.Add(job);
                }
                catch (Exception)
                {
                    // A job.txt from an interrupted run: leave it on disk, out of the list.
                }
            }

            return jobs;
        }
    }
}
