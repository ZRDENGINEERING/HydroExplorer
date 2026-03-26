using System.Configuration;

namespace HydroExplorer
{
    public class UISettings : ConfigurationSection
    {
        [ConfigurationProperty("selectedDetail", DefaultValue = "selecteddetail")]
        public string SelectedDetail
        {
            get { return (string)this["selectedDetail"]; }
            set
            {
                this["selectedDetail"] = value;
            }
        }

        [ConfigurationProperty("projPath", DefaultValue = "C:/")]
        public string ProjPath
        {
            get { return (string)this["projPath"]; }
            set
            {
                this["projPath"] = value;
            }
        }

        [ConfigurationProperty("projName", DefaultValue = "199805001 Test Project")]
        public string ProjName
        {
            get { return (string)this["projName"]; }
            set
            {
                this["projName"] = value;
            }
        }


        [ConfigurationProperty("planName", DefaultValue = "EXST")]
        public string PlanName
        {
            get { return (string)this["planName"]; }
            set { this["planName"] = value; }
        }

        [ConfigurationProperty("planID", DefaultValue = 0)]
        public int PlanID
        {
            get { return (int)this["planID"]; }
            set { this["planID"] = value; }
        }


        [ConfigurationProperty("proName", DefaultValue = "Q100")]
        public string ProName
        {
            get { return (string)this["proName"]; }
            set { this["proName"] = value; }
        }

        [ConfigurationProperty("proID", DefaultValue = 0)]
        public int ProID
        {
            get { return (int)this["proID"]; }
            set { this["proID"] = value; }
        }




        [ConfigurationProperty("theme", DefaultValue = "Dark")]
        public string Theme
        {
            get { return (string)this["theme"]; }
            set { this["theme"] = value; }
        }

        [ConfigurationProperty("currency", DefaultValue = "$")]
        public string Currency
        {
            get { return (string)this["currency"]; }
            set { this["currency"] = value; }
        }

        [ConfigurationProperty("fontsize", DefaultValue = 8)]
        public int FontSize
        {
            get { return (int)this["fontsize"]; }
            set { this["fontsize"] = value; }
        }


        [ConfigurationProperty("language", DefaultValue = "French")]
        public string Language
        {
            get { return (string)this["language"]; }
            set { this["language"] = value; }
        }
    }
}