using AfominDotCom.NgProjectTemplate.Resources;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AfominDotCom.NgProjectTemplate.Wizards
{
    public class NgProjectWizard : IWizard
    {
        private const string NgVersionSuccessFragment = "@angular/cli";
        private const string gitignoreFileName = ".gitignore";
        private const string gitignoreTempFileName = ".gitignore.temp";
        private const string readmeMdFileName = "README.md";
        private const string packageJsonFileName = "package.json";
        private const string includePackageJsonElement = "<None Include=\"package.json\" />";

        private bool isNgFound;
        private bool skipNpmInstall;
        private bool addRouting;
        private Project project;

        // This method is called before opening any item that has the OpenInEditor attribute.  
        public void BeforeOpeningFile(ProjectItem projectItem)
        {
        }

        public void ProjectFinishedGenerating(Project project)
        {
            this.project = project;

            string ngNewOutput = String.Empty;
            string readmeMdFilePath = null;

            // We included empty README.md and package.json to make the files discoverable by the IDE from the very beginning.
            // If the files were marked in the Solution Explorer as missing, opening them would take more time (??? assumption not tested) and cause flicker.
            // Now we are going to replace them with the real ones.
            var projectDirectory = Path.GetDirectoryName(project.FullName);
            if (Directory.Exists(projectDirectory))
            {
                readmeMdFilePath = Path.Combine(projectDirectory, readmeMdFileName);
                if (File.Exists(readmeMdFilePath))
                {
                    File.Delete(readmeMdFilePath);
                }
                var packageJsonFilePath = Path.Combine(projectDirectory, packageJsonFileName);
                if (File.Exists(packageJsonFilePath))
                {
                    File.Delete(packageJsonFilePath);
                }
                // Starting from ver.1.4, the CLI creates a ".gitignore" file even if the "--ignore-git" option is specified. So "ng new --ignore-git" fails if there is an existing .gitignore in the directory.
                // +https://github.com/angular/angular-cli/issues/7686
                var gitignoreFilePath = Path.Combine(projectDirectory, gitignoreFileName);
                var gitignoreTempFilePath = Path.Combine(projectDirectory, gitignoreTempFileName);
                if (File.Exists(gitignoreFilePath))
                {
                    File.Move(gitignoreFilePath, gitignoreTempFilePath);
                }

                // Run "ng new"
                // ngNewOutput = RunNgNew(projectDirectory, project.Name, this.addRouting);
                ngNewOutput = NgWizardHelper.RunNgNew(projectDirectory, project.Name, this.addRouting, this.isNgFound);

                if (File.Exists(gitignoreTempFilePath))
                {
                    if (File.Exists(gitignoreFilePath))
                    {
                        File.Delete(gitignoreFilePath);
                    }
                    File.Move(gitignoreTempFilePath, gitignoreFilePath);
                }
            }

            // Find the file created by the "ng new".
            var ngNewSucceeded = File.Exists(readmeMdFilePath);

            var messageText = ngNewSucceeded
                ? String.Format(WizardResources.ReadmeSuccessMessage, project.Name, NgWizardHelper.GetVersion())
                : WizardResources.ReadmeFailureMessage + ngNewOutput;
            ;

            // The Resource returns backslashes escaped. We cannot use regular 'Shift+Enter' line breakes in the editor, besause they produce "\r\n", whereas Ng-generated text has "\n", and Visual Studio Editor displays a dialog asking to normalize line breakes.
            messageText = messageText.Replace("\\n", "\n");

            // Augment README.md with our message.
            if (ngNewSucceeded)
            {
                var oldText = File.ReadAllText(readmeMdFilePath);
                var newText = messageText + oldText;
                File.WriteAllText(readmeMdFilePath, newText);
            }
            else
            {
                // If the "ng new" failed, create a substitute file to display.
                if (readmeMdFilePath != null)
                {
                    File.WriteAllText(readmeMdFilePath, messageText);
                }
            }
        }

        // This method is only called for item templates, not for project templates.  
        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
        }

        // This method is called after the project is created.  
        public void RunFinished()
        {
            if (this.project != null)
            {
                /* If package.json is included in the project, the npm package manager automatically starts installing packages after project creation.
                 * An install can also be triggered by opening, saving and closing package.json.
                 * The problem is those features are controled by two independant settings. 
                 * We may potentially trigger the npm installer twice. Apparently runs one instance, in very-very rare cases two. 
                 * There were errors logged a couple of times in the Output window which looked like a racing conflict.
                */
                // Trigger the npm package manager built-in in Visual Studio to start installing packages.
                EnvDTE.Window packageJsonWindow = null;
                if (!this.skipNpmInstall)
                {
                    var packageJsonItem = FindProjectItem(this.project, packageJsonFileName);
                    var packageJsonFilePath = GetProjectItemExistingFilePath(packageJsonItem);
                    if (packageJsonFilePath != null)
                    {
                        packageJsonWindow = packageJsonItem.Open();
                        packageJsonWindow.Activate();
                        packageJsonItem.Save();
                    }
                }

                // Display README.md
                var readmeMdItem = FindProjectItem(this.project, readmeMdFileName);
                var readmeMdFilePath = GetProjectItemExistingFilePath(readmeMdItem);
                if (readmeMdFilePath != null)
                {
                    var readmeMdWindow = readmeMdItem.Open();
                    readmeMdWindow.Activate();
                }

                // To avoid flicker, postpone closing package.json until after README.md has opened.
                if (packageJsonWindow != null)
                {
                    packageJsonWindow.Close();
                }

                // Close the ASP.NET Core project's default page. It has sections Overview, Connected Services, Publish.
                var windows = project.DTE.Windows;
                foreach (var w in windows)
                {
                    if (w is Window window)
                    {
                        if ((window.Type == vsWindowType.vsWindowTypeDocument) && (window.Caption == project.Name))
                        {
                            window.Close(vsSaveChanges.vsSaveChangesNo);
                        }
                    }
                }
            }
        }

        public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary,
            WizardRunKind runKind, object[] customParams)
        {
            string destinationDirectory = null;
            string solutionDirectory = null;

            try
            {
                replacementsDictionary.TryGetValue("$safeprojectname$", out string projectName);
                replacementsDictionary.TryGetValue("$destinationdirectory$", out destinationDirectory);
                replacementsDictionary.TryGetValue("$solutiondirectory$", out solutionDirectory);

                // Test if @angular/cli is installed globally.
                this.isNgFound = NgWizardHelper.IsNgFound(solutionDirectory);

                // Display the wizard to the user.
                var viewModel = new NgProjectWizardViewModel(projectName, this.isNgFound);
                var mainWindow = new NgProjectWizardWindow(viewModel);
                var accepted = mainWindow.ShowDialog().GetValueOrDefault();

                this.skipNpmInstall = viewModel.SkipNpmInstall;
                // If package.json is included in the project, the npm package manager automatically starts installing packages after project creation.
                replacementsDictionary.Add("$includepackagejson$", this.skipNpmInstall ? String.Empty : includePackageJsonElement);

                this.addRouting = viewModel.AddRouting;

                if (!accepted)
                {
                    throw new WizardCancelledException("The wizard has been cancelled by the user.");
                }
            }
            catch
            {
                DateTime projectDirCreationTime = DateTime.MinValue;
                if (Directory.Exists(destinationDirectory))
                {
                    projectDirCreationTime = Directory.GetCreationTimeUtc(destinationDirectory);
                    Directory.Delete(destinationDirectory, true);
                }
                if (Directory.Exists(solutionDirectory))
                {
                    // The solution could exist before the template started.
                    // This is a poor man's method of deciding whether the solution dir was created at about the same time as the project dir.
                    var solutionDirCreationTime = Directory.GetCreationTimeUtc(solutionDirectory);
                    if (Math.Abs((projectDirCreationTime - solutionDirCreationTime).TotalSeconds) < 5)
                    {
                        Directory.Delete(solutionDirectory, true);
                    }
                }
                throw;
            }
        }

        // This method is only called for item templates, not for project templates.  
        public bool ShouldAddProjectItem(string filePath)
        {
            return true;
        }

        private static ProjectItem FindProjectItem(Project project, string fileName)
        {
            ProjectItem projectItem = null;
            if (project != null)
            {

                foreach (var i in project.ProjectItems)
                {
                    if (i is ProjectItem item)
                    {
                        var itemName = item.Name;
                        if (itemName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            projectItem = item;
                            break;
                        }
                    }
                }
            }
            return projectItem;
        }

        private static string GetProjectItemExistingFilePath(ProjectItem projectItem)
        {
            string filePath = null;
            if (projectItem != null)
            {
                filePath = projectItem.FileNames[0];
                if (!File.Exists(filePath))
                {
                    filePath = null;
                }
            }
            return filePath;
        }

    }
}
