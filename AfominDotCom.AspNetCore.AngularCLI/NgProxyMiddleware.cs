using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    public class NgProxyMiddleware
    {
        private const string DefaultNgServerHost = "localhost";
        private const int DefaultNgServerPort = 4200;
        private static string DefaultNgServerBaseHref = "/";

        // TODO AF20170930. Adapt RegEx patterns for the other options syntax, --app app1 vs --app=app1.
        private const string HostOptionPattern = @"(?:--host|-H)\s+(\S+)\s*";
        // Our port pattern does not capture negative values. If the value is negative, the "ng serve" displays an error message.
        private const string PortOptionPattern = @"--port\s+(\w+)\s*";
        private const string BaseHrefKeyPattern = @"(--base-href|-bh)";
        private const string BaseHrefOptionPattern = @"(?:--base-href|-bh)\s+(\S+)\s*";
        // private const string PackageJsonVersionPattern = "(?<=\".?)((?:\\d|\\.)+)(?=\")"; // This dosn't support SemVer with a '-beta' ending.
        private const string SemVerShortVersionPattern = @"(?:\D*)(\d+\.\d+)(?:.*)"; // Extacts first two numbers.

        private const string ErrorBaseHrefPassed = "Do not specify a \"--base-href\" path in \"ng serve\" options. Specify a \"baseHref\" in .angular-cli.json instead.";
        private const string ErrorAppCount = "The number of option strings in the list passed as a parameter exceeds the number of items in array \"apps\" in .angular-cli.json .";
        private const string ErrorPortUnavailable = "Port {0} is already in use.";
        // Options validation
        private const string ErrorDuplicatePort = "Duplicate 'port' values are not allowed.";
        private const string ErrorDuplicateBaseHref = "Duplicate \"baseHref\" values are not allowed.";
        private const string ErrorPortZero = "The 'port' value must be greater than zero.";
        private const string ErrorMissingLeadingSlash = "The path in \"baseHref\" must have a leading slash.";
        private const string ErrorMissingTrailingSlash = "The path in \"baseHref\" must have a trailing slash.";
        private const string ErrorDuplicateSlash = "Duplicate slashes in \"baseHref\" are not allowed.";


        private readonly RequestDelegate next;
        private readonly Dictionary<string, NgProxy> pathPrefixToProxyMap;
        private readonly bool proxyToPathRoot;

        public NgProxyMiddleware(RequestDelegate next, IHostingEnvironment hostingEnv, IOptions<List<string>> options)
        {
            // Store the next middleware in the request processing chain.
            this.next = next ?? throw new ArgumentNullException(nameof(next));

            if (hostingEnv == null)
            {
                throw new ArgumentNullException(nameof(hostingEnv));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var contentRootPath = hostingEnv.ContentRootPath;

            // Visual Studio writes Byte Order Mark when saves files. 
            // Webpack fails reading such a package.json. +https://github.com/webpack/enhanced-resolve/issues/87
            // Athough the docs claim that VS is aware of the special case of package.json, better safe than sorry.            
            EnsurePackageJsonFileHasNoBom(contentRootPath);

            // Webpack in Angular CLI 1.1 didn't recognize a path, returned 404. It served scripts from the root.
            // Starting from Angular CLI 1.4 the path in "base-href" is respected.
            var ngVersion = GetNgVersion(contentRootPath);
            this.proxyToPathRoot = (ngVersion != null) && (ngVersion < new Version(1, 4));

            /* We read baseHref values from .angular-cli.json that must be located in the root folder.
             * We set Process.StartInfo.WorkingDirectory to that directory.
             * If the CLI project is created not in the project root folder, the setup becomes overcomplicated.
             * VS gets frozen while reading a non-root node_modules to include all files.
             */
            var passedOptionsList = options.Value;
            PreventBaseHrefInPassedOptions(passedOptionsList);
            var optionsList = AppendBaseHrefToOptions(contentRootPath, passedOptionsList);
            ValidateOptions(optionsList);
            var pathPrefixToProxyOptionsMap = ParseOptions(optionsList);

            // An NG Develpment Server might be started manually from the Command Prompt. Make sure that is not the case.
            CheckPortAvailability(pathPrefixToProxyOptionsMap);
            // Start an Ng server for each app in the list. 
            StartNgServeProcesses(contentRootPath, optionsList);

            // Create an NgProxy for each Ng app. Do not create the proxy until the Ng server is available.
            this.pathPrefixToProxyMap = CreateProxiesAfterNgStarted(pathPrefixToProxyOptionsMap);
        }


        public async Task Invoke(HttpContext context)
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                var pathPrefix = pathPrefixToProxyMap
                .Select(i => i.Key)
                // If baseHref is "/", that means catch all.
                .FirstOrDefault(i => (i == "/") || context.Request.Path.StartsWithSegments(i));

                if (pathPrefix != null)
                {
                    var proxy = pathPrefixToProxyMap[pathPrefix];
                    // Wait for at least two minutes until the Ng server has started.
                    var counter = 240;
                    while ((proxy == null) && (counter > 0))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500));
                        proxy = pathPrefixToProxyMap[pathPrefix];
                        counter--;
                    }

                    if (proxy != null)
                    {
                        // Call the Ng server
                        // Webpack in Angular CLI 1.1 didn't recognize path. It served from the root.
                        // Starting from Angular CLI 1.4 the path in "base-href" is respected.
                        if (this.proxyToPathRoot)
                        {
                            await CallProxyToPathRoot(context, proxy);
                        }
                        else
                        {
                            await proxy.HandleHttpRequest(context);
                        }

                        if (context.Response.StatusCode != (int)HttpStatusCode.NotFound)
                        {
                            return;
                        }
                    }
                }

                // Not an Ng request.
                await this.next.Invoke(context);
            }
        }

        /// <summary>
        /// Ignore the request Path for Angular CLI versions prior to 1.4.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="proxy"></param>
        /// <returns></returns>
        private static async Task CallProxyToPathRoot(HttpContext context, NgProxy proxy)
        {
            var requestPath = context.Request.Path;
            try
            {
                if (requestPath.HasValue)
                {
                    var lastSeparatorIndex = requestPath.Value.LastIndexOf('/');
                    // If the slash is at the start, keep the path as is.
                    if (lastSeparatorIndex > 0)
                    {
                        var proxyPath = requestPath.Value.Substring(lastSeparatorIndex);
                        context.Request.Path = new PathString(proxyPath);
                    }
                }
                // Call the Ng server
                await proxy.HandleHttpRequest(context);
            }
            finally
            {
                context.Request.Path = requestPath;
            }
        }

        private static Dictionary<string, NgProxy> CreateProxiesAfterNgStarted(Dictionary<string, NgProxyOptions> pathPrefixToProxyOptionsMap)
        {
            // Do not assign an NgProxy until the Ng server is available.
            var pathPrefixToProxyMap = pathPrefixToProxyOptionsMap.Keys.ToDictionary(i => i, i => (NgProxy)null);

            // Create a proxy for each Ng app
            foreach (var item in pathPrefixToProxyOptionsMap)
            {
                var pathPrefix = item.Key;
                var proxyOptions = item.Value;

                // Wait until the Ng server has started.
                Task.Factory.StartNew(async () =>
                {
                    do
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                    while (IsPortAvailable(proxyOptions.Port));

                    var proxy = new NgProxy(proxyOptions);
                    // From now on we can proxy requests.
                    pathPrefixToProxyMap[pathPrefix] = proxy;
                }
                );
            }

            return pathPrefixToProxyMap;
        }

        private static void StartNgServeProcesses(string contentRootPath, IEnumerable<string> optionsList)
        {
            foreach (var optionsLine in optionsList)
            {
                var process = new Process();
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = "/k start ng.cmd serve " + optionsLine;
                process.StartInfo.WorkingDirectory = contentRootPath;
                process.Start();
            }
        }

        private static Dictionary<string, NgProxyOptions> ParseOptions(IEnumerable<string> optionsList)
        {
            var pathPrefixToProxyOptionsMap = new Dictionary<string, NgProxyOptions>();

            // Parse the options strings.
            foreach (var optionsLine in optionsList)
            {
                var pathPrefix = GetNgServerBaseHref(optionsLine);
                // Path.StartsWithSegments() needs a pathPrefix without a trailing slash. Angular needs a trailing slash in baseHref.
                if (pathPrefix != "/")
                {
                    pathPrefix = pathPrefix.TrimEnd('/');
                }

                var proxyOptions = new NgProxyOptions
                {
                    Scheme = GetNgServerScheme(optionsLine),
                    Host = GetNgServerHost(optionsLine),
                    Port = GetNgServerPort(optionsLine),
                };

                pathPrefixToProxyOptionsMap.Add(pathPrefix, proxyOptions);
            }

            return pathPrefixToProxyOptionsMap;
        }

        private static void CheckPortAvailability(Dictionary<string, NgProxyOptions> pathPrefixToProxyOptionsMap)
        {
            pathPrefixToProxyOptionsMap
              .Select(i => i.Value.Port)
              .ToList()
              .ForEach(i =>
              {
            // An NG Develpment Server might be started manually from the Command Prompt. Check if that is the case.
            if (!IsPortAvailable(i))
                  {
                      throw new Exception(String.Format(ErrorPortUnavailable, i));
                  }
              })
              ;
        }

        internal static bool IsPortAvailable(int port)
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c netstat.exe -a -n -p TCP | findstr LISTENING | findstr :{port}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.Start();
                var netstatOutput = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return String.IsNullOrWhiteSpace(netstatOutput);
            }
        }

        //--------------

        private static void PreventBaseHrefInPassedOptions(IEnumerable<string> optionsList)
        {
            var baseHrefPassed = optionsList
              .Select(i => GetNgServerOption(i, BaseHrefKeyPattern))
              .Any(i => !String.IsNullOrEmpty(i))
              ;
            if (baseHrefPassed)
            {
                throw new ArgumentException(ErrorBaseHrefPassed);
            }
        }

        private static IEnumerable<string> AppendBaseHrefToOptions(string currentDirectory, IEnumerable<string> optionsList)
        {
            var ngAppSettings = NgMiddlewareHelper.GetAllNgAppSettings(currentDirectory);

            // TODO AF20170929. Check the app names/numbers in optionsList against the app names in .angular-cli.json.
            if (optionsList.Count() > ngAppSettings.Count())
            {
                throw new ArgumentOutOfRangeException(ErrorAppCount);
            }
            // TODO AF20170929. We match apps by position here. Match the app in optionsList with apps in .angular-cli.json by name/number, i.e. --app=app0. See +https://github.com/angular/angular-cli/wiki/stories-multiple-apps       
            var newOptionsList = optionsList.Select((optionsLine, index) =>
            {
                var baseHref = ngAppSettings
                .Where(i => i.AppIndex == index)
                .Select(i => i.BaseHref)
                .FirstOrDefault()
                ;
                if (String.IsNullOrWhiteSpace(baseHref))
                {
                    throw new Exception(String.Format(NgMiddlewareHelper.ErrorNoBaseHrefInAngularCliJson, index));
                }

                return optionsLine + " --base-href " + baseHref; // ng tolerates extra whitespaces in command line.
            })
            .ToList();

            return newOptionsList;
        }

        private static string GetNgServerOption(string ngServeOptions, string pattern)
        {
            if (!String.IsNullOrWhiteSpace(ngServeOptions))
            {
                MatchCollection matches = Regex.Matches(ngServeOptions, pattern, RegexOptions.IgnoreCase);
                // Force the regular expression engine to find all matches at once (otherwise it enumerates match-by-match lazily).
                var count = matches.Count;
                if (count > 0)
                {
                    var match = matches[count - 1]; // When "ng serve" runs, the last occurence of "--foo bar" wins even if it is invalid.
                    return match.Groups[1].Value; // Groups[0] is the match itself.
                }
            }
            return null;
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
            return GetNgServerOption(ngServeOptions, HostOptionPattern) ?? DefaultNgServerHost;
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
            return GetNgServerOption(ngServeOptions, BaseHrefOptionPattern) ?? DefaultNgServerBaseHref;
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
                throw new ArgumentException(ErrorDuplicatePort);
            }

            if (items.Select(i => i.BaseHref).Distinct().Count() != items.Count())
            {
                throw new ArgumentException(ErrorDuplicateBaseHref);
            }

            // If the value is 0, "ng serve" tries to start on 4200, 4201, 4202, 4203, and so on, until it finds an available port.
            if (items.Any(i => i.Port <= 0))
            {
                throw new ArgumentException(ErrorPortZero);
            }

            // This condition is important. Otherwise Path.StartsWithSegments(pathPrefix) throws.
            if (items.Any(i => !i.BaseHref.StartsWith("/")))
            {
                throw new ArgumentException(ErrorMissingLeadingSlash);
            }

            // If there is no trailing slash, Angular does not prepend the script names with the base-href segment
            // when in HTTP requests to load scripts. So we cannot recognise the segment.
            if (items.Any(i => !i.BaseHref.EndsWith("/")))
            {
                throw new ArgumentException(ErrorMissingTrailingSlash);
            }

            // Avoid a "//" in base-href. The scripts are loaded, but WebSocket is not called.
            if (items.Any(i => i.BaseHref.Contains("//")))
            {
                throw new ArgumentException(ErrorDuplicateSlash);
            }
        }

        private static Version GetNgVersion(string currentDirectory)
        {
            var packageJsonFilePath = Path.Combine(currentDirectory, NgMiddlewareHelper.PackageJsonFileName);
            if (File.Exists(packageJsonFilePath))
            {
                /*
                var lines = File.ReadAllLines(packageJsonFilePath);
                var line = lines
                  .Where(i => i.Contains("@angular/cli"))
                  // Although the JSON standard demands double quotes, let's be paranoid.
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
                */
                var rootObj = JObject.Parse(File.ReadAllText(packageJsonFilePath));
                var devDependencies = (JObject)rootObj["devDependencies"];
                if (devDependencies != null)
                {
                    var semVer = (string)devDependencies["@angular/cli"];
                    var match = Regex.Match(semVer, SemVerShortVersionPattern);
                    if (match.Success)
                    {
                        var value = match.Groups[1].Value;
                        return new Version(value);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Find the package.json file and make sure it has no BOM
        /// </summary>
        private static void EnsurePackageJsonFileHasNoBom(string currentDirectory)
        {
            var packageJsonFilePath = Path.Combine(currentDirectory, NgMiddlewareHelper.PackageJsonFileName);
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


    }
}
