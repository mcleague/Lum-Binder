using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        private void BindsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            ScrollViewer? scrollViewer = FindVisualChild<ScrollViewer>(listBox);
            if (scrollViewer == null) return;

            double offset = Math.Clamp(
                scrollViewer.VerticalOffset - e.Delta,
                0,
                scrollViewer.ScrollableHeight);
            if (offset == scrollViewer.VerticalOffset) return;

            scrollViewer.ScrollToVerticalOffset(offset);
            e.Handled = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match) return match;
                T? nestedMatch = FindVisualChild<T>(child);
                if (nestedMatch != null) return nestedMatch;
            }

            return null;
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
