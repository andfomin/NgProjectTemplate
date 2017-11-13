using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    /// <summary>
    /// Extension methods for serving Angular CLI application files by an ASP.NET Core server.
    /// </summary>
    public static class NgMiddlewareExtension
    {

        /// <summary>
        /// Starts an NG Development Server with default options and proxies requests from the ASP.NET Core pipeline to it for request paths that start with the baseHref path specified in the .angular-cli.json file.
        /// </summary>
        /// <param name="app">IApplicationBuilder</param>
        /// <returns>IApplicationBuilder</returns>
        public static IApplicationBuilder UseNgProxy(this IApplicationBuilder app)
        {
            return UseNgProxy(app, new[] { "" }); // Use default options.
        }

        /// <summary>
        /// Starts an NG Development Server with specified options and proxies requests from the ASP.NET Core pipeline to it for request paths that start with the baseHref path specified in the .angular-cli.json file. 
        /// </summary>
        /// <param name="app">IApplicationBuilder</param>
        /// <param name="ngServeOptions">Options passed to the "ng serve" command. For example, "--port 4242".</param>
        /// <returns>IApplicationBuilder</returns>
        public static IApplicationBuilder UseNgProxy(this IApplicationBuilder app, string ngServeOptions)
        {
            return UseNgProxy(app, new[] { ngServeOptions });
        }

        /// <summary>
        /// Starts many instances of NG Development Server with different options and proxies requests from the ASP.NET Core pipeline to an appropriate server for request paths that start with the baseHref paths specified for "app" objects in the .angular-cli.json file. 
        /// </summary>
        /// <param name="app">IApplicationBuilder</param>
        /// <param name="ngServeOptionsList">This is a list of option strings for separate "ng serve" commands. Each string is a set of options for a particular app. This is NOT a list of separate options for an individual app.</param>
        public static IApplicationBuilder UseNgProxy(this IApplicationBuilder app, IEnumerable<string> ngServeOptionsList)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            if (ngServeOptionsList == null)
            {
                throw new ArgumentNullException(nameof(ngServeOptionsList));
            }
            var options = Options.Create(ngServeOptionsList.ToList());
            return app.UseMiddleware<NgProxyMiddleware>(options);
        }

        /// <summary>
        /// Serves the index file from a corresponding subfolder in the wwwroot directory for request paths that start with a baseHref path specified in the .angular-cli.json file. Ignores requests for file names with extensions.
        /// </summary>
        /// <param name="app">IApplicationBuilder</param>
        /// <returns>IApplicationBuilder</returns>
        public static IApplicationBuilder UseNgRoute(this IApplicationBuilder app)
        {
            return app.UseMiddleware<NgRouteMiddleware>();
        }

    }
}
