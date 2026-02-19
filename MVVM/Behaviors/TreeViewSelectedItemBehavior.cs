using System.Windows;
using System.Windows.Controls;


namespace HydroExplorer.MVVM.Behaviors
{
    public static class TreeViewSelectedItemBehavior
    {
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItem",
                typeof(object),
                typeof(TreeViewSelectedItemBehavior),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public static object GetSelectedItem(DependencyObject obj)
            => obj.GetValue(SelectedItemProperty);

        public static void SetSelectedItem(DependencyObject obj, object value)
            => obj.SetValue(SelectedItemProperty, value);

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TreeView treeView)
                treeView.SelectedItemChanged += (s, args) =>
                    SetSelectedItem(treeView, args.NewValue);
        }
    }
}