using System.Windows;
using Tranzx.iVS4.App.ViewModels;

namespace Tranzx.iVS4.App.Views;

public partial class AddSensorDialog : Window
{
    public AddSensorDialogViewModel ViewModel { get; }

    public AddSensorDialog(IEnumerable<int> freeSlots)
    {
        InitializeComponent();
        ViewModel = new AddSensorDialogViewModel(freeSlots);
        DataContext = ViewModel;
        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };
    }
}
