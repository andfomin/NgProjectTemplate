using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace AfominDotCom.NgProjectTemplate.Wizards
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class NgItemWizardWindow : Window
    {

        public NgItemWizardWindow(NgItemWizardViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnClickOk(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void OnClickCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void LearnMore_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            RequestNavigate(e);
        }

        private void RequestNavigate(RequestNavigateEventArgs e)
        {
            var url = e.Uri.AbsoluteUri;
            Task.Factory.StartNew(() =>
            {
                using (var process = Process.Start(url))
                {
                    process.WaitForExit();
                }
            });
            e.Handled = true;
            DialogResult = false;
        }


    }
}
