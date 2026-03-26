using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;



namespace HydroExplorer.Behaviors
{

    public static class DragBehavior
    {
        private static Point _offset;
        private static bool _dragging;

        public static readonly DependencyProperty IsDraggableProperty =
            DependencyProperty.RegisterAttached("IsDraggable", typeof(bool), typeof(DragBehavior),
                new PropertyMetadata(false, OnIsDraggableChanged));

        public static bool GetIsDraggable(DependencyObject obj) => (bool)obj.GetValue(IsDraggableProperty);
        public static void SetIsDraggable(DependencyObject obj, bool value) => obj.SetValue(IsDraggableProperty, value);

        private static void OnIsDraggableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.MouseLeftButtonDown += OnMouseDown;
                    element.MouseLeftButtonUp += OnMouseUp;
                    element.MouseMove += OnMouseMove;
                }
            }
        }


        private static void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            var el = (UIElement)sender;
            _dragging = true;
            _offset = e.GetPosition(el);
            el.CaptureMouse();
        }

        private static void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var el = (UIElement)sender;
            var canvas = VisualTreeHelper.GetParent(el) as Canvas;
            if (canvas == null) return;

            var pos = e.GetPosition(canvas);
            Canvas.SetLeft(el, pos.X - _offset.X);
            Canvas.SetTop(el, pos.Y - _offset.Y);
        }
    }
}
