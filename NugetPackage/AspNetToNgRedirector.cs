using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
        private const string PackageJsonFileName = "package.json";
        private const int DefaultNgServerPort = 4200;
        private const string PollingUrl = "/poll-proxy";
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
    function onTimeout(xhr) {
      return function () {
        xhr.abort();
        doXHR();
      }
    }

    function doXHR() {
      var xhr = new XMLHttpRequest();
      var timeout = setTimeout(onTimeout(xhr), 5000);
      xhr.open('GET', '{{PollingUrl}}');
      xhr.onreadystatechange = function () {
        // Accept 404 if the apps/index setting in .angular-cli.json has been changed to anything else than index.html. 
        // Status 204 comes from our HTTPS proxy.
        if (xhr.readyState == 4 && (xhr.status == 200 || xhr.status == 204 || xhr.status == 404)) {
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
<p><strong>
You can ignore this notification and continue using this browser window.
</strong></p>
<p>
As another option, you can start your project without debugging by pressing Ctrl+F5 in Visual Studio and
<br/>
then open Developer Tools by pressing F12 in the browser.
</p>
<p>
Alternatively, you can disable JavaScript debugging in Visual Studio by going to <i>Tools -> Options -> Debugging -> General</i> and
<br/>
turning off the setting <i>Enable JavaScript Debugging for ASP.NET (Chrome and IE)</i>.
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
  <meta http-equiv='refresh' content='0; url={{NgServerProtocol}}://localhost:{{NgServerPort}}/'/>
</head>
<body>
</body>
</html>
";

        #endregion

        public static void Main(string[] args)
        {
            /* Visual Studio configures IIS Express to host a Kestrel server.
             * When we run the project, Visual Studio starts the IIS Express which in turn starts the Kestrel server, then launches a browser and points it to the IIS Express. 
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
            // TODO AF20170914 Assign explicitly ngProcess.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            var ngProcess = Process.Start("cmd.exe", "/k start ng.cmd serve"
              + (!String.IsNullOrWhiteSpace(ngServeOptions) ? " " + ngServeOptions : String.Empty)); // TODO AF20170914. Simplify: ngServeOptions??String.Empty

            var ngServerProtocol = GetNgServerProtocol(ngServeOptions);
            var ngServerPort = GetNgServerPort(ngServeOptions);
            // An NG Develpment Server may have already been started manually from the Command Prompt. Check if that is the case.
            bool isNgServerPortAvailable = IsNgServerPortAvailable(ngServerPort);

            var startPage = (isNgServerPortAvailable
              ? (StartPage
              .Replace("{{PollingUrl}}", ngServerProtocol == "https" ? PollingUrl : $"http://localhost:{ngServerPort}/")
              .Replace("{{RedirectionPageUrl}}", RedirectionPageUrl)
              .Replace("{{DebuggerWarning}}", (Debugger.IsAttached ? DebuggerWarning : String.Empty))
              )
              // Inform the developer how to specify another port.
              : PortUnavailableErrorPage
              )
              .Replace("{{StyleSection}}", StyleSection)
              ;

            var redirectionPage = RedirectionPage
              .Replace("{{NgServerProtocol}}", ngServerProtocol)
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
                      case PollingUrl:
                          var isNgServerReady = await IsNgServerReady(ngServerProtocol, ngServerPort, cancellationToken);
                          context.Response.StatusCode = isNgServerReady ? StatusCodes.Status204NoContent : StatusCodes.Status503ServiceUnavailable;
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
            var packageJsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), PackageJsonFileName);
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
        /// Searches for the SSL option in NgServeOptions and returns "http" or "https" depending on the presense of the option.
        /// </summary>
        /// <param name="ngServeOptions"></param>
        /// <returns>String "http" or "https"</returns>
        private static string GetNgServerProtocol(string ngServeOptions)
        {
            var isSSL = !String.IsNullOrWhiteSpace(ngServeOptions) && (ngServeOptions.IndexOf("--ssl") >= 0);
            return isSSL ? "https" : "http";
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

        /// <summary>
        /// Calls the NG server and reports whether it is ready.
        /// </summary>
        /// <param name="protocol"></param>
        /// <param name="port"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>bool</returns>
        private static async Task<bool> IsNgServerReady(string protocol, int port, CancellationToken cancellationToken)
        {
            /* Webpack uses a self-signed certificate and it is not trusted by the browser until an exception is added by the user manually.
             * We cannot poll HTTPS from the browser. The browser does not report the actual reason for a connection failure to JavaScript in the case of an untrusted certificate. So we have no means to distinguish between the server being unavailable and a certificate error.
             * We use a server-side HttpClient to recognize a certificate error.
             * But Visual Studio launches Chrome with a separate debug user profile. That Chrome instance does not see the certificate exception added by the regular user profile. We need to redirect to the Ng home page under the debug profile to give the user a chance to see the security warning page and add a certificate exception manually.             
             */
            var result = false;

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                using (var requestMessage = new HttpRequestMessage())
                {
                    requestMessage.Method = HttpMethod.Get;
                    requestMessage.RequestUri = new Uri($"{protocol}://localhost:{port}");
                    requestMessage.Headers.Host = $"localhost:{port}";
                    requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

                    try
                    {
                        using (var responseMessage = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                        {
                            // Returns 404 if the apps/index setting in .angular-cli.json has been changed to something else than index.html
                            var readyStatusCodes = new[] { HttpStatusCode.OK, HttpStatusCode.NotFound };
                            if (readyStatusCodes.Contains(responseMessage.StatusCode))
                            {
                                result = true;
                            }
                        }
                    }
                    catch (HttpRequestException exception)
                    {
                        // HttpRequestException InnerException: System.Net.Http.WinHttpException 
                        // Port: HResult -2147012867 = 0x80072EFD Message "A connection with the server could not be established" ERROR_WINHTTP_CANNOT_CONNECT 12029 = 0x2EFD
                        // SSL:	 HResult -2147012721 = 0x80072F8F Message "A security error occurred" ERROR_WINHTTP_SECURE_FAILURE 12175 = 0x2F8F
                        const int SecurityErrorHResult = -2147012721;

                        if (exception.HResult == SecurityErrorHResult)
                        {
                            result = true;
                        }
                        else
                        {
                            var innerException = exception.InnerException;
                            if (innerException != null)
                            {
                                // var innerExceptionName = innerException.GetType().FullName; // "System.Net.Http.WinHttpException"
                                if (innerException.HResult == SecurityErrorHResult)
                                {
                                    result = true;
                                }
                            }
                        }

                    }
                    catch (Exception)
                    {
                    }
                }

                return result;
            }

        }
    }
}
