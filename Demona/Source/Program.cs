using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using JetBrains.Annotations;
using ZLR.VM;

namespace ZLR.Interfaces.Demona
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        // ReSharper disable once InconsistentNaming
        static async Task Main([ItemNotNull] [NotNull] string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Stream gameFile;
            string storyName;

            if (args.Length > 0)
            {
                if (!File.Exists(args[0]))
                {
                    MessageBox.Show("File not found: " + args[0]);
                    return;
                }

                try
                {
                    gameFile = new FileStream(args[0], System.IO.FileMode.Open, FileAccess.Read);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    return;
                }
                storyName = Path.GetFileName(args[0]);
            }
            else
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "Select Game File";
                    dlg.Filter = "Supported Z-code files (*.z5;*.z8;*.zblorb;*.zlb)|*.z5;*.z8;*.zblorb;*.zlb|All files (*.*)|*.*";
                    dlg.CheckFileExists = true;
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;

                    try
                    {
                        gameFile = dlg.OpenFile();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error Loading Game");
                        return;
                    }
                    storyName = Path.GetFileName(dlg.FileName);
                }
            }

            Debug.Assert(storyName != null, "storyName != null");

            using (var io = new GlkIO(args, storyName))
            {
#if !DEBUG
                try
                {
#endif
                    try
                    {
                        var engine = new ZMachine(gameFile, io);
                        await engine.RunAsync();
                    }
                    finally
                    {
                        gameFile.Close();
                    }
#if !DEBUG
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error Running Game");
                    return;
                }
#endif
            }
        }
    }
}