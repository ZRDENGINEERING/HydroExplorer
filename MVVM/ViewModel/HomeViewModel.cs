
using HydroExplorer.Core;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Configuration;


namespace HydroExplorer.MVVM.ViewModel
{
    internal class HomeViewModel : ObservableObject
    {

        public ObservableCollection<HomeViewModel> UserInputs { get; set; }

        private string _userInput;
        public string UserInput
        {

            get { return _userInput; }
            set
            {
                if (_userInput != value)
                {
                    _userInput = value;
                    OnPropertyChanged(nameof(UserInput));
                }

                System.Diagnostics.Debug.WriteLine(UserInput);

                //UserInputs.Add(_userInput);

                try
                {
                    // Get the text from the TextBox and write it to the selected file path
                    //File.WriteAllText(saveFileDialog.FileName, myTextBox.Text);
                    MessageBox.Show("File saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

            }

        }

        public void YourViewModel()
        {
            UserInputs = new ObservableCollection<HomeViewModel>();
        }
        






    }
}