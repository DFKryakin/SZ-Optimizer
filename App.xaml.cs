using System;
using System.Windows;

namespace SZOptimizer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => MessageBox.Show(ex.ExceptionObject.ToString(), "Fatal Error");
            DispatcherUnhandledException += (s, ex) => { MessageBox.Show(ex.Exception.ToString(), "UI Error"); ex.Handled = true; };
            base.OnStartup(e);
        }
    }
}
