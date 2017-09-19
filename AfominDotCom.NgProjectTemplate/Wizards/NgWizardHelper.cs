using EnvDTE;
using System;
using System.Diagnostics;
using System.IO;

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

        private static string RunNgVersion(string workingDirectory)
        {
            var cmdArguments = "/c ng --version";
            return RunCmdSync(cmdArguments, workingDirectory);
        }

        internal static string RunNgNew(string workingDirectory, string projectName, bool addRouting = true)
        {
            var routingOption = addRouting ? " --routing" : String.Empty;
            var cmdArguments = $"/c ng new {projectName} --directory .{routingOption} --skip-git --skip-install";
            return RunCmdSync(cmdArguments, workingDirectory);
        }

        /// <summary>
        ///  Test if @angular/cli is installed globally.
        /// </summary>
        /// <param name="preferredDirectory">Prefererred directory</param>
        /// <returns></returns>
        internal static bool IsNgFound(string preferredDirectory)
        {
            var isNgFound = true;
            var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var workingDirectory = Directory.Exists(preferredDirectory)
                ? preferredDirectory
                : (Directory.Exists(desktopDirectory) ? desktopDirectory : null);
            if (!String.IsNullOrEmpty(workingDirectory))
            {
                var ngVersionOutput = RunNgVersion(workingDirectory);
                isNgFound = ngVersionOutput.Contains(NgVersionSuccessFragment);
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
            return FindWindow(project, filePath, out Window window);
        }

        internal static bool FindWindow(Project project, string filePath, out Window window)
        {
            window = null;
            var windows = project.DTE.Windows;
            foreach (var w in windows)
            {
                if (w is Window wnd)
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

    }
}
