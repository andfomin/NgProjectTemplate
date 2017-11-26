using AfominDotCom.NgProjectTemplate.Resources;
using System;
using System.ComponentModel;
using System.Linq;

namespace AfominDotCom.NgProjectTemplate.Wizards
{

    public class NgItemWizardViewModel : INotifyPropertyChanged
    {
        public class NgItemWizardSettings
        {
            internal bool IsAspNetCore2 { get; set; }
            internal bool IsAngularCliJsonFound { get; set; }
            internal bool IsNgFound { get; set; }
            internal bool IsOldPackageJsonFound { get; set; }
            internal bool IsNpmAngularFound { get; set; }
            internal bool IsGitignoreOpened { get; set; }
            internal bool IsPackageJsonOpened { get; set; }
            internal bool IsTsconfigJsonOpened { get; set; }
            internal bool IsStartupCsOpened { get; set; }
        }

        private bool isAspNetCore2;
        private bool isAngularCliJsonFound;
        private bool isNpmAngularFound;
        private bool isNgFound;
        private bool installAutomatically;
        private bool isOldPackageJsonFound;

        public event PropertyChangedEventHandler PropertyChanged;

        public string WindowTitle { get; }
        public string OpenedFileNames { get; }

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
                return this.isAspNetCore2 && !this.isAngularCliJsonFound && !this.isNpmAngularFound
                    && String.IsNullOrEmpty(OpenedFileNames);
            }
        }

        public bool DisplayAspNetCore2Warning
        {
            get { return !this.isAspNetCore2; }
        }

        public bool DisplayAngularCliJsonFoundWarning
        {
            get { return this.isAngularCliJsonFound; }
        }

        public bool DisplayNpmAngularFoundWarning
        {
            get { return this.isNpmAngularFound && !DisplayAngularCliJsonFoundWarning; }
        }

        public bool DisplayNgNotFoundWarning
        {
            get { return !this.isNgFound && !DisplayAngularCliJsonFoundWarning && !DisplayNpmAngularFoundWarning; }
        }

        public bool DisplayFileOpenedWarning
        {
            get
            {
                return !String.IsNullOrEmpty(OpenedFileNames)
                    && !DisplayAspNetCore2Warning && !DisplayAngularCliJsonFoundWarning && !DisplayNpmAngularFoundWarning;
            }
        }

        public bool DisplayOldPackageJsonWarning
        {
            get { return this.isOldPackageJsonFound && this.installAutomatically; }
        }


        public NgItemWizardViewModel(NgItemWizardSettings settings)
        {
            WindowTitle = WizardResources.NgItemWindowTitle;

            this.isAspNetCore2 = settings.IsAspNetCore2;
            this.isAngularCliJsonFound = settings.IsAngularCliJsonFound;
            this.isNpmAngularFound = settings.IsNpmAngularFound;
            this.isNgFound = settings.IsNgFound;
            this.isOldPackageJsonFound = settings.IsOldPackageJsonFound;

            var openedFileNames = new[]
            {
                settings.IsGitignoreOpened ? NgWizardHelper.GitignoreFileName : null,
                settings.IsPackageJsonOpened ? NgWizardHelper.PackageJsonFileName : null,
                settings.IsTsconfigJsonOpened ? NgWizardHelper.TsconfigJsonFileName : null,
                settings.IsStartupCsOpened ? NgWizardHelper.StartupCsFileName : null,
            }
            .Where(i => i != null)
            ;
            OpenedFileNames = String.Join(", ", openedFileNames);

            // This must be assigned last because IsInstallAutomaticallyEnabled is calculated based on the above values.
            this.installAutomatically = IsInstallAutomaticallyEnabled && this.isNgFound;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
