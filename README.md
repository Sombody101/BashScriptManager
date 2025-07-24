# Bash Script Manager

A GTK-based Bash Script Manager built with C# and .NET, designed to help users manage and run bash scripts through a simple graphical interface.
Features

    Manage your bash scripts via GUI

    Run scripts without using the terminal

    Clean and intuitive user interface

    Clickable icons and dialogs for improved user experience

# Getting Started

1. Navigate to the Project Directory

Before building or publishing, change to your project folder (where the .csproj file is located):

cd /path/to/your/app/ConsoleApp2

# 2. Build and Publish

Publish a self-contained Linux executable with:

dotnet publish -c Release -r linux-x64 --self-contained=true -o ./publish

This command:

    Builds your project in Release mode

    Targets the Linux x64 platform

    Produces a self-contained executable (no separate runtime needed)

    Outputs the result into the publish folder inside your project directory

    Note: Make sure your images folder is included in the publish output. You can configure your .csproj file to copy it automatically or copy it manually after publishing.

# 3. Run the Application

Run the app from inside the project directory (the parent folder of publish):

./publish/ConsoleApp2

    Important: Running the executable from outside the project directory may cause resource loading errors (e.g., missing images).

# Troubleshooting

    If you get errors about missing image files, verify that the images folder exists inside the publish directory.

    The app uses paths relative to its executable location for resources.

    To ensure consistent behavior, the app sets its working directory to the executable location on startup.

    If you see GTK warnings about adding widgets multiple times, make sure each widget is added to only one container.

License

This project is licensed under the MIT License.
