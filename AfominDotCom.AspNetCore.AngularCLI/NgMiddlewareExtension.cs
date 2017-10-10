using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    public static class NgMiddlewareExtension
    {

        public static IApplicationBuilder UseNgProxy(this IApplicationBuilder app)
        {
            return UseNgProxy(app, new[] { "" }); // Use default options.
        }

        public static IApplicationBuilder UseNgProxy(this IApplicationBuilder app, string ngServeOptions)
        {
            return UseNgProxy(app, new[] { ngServeOptions });
        }

        /// <summary>
        /// Runs one or more Ng apps simultaneously.
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

        public static IApplicationBuilder UseNgRoute(this IApplicationBuilder app)
        {
            return app.UseMiddleware<NgRouteMiddleware>();
        }

    }
}
