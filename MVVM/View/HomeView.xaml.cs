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

        public event PropertyChangedEventHandler PropertyChanged;


        //string ProjName = new("199805001 Test Project");

        //private TreeViewHdf _instance;
        ////private TreeViewHdf.ProjPathText _intance;

        string projPathText = new("C:/");

        readonly string PlanName = new("EXST");
        readonly int PlanID = 0;

        readonly string ProName = new("Q100");
        readonly int ProID = 0;

        readonly string[] Languages = ["English", "French", "Spanish", "Chinese"];


        public HomeView()
        {
            InitializeComponent();

            cboxLanguage.ItemsSource = Languages;

            //projPath.Text = ProjPath;
            ////projName.Text = ProjName;
            //planName.Text = PlanName;
            //proName.Text = ProName;

            //if (AppConfig.Sections["UISettings"] is null)
            //{
            //    AppConfig.Sections.Add("UISettings", new UISettings());
            //}

            //var UISettingSection = AppConfig.GetSection("UISettings");
            //this.DataContext = UISettingSection;
        }


        private void demoTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            //projTest.Text = e.NewValue.ToString();
            
            System.Diagnostics.Debug.WriteLine("\ndemoTreeView_SelectedItemChanged....\n");

        }





    


        //public class HomeViewVars
        //{
        //    string projTest;

        //    public string ProjTest
        //    {
        //        get { return projTest; }
        //        set
        //        {
        //            projTest = value;
        //        }

        //    }
        //}





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