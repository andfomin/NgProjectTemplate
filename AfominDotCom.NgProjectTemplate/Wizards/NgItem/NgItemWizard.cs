using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AfominDotCom.NgProjectTemplate.Wizards
{
    public class NgItemWizard : IWizard
    {
        internal static string LineBreak = Environment.NewLine;
        private const string OpeningBrace = "{";
        private const string ClosingBrace = "}";

        private bool isNgFound;
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
                bool? mergedPackageJsonFiles = null;
                bool? modifiedAngularCliJson = null;
                bool? modifiedStartupSc = null;
                bool? mergedGitignoreFiles = null;
                bool renamedTsconfigJson = false;

                var projectDirectory = Path.GetDirectoryName(project.FullName);
                if (Directory.Exists(projectDirectory))
                {
                    // Starting from ver.1.4, the CLI creates a ".gitignore" file even if the "--ignore-git" option is specified. So "ng new --ignore-git" fails if there is an existing .gitignore in the directory.
                    // +https://github.com/angular/angular-cli/issues/7686
                    RenameFileIfExists(projectDirectory, NgWizardHelper.GitignoreFileName, NgWizardHelper.GitignoreTempFileName);

                    RenameFileIfExists(projectDirectory, NgWizardHelper.PackageJsonFileName, NgWizardHelper.PackageJsonOldFileName);
                    renamedTsconfigJson = RenameFileIfExists(projectDirectory, NgWizardHelper.TsconfigJsonFileName, NgWizardHelper.TsconfigJsonOldFileName);

                    // Run "ng new"
                    ngNewOutput = NgWizardHelper.RunNgNew(projectDirectory, project.Name, true, this.isNgFound);

                    // Find the .angular-cli.json created by "ng new".
                    ngNewSucceeded = NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.AngularCliJsonFileName);

                    if (ngNewSucceeded.Value)
                    {
                        mergedPackageJsonFiles = MergePackageJsonFiles(projectDirectory);
                        modifiedAngularCliJson = ModifyAngularCliJsonFile(projectDirectory);
                        modifiedStartupSc = ModifyStartupCsFile(projectDirectory);
                        mergedGitignoreFiles = MergeGitignoreFile(projectDirectory);
                    }
                    else
                    {
                        // Rallback renamed files.
                        RenameFileIfExists(projectDirectory, NgWizardHelper.GitignoreTempFileName, NgWizardHelper.GitignoreFileName);
                        RenameFileIfExists(projectDirectory, NgWizardHelper.PackageJsonOldFileName, NgWizardHelper.PackageJsonFileName);
                        renamedTsconfigJson = renamedTsconfigJson && !RenameFileIfExists(projectDirectory, NgWizardHelper.TsconfigJsonOldFileName, NgWizardHelper.TsconfigJsonFileName);
                    }
                }

                // Report success/failure of the steps.
                var ngNewReport = (ngNewSucceeded.GetValueOrDefault()
                    ? "An Angular CLI application was added to the project using the item template version " + NgWizardHelper.GetVersion().ToString()
                    : "Something went wrong with the creation of an Angular CLI application." +
                    (this.isNgFound ? LineBreak + "  Error message: " + ngNewOutput : "")
                    ) + LineBreak;
                var packageJsonReport = mergedPackageJsonFiles.HasValue
                   ? "Merging the package.json files: " + GetResultText(mergedPackageJsonFiles) + LineBreak
                   : String.Empty;
                var angularCliJsonReport = modifiedAngularCliJson.HasValue
                   ? "Modifying file .angular-cli.json: " + GetResultText(modifiedAngularCliJson) + LineBreak
                   : String.Empty;
                var startupCsReport = modifiedStartupSc.HasValue
                   ? "Modifying file Startup.cs: " + GetResultText(modifiedStartupSc) + LineBreak
                   : String.Empty;
                var gitignoreReport = mergedGitignoreFiles.HasValue
                   ? "Merging the .gitignore files: " + GetResultText(mergedGitignoreFiles) + LineBreak
                   : String.Empty;
                var tsconfigJsonReport = renamedTsconfigJson
                    ? "Renaming file tsconfig.json to tsconfig.json.old: " + GetResultText(renamedTsconfigJson) + LineBreak 
                    : String.Empty;

                var messageText = ngNewReport + packageJsonReport + angularCliJsonReport + startupCsReport + gitignoreReport 
                    + tsconfigJsonReport + LineBreak;

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

            var settings = new NgItemWizardViewModel.NgItemWizardSettings();

            // Test if @angular/cli is installed globally.
            this.isNgFound = NgWizardHelper.IsNgFound(solutionDirectory);
            settings.IsNgFound = this.isNgFound;

            var dte = (DTE)automationObject;
            var activeProjects = (Array)dte.ActiveSolutionProjects;
            if (activeProjects.Length > 0)
            {
                var project = (Project)activeProjects.GetValue(0);

                // The NuGet package needs netstandard2.0. We don't support ASP.NET Core 1.x projects.
                settings.IsAspNetCore2 = NgWizardHelper.IsAspNetCore2(project);
                // Look for an existing .angular-cli.json indicating there has been already an Angular CLI app created.
                settings.IsAngularCliJsonFound = NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.AngularCliJsonFileName);
                // Test if a package.json exists.
                settings.IsOldPackageJsonFound = NgWizardHelper.FindFileInRootDir(project, NgWizardHelper.PackageJsonFileName, out string packageJsonFilePath);
                // Test if an entry for "@angular/core" exists in package.json.
                settings.IsNpmAngularFound = NgWizardHelper.IsNpmAngularFound(packageJsonFilePath);

                // Automatic installation is disabled if .gitignore or package.json or tsconfig.json or Startup.cs is opened in an editor window.
                settings.IsGitignoreOpened = NgWizardHelper.IsFileOpened(project, NgWizardHelper.GitignoreFileName);
                settings.IsPackageJsonOpened = NgWizardHelper.IsFileOpened(project, NgWizardHelper.PackageJsonFileName);
                settings.IsStartupCsOpened = NgWizardHelper.IsFileOpened(project, NgWizardHelper.StartupCsFileName);
                settings.IsTsconfigJsonOpened = NgWizardHelper.IsFileOpened(project, NgWizardHelper.TsconfigJsonFileName);
            }

            // Display the wizard to the user.
            var viewModel = new NgItemWizardViewModel(settings);
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
                // There may be duplicate patterns in the new file after merge. Let it be.
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

                NgWizardHelper.RewriteFile(filePath, resultObj.ToString());
                return true;
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>
        /// Add a "baseHref" property in .angular-cli.json.
        /// </summary>
        /// <param name="projectDirectory"></param>
        /// <returns></returns>
        private bool ModifyAngularCliJsonFile(string projectDirectory)
        {
            const string BaseHrefPropertyName = "baseHref";
            const string BaseHrefApi = "/";
            const string BaseHrefMvc = "/ng/";

            var projectIsMvc = Directory.Exists(Path.Combine(projectDirectory, "Views"));
            var projectIsRazorPages = Directory.Exists(Path.Combine(projectDirectory, "Pages"));
            var baseHrefPropertyValue = (projectIsMvc || projectIsRazorPages) ? BaseHrefMvc : BaseHrefApi;

            var filePath = Path.Combine(projectDirectory, NgWizardHelper.AngularCliJsonFileName);
            if (File.Exists(filePath))
            {
                var rootObj = JObject.Parse(File.ReadAllText(filePath));
                var apps = (JArray)rootObj["apps"];
                if ((apps != null) && apps.Any())
                {
                    var app = (JObject)apps[0];
                    if (app[BaseHrefPropertyName] == null)
                    {
                        var knownProperty = app.Property("outDir") ?? app.Property("root");
                        if (knownProperty != null)
                        {
                            var baseHrefProperty = new JProperty(BaseHrefPropertyName, baseHrefPropertyValue);
                            knownProperty.AddAfterSelf(baseHrefProperty);

                            NgWizardHelper.RewriteFile(filePath, rootObj.ToString());
                            return true;
                        }
                    }
                }
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

        private bool ModifyStartupCsFile(string projectDirectory)
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

            var methodStartPos = codeText.IndexOf(methodHeaderLine);

            // MVC and WebAPI projects already have UseStaticFiles() inserted by their templates.
            var useStaticFilesPos = codeText.IndexOf($"{appVariableName}.UseStaticFiles", methodStartPos);
            var useFileServerPos = codeText.IndexOf($"{appVariableName}.UseFileServer", methodStartPos);
            var insertUseStaticFiles = (useStaticFilesPos == -1) && (useFileServerPos == -1);

            /* The position of the snippet affects the routing in the project. Middleware handlers are called in the order of registration.
             * We serve at "/" in Empty and WebAPI, at "/ng/" in MVC and Razor Pages.
             * WebAPI, MVC and Razor Pages have routing, we serve everything else (including 404 at dev time). 
             * We hijack processing in the Empty project (it has a hardcoded response in app.Run()). 
             */
            int insertPos = codeText.IndexOf($"{appVariableName}.Run(", methodStartPos);
            if (insertPos == -1)
            {
                insertPos = FindEndOfMethod(codeText, methodStartPos);
            }
            if (insertPos > 0)
            {
                insertPos = RewindWhitespaces(codeText, insertPos);

                var ngSnippet = LineBreak +
                    "#region /* Added by the Angular CLI template. --- BEGIN --- */" + LineBreak +
                    $"if ({envVariableName}.IsDevelopment())" + LineBreak +
                    "{" + LineBreak +
                    $"{appVariableName}.UseWebSockets().UseNgProxy();" + LineBreak +
                    "}" + LineBreak +
                    "else" + LineBreak +
                    "{" + LineBreak;
                if (insertUseStaticFiles)
                {
                    ngSnippet = ngSnippet +
                        $"{appVariableName}.UseStaticFiles();" + LineBreak;
                }
                ngSnippet = ngSnippet +
                    $"{appVariableName}.UseNgRoute();" + LineBreak +
                    "}" + LineBreak +
                    "#endregion /* Added by the Angular CLI template. --- END --- */" + LineBreak + LineBreak;

                codeText = codeText.Insert(insertPos, ngSnippet);

                File.WriteAllText(filePath, codeText);

                return true;
            }

            return false;
        }

        private static int RewindWhitespaces(string text, int initialPos)
        {
            int pos = initialPos;
            while ((pos > 0) && (text[--pos] == ' '))
            {
            }
            if (pos > 0)
            {
                pos++;
            }
            return pos;
        }

        private string GetResultText(bool? success)
        {
            return success.HasValue ? (success.Value ? "DONE" : "FAILED") : "NOT DONE";
        }

        private static bool RenameFileIfExists(string projectDirectory, string sourceFileName, string destFileName)
        {
            var sourceFilePath = Path.Combine(projectDirectory, sourceFileName);
            var destFilePath = Path.Combine(projectDirectory, destFileName);
            if (File.Exists(sourceFilePath))
            {
                if (File.Exists(destFilePath))
                {
                    File.Delete(destFilePath);
                }
                File.Move(sourceFilePath, destFilePath);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Match opening and closing curly braces. Find the position of the closing brace corresponding to the first opening brace.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="startPos">Where to start looking for a first opening brace.</param>
        /// <returns>May be -1 if failed to find a matching closing brace up to the end of the text</returns>
        private int FindEndOfMethod(string text, int startPos)
        {
            var stack = new Stack<int>();

            var firstOpening = text.IndexOf(OpeningBrace, startPos);
            stack.Push(firstOpening);
            var current = firstOpening;

            while (stack.Count > 0)
            {
                var nextOpening = text.IndexOf(OpeningBrace, current + 1);
                var nextClosing = text.IndexOf(ClosingBrace, current + 1);
                if ((nextOpening > 0) && (nextOpening < nextClosing))
                {
                    stack.Push(nextOpening);
                    current = nextOpening;
                }
                else
                {
                    stack.Pop();
                    current = nextClosing;
                }
                var stackCount = stack.Count();
            }

            return current;
        }


    }
}
