using System.Windows;

namespace SZOptimizer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(args.Exception.ToString(), "Ошибка при запуске SZOptimizer");
                args.Handled = true;
            };
            base.OnStartup(e);
        }
    }
}
