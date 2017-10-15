using EnvDTE;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AfominDotCom.NgProjectTemplate.Wizards
{
    public static class NgWizardHelper
    {
        private const string NgVersionSuccessFragment = "@angular/cli";
        internal const string GitignoreFileName = ".gitignore";
        internal const string GitignoreTempFileName = ".gitignore.temp";
        internal const string PackageJsonFileName = "package.json";
        internal const string PackageJsonOldFileName = "package.json.old";
        internal const string AngularCliJsonFileName = ".angular-cli.json";
        internal const string StartupCsFileName = "Startup.cs";
        internal const string NgFoundLogFileName = "ErrorNgNotFound.txt";

        private static string RunCmd(string arguments, string workingDirectory, bool createNoWindow, bool redirectStandardOutput)
        {
            string output = null;
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
                RedirectStandardOutput = redirectStandardOutput,
            };
            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                if (redirectStandardOutput)
                {
                    output = process.StandardOutput.ReadToEnd();
                }
                process.WaitForExit();
            }
            return output;
        }

        private static string RunNgVersion(string workingDirectory)
        {
            var cmdArguments = "/c ng --version";
            return RunCmd(cmdArguments, workingDirectory, true, true);
        }

        internal static string RunNgNew(string workingDirectory, string projectName, bool addRouting, bool isNgFound)
        {
            // CMD writes errors to the StandardError stream. NG writes errors to StandardOutput. 
            // To read both the streams is possible but needs extra effots to avoid a thread deadlock.
            // If NG was not found, we display the Command Window to the user to watch the errors.
            var routingOption = addRouting ? " --routing" : "";
            var cmdArguments = $"/c ng new {projectName} --directory .{routingOption} --skip-git --skip-install"
                + (isNgFound ? "" : " & timeout /t 10");
            return RunCmd(cmdArguments, workingDirectory, isNgFound, isNgFound);
        }

        /// <summary>
        ///  Test if @angular/cli is installed globally.
        /// </summary>
        /// <param name="preferredDirectory">Prefererred directory</param>
        /// <returns></returns>
        internal static bool IsNgFound(string preferredDirectory)
        {
            // Be optimistic. Missing target is better than false alarm. We will check the result of "ng new" anyway.
            var isNgFound = true;
            var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var workingDirectory = Directory.Exists(preferredDirectory)
                ? preferredDirectory
                : (Directory.Exists(desktopDirectory) ? desktopDirectory : null);
            string ngVersionOutput = String.Empty;
            var start = DateTime.Now;
            if (!String.IsNullOrEmpty(workingDirectory))
            {
                ngVersionOutput = RunNgVersion(workingDirectory);
                // Old versions of CLI ~1.1 (actually chalk / supports-color) on Windows 7 fail when the output stream is redirected. ngVersionOutput is null/empty in that case.
                isNgFound = !String.IsNullOrEmpty(ngVersionOutput) && ngVersionOutput.Contains(NgVersionSuccessFragment);
            }
            return isNgFound;
        }

        public static ProjectItem FindProjectItem(Project project, string fileName)
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

        internal static bool FindFileInRootDir(Project project, string fileName)
        {
            return FindFileInRootDir(project, fileName, out string filePath);
        }

        internal static bool FindFileInRootDir(Project project, string fileName, out string filePath)
        {
            filePath = null;
            if (project != null)
            {
                var projectDirectory = Path.GetDirectoryName(project.FullName);
                if (Directory.Exists(projectDirectory))
                {
                    var path = Path.Combine(projectDirectory, fileName);
                    if (File.Exists(path))
                    {
                        filePath = path;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static Version GetVersion()
        {
            return typeof(AfominDotCom.NgProjectTemplate.Wizards.NgWizardHelper).Assembly.GetName().Version;
        }

        internal static bool FindWindow(Project project, string filePath)
        {
            return FindWindow(project, filePath, out EnvDTE.Window window);
        }

        internal static bool FindWindow(Project project, string filePath, out EnvDTE.Window window)
        {
            window = null;
            var windows = project.DTE.Windows;
            foreach (var w in windows)
            {
                if (w is EnvDTE.Window wnd)
                {
                    var projectItem = wnd.ProjectItem;
                    if ((projectItem != null) && (projectItem.FileCount != 0) /* && window.Type == vsWindowType.vsWindowTypeDocument */)
                    {
                        var fileName = projectItem.FileNames[1]; // 1-based array
                        if (fileName == filePath)
                        {
                            window = wnd;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        //internal static bool FindAndCloseWindow(Project project, string filePath, vsSaveChanges saveChanges)
        //{
        //    if (FindWindow(project, filePath, out Window window))
        //    {
        //        window.Close(saveChanges);
        //        return true;
        //    }
        //    return false;
        //}

        internal static void RewriteFile(string filePath, string fileContents)
        {
            // Don't do: File.WriteAllText(filePath, result.ToString(), System.Text.Encoding.UTF8); // That writes a BOM. BOM causes Webpack to fail.
            var bytes = Encoding.UTF8.GetBytes(fileContents);
            using (var memoryStream = new MemoryStream(bytes))
            {
                using (var fileStream = new FileStream(filePath, FileMode.Truncate, FileAccess.Write))
                {
                    memoryStream.CopyTo(fileStream);
                }
            }
        }

        internal static bool IsCoreVersion1(Project project)
        {
            var filePath = project?.FullName;
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                return text.Contains("<TargetFramework>netcoreapp1.");
            }
            return false;
        }

    }
}
