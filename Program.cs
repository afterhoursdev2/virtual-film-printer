using System.Windows.Forms;

namespace VirtualFilmPrinter
{
    internal static class Program
    {
        /// <summary>VirtualFilmPrinter.exe [--port 9100]</summary>
        [STAThread]
        private static void Main(string[] args)
        {
            var port = 9100;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port" && int.TryParse(args[i + 1], out var value) && value > 0 && value < 65536)
                {
                    port = value;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(new JobStore(), port));
        }
    }
}
