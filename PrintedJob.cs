namespace VirtualFilmPrinter
{
    /// <summary>One sheet of a received job, as it would have come out of the printer.</summary>
    public sealed class PrintedPage
    {
        public int Number { get; set; }

        public double WidthInches { get; set; }

        public double HeightInches { get; set; }

        /// <summary>The film or paper size the page matches ("14INx17IN", "A4"), or its size in inches.</summary>
        public string Paper { get; set; }

        public string PngPath { get; set; }

        /// <summary>SHA-256 of the rendered pixels: two identical sheets have the same hash.</summary>
        public string Hash { get; set; }
    }

    /// <summary>One Windows print job, as it arrived on the port.</summary>
    public sealed class PrintedJob
    {
        public int Number { get; set; }

        public DateTime Received { get; set; }

        /// <summary>The document name the spooler held for this job ("AMSI 1.2.3 F1"), or "(unknown)".</summary>
        public string DocumentName { get; set; }

        public long Bytes { get; set; }

        public string Folder { get; set; }

        public List<PrintedPage> Pages { get; } = new List<PrintedPage>();

        /// <summary>Why the job could not be turned into pictures; null when it was.</summary>
        public string Error { get; set; }

        /// <summary>The earlier job an identical sheet was seen in, or null.</summary>
        public int? RepeatOf { get; set; }

        public string PaperSummary => Pages.Count == 0 ? "" : string.Join(", ", Pages.Select(p => p.Paper).Distinct());
    }
}
