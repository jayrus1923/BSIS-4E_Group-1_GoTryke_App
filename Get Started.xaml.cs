using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class GetStarted : ContentPage
    {
        public GetStarted()
        {
            InitializeComponent();
        }

        private void OnGetStartedClicked(object sender, EventArgs e)
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

        protected override void OnAppearing()
        {
            base.OnAppearing();
            AppSession.FinishNumberEntry();
        }

        protected override bool OnBackButtonPressed()
        {
#if ANDROID
            Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#endif
            return true;
        }
    }
}