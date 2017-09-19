using AfominDotCom.NgProjectTemplate.Resources;
using System;
using System.ComponentModel;

namespace AfominDotCom.NgProjectTemplate.Wizards
{

    public class NgItemWizardViewModel : INotifyPropertyChanged
    {
        private bool isAngularCliJsonFound;
        private bool isNgFound;
        private bool installAutomatically;
        private bool isOldPackageJsonFound;
        private bool isGitignoreOpened;
        private bool isPackageJsonOpened;
        private bool isStartupCsOpened;

        public event PropertyChangedEventHandler PropertyChanged;

        public string WindowTitle { get; }

        public bool InstallAutomatically
        {
            get
            {
                return this.installAutomatically;
            }
            set
            {
                this.installAutomatically = value;
                OnPropertyChanged("DisplayOldPackageJsonWarning");
            }
        }

        public bool IsInstallAutomaticallyEnabled
        {
            get
            {
                return this.isNgFound && !this.isAngularCliJsonFound
                  && !this.isGitignoreOpened && !this.isPackageJsonOpened && !this.isStartupCsOpened;
            }
        }

        public bool DisplayAngularCliJsonFoundWarning
        {
            get { return this.isAngularCliJsonFound; }
        }

        public bool DisplayNgNotFoundWarning
        {
            get { return !this.isNgFound && !DisplayAngularCliJsonFoundWarning; }
        }

        public bool DisplayFileOpenedWarning
        {
            get
            {
                return !DisplayAngularCliJsonFoundWarning && !DisplayNgNotFoundWarning
                 && (this.isGitignoreOpened || this.isPackageJsonOpened || this.isStartupCsOpened);
            }
        }

        public bool DisplayGitignoreOpened
        {
            get { return this.isGitignoreOpened; }
        }

        public bool DisplayPackageJsonOpened
        {
            get { return this.isPackageJsonOpened; }
        }

        public bool DisplayStartupCsOpened
        {
            get { return this.isStartupCsOpened; }
        }

        public bool DisplayOldPackageJsonWarning
        {
            get { return this.isOldPackageJsonFound && this.installAutomatically; }
        }

        public NgItemWizardViewModel(bool isNgFound, bool isAngularCliJsonFound, bool isOldPackageJsonFound,
            bool isGitignoreOpened, bool isPackageJsonOpened, bool isStartupCsOpened)
        {
            WindowTitle = WizardResources.NgItemWindowTitle;
            this.isNgFound = isNgFound;
            this.isAngularCliJsonFound = isAngularCliJsonFound;
            this.isGitignoreOpened = isGitignoreOpened;
            this.isPackageJsonOpened = isPackageJsonOpened;
            this.isStartupCsOpened = isStartupCsOpened;
            this.isOldPackageJsonFound = isOldPackageJsonFound;
            // This must be assigned last because IsInstallAutomaticallyEnabled is calculated based on the above values.
            this.installAutomatically = IsInstallAutomaticallyEnabled;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
