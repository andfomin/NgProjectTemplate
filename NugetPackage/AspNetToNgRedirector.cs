using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AfominDotCom.NgProjectTemplate.Server
{

    public class AspNetToNgRedirector
    {

        #region Constants

        /// <summary>
        /// The NgServerPort setting is depricated
        /// </summary>
        private const string NgServerPortSettingName = "NgServerPort";

        private const string NgServeOptionsSettingName = "NgServeOptions";
        private const string packageJsonFileName = "package.json";
        private const int DefaultNgServerPort = 4200;
        private const string RedirectionPageUrl = "/redirect-to-ng-server";

        private const string StyleSection = @"
<style>
  body {
    margin: 25px;
    font: 16px calibri,'segoe ui'
  }
  span.my-red {
    color: red;
  }
  code.my-code {
    font-family: monospace;
    font-size: 85%;
    padding: 0.1em;
    border-radius: 3px;
    background-color: #f1f1f1;
  }
</style>
";

        private const string StartPage = @"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8' />
  <title>Waiting for NG Development Server</title>
</head>
<body>
{{StyleSection}}
{{DebuggerWarning}}
  <div style='margin-top: 50px;'>
    <h3><i>The NG Development Server is starting...</i></h3>
    <progress></progress>
  </div>

  <script>
    const ngUrl = 'http://localhost:{{NgServerPort}}/';

    function onTimeout(xhr) {
      return function () {
        xhr.abort();
        doXHR();
      }
    }

    function doXHR() {
      var xhr = new XMLHttpRequest();
      var timeout = setTimeout(onTimeout(xhr), 2000);
      xhr.open('GET', ngUrl, true);
      xhr.onreadystatechange = function () {
        if (xhr.readyState == 4 && xhr.status == 200) {
          clearTimeout(timeout);
          location.href = '{{RedirectionPageUrl}}';
        }
      }
      xhr.send();
    }

    document.body.onload = doXHR;
  </script>
</body>
</html>
";

        private const string DebuggerWarning = @"
<div>
<h3><span class='my-red'>Note:</span> The Angular CLI Project doesn't support JavaScript Debugging in Visual Studio</h3>
<h3><span class='my-red'>If</span> JavaScript Debugging in Visual Studio is enabled</h3>
<ul>
<li>Opening Developer Tools in Chrome stops the script debugging session</li>
<li>The Hot Module Replacement feature breaks code mapping</li>
<li>If you close the browser window manually, then stopping the debugger in Visual Studio will take longer than usual</li>
</ul>
<p>
You can ignore this note and continue using this browser window.
</p>
<p>
As another option, you can start your project without debugging by pressing Ctrl+F5 in Visual Studio and
<br/>
then open Developer Tools by pressing F12 in the browser.
</p>
<p>
Alternatively, you can disable JavaScript debugging in Visual Studio by going to <strong><i>Tools -> Options -> Debugging -> General</i></strong> and
<br/>
turning off the setting <strong><i>Enable JavaScript Debugging for ASP.NET (Chrome and IE)</i></strong>.
</p>
<p>
<a href='https://aka.ms/chromedebugging' target='_blank'>Learn more about JavaScript debugging in Visual Studio</a>
</p>
</div>
";

        private const string PortUnavailableErrorPage = @"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8' />
  <title>Port Unavailable</title>
</head>
<body>
{{StyleSection}}
<p>
<span class='my-red'>ERROR.</span> Port <strong>{{NgServerPort}}</strong> is already in use. 
<br/>
To specify a different port, open the project's Properties page and select the Debug tab. 
<br/>
Add an Environment Variable named <strong>ASPNETCORE_NgServeOptions</strong> and enter <strong><code class='my-code'>--port Number</code></strong> (for example <code class='my-code'>--port 4201</code>) as its Value.
<br/>
<a href='https://github.com/angular/angular-cli/wiki/serve' target='_blank'>Learn more about the options available in &quot;ng serve&quot;.</a>
</p>
</body>
</html>
";

        private const string RedirectionPage = @"<!DOCTYPE html>
<html>
<head>
  <meta http-equiv='refresh' content='0; url=http://localhost:{{NgServerPort}}/'/>
</head>
<body>
</body>
</html>
";

        #endregion

        public static void Main(string[] args)
        {
            /* Visual Studio configures IIS Express to host an Kestrel server.
             * When we start debugging, Visual Studio starts the IIS Express which in turn starts the Kestrel server, then launches a browser and points it to the IIS Express. 
             * We serve a page from ASP.NET Core that redirects the browser from the IIS Express to the NG Development Server.
             */

            // Visual Studio writes Byte Order Mark when saves files. 
            // Webpack fails reading such a package.json. +https://github.com/webpack/enhanced-resolve/issues/87
            // Athough the docs claim that VS is aware of the special case of package.json, 
            // apparently VS fails to recognize the file when the template wizard saves it during the project creation.
            EnsurePackageJsonFileHasNoBom();

            var webHostBuilder = new WebHostBuilder();

            string ngServeOptions = GetNgServeOptions(webHostBuilder);

            // Run "ng serve". For ASP.NET applications the working directory is the project root.
            var ngProcess = Process.Start("cmd.exe", "/k start ng.cmd serve"
              + (!String.IsNullOrWhiteSpace(ngServeOptions) ? " " + ngServeOptions : String.Empty));

            var ngServerPort = GetNgServerPort(ngServeOptions);
            // An NG Develpment Server may have already been started manually from the Command Prompt. Check if that is the case.
            bool isNgServerPortAvailable = IsNgServerPortAvailable(ngServerPort);

            var startPage = (isNgServerPortAvailable
              ? (StartPage
              .Replace("{{RedirectionPageUrl}}", RedirectionPageUrl)
              .Replace("{{DebuggerWarning}}", (Debugger.IsAttached ? DebuggerWarning : String.Empty))
              )
              // Inform the developer how to specify another port.
              : PortUnavailableErrorPage
              )
              .Replace("{{NgServerPort}}", ngServerPort.ToString())
              .Replace("{{StyleSection}}", StyleSection)
              ;

            var redirectionPage = RedirectionPage
              .Replace("{{NgServerPort}}", ngServerPort.ToString());

            // We use a CancellationToken for shutting down the Kestrel server after the redirection page has been sent to the browser.
            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            var webHost = webHostBuilder
              .UseKestrel()
              .UseIISIntegration()
              .Configure(app => app.Run(async context =>
              {
                  switch (context.Request.Path.Value)
                  {
                      case "/":
                          await context.Response.WriteAsync(startPage);
                          break;
                      case RedirectionPageUrl:
                          await context.Response.WriteAsync(redirectionPage);
                          cancellationTokenSource.Cancel();
                          break;
                      default:
                          context.Response.StatusCode = StatusCodes.Status404NotFound;
                          break;
                  }

              }))
              .Build()
              ;

            // When the profile is "IIS Express" this setting is present. Sometimes we face "{{AppName}}" as the active profile, which doesn't have this setting (its value returns "production"? default?). That "{{AppName}}" profile doesn't open a web browser, so we cannot redirect anyway.
            var environmentSetting = webHostBuilder.GetSetting("environment");
            var isIISExpressEnvironment = !String.IsNullOrEmpty(environmentSetting) && (environmentSetting.ToLower() == "development");
            if (isIISExpressEnvironment)
            {
                webHost.Run(cancellationToken);
            }

            if (ngProcess != null)
            {
                ngProcess.WaitForExit();
                ngProcess.Dispose();
            }
        }

        /// <summary>
        /// Read the custom "ng serve" options from the Environment Variable, if present.
        /// </summary>
        /// <param name="webHostBuilder"></param>
        /// <returns>The options string. May return null if the Environment Variable was not found.</returns>
        private static string GetNgServeOptions(WebHostBuilder webHostBuilder)
        {
            var ngServeOptions = webHostBuilder.GetSetting(NgServeOptionsSettingName);
            if (!String.IsNullOrWhiteSpace(ngServeOptions))
            {
                ngServeOptions = ngServeOptions.Trim();
            }
            else
            {
                // The NgServerPort setting is depricated.
                var ngServerPortSetting = webHostBuilder.GetSetting(NgServerPortSettingName);
                if (Int32.TryParse(ngServerPortSetting, out int port))
                {
                    ngServeOptions = $"--port {port}";
                }
            }
            return ngServeOptions;
        }

        /// <summary>
        /// Find the package.json file and make sure it has no BOM
        /// </summary>
        private static void EnsurePackageJsonFileHasNoBom()
        {
            var packageJsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), packageJsonFileName);
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

        /// <summary>
        /// Parses NgServeOptions, if present, and looks for a custom port value. If it is not found, returns the DefaultNgServerPort value.
        /// </summary>
        /// <param name="ngServeOptions"></param>
        /// <param name="defaultValue"></param>
        /// <returns>The custom port value, if found. Otherwise, returns the DefaultNgServerPort value</returns>
        private static int GetNgServerPort(string ngServeOptions)
        {
            if (!String.IsNullOrWhiteSpace(ngServeOptions))
            {
                // Our pattern does not capture negative values. If the value is negative, the "ng serve" displays an error message.
                string pattern = @"--port\s+(\w+)\s*";
                MatchCollection matches = Regex.Matches(ngServeOptions, pattern, RegexOptions.IgnoreCase);
                // Force the regular expression engine to find all matches at once (otherwise it enumerates match-by-match lazily).
                var count = matches.Count;
                if (count > 0)
                {
                    var match = matches[count - 1]; // When "ng serve" runs, the last occurence of "--port foo" wins even if it is not a number.
                    var value = match.Groups[1].Value; // Groups[0] is the match itself.
                    if (Int32.TryParse(value, out int ngServerPort))
                    {
                        return ngServerPort;
                    }
                }
            }
            // If the value is not valid, the "ng serve" falls back to the default value.
            return DefaultNgServerPort;
        }

        /// <summary>
        /// An NG Develpment Server might be started manually from the Command Prompt. Check if that is the case.
        /// </summary>
        /// <param name="ngServerPort"></param>
        /// <returns></returns>
        private static bool IsNgServerPortAvailable(int ngServerPort)
        {
            // Be optimistic. If the check fails, the CLI will report the conflict anyway.
            bool isNgServerPortAvailable = true;
            // If the value is 0, "ng serve" tries to start on 4200, 4201, 4202, 4203, and so on, until it finds an available port.
            if (ngServerPort > 0)
            {
                using (var netstatProcess = new Process())
                {
                    netstatProcess.StartInfo.FileName = "cmd.exe";
                    netstatProcess.StartInfo.Arguments = $"/c netstat.exe -a -n -p TCP | findstr LISTENING | findstr :{ngServerPort}";
                    netstatProcess.StartInfo.UseShellExecute = false;
                    netstatProcess.StartInfo.RedirectStandardOutput = true;
                    netstatProcess.Start();
                    var netstatOutput = netstatProcess.StandardOutput.ReadToEnd();
                    netstatProcess.WaitForExit();

                    isNgServerPortAvailable = String.IsNullOrWhiteSpace(netstatOutput);
                }
            }
            return isNgServerPortAvailable;
        }

    }

}
