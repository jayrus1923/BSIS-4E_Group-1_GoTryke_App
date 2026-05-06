using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_Menu : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _profileImageUrl = string.Empty;
        private bool _isModalOpen = false;
        private bool _isLoggingOut = false;

        public Commuter_Menu()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_isLoggingOut) return;

            SubscribeToProfileUpdates();
            await CheckSessionAndLoadData();
        }

        private void SubscribeToProfileUpdates()
        {
            MessagingCenter.Subscribe<Commuter_Profile, string>(
                this,
                "ProfilePictureUpdatedMenuCommuter",
                (senderObj, downloadUrl) =>
                {
                    UpdateProfileImageInMenu(downloadUrl);
                });

            MessagingCenter.Subscribe<Commuter_Profile, string>(
                this,
                "ProfilePictureRemovedMenuCommuter",
                (senderObj, userId) =>
                {
                    UpdateProfileImageInMenu("");
                });
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<Commuter_Profile, string>(this, "ProfilePictureUpdatedMenuCommuter");
            MessagingCenter.Unsubscribe<Commuter_Profile, string>(this, "ProfilePictureRemovedMenuCommuter");
        }

        private async Task CheckSessionAndLoadData()
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again.", "warning", true);
                    await RedirectToLogin();
                    return;
                }

                await LoadUserData();
            }
            catch (Exception ex)
            {
                await ShowModal("Error", $"Session check failed: {ex.Message}", "error", false);
            }
        }

        private async Task LoadUserData()
        {
            try
            {
                string commuterId = AppSession.GetCommuterId();
                string mobileNumber = AppSession.GetCommuterMobileNumber();

                if (string.IsNullOrEmpty(commuterId))
                {
                    await ShowModal("Error", "User data not found.", "error", false);
                    return;
                }

                if (!string.IsNullOrEmpty(mobileNumber))
                {
                    string formattedNumber = FormatPhoneNumber(mobileNumber);
                    PhoneNumberLabel.Text = formattedNumber;
                }

                var commuter = await _firebaseConnection.GetCommuterByIdAsync(commuterId);

                if (commuter != null)
                {
                    string fullName = GetDisplayName(commuter);
                    UserNameLabel.Text = string.IsNullOrEmpty(fullName) ? "User" : fullName;
                    await LoadProfileImageInMenu(commuter);
                }
                else
                {
                    UserNameLabel.Text = "User";
                    CommuterProfileImage.Source = "profile_icon.png";
                }
            }
            catch
            {
                UserNameLabel.Text = "User";
                CommuterProfileImage.Source = "profile_icon.png";
            }
        }

        private async Task LoadProfileImageInMenu(Commuter commuter)
        {
            try
            {
                if (!string.IsNullOrEmpty(commuter.ProfileImageUrl))
                {
                    _profileImageUrl = commuter.ProfileImageUrl;

                    if (Uri.IsWellFormedUriString(_profileImageUrl, UriKind.Absolute))
                    {
                        CommuterProfileImage.Source = ImageSource.FromUri(new Uri(_profileImageUrl));
                    }
                    else
                    {
                        CommuterProfileImage.Source = "profile_icon.png";
                    }
                }
                else
                {
                    CommuterProfileImage.Source = "profile_icon.png";
                }
            }
            catch
            {
                CommuterProfileImage.Source = "profile_icon.png";
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
                        CommuterProfileImage.Source = ImageSource.FromUri(new Uri(imageUrl));
                    }
                    else
                    {
                        CommuterProfileImage.Source = "profile_icon.png";
                    }
                }
                catch
                {
                    CommuterProfileImage.Source = "profile_icon.png";
                }
            });
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
                return "+63 900 000 0000";

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

        private async Task RedirectToLogin()
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ShowModal("Session Expired", "Your session has expired. Please login again.", "warning", true);
                    Application.Current.MainPage = new GetStarted();
                });
            }
            catch { }
        }

        private async void OnEditProfileTapped(object sender, TappedEventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_Profile());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Cannot open profile: " + ex.Message, "error", false);
            }
        }

        private async void OnPaymentOptionsTapped(object sender, TappedEventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_PaymentOption());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Unable to open Payment Options.\n" + ex.Message, "error", false);
            }
        }

        private async void OnHelpTapped(object sender, TappedEventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_HelpSupport());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Cannot open Help: " + ex.Message, "error", false);
            }
        }

        private async void OnAboutTapped(object sender, TappedEventArgs e)
        {
            try
            {
                if (!AppSession.IsCommuterLoggedIn())
                {
                    await ShowModal("Session Expired", "Please login again", "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_AboutApp());
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Cannot open About: " + ex.Message, "error", false);
            }
        }

        private async void OnLogoutTapped(object sender, TappedEventArgs e)
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
                                }.WithMenuGridColumn(0),
                                new Button
                                {
                                    Text = "LOGOUT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithMenuGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new CommuterMenuModalPage(modalContent, blurOverlay, this);
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

        private async Task AnimateModalExit(CommuterMenuModalPage modalPage, Frame modalContent, Grid blurOverlay, bool logout)
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

            var modalPage = new CommuterMenuSuccessModalPage(modalContent, blurOverlay, tcs);
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

            var modalPage = new CommuterMenuModalPage(modalContent, blurOverlay, this);
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

    public class CommuterMenuModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_Menu _parentPage;

        public CommuterMenuModalPage(Frame modalContent, Grid blurOverlay, Commuter_Menu parentPage)
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

    public class CommuterMenuSuccessModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private TaskCompletionSource<bool> _tcs;
        private bool _isNavigating = false;

        public CommuterMenuSuccessModalPage(Frame modalContent, Grid blurOverlay, TaskCompletionSource<bool> tcs)
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

    public static class CommuterMenuGridExtensions
    {
        public static Button WithMenuGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}