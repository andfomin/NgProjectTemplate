using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Proxy;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    internal class NgProxyMiddleware
    {

        public static string ForwardSlash = "/";
        internal static bool ProxyToPathRoot;

        private readonly RequestDelegate next;
        private readonly Dictionary<string, ProxyMiddleware> pathPrefixToProxyMap;

        public NgProxyMiddleware(RequestDelegate next, IOptions<Dictionary<string, ProxyOptions>> options)
        {
            // Store the next middleware in the request processing chain.
            this.next = next ?? throw new ArgumentNullException(nameof(next));

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var pathPrefixToOptionsMap = options.Value;
            // Do not assign ProxyMiddleware until the Ng server is available.
            this.pathPrefixToProxyMap = pathPrefixToOptionsMap.Keys.ToDictionary(i => i, i => (ProxyMiddleware)null);

            // Create a proxy for each Ng app
            foreach (var item in pathPrefixToOptionsMap)
            {
                var pathPrefix = item.Key;
                var proxyOptions = item.Value;

                Task.Factory.StartNew(async () =>
                {
                    var port = Int32.Parse(proxyOptions.Port);
                    // Wait until the Ng server has started.
                    do
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                    while (IsPortAvailable(port));

                    var proxyMiddleware = new ProxyMiddleware(next, Options.Create(proxyOptions));
                    // From now on we can proxy requests.
                    pathPrefixToProxyMap[pathPrefix] = proxyMiddleware;
                }
                );
            }
        }

        public async Task Invoke(HttpContext context)
        {
            // Simple routing.
            var pathPrefix = pathPrefixToProxyMap
              .Select(i => i.Key)
              // If base-href is '/', that means catch all.
              .FirstOrDefault(i => (i == ForwardSlash) || context.Request.Path.StartsWithSegments(i));

            if (pathPrefix != null)
            {
                var proxyMiddleware = pathPrefixToProxyMap[pathPrefix];
                // Wait for at least one minute until the Ng server has started.
                var counter = 120;
                while ((proxyMiddleware == null) && (counter > 0))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    proxyMiddleware = pathPrefixToProxyMap[pathPrefix];
                    counter--;
                }

                if (proxyMiddleware != null)
                {
                    // Call the Ng server
                    // Webpack in Angular CLI 1.1 didn't recognize path. It served from the root.
                    // Starting from Angular CLI 1.4 the path in "base-href" is respected.
                    if (ProxyToPathRoot)
                    {
                        await CallProxyToPathRoot(context, proxyMiddleware);
                    }
                    else
                    {
                        await proxyMiddleware.Invoke(context);
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

        private static async Task CallProxyToPathRoot(HttpContext context, ProxyMiddleware proxyMiddleware)
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
                await proxyMiddleware.Invoke(context);
            }
            finally
            {
                context.Request.Path = requestPath;
            }
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

    }
}
