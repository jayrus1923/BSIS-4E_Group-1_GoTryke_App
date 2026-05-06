using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_PersonalInformation : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string? _currentMobileNumber;
        private bool _isProcessing = false;
        private bool _isModalOpen = false;

        private string _originalFirstName = "";
        private string _originalMiddleName = "";
        private string _originalLastName = "";
        private string _originalGender = "";

        public Commuter_PersonalInformation()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadUserData();
        }

        private async Task LoadUserData()
        {
            try
            {
                if (!AppSession.IsLoggedIn())
                {
                    await ShowModal("Notice", "Please login first.", "warning", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                string commuterId = AppSession.GetCommuterId();
                _currentMobileNumber = AppSession.GetMobileNumber();

                if (string.IsNullOrEmpty(commuterId))
                {
                    await ShowModal("Notice", "User data not found.", "warning", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                var commuter = await _firebaseConnection.GetCommuterByIdAsync(commuterId);

                if (commuter != null)
                {
                    FirstNameEntry.Text = commuter.FirstName ?? "";
                    MiddleNameEntry.Text = commuter.MiddleName ?? "";
                    LastNameEntry.Text = commuter.LastName ?? "";

                    _originalFirstName = commuter.FirstName ?? "";
                    _originalMiddleName = commuter.MiddleName ?? "";
                    _originalLastName = commuter.LastName ?? "";

                    SetGenderSelection(commuter.Gender);
                    _originalGender = GetSelectedGender();
                }
                else
                {
                    await ShowModal("Notice", "Unable to load your information. Please try again.", "warning", false);
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Notice", $"Something went wrong: {ex.Message}", "warning", false);
            }
        }

        private void SetGenderSelection(string gender)
        {
            MaleRadioButton.IsChecked = false;
            FemaleRadioButton.IsChecked = false;
            PreferNotToSayRadioButton.IsChecked = false;

            if (string.IsNullOrEmpty(gender) || gender == "Unspecified")
            {
                PreferNotToSayRadioButton.IsChecked = true;
                return;
            }

            switch (gender.ToLower())
            {
                case "male":
                    MaleRadioButton.IsChecked = true;
                    break;
                case "female":
                    FemaleRadioButton.IsChecked = true;
                    break;
                case "prefer not to say":
                    PreferNotToSayRadioButton.IsChecked = true;
                    break;
                default:
                    PreferNotToSayRadioButton.IsChecked = true;
                    break;
            }
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
                                }.WithCommuterPersonalInfoGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithCommuterPersonalInfoGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new CommuterPersonalInfoExitConfirmationModalPage(modalContent, blurOverlay, this);
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

        private void OnMaleClicked(object? sender, EventArgs e)
        {
            MaleRadioButton.IsChecked = true;
            FemaleRadioButton.IsChecked = false;
            PreferNotToSayRadioButton.IsChecked = false;
        }

        private void OnFemaleClicked(object? sender, EventArgs e)
        {
            MaleRadioButton.IsChecked = false;
            FemaleRadioButton.IsChecked = true;
            PreferNotToSayRadioButton.IsChecked = false;
        }

        private void OnPreferNotToSayClicked(object? sender, EventArgs e)
        {
            MaleRadioButton.IsChecked = false;
            FemaleRadioButton.IsChecked = false;
            PreferNotToSayRadioButton.IsChecked = true;
        }

        private bool HasChanges()
        {
            string currentGender = GetSelectedGender();

            return FirstNameEntry.Text != _originalFirstName ||
                   MiddleNameEntry.Text != _originalMiddleName ||
                   LastNameEntry.Text != _originalLastName ||
                   currentGender != _originalGender;
        }

        private async void OnSaveChangesClicked(object? sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            if (!HasChanges())
            {
                await ShowModal("No Changes", "No changes were made to your personal information.", "info", false);
                return;
            }

            string firstName = FirstNameEntry.Text?.Trim() ?? "";
            string middleName = MiddleNameEntry.Text?.Trim() ?? "";
            string lastName = LastNameEntry.Text?.Trim() ?? "";
            string selectedGender = GetSelectedGender();

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                await ShowModal("Required Field", "Please enter your first name and last name.", "warning", false);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedGender))
            {
                await ShowModal("Required Field", "Please select your gender preference.", "warning", false);
                return;
            }

            _isProcessing = true;
            _isModalOpen = true;
            ShowProcessing("Saving...");

            try
            {
                string commuterId = AppSession.GetCommuterId();

                if (string.IsNullOrEmpty(commuterId))
                {
                    HideProcessing();
                    _isProcessing = false;
                    _isModalOpen = false;
                    await ShowModal("Notice", "Your session has expired. Please login again.", "warning", true);
                    await Navigation.PushModalAsync(new Commuter_MobileNumber());
                    return;
                }

                var updatedCommuter = new Commuter
                {
                    FirstName = firstName,
                    MiddleName = middleName,
                    LastName = lastName,
                    Gender = selectedGender
                };

                await _firebaseConnection.UpdatePersonalInfoAsync(commuterId, updatedCommuter);

                HideProcessing();
                _isProcessing = false;
                _isModalOpen = false;

                await ShowModal("Success", "Your personal information has been updated successfully!", "success", true);
            }
            catch (Exception ex)
            {
                HideProcessing();
                _isProcessing = false;
                _isModalOpen = false;
                await ShowModal("Notice", $"Unable to update your information: {ex.Message}", "warning", false);
            }
        }

        private string GetSelectedGender()
        {
            if (MaleRadioButton.IsChecked) return "Male";
            if (FemaleRadioButton.IsChecked) return "Female";
            if (PreferNotToSayRadioButton.IsChecked) return "Prefer not to say";
            return "";
        }

        private void ShowProcessing(string message = "Saving...")
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

            var modal = new CommuterPersonalInfoCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }
    }

    public class CommuterPersonalInfoCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_PersonalInformation _parentPage;

        public CommuterPersonalInfoCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_PersonalInformation parentPage)
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

    public class CommuterPersonalInfoExitConfirmationModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_PersonalInformation _parentPage;

        public CommuterPersonalInfoExitConfirmationModalPage(Frame modalContent, Grid blurOverlay, Commuter_PersonalInformation parentPage)
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

    public static class CommuterPersonalInfoGridExtensions
    {
        public static Button WithCommuterPersonalInfoGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}