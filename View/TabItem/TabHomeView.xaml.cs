using System.ComponentModel;
using System.Configuration;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;



namespace HydroExplorer.View.TabItem
{
    public partial class TabHomeView : INotifyPropertyChanged
    {
        public Configuration AppConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set { _selectedImagePath = value; OnPropertyChanged(); }
        }

        readonly string ProjPath = new("projTest");
        readonly string ProjName = new("199805001 Test Project");
        readonly string PlanName = new("EXST");
        readonly string ProName = new("Q100");
        readonly string[] Languages = ["English", "French", "Spanish", "Chinese"];
        //readonly int PlanID = 0;
        //readonly int ProID = 0;



        public TabHomeView()
        {
            InitializeComponent();

            DataContext = this;
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

        public void CbAllFeatures_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool newVal = (cbFeatureXyz.IsChecked == true);
            cbFeatureAbc.IsChecked = newVal;
            cbFeatureXyz.IsChecked = newVal;
            cbFeatureWww.IsChecked = newVal;
        }


        public void CbFeature_CheckedChanged(object sender, RoutedEventArgs e)
        {
            cbFeatureAbc.IsChecked = null;
            if ((cbFeatureAbc.IsChecked == true) && (cbFeatureXyz.IsChecked == true) && (cbFeatureWww.IsChecked == true))
                cbFeatureAbc.IsChecked = true;

            if ((cbFeatureAbc.IsChecked == false) && (cbFeatureXyz.IsChecked == false) && (cbFeatureWww.IsChecked == false))
                cbFeatureAbc.IsChecked = false;
        }

        public void Button_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("\nSETTINGS SAVED....\n");
            AppConfig.Save();
        }


        public void Update_box()
        {
            HomeView homeView = new();

            var textBox = LogicalTreeHelper.FindLogicalNode(homeView, "myTextBox") as TextBox;
            textBox?.Text = "Hello!";
        }


    }
}
