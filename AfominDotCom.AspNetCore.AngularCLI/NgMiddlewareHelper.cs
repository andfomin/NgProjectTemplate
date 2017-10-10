using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    internal static class NgMiddlewareHelper
    {
        internal const string PackageJsonFileName = "package.json";
        internal const string AngularCliJsonFileName = ".angular-cli.json";

        internal const string ErrorAngularCliJsonNotFound = "File .angular-cli.json was not found in folder {0}.";
        internal const string ErrorAppsNotFound = "The \"apps\" element not found in .angular-cli.json .";
        internal const string ErrorNoBaseHrefInAngularCliJson = "A \"baseHref\" value is missing in apps[{0}] in .angular-cli.json .";

        //-------

        internal class NgAppSettings
        {
            public int AppIndex;
            public string AppName;
            public string BaseHref;
            public string IndexFileName;
        }

        /// <summary>
        /// Reads settings from the "apps" array in .angular-cli.json.
        /// </summary>
        /// <param name="directory"></param>
        /// <returns></returns>
        internal static IEnumerable<NgAppSettings> GetAllNgAppSettings(string directory)
        {
            var angularCliJsonFilePath = Path.Combine(directory, NgMiddlewareHelper.AngularCliJsonFileName);
            if (!File.Exists(angularCliJsonFilePath))
            {
                throw new Exception(String.Format(ErrorAngularCliJsonNotFound, directory));
            }

            var obj = JObject.Parse(File.ReadAllText(angularCliJsonFilePath));

            var apps = (JArray)obj["apps"];
            if (apps == null)
            {
                throw new Exception(ErrorAppsNotFound);
            }

            var ngAppSettings = apps
              .Select((i, index) => new NgAppSettings
              {
                  AppIndex = index,
                  AppName = (string)i["name"],
                  BaseHref = (string)i["baseHref"],
                  IndexFileName = (string)i["index"],
              })
              .ToList()
              ;
            return ngAppSettings;
        }


    }
}
