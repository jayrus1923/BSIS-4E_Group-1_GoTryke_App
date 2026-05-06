using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_Profile : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _profileImageUrl = string.Empty;

        public Commuter_Profile()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadUserData();
            SubscribeToProfileUpdates();
        }

        private void SubscribeToProfileUpdates()
        {
            MessagingCenter.Subscribe<ProfilePictureEditor, string>(
                this,
                "ProfilePictureUpdated",
                async (senderObj, downloadUrl) =>
                {
                    await HandleProfilePictureUpdated(downloadUrl);
                });

            MessagingCenter.Subscribe<ProfilePictureEditor, string>(
                this,
                "ProfilePictureRemoved",
                async (senderObj, userId) =>
                {
                    await HandleProfilePictureRemoved();
                });
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<ProfilePictureEditor, string>(this, "ProfilePictureUpdated");
            MessagingCenter.Unsubscribe<ProfilePictureEditor, string>(this, "ProfilePictureRemoved");
        }

        private async Task LoadUserData()
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    await Navigation.PopModalAsync();
                    return;
                }

                string commuterId = AppSession.GetCommuterId();
                string mobileNumber = AppSession.GetCommuterMobileNumber();

                if (string.IsNullOrEmpty(commuterId))
                {
                    await DisplayAlert("Error", "User ID not found", "OK");
                    return;
                }

                var commuter = await _firebaseConnection.GetCommuterByIdAsync(commuterId);

                if (commuter != null)
                {
                    UpdateUserInterface(commuter, mobileNumber);
                    await LoadProfileImage(commuter);
                }
                else
                {
                    await DisplayAlert("Error", "User data not found", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load profile: {ex.Message}", "OK");
            }
        }

        private async Task LoadProfileImage(Commuter commuter)
        {
            try
            {
                if (!string.IsNullOrEmpty(commuter.ProfileImageUrl))
                {
                    _profileImageUrl = commuter.ProfileImageUrl;
                    if (Uri.IsWellFormedUriString(_profileImageUrl, UriKind.Absolute))
                    {
                        ProfileImage.Source = ImageSource.FromUri(new Uri(_profileImageUrl));
                    }
                    else
                    {
                        ProfileImage.Source = "profile_icon.png";
                    }
                }
                else
                {
                    ProfileImage.Source = "profile_icon.png";
                }
            }
            catch
            {
                ProfileImage.Source = "profile_icon.png";
            }
        }

        private void UpdateUserInterface(Commuter commuter, string mobileNumber)
        {
            string displayName = GetDisplayName(commuter);
            string formattedPhone = FormatPhoneNumber(mobileNumber);

            FullNameLabel.Text = displayName;
            GenderLabel.Text = string.IsNullOrEmpty(commuter.Gender) ? "Not specified" : commuter.Gender;
            MobileNumberLabel.Text = formattedPhone;
            EmailLabel.Text = string.IsNullOrEmpty(commuter.Email) ? "Add an email address" : commuter.Email;
        }

        private string GetDisplayName(Commuter commuter)
        {
            try
            {
                if (!string.IsNullOrEmpty(commuter.FirstName) && !string.IsNullOrEmpty(commuter.LastName))
                {
                    string middleInitial = string.IsNullOrEmpty(commuter.MiddleName) ? "" : $"{commuter.MiddleName[0]}.";
                    string name = $"{commuter.FirstName} {middleInitial} {commuter.LastName}".Trim();
                    return name.Replace("  ", " ");
                }
                else if (!string.IsNullOrEmpty(commuter.FirstName))
                {
                    return commuter.FirstName.Trim();
                }
                else if (!string.IsNullOrEmpty(commuter.LastName))
                {
                    return commuter.LastName.Trim();
                }
                else
                {
                    return "User";
                }
            }
            catch
            {
                return "User";
            }
        }

        private string FormatPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return "Not available";

            phoneNumber = phoneNumber.Trim();

            if (phoneNumber.StartsWith("+63") && phoneNumber.Length == 13)
            {
                return $"+63 {phoneNumber.Substring(3, 3)} {phoneNumber.Substring(6, 3)} {phoneNumber.Substring(9, 4)}";
            }
            else if (phoneNumber.StartsWith("09") && phoneNumber.Length == 11)
            {
                return $"+63 {phoneNumber.Substring(1, 3)} {phoneNumber.Substring(4, 3)} {phoneNumber.Substring(7, 4)}";
            }

            return phoneNumber;
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private async void OnPersonalInfoEditClicked(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_PersonalInformation());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Cannot edit personal info: " + ex.Message, "OK");
            }
        }

        private async void OnContactInfoEditClicked(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_ContactInformation());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Cannot edit contact info: " + ex.Message, "OK");
            }
        }

        private async void OnChangePinClicked(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_ChangePIN());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Cannot open change PIN: " + ex.Message, "OK");
            }
        }

        private async void OnIdVerificationClicked(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_IDVerification());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Cannot open ID verification: " + ex.Message, "OK");
            }
        }

        private async void OnProfileImageTapped(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await DisplayAlert("Error", "Please login to continue.", "OK");
                    return;
                }

                string commuterId = AppSession.GetCommuterId();

                if (string.IsNullOrEmpty(commuterId))
                {
                    await DisplayAlert("Error", "User ID not found", "OK");
                    return;
                }

                var editorPage = new ProfilePictureEditor(commuterId, "commuter", _profileImageUrl);
                await Navigation.PushModalAsync(editorPage);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to open editor: {ex.Message}", "OK");
            }
        }

        private async Task HandleProfilePictureUpdated(string downloadUrl)
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                string commuterId = AppSession.GetCommuterId();

                if (string.IsNullOrEmpty(commuterId))
                {
                    await DisplayAlert("Error", "User not found", "OK");
                    LoadingOverlay.IsVisible = false;
                    return;
                }

                _profileImageUrl = downloadUrl;

                if (Uri.IsWellFormedUriString(downloadUrl, UriKind.Absolute))
                {
                    ProfileImage.Source = ImageSource.FromUri(new Uri(downloadUrl));
                }

                bool success = await _firebaseConnection.UpdateCommuterProfilePictureAsync(commuterId, downloadUrl);

                if (success)
                {
                    MessagingCenter.Send(this, "ProfilePictureUpdatedMenuCommuter", downloadUrl);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to update: {ex.Message}", "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async Task HandleProfilePictureRemoved()
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                string commuterId = AppSession.GetCommuterId();

                if (string.IsNullOrEmpty(commuterId))
                {
                    await DisplayAlert("Error", "User not found", "OK");
                    LoadingOverlay.IsVisible = false;
                    return;
                }

                _profileImageUrl = string.Empty;
                ProfileImage.Source = "profile_icon.png";

                bool success = await _firebaseConnection.UpdateCommuterProfilePictureAsync(commuterId, string.Empty);

                if (success)
                {
                    MessagingCenter.Send(this, "ProfilePictureRemovedMenuCommuter", commuterId);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to remove: {ex.Message}", "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }
    }
}