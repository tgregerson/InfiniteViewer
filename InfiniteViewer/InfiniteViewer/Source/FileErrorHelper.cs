using System;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace InfiniteViewer
{
    public class FileErrorHelper
    {
        public static async Task RaisePermissionsDialog(String path)
        {
            ContentDialog permissionsDialog = new ContentDialog
            {
                Title = "File system permissions required",
                Content = "Infinite Viewer must be granted file system access permissions and "
                   + "reloaded in order to open " + path,
                PrimaryButtonText = "Open App Settings",
                CloseButtonText = "Cancel"
            };
            var result = await permissionsDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:appsfeatures-app"));
            }
        }

        public static async Task RaiseNotFoundDialog(String path)
        {
            ContentDialog notFoundDialog = new ContentDialog
            {
                Title = "Folder not found",
                Content = "Folder '" + path + " not found.",
                CloseButtonText = "OK"
            };
            await notFoundDialog.ShowAsync();
        }

        public static async Task RaiseImageErrorDialog(String path)
        {
            ContentDialog imageErrorDialog = new ContentDialog
            {
                Title = "Error opening image",
                Content = "InfiniteViewer could not render " + path + ". The file may be corrupt.\n\n" +
                          "Select Silence to suppress further errors of this type for the remainder of the session.",
                PrimaryButtonText = "Silence",
                CloseButtonText = "Dismiss"
            };
            var result = await imageErrorDialog.ShowAsync();
            SuppressImageErrors = result == ContentDialogResult.Primary;
        }

        public static async Task RunMethodAsync(Func<String, Task> fn, String path)
        {
            try
            {
                await fn(path);
            }
            catch (System.IO.FileNotFoundException)
            {
                await FileErrorHelper.RaiseNotFoundDialog(path);
            }
            catch (UnauthorizedAccessException)
            {
                await FileErrorHelper.RaisePermissionsDialog(path);
            }
        }

        public static bool SuppressImageErrors { get; set; } = false;
    }
}