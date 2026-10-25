using System;
using System.Windows;
using System.Windows.Input;

namespace BinderApp.Controls
{
    /// <summary>
    /// Вспомогательные методы и прикрепленные свойства для кастомных окон.
    /// </summary>
    public static class CustomWindowChrome
    {
        public static readonly DependencyProperty EnableDragProperty =
            DependencyProperty.RegisterAttached(
                "EnableDrag",
                typeof(bool),
                typeof(CustomWindowChrome),
                new PropertyMetadata(false, OnEnableDragChanged));

        public static bool GetEnableDrag(UIElement element) => (bool)element.GetValue(EnableDragProperty);
        public static void SetEnableDrag(UIElement element, bool value) => element.SetValue(EnableDragProperty, value);

        private static void OnEnableDragChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element && (bool)e.NewValue)
            {
                element.MouseLeftButtonDown += (s, args) =>
                {
                    var window = Window.GetWindow(element);
                    if (window != null && args.ButtonState == MouseButtonState.Pressed)
                    {
                        window.DragMove();
                    }
                };
            }
        }
    }
}
