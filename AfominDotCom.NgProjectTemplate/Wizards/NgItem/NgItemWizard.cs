using AfominDotCom.NgProjectTemplate.Resources;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AfominDotCom.NgProjectTemplate.Wizards
{
    public class NgItemWizard : IWizard
    {
        internal static string LineBreak = Environment.NewLine;

        private bool installAutomatically;

        // This method is called before opening any item that has the OpenInEditor attribute.  
        public void BeforeOpeningFile(ProjectItem projectItem)
        {
        }

        public void ProjectFinishedGenerating(Project project)
        {
        }

        // This method is only called for item templates, not for project templates.  
        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
            var project = projectItem.ContainingProject;

            if (this.installAutomatically && (project != null))
            {
                string ngNewOutput = String.Empty;
                bool? ngNewSucceeded = null;
                bool? mergedGitignoreFiles = null;
                bool? mergedPackageJsonFiles = null;
                bool? modifiedStartupSc = null;

                var projectDirectory = Path.GetDirectoryName(project.FullName);
                if (Directory.Exists(projectDirectory))
                {
                    // Starting from ver.1.4, the CLI creates a ".gitignore" file even if the "--ignore-git" option is specified. So "ng new --ignore-git" fails if there is an existing .gitignore in the directory.
                    // +https://github.com/angular/angular-cli/issues/7686
                    PreserveExistingFile(projectDirectory, NgWizardHelper.GitignoreFileName, NgWizardHelper.GitignoreTempFileName);

                    PreserveExistingFile(projectDirectory, NgWizardHelper.PackageJsonFileName, NgWizardHelper.PackageJsonOldFileName);

                    // Run "ng new"
                    ngNewOutput = NgWizardHelper.RunNgNew(projectDirectory, project.Name);

                    // Find the .angular-cli.json created by "ng new".
                    ngNewSucceeded = NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.AngularCliJsonFileName);

                    if (ngNewSucceeded.Value)
                    {
                        mergedGitignoreFiles = MergeGitignoreFile(projectDirectory);
                        mergedPackageJsonFiles = MergePackageJsonFiles(projectDirectory);
                        modifiedStartupSc = ModifyStartupFile(projectDirectory);
                    }
                }

                // Report success/failure of the steps.
                var ngNewReport = (ngNewSucceeded.GetValueOrDefault()
                    ? "An Angular CLI application was added to the project using the item template version " + NgWizardHelper.GetVersion().ToString()
                    : "Something went wrong with the creation of an Angular CLI application." + LineBreak + "  Error message: " + ngNewOutput
                    ) + LineBreak;
                var gitignoreReport = mergedGitignoreFiles.HasValue
                   ? "Merging the .gitignore files: " + GetResultText(mergedGitignoreFiles.Value) + LineBreak
                   : String.Empty;
                var packageJsonReport = mergedPackageJsonFiles.HasValue
                   ? "Merging the package.json files: " + GetResultText(mergedPackageJsonFiles.Value) + LineBreak
                   : String.Empty;
                var startupCsReport = modifiedStartupSc.HasValue
                   ? "Modifying the Startup.cs file: " + GetResultText(modifiedStartupSc.Value) + LineBreak
                   : String.Empty;
                var messageText = ngNewReport + packageJsonReport + gitignoreReport + startupCsReport + LineBreak;

                // Augment the item file with our message.
                if (projectItem.FileCount != 0)
                {
                    var fileName = projectItem.FileNames[1]; // 1-based array
                    if (File.Exists(fileName))
                    {
                        var oldText = File.ReadAllText(fileName);
                        var newText = messageText + oldText;
                        File.WriteAllText(fileName, newText);
                    }
                }
            }
        }

        // This method is called after the project is created.  
        public void RunFinished()
        {
        }

        public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary,
        WizardRunKind runKind, object[] customParams)
        {
            replacementsDictionary.TryGetValue("$solutiondirectory$", out string solutionDirectory);

            // Test if @angular/cli is installed globally.
            bool isNgFound = NgWizardHelper.IsNgFound(solutionDirectory);

            bool isAngularCliJsonFound = false;
            bool isOldPackageJsonFound = false;
            bool isGitignoreOpened = false;
            bool isPackageJsonOpened = false;
            bool isStartupCsOpened = false;

            var dte = (DTE)automationObject;
            var activeProjects = (Array)dte.ActiveSolutionProjects;
            if (activeProjects.Length > 0)
            {
                var project = (Project)activeProjects.GetValue(0);
                // Look for an existing .angular-cli.json indicating there has been already an Angular CLI app created.
                isAngularCliJsonFound = NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.AngularCliJsonFileName);
                // Test if a package.json exists.
                isOldPackageJsonFound = NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.PackageJsonFileName, out string packageJsonFilePath);

                // If .gitignore or package.json or Startup.cs is opened in a editor window, automatic installation is disabled.
                if (NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.GitignoreFileName, out string gitignoreFilePath))
                {
                    isGitignoreOpened = NgWizardHelper.FindWindow(project, gitignoreFilePath);
                }
                if (isOldPackageJsonFound)
                {
                    isPackageJsonOpened = NgWizardHelper.FindWindow(project, packageJsonFilePath);
                }
                if (NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.StartupCsFileName, out string startupCsFilePath))
                {
                    isStartupCsOpened = NgWizardHelper.FindWindow(project, startupCsFilePath);
                }
            }

            // Display the wizard to the user.
            var viewModel = new NgItemWizardViewModel(isNgFound, isAngularCliJsonFound, isOldPackageJsonFound,
                isGitignoreOpened, isPackageJsonOpened, isStartupCsOpened);
            var mainWindow = new NgItemWizardWindow(viewModel);
            var accepted = mainWindow.ShowDialog().GetValueOrDefault();

            this.installAutomatically = viewModel.InstallAutomatically;

            if (!accepted)
            {
                throw new WizardCancelledException("The wizard has been cancelled by the user.");
            }
        }

        // This method is only called for item templates, not for project templates.  
        public bool ShouldAddProjectItem(string filePath)
        {
            return true;
        }

        /// <summary>
        /// Try to merge and return the operation result.
        /// </summary>
        /// <param name="projectDirectory"></param>
        /// <returns>null/false/true = not-applicable/failure/success</returns>
        private static bool? MergeGitignoreFile(string projectDirectory)
        {
            var gitignoreFilePath = Path.Combine(projectDirectory, NgWizardHelper.GitignoreFileName);
            var gitignoreTempFilePath = Path.Combine(projectDirectory, NgWizardHelper.GitignoreTempFileName);
            // Always delete the .gitignore created by Angular CLI because we asked to avoid creating it. 
            // See the comment at the top of ProjectItemFinishedGenerating()
            if (File.Exists(gitignoreFilePath))
            {
                File.Delete(gitignoreFilePath);
            }
            // We keep a .gitignore only in the case if an old one had existed before the template started.
            // If an old .gitignore has existed, extend it with Angular CLI entries and restore back.
            if (File.Exists(gitignoreTempFilePath))
            {
                var oldText = File.ReadAllText(gitignoreTempFilePath);
                var newText = oldText + GitignoreNg.GitignoreNgContent;
                File.WriteAllText(gitignoreFilePath, newText);
                File.Delete(gitignoreTempFilePath);
                return true;
            }
            else
            {
                return (bool?)null;
            }
        }

        /// <summary>
        /// Try to merge and return the operation result.
        /// </summary>
        /// <param name="projectDirectory"></param>
        /// <returns>null/false/true = not-applicable/failure/success</returns>
        private static bool? MergePackageJsonFiles(string projectDirectory)
        {
            const string ScriptsName = "scripts";
            const string DependenciesName = "dependencies";
            const string DevDependenciesName = "devDependencies";
            const string OldNameSuffix = "_old";

            //var result = false;

            var filePath = Path.Combine(projectDirectory, NgWizardHelper.PackageJsonFileName);
            var oldFilePath = Path.Combine(projectDirectory, NgWizardHelper.PackageJsonOldFileName);

            if (File.Exists(oldFilePath))
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }
            }
            else
            {
                return (bool?)null;
            }

            try
            {
                var oldObj = JObject.Parse(File.ReadAllText(oldFilePath));
                var newObj = JObject.Parse(File.ReadAllText(filePath));

                // We will give a higher priority in the three main sections to Ng, but keep the metadata properties from the old file.
                var resultObj = new JObject(oldObj);
                // The argument's content (i.e. newObj) wins over the "this" file (i.e. result).
                resultObj.Merge(newObj);

                // Clone the old content and delete the three main sections. Leave the metadata properties intact.
                var oObj = new JObject(oldObj);

                var propScr = oObj.Property(ScriptsName);
                if (propScr != null)
                {
                    propScr.Remove();
                }
                var propDep = oObj.Property(DependenciesName);
                if (propDep != null)
                {
                    propDep.Remove();
                }
                var propDev = oObj.Property(DevDependenciesName);
                if (propDev != null)
                {
                    propDev.Remove();
                }

                // Restore the old metadata properties.
                resultObj.Merge(oObj);

                // Add the three main sections from the old file for reference.
                var oScr = oldObj[ScriptsName];
                var oDep = oldObj[DependenciesName];
                var oDev = oldObj[DevDependenciesName];

                resultObj.Property(ScriptsName).AddAfterSelf(new JProperty(ScriptsName + OldNameSuffix, oScr ?? new JObject()));
                resultObj.Property(DependenciesName).AddAfterSelf(new JProperty(DependenciesName + OldNameSuffix, oDep ?? new JObject()));
                resultObj.Property(DevDependenciesName).AddAfterSelf(new JProperty(DevDependenciesName + OldNameSuffix, oDev ?? new JObject()));

                // Don't do: File.WriteAllText(filePath, result.ToString(), System.Text.Encoding.UTF8); // This writes a BOM. BOM causes Webpack to fail.
                var bytes = Encoding.UTF8.GetBytes(resultObj.ToString());
                using (var memoryStream = new MemoryStream(bytes))
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Truncate, FileAccess.Write))
                    {
                        memoryStream.CopyTo(fileStream);
                    }
                }

                return true;
            }
            catch (Exception)
            {
            }
            return false;
        }

        private string FindMatch(string text, string pattern)
        {
            string result = null;
            if (!String.IsNullOrWhiteSpace(text))
            {
                MatchCollection matches = Regex.Matches(text, pattern);
                // Force the regular expression engine to find all matches at once (otherwise it enumerates match-by-match lazily).
                var count = matches.Count;
                if (count > 0)
                {
                    var match = matches[count - 1]; // Last match
                    result = match.Groups[1].Value; // Groups[0] is the match itself.
                }
            }
            return result;
        }

        private bool ModifyStartupFile(string projectDirectory)
        {
            var filePath = Path.Combine(projectDirectory, NgWizardHelper.StartupCsFileName);
            if (!File.Exists(filePath))
            {
                return false;
            }

            var codeLines = File.ReadAllLines(filePath);
            var methodHeaderLine = codeLines
            .Where(i => i.Contains("public void") && i.Contains("Configure") && i.Contains("IApplicationBuilder") && i.Contains("IHostingEnvironment"))
            .FirstOrDefault()
            ;

            if (String.IsNullOrEmpty(methodHeaderLine))
            {
                return false;
            }

            var appPattern = @"(?:IApplicationBuilder)\s+(\w+(?=\)|,|\s))";
            var appVariableName = FindMatch(methodHeaderLine, appPattern);
            var envPattern = @"(?:IHostingEnvironment)\s+(\w+(?=\)|,|\s))";
            var envVariableName = FindMatch(methodHeaderLine, envPattern);

            if (String.IsNullOrEmpty(appVariableName) || String.IsNullOrEmpty(envVariableName))
            {
                return false;
            }

            var codeText = File.ReadAllText(filePath);

            var methodHeaderPos = codeText.IndexOf(methodHeaderLine);
            var methodBeginPos = codeText.IndexOf("{", methodHeaderPos);
            var ngServeSnippet = LineBreak +
                $"if ({envVariableName}.IsDevelopment())" + LineBreak +
                "{" + LineBreak +
                $"{appVariableName}.RunNgServe(\"--base-href /my-ng-app/\");" + LineBreak +
                "}" + LineBreak;
            codeText = codeText.Insert(methodBeginPos + 1, ngServeSnippet);

            var usingSnippet = "using AfominDotCom.AspNetCore.AngularCLI;" + LineBreak;
            codeText = codeText.Insert(0, usingSnippet);

            File.WriteAllText(filePath, codeText);

            return true;
        }

        private string GetResultText(bool success)
        {
            return success ? "DONE" : "FAILED";
        }

        private static void PreserveExistingFile(string projectDirectory, string originalFileName, string copyFileName)
        {
            var packageJsonFilePath = Path.Combine(projectDirectory, originalFileName);
            var packageJsonOldFilePath = Path.Combine(projectDirectory, copyFileName);
            if (File.Exists(packageJsonFilePath))
            {
                if (File.Exists(packageJsonOldFilePath))
                {
                    File.Delete(packageJsonOldFilePath);
                }
                File.Move(packageJsonFilePath, packageJsonOldFilePath);
            }
        }


    }
}
