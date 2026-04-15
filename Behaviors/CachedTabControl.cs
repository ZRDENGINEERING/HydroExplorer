using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.Behaviors
{
    public class CachedTabControl : TabControl
    {
        private readonly Dictionary<object, ContentPresenter> _cache = [];

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);

            if (SelectedItem == null) return;

            if (!_cache.TryGetValue(SelectedItem, out var presenter))
            {
                presenter = new ContentPresenter
                {
                    Content = SelectedItem,
                    ContentTemplate = FindDataTemplate(SelectedItem)
                };
                _cache[SelectedItem] = presenter;
            }

            // Swap content into the ContentPresenter without recreating the view
            var container = ItemContainerGenerator.ContainerFromItem(SelectedItem) as TabItem;
            if (container != null)
                container.Content = presenter;
        }

        private DataTemplate? FindDataTemplate(object item)
        {
            return Resources.Values
                .OfType<DataTemplate>()
                .FirstOrDefault(dt => dt.DataType is Type t && t.IsInstanceOfType(item));
        }
    }
}