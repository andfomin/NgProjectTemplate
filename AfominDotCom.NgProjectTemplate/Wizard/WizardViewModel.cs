using AfominDotCom.NgProjectTemplate.Resources;
using System;

namespace AfominDotCom.NgProjectTemplate.Wizard
{

    public class WizardViewModel
    {
        public string WindowTitle { get; set; }
        public bool IsNgFound { get; set; }
        public bool SkipNpmInstall { get; set; }        
        public bool AddRouting { get; set; } // Generate a routing module for the app

        public bool IsNgNotFound
        {
            get { return !this.IsNgFound; }
        }

        public WizardViewModel(string projectName, bool isNgFound)
        {
            this.WindowTitle = String.Format(WizardResources.WindowTitle, projectName); 
            this.IsNgFound = isNgFound;
            this.SkipNpmInstall = true;
            this.AddRouting = true;
        }



    }
}
