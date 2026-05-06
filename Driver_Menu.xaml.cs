using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_Menu : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _profileImageUrl = string.Empty;
        private string _currentDriverId;
        private IDisposable _accountStatusListener;
        private IDisposable _documentStatusListener;
        private bool _isModalOpen = false;
        private bool _isLoggingOut = false;

        private bool _isDriverApproved = false;
        private string _accountStatus = "Active";
        private string _suspensionReason = "";
        private string _suspensionUntil = "";
        private string _deactivationReason = "";
        private string _documentStatus = "Pending";

        public Driver_Menu()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_isLoggingOut) return;

            SubscribeToProfileUpdates();
            await CheckSessionAndLoadData();
            StartRealTimeListeners();
        }

        private void SubscribeToProfileUpdates()
        {
            MessagingCenter.Subscribe<Driver_Profile, string>(
                this,
                "ProfilePictureUpdatedMenu",
                (senderObj, downloadUrl) =>
                {
                    UpdateProfileImageInMenu(downloadUrl);
                });

            MessagingCenter.Subscribe<Driver_Profile, string>(
                this,
                "ProfilePictureRemovedMenu",
                (senderObj, userId) =>
                {
                    UpdateProfileImageInMenu("");
                });
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<Driver_Profile, string>(this, "ProfilePictureUpdatedMenu");
            MessagingCenter.Unsubscribe<Driver_Profile, string>(this, "ProfilePictureRemovedMenu");
            _accountStatusListener?.Dispose();
            _documentStatusListener?.Dispose();
        }

        private void StartRealTimeListeners()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentDriverId)) return;

                _documentStatusListener = _firebaseConnection.ListenForDriverDocumentStatus(
                    _currentDriverId,
                    async (docStatus, regCompleted, rejectionReason) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            _documentStatus = docStatus;
                            _isDriverApproved = regCompleted && docStatus == "Approved";
                            await UpdateMenuState();
                        });
                    });

                Device.StartTimer(TimeSpan.FromSeconds(3), () =>
                {
                    if (string.IsNullOrEmpty(_currentDriverId)) return false;

                    Task.Run(async () =>
                    {
                        try
                        {
                            var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                            if (driver != null)
                            {
                                string newAccountStatus = driver.AccountStatus ?? "Active";
                                string newSuspensionReason = driver.SuspensionReason ?? "";
                                string newSuspensionUntil = driver.SuspendedUntil ?? "";
                                string newDeactivationReason = driver.DeactivationReason ?? "";

                                if (_accountStatus != newAccountStatus ||
                                    _suspensionReason != newSuspensionReason ||
                                    _deactivationReason != newDeactivationReason)
                                {
                                    _accountStatus = newAccountStatus;
                                    _suspensionReason = newSuspensionReason;
                                    _suspensionUntil = newSuspensionUntil;
                                    _deactivationReason = newDeactivationReason;

                                    MainThread.BeginInvokeOnMainThread(async () =>
                                    {
                                        await UpdateMenuState();
                                    });
                                }
                            }
                        }
                        catch { }
                    });

                    return true;
                });
            }
            catch { }
        }

        private async Task UpdateMenuState()
        {
            try
            {
                bool isEnabled = ShouldEnableTricycleProfile();

                TricycleProfileBorder.Opacity = isEnabled ? 1f : 0.5f;
                TricycleLabel.TextColor = isEnabled ? Color.FromArgb("#333333") : Color.FromArgb("#808080");
                TricycleProfileBorder.BackgroundColor = isEnabled ? Colors.White : Color.FromArgb("#F5F5F5");
            }
            catch { }
        }

        private bool ShouldEnableTricycleProfile()
        {
            if (_accountStatus == "Suspended") return false;
            if (_accountStatus == "Deactivated") return false;
            if (_documentStatus != "Approved") return false;
            if (!_isDriverApproved) return false;
            return true;
        }

        private async Task CheckSessionAndLoadData()
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again.", "warning", true);
                    await Navigation.PopToRootAsync();
                    Application.Current.MainPage = new NavigationPage(new GetStarted());
                    return;
                }

                await LoadDriverData();
            }
            catch (Exception ex)
            {
                await ShowModal("Error", $"Session check failed: {ex.Message}", "error", false);
            }
        }

        private async Task LoadDriverData()
        {
            try
            {
                _currentDriverId = AppSession.GetDriverId();
                string mobileNumber = AppSession.GetDriverMobileNumber();

                if (string.IsNullOrEmpty(_currentDriverId))
                {
                    await ShowModal("Error", "Driver data not found.", "error", false);
                    return;
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);

                if (driver != null)
                {
                    UpdateDriverInterface(driver, mobileNumber);

                    _documentStatus = driver.DocumentStatus ?? "Pending";
                    _accountStatus = driver.AccountStatus ?? "Active";
                    _suspensionReason = driver.SuspensionReason ?? "";
                    _suspensionUntil = driver.SuspendedUntil ?? "";
                    _deactivationReason = driver.DeactivationReason ?? "";
                    _isDriverApproved = driver.RegistrationCompleted && _documentStatus == "Approved";

                    await UpdateMenuState();
                    await LoadProfileImageInMenu(driver);
                }
                else
                {
                    await ShowModal("Error", "Failed to load driver data.", "error", false);
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Error", $"Failed to load driver info: {ex.Message}", "error", false);
            }
        }

        private async Task LoadProfileImageInMenu(Driver driver)
        {
            try
            {
                if (!string.IsNullOrEmpty(driver.ProfileImageUrl))
                {
                    _profileImageUrl = driver.ProfileImageUrl;

                    if (Uri.IsWellFormedUriString(_profileImageUrl, UriKind.Absolute))
                    {
                        DriverProfileImage.Source = ImageSource.FromUri(new Uri(_profileImageUrl));
                    }
                    else
                    {
                        DriverProfileImage.Source = "profile_icon.png";
                    }
                }
                else
                {
                    DriverProfileImage.Source = "profile_icon.png";
                }
            }
            catch
            {
                DriverProfileImage.Source = "profile_icon.png";
            }
        }

        private void UpdateProfileImageInMenu(string imageUrl)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(imageUrl) && Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
                    {
                        DriverProfileImage.Source = ImageSource.FromUri(new Uri(imageUrl));
                    }
                    else
                    {
                        DriverProfileImage.Source = "profile_icon.png";
                    }
                }
                catch
                {
                    DriverProfileImage.Source = "profile_icon.png";
                }
            });
        }

        private void UpdateDriverInterface(Driver driver, string mobileNumber)
        {
            string displayName = GetDisplayName(driver);
            DriverNameLabel.Text = displayName;

            string formattedPhone = FormatPhoneNumberForDisplay(mobileNumber);
            DriverPhoneLabel.Text = formattedPhone;
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

        private string FormatPhoneNumberForDisplay(string phoneNumber)
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

        private async void OnEditIconTapped(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Driver_Profile());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Cannot open profile: " + ex.Message, "error", false);
            }
        }

        private async void OnTricycleProfileTapped(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                if (!ShouldEnableTricycleProfile())
                {
                    string message = GetAccessDeniedMessage();
                    await ShowModal("Access Restricted", message, "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Driver_TricycleProfile());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Cannot open tricycle profile: " + ex.Message, "error", false);
            }
        }

        private string GetAccessDeniedMessage()
        {
            if (_accountStatus == "Suspended")
            {
                string untilText = "";
                if (!string.IsNullOrEmpty(_suspensionUntil))
                {
                    try
                    {
                        DateTime untilDate = DateTime.Parse(_suspensionUntil);
                        untilText = $" until {untilDate:MMMM dd, yyyy}";
                    }
                    catch { }
                }
                return $"Your account is suspended{untilText}.\n\nTricycle Profile is not available while suspended.";
            }

            if (_accountStatus == "Deactivated")
            {
                return "Your account has been deactivated.\n\nTricycle Profile is not available.";
            }

            if (_documentStatus == "Pending")
            {
                return "Your documents are still pending approval.\n\nTricycle Profile will be available once your account is approved.";
            }

            if (_documentStatus == "Rejected")
            {
                return "Your documents were rejected.\n\nPlease re-upload the required documents first.";
            }

            if (_documentStatus != "Approved")
            {
                return $"Document status is '{_documentStatus}'.\n\nTricycle Profile is not available until documents are approved.";
            }

            if (!_isDriverApproved)
            {
                return "Your driver account is not yet fully approved.\n\nPlease wait for admin approval.";
            }

            return "Tricycle Profile is not available at this time.";
        }

        private async void OnHelpTapped(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Driver_HelpSupport());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Cannot open help: " + ex.Message, "error", false);
            }
        }

        private async void OnAboutTapped(object sender, EventArgs e)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Driver_AboutApp());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Cannot open about: " + ex.Message, "error", false);
            }
        }

        private async void OnLogoutTapped(object sender, EventArgs e)
        {
            await ShowLogoutConfirmation();
        }

        private async Task ShowLogoutConfirmation()
        {
            _isModalOpen = true;

            string iconSource = "complaint.png";
            Color iconColor = Color.FromArgb("#DC3545");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 20,
                Padding = 25,
                Margin = 40,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                Scale = 0.7,
                TranslationY = 0,
                WidthRequest = 280,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Image
                        {
                            Source = iconSource,
                            HeightRequest = 60,
                            WidthRequest = 60,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Logout?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to log out?",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitionCollection
                            {
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                            },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button
                                {
                                    Text = "CANCEL",
                                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverMenuGridColumn(0),
                                new Button
                                {
                                    Text = "LOGOUT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverMenuGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new DriverMenuModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            var cancelButton = FindButtonInGrid(modalContent, "CANCEL");
            var logoutButton = FindButtonInGrid(modalContent, "LOGOUT");

            if (cancelButton != null)
            {
                cancelButton.Clicked += async (s, e) =>
                {
                    await AnimateModalExit(modalPage, modalContent, blurOverlay, false);
                };
            }

            if (logoutButton != null)
            {
                logoutButton.Clicked += async (s, e) =>
                {
                    logoutButton.IsEnabled = false;
                    await AnimateModalExit(modalPage, modalContent, blurOverlay, true);
                };
            }
        }

        private Button FindButtonInGrid(object element, string text)
        {
            if (element is Button btn && btn.Text == text)
                return btn;

            if (element is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    var result = FindButtonInGrid(child, text);
                    if (result != null)
                        return result;
                }
            }

            if (element is ContentView cv && cv.Content != null)
            {
                return FindButtonInGrid(cv.Content, text);
            }

            if (element is Frame frame && frame.Content != null)
            {
                return FindButtonInGrid(frame.Content, text);
            }

            return null;
        }

        private async Task AnimateModalExit(DriverMenuModalPage modalPage, Frame modalContent, Grid blurOverlay, bool logout)
        {
            await Task.WhenAll(
                modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                modalContent.FadeTo(0, 200, Easing.CubicIn),
                modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();

            if (logout)
            {
                _isLoggingOut = true;
                AppSession.LogoutAll();
                await ShowSuccessModalAndWait();
            }
        }

        private async Task ShowSuccessModalAndWait()
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            string iconSource = "happy.png";
            Color iconColor = Color.FromArgb("#2E7D32");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 20,
                Padding = 25,
                Margin = 40,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                Scale = 0.7,
                TranslationY = 0,
                WidthRequest = 280,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Image
                        {
                            Source = iconSource,
                            HeightRequest = 60,
                            WidthRequest = 60,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Logged Out",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "You have been logged out successfully.",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Button
                        {
                            Text = "OK",
                            BackgroundColor = iconColor,
                            TextColor = Colors.White,
                            CornerRadius = 10,
                            HeightRequest = 45,
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold
                        }
                    }
                }
            };

            var modalPage = new DriverMenuSuccessModalPage(modalContent, blurOverlay, tcs);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            var button = FindButtonInGrid(modalContent, "OK");
            if (button != null)
            {
                button.Clicked += async (s, e) =>
                {
                    button.IsEnabled = false;
                    await Task.WhenAll(
                        modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                        modalContent.FadeTo(0, 200, Easing.CubicIn),
                        modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                        blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                    );
                    await Navigation.PopModalAsync();
                    tcs.SetResult(true);
                };
            }

            await tcs.Task;
            NavigateToGetStarted();
        }

        public void NavigateToGetStarted()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Application.Current.MainPage = new GetStarted();
            });
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "document.png" : "sad.png";
            Color iconColor = type == "success" ? Color.FromArgb("#2E7D32") :
                             type == "warning" ? Color.FromArgb("#FF9800") :
                             type == "info" ? Color.FromArgb("#2196F3") : Color.FromArgb("#D32F2F");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 20,
                Padding = 25,
                Margin = 40,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                Scale = 0.7,
                TranslationY = 0,
                WidthRequest = 280,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Image
                        {
                            Source = iconSource,
                            HeightRequest = 60,
                            WidthRequest = 60,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = title,
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = message,
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Button
                        {
                            Text = "OK",
                            BackgroundColor = iconColor,
                            TextColor = Colors.White,
                            CornerRadius = 10,
                            HeightRequest = 45,
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold
                        }
                    }
                }
            };

            var modalPage = new DriverMenuModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            var button = FindButtonInGrid(modalContent, "OK");
            if (button != null)
            {
                button.Clicked += async (s, e) =>
                {
                    button.IsEnabled = false;
                    await Task.WhenAll(
                        modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                        modalContent.FadeTo(0, 200, Easing.CubicIn),
                        modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                        blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                    );
                    await Navigation.PopModalAsync();
                    _isModalOpen = false;

                    if (closePageOnOk)
                    {
                        await Navigation.PopModalAsync();
                    }
                };
            }
        }
    }

    public class DriverMenuModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_Menu _parentPage;

        public DriverMenuModalPage(Frame modalContent, Grid blurOverlay, Driver_Menu parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.WhenAll(
                    _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                    _modalContent.FadeTo(0, 200, Easing.CubicIn),
                    _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                    _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                );
                await Navigation.PopModalAsync();
            });
            return true;
        }
    }

    public class DriverMenuSuccessModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private TaskCompletionSource<bool> _tcs;
        private bool _isNavigating = false;

        public DriverMenuSuccessModalPage(Frame modalContent, Grid blurOverlay, TaskCompletionSource<bool> tcs)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _tcs = tcs;

            BackgroundColor = Colors.Transparent;

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        protected override bool OnBackButtonPressed()
        {
            if (_isNavigating) return true;

            _isNavigating = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.WhenAll(
                    _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                    _modalContent.FadeTo(0, 200, Easing.CubicIn),
                    _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                    _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                );
                await Navigation.PopModalAsync();
                _tcs.SetResult(true);
            });
            return true;
        }
    }

    public static class DriverMenuGridExtensions
    {
        public static Button WithDriverMenuGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}