using System.Configuration;
using System.Data;
using System.Windows;

namespace XMLAndDBAppGUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            MessageBox.Show($"Запуск","Запуск", MessageBoxButton.OK);
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Критическая ошибка: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                    "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
                Shutdown();
            };
        }
    }

}
