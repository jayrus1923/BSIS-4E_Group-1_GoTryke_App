using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public static class NavigationHelper
    {
        private static Shell CurrentShell => Application.Current?.MainPage as Shell;

        public static void NavigateToChoose()
        {
            try
            {
                var choosePage = new Choose();
                var navPage = new NavigationPage(choosePage)
                {
                    BarBackgroundColor = Colors.Transparent,
                    BarTextColor = Colors.Transparent
                };

                NavigationPage.SetHasNavigationBar(choosePage, false);

                Application.Current.MainPage = navPage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NavigateToChoose error: {ex.Message}");
                Application.Current.MainPage = new Choose();
            }
        }

        public static void NavigateToGetStarted()
        {
            try
            {
                Application.Current.MainPage = new GetStarted();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NavigateToGetStarted error: {ex.Message}");
            }
        }

        public static async Task PushModalAsync(Page page)
        {
            try
            {
                if (Application.Current?.MainPage != null)
                {
                    if (CurrentShell != null)
                    {
                        await CurrentShell.Navigation.PushModalAsync(page);
                    }
                    else if (Application.Current.MainPage is NavigationPage navPage)
                    {
                        await navPage.Navigation.PushModalAsync(page);
                    }
                    else
                    {
                        await Application.Current.MainPage.Navigation.PushModalAsync(page);
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: Application.Current.MainPage is null!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PushModalAsync error: {ex.Message}");
                throw;
            }
        }

        public static async Task PopModalAsync()
        {
            try
            {
                if (Application.Current?.MainPage != null)
                {
                    if (CurrentShell != null)
                    {
                        await CurrentShell.Navigation.PopModalAsync();
                    }
                    else if (Application.Current.MainPage is NavigationPage navPage)
                    {
                        await navPage.Navigation.PopModalAsync();
                    }
                    else
                    {
                        await Application.Current.MainPage.Navigation.PopModalAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PopModalAsync error: {ex.Message}");
                throw;
            }
        }

        public static async Task ShellGoToAsync(string route)
        {
            try
            {
                if (CurrentShell != null)
                {
                    await CurrentShell.GoToAsync(route);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ShellGoToAsync error: {ex.Message}");
            }
        }
        public static async Task NavigateToShell(string shellRoute)
        {
            try
            {
                await Shell.Current.GoToAsync($"//{shellRoute}", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigation error: {ex.Message}");

                Application.Current.MainPage = shellRoute switch
                {
                    "Commuter_Home" => new CommuterShell(),
                    "Driver_Home" => new DriverShell(),
                    _ => new GetStarted()
                };
            }
        }
    }
}