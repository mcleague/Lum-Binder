using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BinderApp.Models;
using BinderApp.ViewModels;

namespace BinderApp.Controls
{
    public partial class TabStrip : UserControl
    {
        public TabStrip()
        {
            InitializeComponent();
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is Tab tab && DataContext is MainViewModel vm)
            {
                vm.SelectedTab = tab;
            }
        }

        private void TabButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is Tab tab && DataContext is MainViewModel vm)
            {
                if (vm.SelectedTab == tab)
                {
                    rb.IsChecked = true;
                }
            }
        }

        private void TabButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is Tab tab && DataContext is MainViewModel vm)
            {
                vm.SelectedTab = tab;
                rb.IsChecked = true;
            }
        }

        private void TabButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is Tab tab && DataContext is MainViewModel vm)
            {
                vm.RenameTabCommand.Execute(tab);
                e.Handled = true;
            }
        }

        private void RenameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is Tab tab && DataContext is MainViewModel vm)
            {
                vm.RenameTabCommand.Execute(tab);
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is Tab tab && DataContext is MainViewModel vm)
            {
                vm.DeleteTabCommand.Execute(tab);
            }
        }
    }
}
