//using System.Configuration;

//namespace HydroExplorer
//{
//    public class UISettings : ConfigurationSection
//    {
//        [ConfigurationProperty("selectedDetail", DefaultValue = "selecteddetail")]
//        public string SelectedDetail
//        {
//            get { return (string)this["selectedDetail"]; }
//            set
//            {
//                this["selectedDetail"] = value;
//            }
//        }

//        [ConfigurationProperty("cboxHdfPathA", DefaultValue = "C:/")]
//        public string HdfPathA
//        {
//            get { return (string)this["cboxHdfPathA"]; }
//            set
//            {
//                this["cboxHdfPathA"] = value;
//            }
//        }


//        [ConfigurationProperty("cboxHdfPathB", DefaultValue = "C:/")]
//        public string HdfPathB
//        {
//            get { return (string)this["cboxHdfPathB"]; }
//            set
//            {
//                this["cboxHdfPathB"] = value;
//            }
//        }


//        [ConfigurationProperty("txtBoxProjPath", DefaultValue = "C:/")]
//        public string ProjPath
//        {
//            get { return (string)this["txtBoxProjPath"]; }
//            set
//            {
//                this["txtBoxProjPath"] = value;
//            }
//        }


//        [ConfigurationProperty("projDir", DefaultValue = "dir")]
//        public string ProjDir
//        {
//            get { return (string)this["projDir"]; }
//            set
//            {
//                this["projDir"] = value;
//            }
//        }


//        [ConfigurationProperty("projName", DefaultValue = "199805001 Test Project")]
//        public string ProjName
//        {
//            get { return (string)this["projName"]; }
//            set
//            {
//                this["projName"] = value;
//            }
//        }


//        [ConfigurationProperty("txtBoxPlanNameA", DefaultValue = "EXST")]
//        public string PlanName
//        {
//            get { return (string)this["txtBoxPlanNameA"]; }
//            set { this["txtBoxPlanNameA"] = value; }
//        }

//        [ConfigurationProperty("planID", DefaultValue = 0)]
//        public int PlanID
//        {
//            get { return (int)this["planID"]; }
//            set { this["planID"] = value; }
//        }


//        [ConfigurationProperty("txtBoxProName", DefaultValue = "Q100")]
//        public string ProName
//        {
//            get { return (string)this["txtBoxProName"]; }
//            set { this["txtBoxProName"] = value; }
//        }

//        [ConfigurationProperty("proID", DefaultValue = 0)]
//        public int ProID
//        {
//            get { return (int)this["proID"]; }
//            set { this["proID"] = value; }
//        }




//        [ConfigurationProperty("theme", DefaultValue = "Dark")]
//        public string Theme
//        {
//            get { return (string)this["theme"]; }
//            set { this["theme"] = value; }
//        }

//        [ConfigurationProperty("currency", DefaultValue = "$")]
//        public string Currency
//        {
//            get { return (string)this["currency"]; }
//            set { this["currency"] = value; }
//        }

//        [ConfigurationProperty("fontsize", DefaultValue = 8)]
//        public int FontSize
//        {
//            get { return (int)this["fontsize"]; }
//            set { this["fontsize"] = value; }
//        }


//    }
//}