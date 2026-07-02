using HydroExplorer.ViewModel.TabBotItem;
using System.Windows;
using System.Windows.Controls;


namespace HydroExplorer.View.TabBotItem
{
    public partial class TabBotChartsView : UserControl
    {
        // Mirrors DataContext into a real DependencyProperty so XAML bindings can
        // use ElementName=Root, Path=ViewModel.* instead of plain {Binding}.
        // Needed because this view is resolved via a DataTemplate inside a
        // TabControl (TabBotControlView), and DataContext set that way doesn't
        // reliably propagate to descendant elements' bindings the way a directly-
        // assigned DataContext would — confirmed by prior debugging where plain
        // {Binding PlotVm.PlotModel} silently resolved to nothing despite PlotVm
        // having valid data, while the same path via ElementName=Root worked.
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(TabBotChartsViewModel),
                typeof(TabBotChartsView),
                new PropertyMetadata(null));

        public TabBotChartsViewModel? ViewModel
        {
            get => (TabBotChartsViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public TabBotChartsView()
        {
            InitializeComponent();

            // DataContextChanged fires once the DataTemplate (or whatever sets
            // DataContext) actually assigns a TabBotChartsViewModel — mirror it
            // into the DP that this view's own bindings actually use.
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is TabBotChartsViewModel vm)
                    ViewModel = vm;
            };

            // CreagerVm loads its own data (drainage area + peak discharge) as part
            // of TabBotChartsViewModel's constructor — no explicit load call needed
            // here. InvalidatePlot is kept since OxyPlot's PlotView sometimes needs a
            // nudge to render after the model is set during initial layout.
            Loaded += async (s, e) =>
            {
                if (ViewModel is { } vm)
                {
                    await vm.PlotVm.LoadDataAsync();
                    vm.PlotVm.PlotModel?.InvalidatePlot(true);
                    vm.CreagerVm.PlotModelChartsTab?.InvalidatePlot(true);
                }
            };

            // TabBotControlView's bottom TabControl uses a single shared
            // ContentPresenter (TabControl.ContentTemplate -> ContentControl) for
            // whichever tab is selected. Switching between the "Charts" and
            // "Output Window" bottom tabs rebuilds this entire View from scratch
            // via the DataTemplate every time — even though the underlying
            // TabBotChartsViewModel (and therefore PlotVm/CreagerVm/LP3Vm and
            // their PlotModel objects) is NOT recreated; it persists for the
            // whole app lifetime.
            //
            // That means a brand-new PlotView gets constructed here and tries to
            // attach to a PlotModel that the PREVIOUS instance of this View may
            // still be attached to — WPF's Unloaded event (when the old PlotView
            // would normally detach) fires asynchronously relative to the
            // ContentPresenter swap, not synchronously with it. Explicitly
            // nulling each PlotView's Model here forces detachment to happen
            // immediately as this View is torn down, so by the time a new one
            // is built on the next tab switch, the old attachment is guaranteed
            // gone. Without this, OxyPlot throws "This PlotModel is already in
            // use by some other PlotView control."
            Unloaded += (s, e) =>
            {
                ProfilePlotView.Model = null;
                CreagerPlotView.Model = null;
                LP3PlotView.Model = null;
            };
        }


    }
}