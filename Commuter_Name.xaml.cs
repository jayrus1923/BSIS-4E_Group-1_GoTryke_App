using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_Name : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly string _phoneNumber;
        private readonly string _pin;
        private bool _isModalOpen = false;

        public Commuter_Name(string phoneNumber, string pin = null)
        {
            InitializeComponent();
            _phoneNumber = phoneNumber;
            _pin = pin;

            if (!string.IsNullOrEmpty(_pin))
            {
                SignUpButton.Text = "Complete Registration";
            }
            else
            {
                SignUpButton.Text = "Continue";
            }

            AttachFocus(FirstNameEntry);
            AttachFocus(MiddleNameEntry);
            AttachFocus(LastNameEntry);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await this.FadeTo(1, 300, Easing.CubicOut);
            NavigationPage.SetHasNavigationBar(this, false);

            if (Parent is NavigationPage navPage)
            {
                navPage.BarBackgroundColor = Colors.Transparent;
                navPage.BarTextColor = Colors.Transparent;
            }
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                if (!_isModalOpen)
                {
                    await ShowExitConfirmation();
                }
            });
            return true;
        }

        private async Task ShowExitConfirmation()
        {
            if (_isModalOpen) return;

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
                            Text = "Exit Registration?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to go back? Your progress will be lost.",
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
                                }.WithColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new CommuterNameExitConfirmationModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(exitModal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public void CloseModal(bool closePage)
        {
            _isModalOpen = false;
            if (closePage)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Navigation.PopModalAsync();
                });
            }
        }

        private void AttachFocus(Entry entry)
        {
            entry.Focused += OnEntryFocusChanged;
            entry.Unfocused += OnEntryFocusChanged;
        }

        private void OnEntryFocusChanged(object sender, FocusEventArgs e)
        {
            if (sender is Entry entry && entry.Parent is Border border)
            {
                border.Stroke = e.IsFocused
                    ? Color.FromArgb("#2E7D32")
                    : Color.FromArgb("#E0E0E0");
            }
        }

        private void OnNameFieldChanged(object sender, TextChangedEventArgs e)
        {
            string? firstName = FirstNameEntry.Text?.Trim();
            string? lastName = LastNameEntry.Text?.Trim();

            bool allRequiredFilled =
                !string.IsNullOrEmpty(firstName) &&
                !string.IsNullOrEmpty(lastName);

            SignUpButton.IsEnabled = allRequiredFilled;

            if (allRequiredFilled)
            {
                SignUpButton.BackgroundColor = Color.FromArgb("#2E7D32");
                SignUpButton.TextColor = Colors.White;
            }
            else
            {
                SignUpButton.BackgroundColor = Color.FromArgb("#DADADA");
                SignUpButton.TextColor = Color.FromArgb("#666666");
            }
        }

        private async void Sign_Up(object sender, EventArgs e)
        {
            string? firstName = FirstNameEntry.Text?.Trim();
            string? middleName = MiddleNameEntry.Text?.Trim();
            string? lastName = LastNameEntry.Text?.Trim();

            if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
            {
                await ShowModal("Error", "Please fill in all required fields.", "error", false);
                return;
            }

            SignUpButton.IsEnabled = false;
            SignUpButton.Text = "Saving...";

            try
            {
                var existingCommuter = await _firebaseConnection.GetCommuterByPhoneNumberAsync(_phoneNumber);
                string commuterId;

                if (existingCommuter != null)
                {
                    commuterId = existingCommuter.FirebaseKey;
                    var updatedCommuter = new Commuter
                    {
                        FirstName = firstName,
                        MiddleName = middleName,
                        LastName = lastName,
                        Gender = "Unspecified",
                        MobileNumber = _phoneNumber
                    };

                    if (!string.IsNullOrEmpty(_pin))
                    {
                        await _firebaseConnection.UpdateCommuterInfoWithPinAsync(commuterId, updatedCommuter, _pin);
                    }
                    else
                    {
                        await _firebaseConnection.UpdateCommuterInfoAsync(commuterId, updatedCommuter);
                    }
                }
                else
                {
                    commuterId = await _firebaseConnection.SaveMobileNumberTempAsync(_phoneNumber);
                    var updatedCommuter = new Commuter
                    {
                        FirstName = firstName,
                        MiddleName = middleName,
                        LastName = lastName,
                        Gender = "Unspecified"
                    };

                    if (!string.IsNullOrEmpty(_pin))
                    {
                        await _firebaseConnection.UpdateCommuterInfoWithPinAsync(commuterId, updatedCommuter, _pin);
                    }
                    else
                    {
                        await _firebaseConnection.UpdateCommuterInfoAsync(commuterId, updatedCommuter);
                    }
                }

                AppSession.LoginCommuter(commuterId, _phoneNumber);

                await ShowModal("Success", "Registration completed successfully!", "success", true);
                Application.Current.MainPage = new CommuterShell();
            }
            catch (Exception ex)
            {
                await ShowModal("Error", $"Failed to complete registration: {ex.Message}", "error", false);

                SignUpButton.IsEnabled = true;
                SignUpButton.Text = !string.IsNullOrEmpty(_pin) ? "Complete Registration" : "Continue";
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            FirstNameEntry.Focused -= OnEntryFocusChanged;
            FirstNameEntry.Unfocused -= OnEntryFocusChanged;

            MiddleNameEntry.Focused -= OnEntryFocusChanged;
            MiddleNameEntry.Unfocused -= OnEntryFocusChanged;

            LastNameEntry.Focused -= OnEntryFocusChanged;
            LastNameEntry.Unfocused -= OnEntryFocusChanged;
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "phone.png" : "sad.png";
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

            var modal = new CommuterNameModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }
    }

    public class CommuterNameModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_Name _parentPage;

        public CommuterNameModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_Name parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var button = (modalContent.Content as VerticalStackLayout).Children[3] as Button;
            button.Clicked += async (s, e) =>
            {
                button.IsEnabled = false;
                await AnimateModalExit();
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit()
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseModal(_closePageOnOk);
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var button = (_modalContent.Content as VerticalStackLayout).Children[3] as Button;
                if (button != null)
                    button.IsEnabled = false;

                await AnimateModalExit();
            });
            return true;
        }
    }

    public class CommuterNameExitConfirmationModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_Name _parentPage;

        public CommuterNameExitConfirmationModalPage(Frame modalContent, Grid blurOverlay, Commuter_Name parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var exitButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            exitButton.Clicked += async (s, e) =>
            {
                exitButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool exit)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseModal(false);

            if (exit)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        if (Navigation.ModalStack.Count > 1)
                        {
                            await Navigation.PopModalAsync();
                        }
                        else
                        {
                            Application.Current.MainPage = new Commuter_MobileNumber();
                        }
                    }
                    catch
                    {
                        Application.Current.MainPage = new Commuter_MobileNumber();
                    }
                });
            }
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await AnimateModalExit(false);
            });
            return true;
        }
    }

    public static class CommuterNameGridExtensions
    {
        public static Button WithColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}