using Microsoft.Maui.Controls;
using System;

namespace ServiceCo
{
    public partial class CommuterShell : Shell
    {
        public CommuterShell()
        {
            InitializeComponent();
            Console.WriteLine("?? CommuterShell loaded");

            CheckSession();
        }

        private void CheckSession()
        {
            if (!AppSession.IsCommuterLoggedIn())
            {
                Console.WriteLine("?? No valid commuter session in shell");
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Session Expired", "Please login again.", "OK");
                    NavigationHelper.NavigateToChoose();
                });
            }
            else
            {
                Console.WriteLine($"? Valid commuter session: {AppSession.GetCommuterMobileNumber()}");
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Logout",
                "Are you sure you want to logout?",
                "Logout", "Cancel");

            if (confirm)
            {
                Console.WriteLine("?? Logging out...");
                AppSession.LogoutAll();
                Application.Current.MainPage = new GetStarted();
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Console.WriteLine("?? CommuterShell appearing");
        }
    }
}