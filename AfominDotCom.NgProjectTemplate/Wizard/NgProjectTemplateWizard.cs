using AfominDotCom.NgProjectTemplate.Resources;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AfominDotCom.NgProjectTemplate.Wizard
{
    public class NgProjectTemplateWizard : IWizard
    {
        private const string NgVersionSuccessFragment = "@angular/cli";
        private const string readmeMdFileName = "README.md";
        private const string packageJsonFileName = "package.json";
        private const string includePackageJsonElement = "<None Include=\"package.json\" />";

        private bool skipInstall = true;
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
                // Run "ng new"
                ngNewOutput = RunNgNew(projectDirectory, project.Name);
            }

            // Find the file created by the "ng new".
            var ngNewSucceeded = File.Exists(readmeMdFilePath);

            var messageText = ngNewSucceeded
                ? String.Format(WizardResources.ReadmeSuccessMessage, project.Name, GetVersion())
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
                // Trigger the NPM package manager built-in in Visual Studio to start installing packages.
                EnvDTE.Window packageJsonWindow = null;
                if (!this.skipInstall)
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
                var isNgFound = false;
                var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktopDirectory))
                {
                    var ngVersionOutput = RunNgVersion(desktopDirectory);
                    isNgFound = ngVersionOutput.Contains(NgVersionSuccessFragment);
                }

                // Display the wizard to the user.
                var viewModel = new WizardViewModel(projectName, isNgFound, this.skipInstall);
                var mainWindow = new WizardWindow(viewModel);
                var accepted = mainWindow.ShowDialog().GetValueOrDefault();

                this.skipInstall = viewModel.SkipInstall;
                // If package.json is included in the project, NPM package manager automatically starts installing packages after project creation.
                replacementsDictionary.Add("$includepackagejson$", this.skipInstall ? String.Empty : includePackageJsonElement);

                if (!accepted)
                {
                    throw new WizardCancelledException();
                }
            }
            catch
            {
                if (Directory.Exists(destinationDirectory))
                {
                    Directory.Delete(destinationDirectory, true);
                }
                if (Directory.Exists(solutionDirectory))
                {
                    Directory.Delete(solutionDirectory, true);
                }
                throw;
            }
        }

        // This method is only called for item templates, not for project templates.  
        public bool ShouldAddProjectItem(string filePath)
        {
            return true;
        }

        private static string RunCmdSync(string arguments, string workingDirectory)
        {
            string processOutput = null;
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                processOutput = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
            }
            return processOutput;
        }

        public static string RunNgVersion(string workingDirectory)
        {
            var cmdArguments = $"/c ng --version";
            return RunCmdSync(cmdArguments, workingDirectory);
        }

        public static string RunNgNew(string workingDirectory, string projectName)
        {
            var cmdArguments = $"/c ng new {projectName} --directory . --skip-git --skip-install";
            return RunCmdSync(cmdArguments, workingDirectory);
        }

        private static ProjectItem FindProjectItem(Project project, string fileName)
        {
            ProjectItem projectItem = null;
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

        private static Version GetVersion()
        {
            return typeof(AfominDotCom.NgProjectTemplate.Wizard.NgProjectTemplateWizard).Assembly.GetName().Version;
        }

    }
}
