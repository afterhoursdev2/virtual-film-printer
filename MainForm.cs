using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace VirtualFilmPrinter
{
    /// <summary>
    /// The receiving end on screen: every job that reached the port, its sheets as they would have printed, and the
    /// sheets that are an exact repeat of an earlier one, which is what a duplicate-print bug looks like.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly JobStore _store;
        private readonly JobReceiver _receiver;
        private readonly List<PrintedJob> _jobs = new List<PrintedJob>();

        private readonly Label _status = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
        private readonly Label _printers = new Label { AutoSize = true };
        private readonly Label _counts = new Label { AutoSize = true };
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false
        };
        private readonly PictureBox _picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.DimGray };
        private readonly Button _previous = new Button { Text = "◀", Width = 36 };
        private readonly Button _next = new Button { Text = "▶", Width = 36 };
        private readonly Label _pageInfo = new Label { AutoSize = true, Padding = new Padding(6, 8, 0, 0) };
        private readonly System.Windows.Forms.Timer _printerCheck = new System.Windows.Forms.Timer { Interval = 5000 };

        private readonly SplitContainer _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };

        private PrintedJob _shownJob;
        private int _shownPage;

        public MainForm(JobStore store, int port)
        {
            _store = store;
            _receiver = new JobReceiver(store, port);

            Text = "Virtual Film Printer";
            Width = 1200;
            Height = 780;
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;

            BuildLayout();

            _receiver.JobReady += job => BeginInvokeSafe(() => AddJob(job, select: true));
            _receiver.Problem += text => BeginInvokeSafe(() => ShowProblem(text));
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            _list.DoubleClick += (s, e) => OpenShownPage();
            _picture.DoubleClick += (s, e) => OpenShownPage();
            _previous.Click += (s, e) => ShowPage(_shownPage - 1);
            _next.Click += (s, e) => ShowPage(_shownPage + 1);
            _printerCheck.Tick += (s, e) => RefreshPrinters();
        }

        private void BuildLayout()
        {
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 8, 0) };
            buttons.Controls.Add(MakeButton("Open folder", (s, e) => OpenFolder()));
            buttons.Controls.Add(MakeButton("Copy summary", (s, e) => CopySummary()));
            buttons.Controls.Add(MakeButton("Clear list", (s, e) => ClearList()));

            var labels = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 6, 0, 0) };
            labels.Controls.Add(_status);
            labels.Controls.Add(_printers);
            labels.Controls.Add(_counts);

            var header = new Panel { Dock = DockStyle.Top, Height = 72 };
            header.Controls.Add(labels);
            header.Controls.Add(buttons);

            _list.Columns.Add("#", 44, HorizontalAlignment.Right);
            _list.Columns.Add("Time", 78);
            _list.Columns.Add("Document", 250);
            _list.Columns.Add("Paper", 110);
            _list.Columns.Add("Sheets", 56, HorizontalAlignment.Right);
            _list.Columns.Add("Repeat of", 76);

            var pager = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4, 4, 0, 0) };
            pager.Controls.Add(_previous);
            pager.Controls.Add(_next);
            pager.Controls.Add(_pageInfo);

            var preview = new Panel { Dock = DockStyle.Fill };
            preview.Controls.Add(_picture);
            preview.Controls.Add(pager);

            _split.Panel1.Controls.Add(_list);
            _split.Panel2.Controls.Add(preview);

            Controls.Add(_split);
            Controls.Add(header);
            ShowPage(0);
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            button.Click += onClick;
            return button;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _split.SplitterDistance = 640; // once the form has its size; the list keeps it, the preview takes the rest

            var today = _store.Load(DateTime.Today);
            _receiver.Remember(today);
            foreach (var job in today)
            {
                AddJob(job, select: false);
            }

            try
            {
                _receiver.Start();
                _status.Text = "Listening on 127.0.0.1:" + _receiver.Port + " — jobs are kept in " + _store.Root;
                _status.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _status.Text = "Not listening: port " + _receiver.Port + " could not be opened (" + ex.Message + "). Is another copy running?";
                _status.ForeColor = Color.DarkRed;
            }

            RefreshPrinters();
            _printerCheck.Start();
            if (_list.Items.Count > 0)
            {
                _list.Items[_list.Items.Count - 1].Selected = true;
                _list.Items[_list.Items.Count - 1].EnsureVisible();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _printerCheck.Stop();
            _receiver.Dispose();
            _picture.Image?.Dispose();
            base.OnFormClosed(e);
        }

        private void BeginInvokeSafe(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // closing
            }
        }

        private void RefreshPrinters()
        {
            var printers = Spooler.PrintersFor(_receiver.Port);
            _printers.Text = printers.Count > 0
                ? "Printers sending here: " + string.Join(", ", printers)
                : "No printer sends to this port yet: run scripts\\Install-Printer.ps1 as administrator.";
            _printers.ForeColor = printers.Count > 0 ? SystemColors.ControlText : Color.DarkRed;
        }

        private void AddJob(PrintedJob job, bool select)
        {
            _jobs.Add(job);
            var item = new ListViewItem(job.Number.ToString())
            {
                Tag = job
            };
            item.SubItems.Add(job.Received.ToString("HH:mm:ss"));
            item.SubItems.Add(job.DocumentName ?? "");
            item.SubItems.Add(job.Error != null ? "not readable" : job.PaperSummary);
            item.SubItems.Add(job.Pages.Count.ToString());
            item.SubItems.Add(job.RepeatOf.HasValue ? "#" + job.RepeatOf.Value : "");
            if (job.Error != null)
            {
                item.BackColor = Color.LightYellow;
                item.ToolTipText = job.Error;
            }
            else if (job.RepeatOf.HasValue)
            {
                item.BackColor = Color.MistyRose;
            }

            _list.Items.Add(item);
            UpdateCounts();
            if (select)
            {
                _list.SelectedItems.Clear();
                item.Selected = true;
                item.EnsureVisible();
            }
        }

        private void UpdateCounts()
        {
            var sheets = _jobs.Sum(j => j.Pages.Count);
            var repeats = _jobs.Count(j => j.RepeatOf.HasValue);
            var unreadable = _jobs.Count(j => j.Error != null);
            _counts.Text = "Jobs " + _jobs.Count + "  ·  Sheets " + sheets + "  ·  Repeats " + repeats
                           + (unreadable > 0 ? "  ·  Not readable " + unreadable : "");
            _counts.ForeColor = repeats > 0 ? Color.DarkRed : SystemColors.ControlText;
        }

        private void ShowProblem(string text)
        {
            _status.Text = text;
            _status.ForeColor = Color.DarkRed;
        }

        private void ShowSelected()
        {
            _shownJob = _list.SelectedItems.Count > 0 ? (PrintedJob)_list.SelectedItems[0].Tag : null;
            ShowPage(0);
        }

        private void ShowPage(int index)
        {
            var old = _picture.Image;
            _picture.Image = null;
            old?.Dispose();

            if (_shownJob == null || _shownJob.Pages.Count == 0)
            {
                _shownPage = 0;
                _pageInfo.Text = _shownJob?.Error != null ? "Not readable: " + _shownJob.Error : "";
                _previous.Enabled = _next.Enabled = false;
                return;
            }

            _shownPage = Math.Max(0, Math.Min(index, _shownJob.Pages.Count - 1));
            var page = _shownJob.Pages[_shownPage];
            try
            {
                // Read into memory so the PNG is not held open.
                using (var stream = new MemoryStream(File.ReadAllBytes(page.PngPath)))
                using (var image = Image.FromStream(stream))
                {
                    _picture.Image = new Bitmap(image);
                }
            }
            catch (Exception ex)
            {
                _pageInfo.Text = "Sheet " + page.Number + " could not be shown: " + ex.Message;
                return;
            }

            _pageInfo.Text = string.Format("Sheet {0} of {1}  ·  {2}  ·  {3:0.00} x {4:0.00} in{5}",
                page.Number, _shownJob.Pages.Count, page.Paper, page.WidthInches, page.HeightInches,
                _shownJob.RepeatOf.HasValue ? "  ·  repeat of #" + _shownJob.RepeatOf.Value : "");
            _previous.Enabled = _shownPage > 0;
            _next.Enabled = _shownPage < _shownJob.Pages.Count - 1;
        }

        private void OpenShownPage()
        {
            if (_shownJob != null && _shownJob.Pages.Count > 0)
            {
                Process.Start(new ProcessStartInfo(_shownJob.Pages[_shownPage].PngPath) { UseShellExecute = true });
            }
        }

        private void OpenFolder()
        {
            var folder = _shownJob?.Folder ?? _store.DayFolder(DateTime.Today);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }

        private void CopySummary()
        {
            var text = new StringBuilder();
            text.AppendLine(_counts.Text);
            foreach (var job in _jobs)
            {
                text.AppendFormat("#{0}  {1:HH:mm:ss}  {2}  {3}  {4} sheet(s){5}{6}",
                    job.Number, job.Received, job.DocumentName, job.PaperSummary, job.Pages.Count,
                    job.RepeatOf.HasValue ? "  REPEAT of #" + job.RepeatOf.Value : "",
                    job.Error != null ? "  NOT READABLE: " + job.Error : "");
                text.AppendLine();
            }

            Clipboard.SetText(text.ToString());
        }

        /// <summary>Empties the list and forgets the sheets seen, for a fresh test. The files stay on disk.</summary>
        private void ClearList()
        {
            _jobs.Clear();
            _list.Items.Clear();
            _receiver.ForgetSheets();
            _shownJob = null;
            ShowPage(0);
            UpdateCounts();
        }
    }
}
