using System.ComponentModel;
using System.Configuration;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;


namespace HydroExplorer.MVVM.View
{
    public partial class HomeView : UserControl
    {
        private Configuration AppConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        string ProjPath = new("C:/");
        readonly string ProjName = new("199805001 Test Project");

        readonly string PlanName = new("EXST");
        readonly int PlanID = 0;

        readonly string ProName = new("Q100");
        readonly int ProID = 0;

        readonly string[] Languages = ["English", "French", "Spanish", "Chinese"];

        private HydroExplorer.MVVM.View.UserControls.TreeView _treeView;


        //private Form1 _instance; using below:::::::::
        //private TreeView _instance;
        private HydroExplorer.MVVM.View.UserControls.TreeView _instance;

        public HomeView(HydroExplorer.MVVM.View.UserControls.TreeView instance)
        {
            _instance = instance;
            System.Diagnostics.Debug.WriteLine("HomeView.............................................");
        }

        public HomeView()
        {
            InitializeComponent();

            cboxLanguage.ItemsSource = Languages;
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


        private void cbAllFeatures_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool newVal = (cbFeatureXyz.IsChecked == true);
            cbFeatureAbc.IsChecked = newVal;
            cbFeatureXyz.IsChecked = newVal;
            cbFeatureWww.IsChecked = newVal;
        }

        private void cbFeature_CheckedChanged(object sender, RoutedEventArgs e)
        {
            cbFeatureAbc.IsChecked = null;
            if ((cbFeatureAbc.IsChecked == true) && (cbFeatureXyz.IsChecked == true) && (cbFeatureWww.IsChecked == true))
                cbFeatureAbc.IsChecked = true;

            if ((cbFeatureAbc.IsChecked == false) && (cbFeatureXyz.IsChecked == false) && (cbFeatureWww.IsChecked == false))
                cbFeatureAbc.IsChecked = false;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("settings saved");
            AppConfig.Save(); 
        }


        //public event PropertyChangedEventHandler PropertyChanged;
        //protected void OnPropertyChanged([CallerMemberName] string name = null)
        //{
        //    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        //    //var TreeView = new UserControls.TreeView();
        //    var tView = _treeView;
        //    //_treeView.

        //    System.Diagnostics.Debug.WriteLine($"HomeView name : {name} \n");
        //}




    }
}