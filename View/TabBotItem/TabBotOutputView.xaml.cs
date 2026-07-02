using HydroExplorer.ViewModel.TabBotItem;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.View.TabBotItem
{
    public partial class TabBotOutputView : UserControl
    {
        // Mirrors DataContext into a real DependencyProperty, same pattern (and
        // same reason) as TabBotChartsView — this view is resolved via a
        // DataTemplate inside TabBotControlView's TabControl, and plain {Binding}
        // against DataContext doesn't reliably resolve in that composition.
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(TabBotOutputViewModel),
                typeof(TabBotOutputView),
                new PropertyMetadata(null, OnViewModelChanged));

        public TabBotOutputViewModel? ViewModel
        {
            get => (TabBotOutputViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public TabBotOutputView()
        {
            InitializeComponent();

            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is TabBotOutputViewModel vm)
                    ViewModel = vm;
            };
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TabBotOutputView view) return;

            if (e.OldValue is TabBotOutputViewModel oldVm)
                oldVm.Lines.CollectionChanged -= view.OnLinesCollectionChanged;

            if (e.NewValue is TabBotOutputViewModel newVm)
                newVm.Lines.CollectionChanged += view.OnLinesCollectionChanged;
        }

        private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (ViewModel?.AutoScroll != true) return;
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            if (OutputListBox.Items.Count == 0) return;

            // Defer ScrollIntoView to after the full batch of CollectionChanged
            // notifications has settled — firing it synchronously mid-batch (e.g.
            // while FlushPending is still adding items and TrimToMax is removing
            // from the front) causes the ItemsControl inconsistency exception because
            // the control's internal snapshot doesn't yet match the collection state.
            Dispatcher.InvokeAsync(() =>
            {
                if (OutputListBox.Items.Count > 0)
                    OutputListBox.ScrollIntoView(OutputListBox.Items[^1]);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}