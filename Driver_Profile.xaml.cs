using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_Profile : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _profileImageUrl = string.Empty;

        public Driver_Profile()
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
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    await Navigation.PopModalAsync();
                    return;
                }

                string driverId = AppSession.GetDriverId();
                string mobileNumber = AppSession.GetDriverMobileNumber();

                if (string.IsNullOrEmpty(driverId))
                {
                    await DisplayAlert("Error", "Driver ID not found", "OK");
                    return;
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);

                if (driver != null)
                {
                    UpdateUserInterface(driver, mobileNumber);
                    await LoadProfileImage(driver);
                }
                else
                {
                    await DisplayAlert("Error", "Driver data not found", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load driver profile: {ex.Message}", "OK");
            }
        }

        private async Task LoadProfileImage(Driver driver)
        {
            try
            {
                if (!string.IsNullOrEmpty(driver.ProfileImageUrl))
                {
                    _profileImageUrl = driver.ProfileImageUrl;
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

        private void UpdateUserInterface(Driver driver, string mobileNumber)
        {
            string displayName = GetDisplayName(driver);
            string formattedPhone = FormatPhoneNumber(mobileNumber);
            string formattedAddress = GetFormattedAddress(driver);

            FullNameLabel.Text = displayName;
            AddressLabel.Text = formattedAddress;
            GenderLabel.Text = string.IsNullOrEmpty(driver.Gender) ? "Not specified" : driver.Gender;
            MobileNumberLabel.Text = formattedPhone;
            EmailLabel.Text = string.IsNullOrEmpty(driver.Email) ? "Add an e-mail address" : driver.Email;
        }

        private string GetDisplayName(Driver driver)
        {
            try
            {
                if (!string.IsNullOrEmpty(driver.FirstName) && !string.IsNullOrEmpty(driver.LastName))
                {
                    string middleInitial = string.IsNullOrEmpty(driver.MiddleName) ? "" : $"{driver.MiddleName[0]}.";
                    string name = $"{driver.FirstName} {middleInitial} {driver.LastName}".Trim();
                    return name.Replace("  ", " ");
                }
                else if (!string.IsNullOrEmpty(driver.FirstName))
                {
                    return driver.FirstName.Trim();
                }
                else if (!string.IsNullOrEmpty(driver.LastName))
                {
                    return driver.LastName.Trim();
                }
                else
                {
                    return "Driver";
                }
            }
            catch
            {
                return "Driver";
            }
        }

        private string GetFormattedAddress(Driver driver)
        {
            try
            {
                if (string.IsNullOrEmpty(driver.HouseNumber) &&
                    string.IsNullOrEmpty(driver.Street) &&
                    string.IsNullOrEmpty(driver.Barangay) &&
                    string.IsNullOrEmpty(driver.Municipality) &&
                    string.IsNullOrEmpty(driver.Province))
                {
                    return "Address not set";
                }

                var parts = new System.Collections.Generic.List<string>();

                if (!string.IsNullOrEmpty(driver.HouseNumber))
                    parts.Add(driver.HouseNumber.Trim());
                if (!string.IsNullOrEmpty(driver.Street))
                    parts.Add(driver.Street.Trim());
                if (!string.IsNullOrEmpty(driver.Barangay))
                    parts.Add(driver.Barangay.Trim());
                if (!string.IsNullOrEmpty(driver.Municipality))
                    parts.Add(driver.Municipality.Trim());
                if (!string.IsNullOrEmpty(driver.Province))
                    parts.Add(driver.Province.Trim());

                return string.Join(", ", parts);
            }
            catch
            {
                return "Address not set";
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
            try
            {
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Cannot go back: " + ex.Message, "OK");
            }
        }

        private async void OnPersonalInfoEditClicked(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Driver_PersonalInformation());
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
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Driver_ContactInformation());
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
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Driver_ChangePIN());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Cannot open change PIN: " + ex.Message, "OK");
            }
        }

        private async void OnRequirementsClicked(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new Driver_Requirements());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Cannot open requirements: " + ex.Message, "OK");
            }
        }

        private async void OnProfileImageTapped(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Error", "Please login to continue.", "OK");
                    return;
                }

                string driverId = AppSession.GetDriverId();

                if (string.IsNullOrEmpty(driverId))
                {
                    await DisplayAlert("Error", "Driver ID not found", "OK");
                    return;
                }

                var editorPage = new ProfilePictureEditor(driverId, "driver", _profileImageUrl);
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

                string driverId = AppSession.GetDriverId();

                if (string.IsNullOrEmpty(driverId))
                {
                    await DisplayAlert("Error", "Driver not found", "OK");
                    LoadingOverlay.IsVisible = false;
                    return;
                }

                _profileImageUrl = downloadUrl;

                if (Uri.IsWellFormedUriString(downloadUrl, UriKind.Absolute))
                {
                    ProfileImage.Source = ImageSource.FromUri(new Uri(downloadUrl));
                }

                bool success = await _firebaseConnection.UpdateDriverProfilePictureAsync(driverId, downloadUrl);

                if (success)
                {
                    MessagingCenter.Send(this, "ProfilePictureUpdatedMenu", downloadUrl);
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

                string driverId = AppSession.GetDriverId();

                if (string.IsNullOrEmpty(driverId))
                {
                    await DisplayAlert("Error", "Driver not found", "OK");
                    LoadingOverlay.IsVisible = false;
                    return;
                }

                _profileImageUrl = string.Empty;
                ProfileImage.Source = "profile_icon.png";

                bool success = await _firebaseConnection.UpdateDriverProfilePictureAsync(driverId, string.Empty);

                if (success)
                {
                    MessagingCenter.Send(this, "ProfilePictureRemovedMenu", driverId);
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