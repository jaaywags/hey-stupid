namespace HeyStupid
{
    using System;
    using System.IO;
    using System.Threading;
    using Microsoft.UI.Dispatching;
    using Microsoft.UI.Xaml;

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Application.Start((p) =>
                {
                    var context = new DispatcherQueueSynchronizationContext(
                        DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    _ = new App();
                });
            }
            catch (Exception ex)
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HeyStupid",
                    "crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"{DateTime.Now:O}\n{ex}\n");
                throw;
            }
        }
    }
}