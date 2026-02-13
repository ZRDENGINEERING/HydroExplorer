using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;

namespace HydroExplorer.Utils
{
    internal class UserData
    {
        public string UserName { get; set; }
        public bool IsDarkModeEnables { get; set; }

    }

    //var userData = new UserData
    //{
    //    UserName = txtUserName.Text,
    //    IsDarkModeEnables = true

    //};
    //    string json = System.Text.Json.JsonSerializer.Serialize(userData);

    //    System.IO.File.WriteAllText("user_data.json", json);

    //if (System.IO.File.Exists("user_data.json"))
    //{
    //    string json = System.IO.File.ReadAllText("user_data.json");
    //    var userData = System.Text.Json.JsonSerializer.Deserialize<UserData>(json);
    //    txtUserName.Text = userData.UserName;
    //    chkDarkMode.IsChecked = userData.IsDarkModeEnabled;
    //}

        //SaveFileDialog sfd = new SaveFileDialog();
        //sfd.Filter = "XML-File|*.xml";
        //sfd.Title = "Save Char-Information";
        //sfd.ShowDialog();

        //if (sfd.FileName != "")
        //{
        //    System.IO.FileStream fs = (System.IO.FileStream)sfd.OpenFile();

        //    switch (sfd.FilterIndex)
        //    {
        //        case 1: dataset.WriteXml(fs, XmlWriteMode.WriteSchema);                   
        //    }

        //    fs.Close();
        //}



        //OpenFileDialog ofd = new OpenFileDialog();
        //ofd.Filter = "XML-File|*.xml";
        //ofd.Title = "Open Char-Information";
        //ofd.ShowDialog();

        //if (ofd.FileName != "")
        //{
        //    System.IO.FileStream fs = (System.IO.FileStream)ofd.OpenFile();

        //    switch (ofd.FilterIndex)
        //    {
        //        case 1: dataset.ReadXml(fs.Name, XmlReadMode.Auto);
        //    }
        //    fs.Close();








}
