namespace HydroExplorer.View
{
    public partial class HomeView
    {

        public HomeView()
        {
            InitializeComponent();
            DataContext = this;

            //if (AppConfig.Sections["UISettings"] is null)
            //{
            //    AppConfig.Sections.Add("UISettings", new UISettings());
            //}

            //var UISettingSection = AppConfig.GetSection("UISettings");
            //this.DataContext = UISettingSection;
        }

    }
}