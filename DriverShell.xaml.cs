using Microsoft.Maui.Controls;
using System;

namespace ServiceCo
{
    public partial class DriverShell : Shell
    {
        public DriverShell()
        {
            InitializeComponent();
            Console.WriteLine("🏠 DriverShell loaded");

            CheckSession();
        }

        private void CheckSession()
        {
            if (!AppSession.IsDriverLoggedIn())
            {
                Console.WriteLine("⚠️ No valid driver session in shell");
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Session Expired", "Please login again.", "OK");
                    NavigationHelper.NavigateToChoose();
                });
            }
            else
            {
                Console.WriteLine($"✅ Valid driver session: {AppSession.GetDriverMobileNumber()}");
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Logout",
                "Are you sure you want to logout?",
                "Logout", "Cancel");

            if (confirm)
            {
                Console.WriteLine("👋 Logging out...");
                AppSession.LogoutAll();
                Application.Current.MainPage = new GetStarted();
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Console.WriteLine("🏠 DriverShell appearing");
        }
    }
}