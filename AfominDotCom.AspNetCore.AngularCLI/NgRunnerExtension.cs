using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    public static class NgRunnerExtension
    {
        // TODO Strip BOM of package.json.

        private const string DefaultNgServerHost = "localhost";
        private const int DefaultNgServerPort = 4200;
        private static string DefaultNgServerBaseHref = NgProxyMiddleware.ForwardSlash;
        private const string HostOptionPattern = @"(?:--host|-H)\s+(\S+)\s*";
        // Our port pattern does not capture negative values. If the value is negative, the "ng serve" displays an error message.
        private const string PortOptionPattern = @"--port\s+(\w+)\s*";
        private const string BaseHrefOptionPattern = @"(?:--base-href|-bh)\s+(\S+)\s*";
        private const string AngularCliJsonFileName = ".angular-cli.json";
        private const string PackageJsonFileName = "package.json";
        private const string PackageJsonVersionPattern = "(?<=\".?)((?:\\d|\\.)+)(?=\")";

        public static void RunNgServe(this IApplicationBuilder app)
        {            
            RunNgServe(app, ""); // Use defaults.
        }

        public static void RunNgServe(this IApplicationBuilder app, string ngServeOptions)
        {
            RunNgServe(app, new[] { ngServeOptions });
        }

        /// <summary>
        /// Runs one or more Ng apps simultaneously.
        /// </summary>
        /// <param name="app">IApplicationBuilder</param>
        /// <param name="optionsList">This is a list of option strings for separate apps. Each string is a set of options for a particular app. This is NOT a list of separate options for a same single app.</param>
        public static void RunNgServe(this IApplicationBuilder app, IEnumerable<string> optionsList)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            if (optionsList == null)
            {
                throw new ArgumentNullException(nameof(optionsList));
            }

            ValidateOptions(optionsList);

            var pathPrefixToOptionsMap = new Dictionary<string, ProxyOptions>();

            // Parse the options strings.
            foreach (var options in optionsList)
            {
                var pathPrefix = GetNgServerBaseHref(options);
                var scheme = GetNgServerScheme(options);
                var host = GetNgServerHost(options);
                var port = GetNgServerPort(options);

                // An NG Develpment Server might be started manually from the Command Prompt. Check if that is the case.
                if (!NgProxyMiddleware.IsPortAvailable(port))
                {
                    throw new Exception($"Port {port} is already in use.");
                }

                // Path.StartsWithSegments() expects pathPrefix without a trailing slash. Angular needs a trailing slash in base-href.
                if ((pathPrefix.Length > 1) && pathPrefix.EndsWith(NgProxyMiddleware.ForwardSlash))
                {
                    pathPrefix = pathPrefix.Substring(0, pathPrefix.Length - 1);
                }

                var proxyOptions = new ProxyOptions
                {
                    Scheme = scheme,
                    Host = host,
                    Port = port.ToString(),
                };
                pathPrefixToOptionsMap.Add(pathPrefix, proxyOptions);
            }

            // For ASP.NET applications the working directory is the project root.
            var currentDirectory = Directory.GetCurrentDirectory();
            // Ensure .angular-cli.json is located in the root folder. We set process.StartInfo.WorkingDirectory to that directory.
            // If the CLI project is created not in the project root folder, the setup becomes overcomplicated. VS gets frozen while reading a non-root node_modules to include all files.
            EnsureAngularCliJsonFilePresent(currentDirectory);

            // Visual Studio writes Byte Order Mark when saves files. 
            // Webpack fails reading such a package.json. +https://github.com/webpack/enhanced-resolve/issues/87
            // Athough the docs claim that VS is aware of the special case of package.json, better safe than sorry.            
            EnsurePackageJsonFileHasNoBom(currentDirectory);

            // Webpack in Angular CLI 1.1 didn't recognize path, returned 404. It served from the root.
            // Starting from Angular CLI 1.4 (version???) the path in "base-href" is respected.
            var ngVersion = GetNgVersion(currentDirectory);
            NgProxyMiddleware.ProxyToPathRoot = (ngVersion != null) && (ngVersion < new Version(1, 4));

            // Start an Ng server for each app in the list. 
            foreach (var options in optionsList)
            {
                var process = new Process();
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = "/k start ng.cmd serve " + options;
                process.StartInfo.WorkingDirectory = currentDirectory;
                process.Start();
            }

            // Add our proxy middleware to the request processing chain.
            app.UseMiddleware<NgProxyMiddleware>(Options.Create(pathPrefixToOptionsMap));
        }

        private static string GetNgServerOption(string ngServeOptions, string pattern)
        {
            string result = null;
            if (!String.IsNullOrWhiteSpace(ngServeOptions))
            {
                MatchCollection matches = Regex.Matches(ngServeOptions, pattern, RegexOptions.IgnoreCase);
                // Force the regular expression engine to find all matches at once (otherwise it enumerates match-by-match lazily).
                var count = matches.Count;
                if (count > 0)
                {
                    var match = matches[count - 1]; // When "ng serve" runs, the last occurence of "--foo bar" wins even if it is invalid.
                    result = match.Groups[1].Value; // Groups[0] is the match itself.
                }
            }
            return result;
        }

        /// <summary>
        /// Searches for the SSL option in NgServeOptions and returns "http" or "https" depending on the presense of the option.
        /// </summary>
        /// <param name="ngServeOptions"></param>
        /// <returns>String "http" or "https"</returns>
        private static string GetNgServerScheme(string ngServeOptions)
        {
            var isSSL = !String.IsNullOrWhiteSpace(ngServeOptions) && (ngServeOptions.IndexOf("--ssl") >= 0);
            return isSSL ? "https" : "http";
        }

        private static string GetNgServerHost(string ngServeOptions)
        {
            var value = GetNgServerOption(ngServeOptions, HostOptionPattern);
            return value ?? DefaultNgServerHost;
        }

        /// <summary>
        /// Parses NgServeOptions, if present, and looks for a custom port value. If it is not found, returns the DefaultNgServerPort value.
        /// </summary>
        /// <param name="ngServeOptions"></param>
        /// <returns>The custom port value, if found. Otherwise, returns the DefaultNgServerPort value</returns>
        private static int GetNgServerPort(string ngServeOptions)
        {
            var value = GetNgServerOption(ngServeOptions, PortOptionPattern);
            if (Int32.TryParse(value, out int ngServerPort))
            {
                return ngServerPort;
            }
            // If the value is not valid, the "ng serve" falls back to the default value.
            return DefaultNgServerPort;
        }

        private static string GetNgServerBaseHref(string ngServeOptions)
        {
            var value = GetNgServerOption(ngServeOptions, BaseHrefOptionPattern);
            return value ?? DefaultNgServerBaseHref;
        }

        private static void ValidateOptions(IEnumerable<string> optionsList)
        {
            var items = optionsList.Select(i => new
            {
                Port = GetNgServerPort(i),
                BaseHref = GetNgServerBaseHref(i),
            })
            .ToList();

            if (items.Select(i => i.Port).Distinct().Count() != items.Count())
            {
                throw new ArgumentException("Duplicate 'port' values.");
            }

            if (items.Select(i => i.BaseHref).Distinct().Count() != items.Count())
            {
                throw new ArgumentException("Duplicate 'base-href' values.");
            }

            // If the value is 0, "ng serve" tries to start on 4200, 4201, 4202, 4203, and so on, until it finds an available port.
            if (items.Any(i => i.Port <= 0))
            {
                throw new ArgumentException("The port value must greater than zero.");
            }

            // This condition is important. Otherwise Path.StartsWithSegments(pathPrefix) throws.
            if (items.Any(i => !i.BaseHref.StartsWith(NgProxyMiddleware.ForwardSlash)))
            {
                throw new ArgumentException("The path in 'base-href' must have a leading '/'.");
            }

            // If there is no trailing slash, Angular does not prepend the script names with the base-href segment
            // when in HTTP requests to load scripts. So we cannot recognise the segment.
            if (items.Any(i => !i.BaseHref.EndsWith(NgProxyMiddleware.ForwardSlash)))
            {
                throw new ArgumentException("The path in 'base-href' must have a trailing '/'.");
            }

            // Avoid a "//" in base-href. The scripts are loaded, but WebSocket is not called.
            if (items.Any(i => i.BaseHref.Length == 2))
            {
                throw new ArgumentException("A path segment 'base-href' must be non-empty.");
            }
        }

        /// <summary>
        /// If the CLI project is created not in the project root folder, the setup gets over complicated.
        /// </summary>
        private static void EnsureAngularCliJsonFilePresent(string currentDirectory)
        {
            var angularCliJsonFilePath = Path.Combine(currentDirectory, AngularCliJsonFileName);
            if (!File.Exists(angularCliJsonFilePath))
            {
                throw new Exception("The .angular-cli.json file was not found in the project's root folder.");
            }
        }

        /// <summary>
        /// Find the package.json file and make sure it has no BOM
        /// </summary>
        private static void EnsurePackageJsonFileHasNoBom(string currentDirectory)
        {
            var packageJsonFilePath = Path.Combine(currentDirectory, PackageJsonFileName);
            if (File.Exists(packageJsonFilePath))
            {
                EnsureFileHasNoBom(packageJsonFilePath);
            }
        }

        /// <summary>
        /// Reads the file, looks for a Byte Order Mark and if a BOM is found, writes the file back without a BOM.
        /// </summary>
        /// <param name="filePath"></param>
        private static void EnsureFileHasNoBom(string filePath)
        {
            var memoryStream = new MemoryStream();
            using (var fileReader = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                fileReader.CopyTo(memoryStream);
            }
            var fileContent = memoryStream.ToArray();
            var utf8Preamble = Encoding.UTF8.GetPreamble();
            if (fileContent.Length > utf8Preamble.Length)
            {
                var hasBom = true;
                for (int i = 0; i < utf8Preamble.Length; ++i)
                {
                    if (fileContent[i] != utf8Preamble[i])
                    {
                        hasBom = false;
                    }
                }
                if (hasBom)
                {
                    memoryStream.Position = utf8Preamble.Length;
                    using (var fileWriter = new FileStream(filePath, FileMode.Truncate, FileAccess.Write))
                    {
                        memoryStream.CopyTo(fileWriter);
                    }
                }
            }
        }

        private static Version GetNgVersion(string currentDirectory)
        {
            var packageJsonFilePath = Path.Combine(currentDirectory, PackageJsonFileName);
            if (File.Exists(packageJsonFilePath))
            {
                var lines = File.ReadAllLines(packageJsonFilePath);
                var line = lines
                  .Where(i => i.Contains("@angular/cli"))
                  // Although the JSON standard demands double quotes, we defence ourselves here.
                  .Select(i => i.Replace("'", "\""))
                  .Where(i => i.Contains("\"@angular/cli\""))
                  .FirstOrDefault()
                  ;
                var match = Regex.Match(line, PackageJsonVersionPattern);
                if (match.Success)
                {
                    var value = match.Value;
                    return new Version(value);
                }
            }
            return null;
        }

    }
}
