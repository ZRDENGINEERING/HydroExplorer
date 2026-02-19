using System.ComponentModel;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using HydroExplorer.MVVM.Behaviors;
using HydroExplorer.MVVM.ViewModels;




namespace HydroExplorer.MVVM.View
{
    public partial class HomeView : UserControl
    {
        private Configuration AppConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        //public event PropertyChangedEventHandler PropertyChanged;
        string ProjName = new("199805001 Test Project");

        
        //private TreeViewHdf _instance;
        ////private TreeViewHdf.ProjPathText _intance;


        readonly string ProjPath = new("projTest");

        readonly string PlanName = new("EXST");
        readonly int PlanID = 0;

        readonly string ProName = new("Q100");
        readonly int ProID = 0;

        readonly string[] Languages = ["English", "French", "Spanish", "Chinese"];


        public string Header { get; set; }
        public string Tag { get; set; }


        BaseViewModel _baseVM = new();

        private HomeView _selectedNode;
        public HomeView SelectedNode
        {
            get => _selectedNode;
            set
            {
                _baseVM.SetField(ref _selectedNode, value);
                _baseVM.OnPropertyChanged(nameof(SelectedDetail));

                System.Diagnostics.Debug.WriteLine($"SelectedNode SelectedNode....");
            }
        }

        //--- What shows in the TextBox ---
        public string SelectedDetail => SelectedNode != null
            ? $"{SelectedNode.Header}: {SelectedNode.Tag}"
            : "No item selected.";




        public HomeView()
        {
            InitializeComponent();

            //TreeNode();

            cboxLanguage.ItemsSource = Languages;

            //projTest.Text = ProjTest;

            projPath.Text = ProjPath;
            projName.Text = ProjName;
            planName.Text = PlanName;
            proName.Text = ProName;

            if (AppConfig.Sections["UISettings"] is null)
            {
                AppConfig.Sections.Add("UISettings", new UISettings());
            }

            var UISettingSection = AppConfig.GetSection("UISettings");
            this.DataContext = UISettingSection;
        }




        //private void OnPropertyChanged([CallerMemberName] string? n = null)
        //    => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        


        public event PropertyChangedEventHandler PropertyChanged;
        internal void OnPropertyChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("name"));

            projPath.Text = "testeestest";

            projPath.Text = e.NewValue.ToString();


            System.Diagnostics.Debug.WriteLine($"OnPropertyTestFunc....{e.NewValue.ToString()}");

        }




        private void cbAllFeatures_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool newVal = (cbFeatureXyz.IsChecked == true);
            cbFeatureAbc.IsChecked = newVal;
            cbFeatureXyz.IsChecked = newVal;
            cbFeatureWww.IsChecked = newVal;
        }

        internal void cbFeature_CheckedChanged(object sender, RoutedEventArgs e)
        {
            cbFeatureAbc.IsChecked = null;
            if ((cbFeatureAbc.IsChecked == true) && (cbFeatureXyz.IsChecked == true) && (cbFeatureWww.IsChecked == true))
                cbFeatureAbc.IsChecked = true;

            if ((cbFeatureAbc.IsChecked == false) && (cbFeatureXyz.IsChecked == false) && (cbFeatureWww.IsChecked == false))
                cbFeatureAbc.IsChecked = false;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("\nSETTINGS SAVED....\n");
            AppConfig.Save(); 
        }
    }



}