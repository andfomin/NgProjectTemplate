# Introduction

NgProjectTemplate is a project template which creates an Angular CLI application as a Visual Studio project. 

The project is basically a customized ASP.NET Core project which shares the root folder with the Angular CLI application and is an adapter between the traditional development experience in Visual Studio and the infrastructure provided by Angular CLI.

# Installing the project template

You can find the template in Visual Studio by opening the **New Project** dialog and navigating to the **Online** section. Use the Search box to look up the Angular CLI Application project template.

Alternatively, you can download the VSIX file and install it in Visual Studio manually. The template will be installed under the TypeScript category.

# <a id="installation"></a>Creating a project

Open the standard **New Project** dialog, navigate to Angular CLI Application template, specify the project name and its location, as well as whether to initialize a Git repository for the project.

To generate a project, you must have Angular CLI installed globally with npm as described [here](https://github.com/angular/angular-cli#installation). If Angular CLI is not available on your computer, you can still proceed with creating an empty project. To generate an Angular CLI application later on, execute `ng new -dir .` in the command line within the project directory.

You can customize your Angular CLI application by manually editing the *.angular-cli.json* configuration file or executing  `ng set` in the command line window.

Angular CLI is a Node application which uses npm modules installed locally in the *node_modules* folder under the project root. The process of installing npm modules may take a few minutes depending on the network connection and the npm module cache. It is the longest part of creating the project. During a project creation, you will be presented with a dialog window where you can choose whether to create the project without actually installing npm modules. You will obviously need to install them at a same point later on.

A custom *.gitignore* file with combined settings for Visual Studio and Angular CLI is always added to the project root.

# <a id="skipnpminstall"></a>Installing npm modules.

If you have decided to skip installing npm packages during a project creation, an initial npm install will be postponed until the first **Build** or **Run** of the project. An npm install will be executed during the first **Build** only if there is no *node_modules* folder found under the project root. That process uses the globally installed Node and npm executables.

If you have opted to install npm packages immediately after the project creation, the template will trigger an npm install. You might want to switch the **Output** window to the **Bower/npm** mode to observe the process.

Please note that regardless of your choice, Visual Studio may trigger npm install on opening of a project and as soon as you save any changes to the *package.json* file. This feature is described [here](https://webtooling.visualstudio.com/package-managers/npm/). To control this feature, navigate in the IDE to **Tools –> Options –> Projects and Solutions –> Web Package Management –> Package Restore**. 

The version of npm preinstalled with Visual Studio 2017 apparently meets the Angular CLI requirements. But the version of the Node.js executable which is preinstalled with Visual Studio 2017 does not entirely satisfy the Angular CLI requirements. As a result, you may see non-critical warnings during Angular CLI installation. If you want to use the globally installed Node and npm in Visual Studio, you can find the instructions [here](https://blogs.msdn.microsoft.com/webdev/2015/03/19/customize-external-web-tools-in-visual-studio-2015/)


# Running the project.

Run the project by pressing **F5**. The project is started by Visual Studio as an ASP.NET Core application which in turn launches the genuine Angular CLI Development Server, redirects the launched web browser to it and exits afterwards.

Since the project does not have server-side code to debug and since Angular CLI does not support the JavaScript Debugging feature in Visual Studio anyway, you might prefer to start the project without debugging by pressing **Ctrl+F5** in Visual Studio instead and then open Developer Tools by pressing **F12** in the browser.

Alternatively, you may want to disable JavaScript debugging in Visual Studio by going to **Tools -> Options -> Debugging -> General** and turning off the setting **Enable JavaScript Debugging for ASP.NET (Chrome and IE)**. Learn more about JavaScript debugging in Visual Studio [here](https://aka.ms/chromedebugging). If you keep the JavaScript Debugging feature in Visual Studio enabled, then you may face the following effects:
* Opening Developer Tools in Chrome stops the script debugging session
* The Hot Module Replacement feature of Angular CLI breaks code mapping
* If you close the browser window manually, then stopping the debugger in Visual Studio will take longer than usual.

This project relies on the default hosting settings stored in the *Properties\launchSettings.json* file. That file is controlled by Visual Studio, do not edit it manually. Its contents correspond to the **Debug** tab on the project’s **Properties** dialog page. Make sure the **IIS Express** profile is selected as active. Although that is the default setting, sometimes the other profile may unexpectedly get active. That would cause no problem, but confusion when starting the project.

NG Development Server uses port 4200 by default. If that port is already in use, you may want to specify a different port. To do that, open the project's **Properties** page and select the **Debug** tab. Add an **Environment Variable** named `ASPNETCORE_NgServerPort` and specify the port number as its **Value**.

# Building and publishing the Angular application.

The project executes `npm run build` during a **Publish** process. You can customize the build options by editing the predefined **build** task in the *package.json* file.

Only the files produced by Angular CLI during the build are published to the target destination.

To start a publish, right-click the project in **Solution Explorer** and choose **Publish** in the pop-up menu. Select a desired publish target on the dialog page and proceed according to your selection. The project template was tested by publishing to a local folder and to Azure App Service.

As a side note, Azure App Service supports hosting of static web sites without any additional configuration or modification. Learn more [here](https://www.microsoft.com/middleeast/azureboxes/cloud-hosting-for-a-static-website.aspx) and [here](https://docs.microsoft.com/en-us/azure/app-service-web/app-service-web-get-started-html)

# Have fun

As a demonstration of how it is really easy to use the tempate, I have created a video [Create an Angular app and get it on the web in four minutes without touching the keyboard!](https://www.youtube.com/watch?v=qEPax1eyUbk) [![Create an Angular app and get it on the web in four minutes without touching the keyboard!](https://user-images.githubusercontent.com/3043428/27361781-bc19ed74-55f7-11e7-89fe-98ee894a0c41.png)](https://www.youtube.com/watch?v=qEPax1eyUbk)
