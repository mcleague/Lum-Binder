using System.Windows;
using System.Windows.Input;
using BinderApp.ViewModels;

namespace BinderApp.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (DataContext is MainViewModel vm && vm.BindEditor?.IsCapturing == true)
            {
                vm.BindEditor.ProcessKeyDown(e);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && !vm.MinimizeToTray)
            {
                (Application.Current as App)?.ExitApplication();
                return;
            }

            Hide();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.EditBindCommand.Execute(null);
            }
        }

        private void BindCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SaveCurrentConfig();
                vm.RegisterHotkeys();
            }
        }

    }
}
