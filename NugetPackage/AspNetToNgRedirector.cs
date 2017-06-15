using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AfominDotCom.NgProjectTemplate.Server
{

    public class AspNetToNgRedirector
    {

        #region Constants

        private const string NgServerPortSettingName = "NgServerPort";
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

        private const string UnsupportedEnvironmentErrorPage = @"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8' />
  <title>Unsupported Environment value</title>
</head>
<body>
{{StyleSection}}
<p>
<span class='my-red'>ERROR.</span> Environment value <strong>{{EnvironmentSetting}}</strong> is not supported. 
<br/>
To enable the NG Development Server, open the project's Properies page and select the Debug tab, 
<br/>
than make sure the Environment Variable named <strong>ASPNETCORE_ENVIRONMENT</strong> has value <strong>Development</strong>.
</p>
</body>
</html>
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
To specify a different port, open the project's Properies page and select the Debug tab. 
<br/>
Add an Environment Variable named <strong>ASPNETCORE_NgServerPort</strong> and specify the port number as its Value.
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

            var webHostBuilder = new WebHostBuilder();

            var environmentSetting = webHostBuilder.GetSetting("environment");
            var isSupportedEnvironment = !String.IsNullOrEmpty(environmentSetting) && (environmentSetting.ToLower() == "development");

            var ngServerPortSetting = webHostBuilder.GetSetting(NgServerPortSettingName);
            if (!Int32.TryParse(ngServerPortSetting, out int ngServerPort))
            {
                ngServerPort = DefaultNgServerPort;
            }

            string startPage = null;
            Process ngProcess = null;

            if (isSupportedEnvironment)
            {
                // Visual Studio writes Byte Order Mark when saves files. 
                // Webpack fails reading such a package.json. +https://github.com/webpack/enhanced-resolve/issues/87
                var packageJsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), packageJsonFileName);
                if (File.Exists(packageJsonFilePath))
                {
                    StripBomFromFile(packageJsonFilePath);
                }

                // An NG Develpment Server may have already been started manually from the Command Prompt. If that's the case, don't start our instance.
                using (var netstatProcess = new Process())
                {
                    netstatProcess.StartInfo.FileName = "cmd.exe";
                    netstatProcess.StartInfo.Arguments = $"/c netstat.exe -a -n -p TCP | findstr LISTENING | findstr :{ngServerPort}";
                    netstatProcess.StartInfo.UseShellExecute = false;
                    netstatProcess.StartInfo.RedirectStandardOutput = true;
                    netstatProcess.Start();
                    var netstatOutput = netstatProcess.StandardOutput.ReadToEnd();
                    netstatProcess.WaitForExit();

                    var isNgServerPortAvailable = String.IsNullOrWhiteSpace(netstatOutput);
                    if (!isNgServerPortAvailable)
                    {
                        startPage = PortUnavailableErrorPage
                          .Replace("{{EnvironmentSetting}}", environmentSetting);
                    }
                }
                // Run "ng serve". For ASP.NET applications the working directory is the project root.
                ngProcess = Process.Start("cmd.exe", $"/k start ng.cmd serve --port {ngServerPort}");
            }
            else
            {
                startPage = UnsupportedEnvironmentErrorPage
                  .Replace("{{EnvironmentSetting}}", environmentSetting);
            }

            if (startPage == null)
            {
                startPage = StartPage
                  .Replace("{{RedirectionPageUrl}}", RedirectionPageUrl)
                  .Replace("{{DebuggerWarning}}", (Debugger.IsAttached ? DebuggerWarning : ""))
                  ;
            }

            startPage = startPage
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

            webHost.Run(cancellationToken);

            if (ngProcess != null)
            {
                ngProcess.WaitForExit();
                ngProcess.Dispose();
            }
        }

        /// <summary>
        /// Reads the file, looks for a Byte Order Mark and if a BOM found, writes the file back without a BOM.
        /// </summary>
        /// <param name="filePath"></param>
        private static void StripBomFromFile(string filePath)
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
