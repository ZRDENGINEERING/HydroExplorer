using System.Configuration;
using System.Windows;
using System.Windows.Controls;


namespace HydroExplorer.MVVM.View
{
    public partial class HomeView : UserControl
    {
        private Configuration AppConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        string ProjPath = new("C:/");
        string ProjName = new("199805001 Test Project");

        readonly string PlanName = new("EXST");
        readonly int PlanID = 0;

        readonly string ProName = new("Q100");
        readonly int ProID = 0;

        readonly string[] Languages = ["English", "French", "Spanish", "Chinese"];
        

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