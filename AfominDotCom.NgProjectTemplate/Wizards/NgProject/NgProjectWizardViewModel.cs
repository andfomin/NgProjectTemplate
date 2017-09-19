using AfominDotCom.NgProjectTemplate.Resources;
using System;

namespace AfominDotCom.NgProjectTemplate.Wizards
{

    public class NgProjectWizardViewModel
    {
        public string WindowTitle { get; set; }
        public bool IsNgFound { get; set; }
        public bool SkipNpmInstall { get; set; }        
        public bool AddRouting { get; set; } // Generate a routing module for the app

        public bool IsNgNotFound
        {
            get { return !this.IsNgFound; }
        }

        public NgProjectWizardViewModel(string projectName, bool isNgFound)
        {
            this.WindowTitle = String.Format(WizardResources.WindowTitle, projectName); 
            this.IsNgFound = isNgFound;
            this.SkipNpmInstall = true;
            this.AddRouting = true;
        }



    }
}
