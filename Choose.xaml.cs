using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Choose : ContentPage
    {
        public Choose()
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            
            NavigationPage.SetHasNavigationBar(this, false);

            if (Parent is NavigationPage navPage)
            {
                navPage.BarBackgroundColor = Colors.Transparent;
                navPage.BarTextColor = Colors.Transparent;
            }

            NavigationPage.SetHasBackButton(this, false);
            Shell.SetNavBarIsVisible(this, false);
        }

        private async void ForCustomer(object sender, EventArgs e)
        {
            if (AppSession.IsCommuterLoggedIn())
            {
                Application.Current.MainPage = new CommuterShell();
            }
            else
            {
                await EnsureTransparentNavigation(new Commuter_MobileNumber());
            }
        }

        private async void ForDriver(object sender, EventArgs e)
        {
            if (AppSession.IsDriverLoggedIn())
            {
                Application.Current.MainPage = new DriverShell();
            }
            else
            {
                await EnsureTransparentNavigation(new Driver_MobileNumber());
            }
        }

        private async Task EnsureTransparentNavigation(ContentPage nextPage)
        {
            NavigationPage.SetHasNavigationBar(nextPage, false);
            await Navigation.PushAsync(nextPage);
        }

        protected override bool OnBackButtonPressed()
        {
            if (Navigation.NavigationStack.Count <= 1)
            {
                Application.Current.MainPage = new GetStarted();
                return true;
            }
            return false;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            if (!Navigation.NavigationStack.Contains(this))
            {
                AppSession.FinishNumberEntry();
            }
        }
    }
}