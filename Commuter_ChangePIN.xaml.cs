using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_ChangePIN : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private bool _isProcessing = false;
        private bool _isModalOpen = false;

        public Commuter_ChangePIN()
        {
            InitializeComponent();
        }

        private async void OnBackButtonClicked(object? sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;
            await ShowExitConfirmation();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_isProcessing && !_isModalOpen)
                {
                    await ShowExitConfirmation();
                }
            });
            return true;
        }

        private async Task ShowExitConfirmation()
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
                            Text = "Exit Editing?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Your changes will not be saved if you exit.",
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
                                }.WithCommuterChangePinGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithCommuterChangePinGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new CommuterChangePinExitConfirmationModalPage(modalContent, blurOverlay, this);
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

        private string ValidatePinStrength(string pin)
        {
            if (pin.Length != 6)
                return "PIN must be exactly 6 digits";

            if (Regex.IsMatch(pin, @"^(.)\1+$"))
                return "PIN cannot have all same digits";

            if (IsSequential(pin))
                return "PIN cannot be sequential numbers";

            if (IsSimplePattern(pin))
                return "PIN is too predictable";

            return string.Empty;
        }

        private bool IsSequential(string pin)
        {
            int ascending = 0, descending = 0;
            for (int i = 0; i < pin.Length - 1; i++)
            {
                int current = pin[i] - '0';
                int next = pin[i + 1] - '0';
                if (next == current + 1) ascending++;
                if (next == current - 1) descending++;
            }
            return ascending == 5 || descending == 5;
        }

        private bool IsSimplePattern(string pin)
        {
            bool isAlternating = true;
            for (int i = 2; i < pin.Length; i++)
                if (pin[i] != pin[i % 2]) { isAlternating = false; break; }

            bool isMirrored = true;
            for (int i = 0; i < pin.Length / 2; i++)
                if (pin[i] != pin[pin.Length - 1 - i]) isMirrored = false;

            return isAlternating || isMirrored;
        }

        private async void OnUpdatePinClicked(object? sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            string currentPin = CurrentPinEntry.Text?.Trim() ?? "";
            string newPin = NewPinEntry.Text?.Trim() ?? "";
            string confirmPin = ConfirmPinEntry.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(currentPin))
            {
                await ShowModal("Required Field", "Please enter your current PIN.", "warning", false);
                return;
            }

            if (currentPin.Length != 6)
            {
                await ShowModal("Invalid PIN", "Current PIN must be exactly 6 digits.", "warning", false);
                return;
            }

            if (string.IsNullOrEmpty(newPin))
            {
                await ShowModal("Required Field", "Please enter your new PIN.", "warning", false);
                return;
            }

            string validationMessage = ValidatePinStrength(newPin);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                await ShowModal("Invalid PIN", validationMessage, "warning", false);
                return;
            }

            if (newPin != confirmPin)
            {
                await ShowModal("PIN Mismatch", "New PIN and confirm PIN do not match.", "warning", false);
                return;
            }

            if (currentPin == newPin)
            {
                await ShowModal("Invalid PIN", "New PIN must be different from current PIN.", "warning", false);
                return;
            }

            _isProcessing = true;
            _isModalOpen = true;
            ShowProcessing("Verifying...");

            try
            {
                string commuterId = AppSession.GetCommuterId();

                if (string.IsNullOrEmpty(commuterId))
                {
                    HideProcessing();
                    _isProcessing = false;
                    _isModalOpen = false;
                    await ShowModal("Session Expired", "Please login again.", "warning", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                var commuter = await _firebaseConnection.GetCommuterByIdAsync(commuterId);

                if (commuter == null)
                {
                    HideProcessing();
                    _isProcessing = false;
                    _isModalOpen = false;
                    await ShowModal("Error", "User not found.", "warning", false);
                    return;
                }

                if (commuter.Pin != currentPin)
                {
                    HideProcessing();
                    _isProcessing = false;
                    _isModalOpen = false;
                    await ShowModal("Incorrect PIN", "Current PIN is incorrect.", "warning", false);
                    CurrentPinEntry.Text = "";
                    return;
                }

                ShowProcessing("Updating PIN...");

                bool success = await _firebaseConnection.UpdateCommuterPinCodeAsync(commuterId, newPin);

                HideProcessing();
                _isProcessing = false;

                if (success)
                {
                    await ShowModal("Success", "Your PIN has been updated successfully!", "success", true);
                }
                else
                {
                    _isModalOpen = false;
                    await ShowModal("Error", "Failed to update PIN. Please try again.", "warning", false);
                }
            }
            catch (Exception ex)
            {
                HideProcessing();
                _isProcessing = false;
                _isModalOpen = false;
                await ShowModal("Error", $"Unable to update PIN: {ex.Message}", "warning", false);
            }
        }

        private void ShowProcessing(string message = "Updating PIN...")
        {
            ProcessingLabel.Text = message;
            ProcessingOverlay.IsVisible = true;
            ProcessingIndicator.IsRunning = true;
        }

        private void HideProcessing()
        {
            ProcessingOverlay.IsVisible = false;
            ProcessingIndicator.IsRunning = false;
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
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

            var modal = new CommuterChangePinCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }
    }

    public class CommuterChangePinCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_ChangePIN _parentPage;

        public CommuterChangePinCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_ChangePIN parentPage)
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

    public class CommuterChangePinExitConfirmationModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_ChangePIN _parentPage;

        public CommuterChangePinExitConfirmationModalPage(Frame modalContent, Grid blurOverlay, Commuter_ChangePIN parentPage)
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
                    await _parentPage.Navigation.PopModalAsync();
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

    public static class CommuterChangePinGridExtensions
    {
        public static Button WithCommuterChangePinGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}