using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace ServiceCo
{
    public partial class Commuter_AboutApp : ContentPage
    {
        public Commuter_AboutApp()
        {
            InitializeComponent();
        }

        private async void OnBackButtonClicked(object sender, System.EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private async void OnTermsOfServiceClicked(object sender, System.EventArgs e)
        {
            string termsUrl = "https://docs.google.com/document/d/e/2PACX-1vSG4PibEYFbf68wMbvAlhQ7Grv2r5xGYl0y799BjDzx69vqJccz31Z_XNdtZYonLCKAVtSrWtzYW7le/pub";
            try
            {
                await Launcher.Default.OpenAsync(termsUrl);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to open Terms of Service: {ex.Message}", "OK");
            }
        }

        private async void OnPrivacyPolicyClicked(object sender, System.EventArgs e)
        {
            string privacyUrl = "https://docs.google.com/document/d/e/2PACX-1vSG4PibEYFbf68wMbvAlhQ7Grv2r5xGYl0y799BjDzx69vqJccz31Z_XNdtZYonLCKAVtSrWtzYW7le/pub";
            try
            {
                await Launcher.Default.OpenAsync(privacyUrl);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to open Privacy Policy: {ex.Message}", "OK");
            }
        }
    }
}