using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ElevenLabsSpeechGenerator
{
    internal static class Program
    {
        public const string AppName = "ElevenLabs Speech Generator";
        public const string Version = AppVersion.Short;
        private const string MutexName = "OnjLouis.ElevenLabsSpeechGenerator";

        [STAThread]
        private static void Main(string[] args)
        {
            if (ProgramUpdater.IsApplyUpdateCommand(args))
            {
                ProgramUpdater.ApplyUpdateFromCommandLine(args);
                return;
            }

            bool ownsMutex;
            using (var mutex = new Mutex(true, MutexName, out ownsMutex))
            {
                if (!ownsMutex)
                {
                    MessageBox.Show("ElevenLabs Speech Generator is already running.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppPaths.EnsureUserFolders();
                AppLog.Initialize();
                ProgramUpdater.ScheduleCleanupFromCommandLine(args);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs eventArgs)
                {
                    AppLog.WriteException("Unhandled interface exception", eventArgs.Exception);
                    MessageBox.Show("An unexpected error occurred." + Environment.NewLine + Environment.NewLine + eventArgs.Exception.Message + Environment.NewLine + Environment.NewLine + "Details were written to the log.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                var initialFile = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
                try
                {
                    Application.Run(new MainForm(initialFile));
                }
                catch (Exception ex)
                {
                    AppLog.WriteException("Fatal application exception", ex);
                    MessageBox.Show("The application could not continue." + Environment.NewLine + Environment.NewLine + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
