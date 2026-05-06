using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class ProfilePictureEditor : ContentPage
    {
        private FileResult _selectedPhoto;
        private string _userId;
        private string _userType;
        private string _existingImageUrl;
        private bool _hasChangedPhoto = false;

        public ProfilePictureEditor(string userId, string userType, string existingImageUrl = null)
        {
            InitializeComponent();
            _userId = userId;
            _userType = userType;
            _existingImageUrl = existingImageUrl;
            InitializeUI();
        }

        private void InitializeUI()
        {
            if (!string.IsNullOrEmpty(_existingImageUrl))
            {
                try
                {
                    Console.WriteLine($"Loading existing profile image: {_existingImageUrl}");
                    ProfileImage.Source = ImageSource.FromUri(new Uri(_existingImageUrl));
                    DefaultAvatar.IsVisible = false;
                    RemovePhotoButton.IsVisible = true;
                    SaveButton.IsVisible = false;
                    Console.WriteLine($"? Existing image loaded successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed loading existing image: {ex.Message}");
                    ProfileImage.Source = null;
                    DefaultAvatar.IsVisible = true;
                    RemovePhotoButton.IsVisible = false;
                    SaveButton.IsVisible = false;
                }
            }
            else
            {
                Console.WriteLine(" No existing profile image found");
                ProfileImage.Source = null;
                DefaultAvatar.IsVisible = true;
                RemovePhotoButton.IsVisible = false;
                SaveButton.IsVisible = false;
            }
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private async void OnTakePhotoClicked(object sender, EventArgs e)
        {
            try
            {
                if (MediaPicker.Default.IsCaptureSupported)
                {
                    var photo = await MediaPicker.Default.CapturePhotoAsync();
                    if (photo != null)
                    {
                        await ProcessSelectedPhoto(photo);
                    }
                }
                else
                {
                    await DisplayAlert("Please try again", "Camera is not supported on this device.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Please try again", $"Failed to take photo: {ex.Message}", "OK");
            }
        }

        private async void OnChooseGalleryClicked(object sender, EventArgs e)
        {
            try
            {
                var photo = await MediaPicker.Default.PickPhotoAsync();
                if (photo != null)
                {
                    await ProcessSelectedPhoto(photo);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Please try again", $"Failed to pick photo: {ex.Message}", "OK");
            }
        }

        private async Task ProcessSelectedPhoto(FileResult photo)
        {
            try
            {
                _selectedPhoto = photo;
                _hasChangedPhoto = true;

                ProfileImage.Source = ImageSource.FromFile(photo.FullPath);
                DefaultAvatar.IsVisible = false;
                SaveButton.IsVisible = true;
                RemovePhotoButton.IsVisible = true;

                Console.WriteLine($"?? Photo selected: {photo.FileName}, Size: {photo.FullPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to process photo: {ex.Message}");
                await DisplayAlert("Please try again", $"Failed to process photo: {ex.Message}", "OK");
            }
        }

        private async void OnRemovePhotoClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Remove Photo",
                "Are you sure you want to remove your profile picture?",
                "Yes, Remove", "Cancel");

            if (confirm)
            {
                try
                {
                    LoadingOverlay.IsVisible = true;

                    if (!string.IsNullOrEmpty(_existingImageUrl))
                    {
                        try
                        {
                            var storageService = new FirebaseStorageService();
                            await storageService.DeleteFileAsync(_existingImageUrl);
                            Console.WriteLine($" Deleted old profile picture from storage");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to delete from storage: {ex.Message}");
                        }
                    }

                    var firebaseConnection = new FirebaseConnection();
                    if (_userType == "driver")
                    {
                        await firebaseConnection.UpdateDriverProfilePictureAsync(_userId, "");
                    }
                    else if (_userType == "commuter")
                    {
                        await firebaseConnection.UpdateCommuterProfilePictureAsync(_userId, "");
                    }

                    MessagingCenter.Send(this, "ProfilePictureRemoved", _userId);

                    ProfileImage.Source = null;
                    DefaultAvatar.IsVisible = true;
                    RemovePhotoButton.IsVisible = false;
                    SaveButton.IsVisible = false;
                    _hasChangedPhoto = false;
                    _existingImageUrl = null;
                    _selectedPhoto = null;

                    LoadingOverlay.IsVisible = false;

                    await DisplayAlert("Success", "Profile picture removed!", "OK");
                    await Navigation.PopModalAsync();
                }
                catch (Exception ex)
                {
                    LoadingOverlay.IsVisible = false;
                    Console.WriteLine($"? Failed to remove photo: {ex.Message}");
                    await DisplayAlert("Please try again", $"Failed to remove photo: {ex.Message}", "OK");
                }
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            if (_selectedPhoto == null)
            {
                await DisplayAlert("No Photo", "Please select or take a photo first.", "OK");
                return;
            }

            try
            {
                LoadingOverlay.IsVisible = true;
                Console.WriteLine($"?? Starting upload process for {_userType}...");

                var storageService = new FirebaseStorageService();

                string downloadUrl = await storageService.UploadProfilePictureAsync(_userId, _selectedPhoto, _userType);

                Console.WriteLine($"? Upload completed. Download URL: {downloadUrl}");

                var firebaseConnection = new FirebaseConnection();
                if (_userType == "driver")
                {
                    bool updated = await firebaseConnection.UpdateDriverProfilePictureAsync(_userId, downloadUrl);
                    Console.WriteLine($"?? Driver profile picture update result: {updated}");
                }
                else if (_userType == "commuter")
                {
                    bool updated = await firebaseConnection.UpdateCommuterProfilePictureAsync(_userId, downloadUrl);
                    Console.WriteLine($"?? Commuter profile picture update result: {updated}");
                }

                MessagingCenter.Send(this, "ProfilePictureUpdated", downloadUrl);

                LoadingOverlay.IsVisible = false;

                await DisplayAlert("Success", "Profile picture saved!", "OK");
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                Console.WriteLine($"? Failed to save photo: {ex.Message}");
                Console.WriteLine($"?? Stack trace: {ex.StackTrace}");
                await DisplayAlert("Please try again", $"Failed to save photo: {ex.Message}", "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<ProfilePictureEditor>(this, "ProfilePictureUpdated");
            MessagingCenter.Unsubscribe<ProfilePictureEditor>(this, "ProfilePictureRemoved");
        }
    }
}