using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BinderApp.Models;
using BinderApp.Native;
using BinderApp.ViewModels;

namespace BinderApp.Views
{
    public partial class OverlayWindow : Window
    {
        private readonly OverlayViewModel _vm;
        private readonly AppSettings _settings;

        public OverlayWindow(OverlayViewModel vm, AppSettings settings)
        {
            InitializeComponent();
            _vm = vm;
            _settings = settings;
            DataContext = vm;

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.IsVisible))
                {
                    if (_vm.IsVisible)
                    {
                        UpdatePosition();
                        Show();
                    }
                    else
                    {
                        Hide();
                    }
                }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetOverlayWindowStyles(hwnd);
            UpdatePosition();
        }

        public void UpdatePosition()
        {
            var area = SystemParameters.WorkArea;
            string pos = _settings.OverlayPosition?.ToLowerInvariant() ?? "top-right";

            switch (pos)
            {
                case "top-left":
                    Left = area.Left + 16;
                    Top = area.Top + 16;
                    break;
                case "bottom-left":
                    Left = area.Left + 16;
                    Top = area.Bottom - Height - 16;
                    break;
                case "bottom-right":
                    Left = area.Right - Width - 16;
                    Top = area.Bottom - Height - 16;
                    break;
                case "top-right":
                default:
                    Left = area.Right - Width - 16;
                    Top = area.Top + 16;
                    break;
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
