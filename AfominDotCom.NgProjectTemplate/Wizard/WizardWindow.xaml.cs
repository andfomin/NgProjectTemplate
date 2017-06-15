using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;

namespace AfominDotCom.NgProjectTemplate.Wizard
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class WizardWindow : Window
    {

        public WizardWindow(WizardViewModel viewModel)
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

        private void LearnMoreNgNotFound_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            RequestNavigate(e);
        }

        private void LearnMoreSkipInstall_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            RequestNavigate(e);
        }

        private void RequestNavigate(RequestNavigateEventArgs e)
        {
            Task.Factory.StartNew(() =>
            {
                using (var process = Process.Start(e.Uri.AbsoluteUri))
                {
                    process.WaitForExit();
                }
            });
            e.Handled = true;
            DialogResult = false;
        }
    }
}
