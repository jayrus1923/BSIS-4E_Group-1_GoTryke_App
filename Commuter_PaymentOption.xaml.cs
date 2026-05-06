using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_PaymentOption : ContentPage
    {
        private string _selectedPaymentMethod;
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _currentRideRequestId;
        private bool _isModalOpen = false;
        private string _originalPaymentMethod;

        public event EventHandler<string> PaymentMethodSelected;

        public Commuter_PaymentOption(string currentMethod = "Cash", string rideRequestId = null)
        {
            InitializeComponent();
            _selectedPaymentMethod = currentMethod;
            _currentRideRequestId = rideRequestId;
            _originalPaymentMethod = currentMethod;
            LoadDefaultPaymentMethod();
        }

        private async void LoadDefaultPaymentMethod()
        {
            try
            {
                string commuterId = AppSession.GetCommuterId();
                if (!string.IsNullOrEmpty(commuterId))
                {
                    string defaultMethod = await _firebaseConnection.GetDefaultPaymentMethodAsync(commuterId);

                    if (!string.IsNullOrEmpty(defaultMethod))
                    {
                        _selectedPaymentMethod = defaultMethod;
                        _originalPaymentMethod = defaultMethod;
                    }
                    else if (string.IsNullOrEmpty(_selectedPaymentMethod))
                    {
                        _selectedPaymentMethod = "Cash";
                        _originalPaymentMethod = "Cash";
                    }

                    UpdateUI();
                }
                else
                {
                    if (string.IsNullOrEmpty(_selectedPaymentMethod))
                    {
                        _selectedPaymentMethod = "Cash";
                        _originalPaymentMethod = "Cash";
                    }
                    UpdateUI();
                }
            }
            catch
            {
                if (string.IsNullOrEmpty(_selectedPaymentMethod))
                {
                    _selectedPaymentMethod = "Cash";
                    _originalPaymentMethod = "Cash";
                }
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            CashCheckmark.IsVisible = false;
            GCashCheckmark.IsVisible = false;

            CashFrame.BorderColor = Color.FromArgb("#E5E5E5");
            GCashFrame.BorderColor = Color.FromArgb("#E5E5E5");

            if (_selectedPaymentMethod == "Cash")
            {
                CashCheckmark.IsVisible = true;
                CashFrame.BorderColor = Color.FromArgb("#2E7D32");
            }
            else if (_selectedPaymentMethod == "GCash")
            {
                GCashCheckmark.IsVisible = true;
                GCashFrame.BorderColor = Color.FromArgb("#2E7D32");
            }
        }

        private void OnCashTapped(object sender, EventArgs e)
        {
            _selectedPaymentMethod = "Cash";
            UpdateUI();
        }

        private void OnGCashTapped(object sender, EventArgs e)
        {
            _selectedPaymentMethod = "GCash";
            UpdateUI();
        }

        private async void OnSaveButtonClicked(object sender, EventArgs e)
        {
            string commuterId = AppSession.GetCommuterId();
            if (string.IsNullOrEmpty(commuterId))
            {
                await ShowModal("Error", "Please login first", "warning", false);
                return;
            }

            if (_selectedPaymentMethod == _originalPaymentMethod && string.IsNullOrEmpty(_currentRideRequestId))
            {
                await ShowModal("No Changes", "Payment method is already set to " + _selectedPaymentMethod, "info", false);
                return;
            }

            await ShowSaveConfirmation();
        }

        private async Task ShowSaveConfirmation()
        {
            _isModalOpen = true;

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
                            Text = "Save Payment Method",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = $"Set {_selectedPaymentMethod} as your default payment method?",
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
                                }.WithPaymentOptionGridColumn(0),
                                new Button
                                {
                                    Text = "SAVE",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithPaymentOptionGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new PaymentOptionConfirmModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async void ConfirmSave()
        {
            _isModalOpen = false;
            await SavePaymentMethod();
        }

        private async Task SavePaymentMethod()
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                string commuterId = AppSession.GetCommuterId();

                bool success = await _firebaseConnection.SaveDefaultPaymentMethodAsync(commuterId, _selectedPaymentMethod);

                if (!string.IsNullOrEmpty(_currentRideRequestId))
                {
                    await _firebaseConnection.UpdatePaymentMethodAsync(_currentRideRequestId, _selectedPaymentMethod);
                }

                LoadingOverlay.IsVisible = false;

                if (success)
                {
                    PaymentMethodSelected?.Invoke(this, _selectedPaymentMethod);
                    await ShowSuccessModal();
                }
                else
                {
                    await ShowModal("Error", "Failed to save payment method. Please try again.", "error", false);
                }
            }
            catch
            {
                LoadingOverlay.IsVisible = false;
                await ShowModal("Connection Error", "Failed to save. Please check your internet.", "warning", false);
            }
        }

        private async Task ShowSuccessModal()
        {
            _isModalOpen = true;

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
                            Text = "Success!",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = $"Payment method set to {_selectedPaymentMethod}",
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

            var modalPage = new PaymentOptionSuccessModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async void CloseSuccessModal()
        {
            _isModalOpen = false;
            await ClosePage();
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            if (_selectedPaymentMethod == _originalPaymentMethod && string.IsNullOrEmpty(_currentRideRequestId))
            {
                await ClosePage();
                return;
            }

            await ShowBackConfirmation();
        }

        private async Task ShowBackConfirmation()
        {
            _isModalOpen = true;

            string iconSource = "complaint.png";
            Color iconColor = Color.FromArgb("#FF9800");

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
                            Text = "Unsaved Changes",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "You have unsaved changes. Do you want to save before leaving?",
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
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                            },
                            ColumnSpacing = 8,
                            Children =
                            {
                                new Button
                                {
                                    Text = "LEAVE",
                                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 40,
                                    FontSize = 13,
                                    FontAttributes = FontAttributes.Bold
                                }.WithPaymentOptionGridColumn(0),
                                new Button
                                {
                                    Text = "CANCEL",
                                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 40,
                                    FontSize = 13,
                                    FontAttributes = FontAttributes.Bold
                                }.WithPaymentOptionGridColumn(1),
                                new Button
                                {
                                    Text = "SAVE",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 40,
                                    FontSize = 13,
                                    FontAttributes = FontAttributes.Bold
                                }.WithPaymentOptionGridColumn(2)
                            }
                        }
                    }
                }
            };

            var modalPage = new PaymentOptionBackModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async void HandleBackAction(string action)
        {
            _isModalOpen = false;
            if (action == "SAVE")
            {
                await SavePaymentMethod();
            }
            else if (action == "LEAVE")
            {
                await ClosePage();
            }
        }

        private async Task ClosePage()
        {
            if (!string.IsNullOrEmpty(_currentRideRequestId))
            {
                PaymentMethodSelected?.Invoke(this, _selectedPaymentMethod);
                await _firebaseConnection.UpdatePaymentMethodAsync(_currentRideRequestId, _selectedPaymentMethod);
            }

            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync();
            else
                await Navigation.PopAsync();
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

            var modalPage = new PaymentOptionModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modalPage);

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
    }

    public class PaymentOptionModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_PaymentOption _parentPage;

        public PaymentOptionModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_PaymentOption parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var button = FindButton(modalContent.Content);
            if (button != null)
            {
                button.Clicked += async (s, e) =>
                {
                    button.IsEnabled = false;
                    await AnimateModalExit();
                };
            }

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private Button FindButton(object element)
        {
            if (element is Button btn)
                return btn;

            if (element is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    var result = FindButton(child);
                    if (result != null)
                        return result;
                }
            }

            if (element is ContentView cv && cv.Content != null)
            {
                return FindButton(cv.Content);
            }

            if (element is Frame frame && frame.Content != null)
            {
                return FindButton(frame.Content);
            }

            return null;
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
                await AnimateModalExit();
            });
            return true;
        }
    }

    public class PaymentOptionConfirmModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_PaymentOption _parentPage;

        public PaymentOptionConfirmModalPage(Frame modalContent, Grid blurOverlay, Commuter_PaymentOption parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                confirmButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();

            if (confirm)
            {
                _parentPage.ConfirmSave();
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

    public class PaymentOptionSuccessModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_PaymentOption _parentPage;

        public PaymentOptionSuccessModalPage(Frame modalContent, Grid blurOverlay, Commuter_PaymentOption parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var button = FindButton(modalContent.Content);
            if (button != null)
            {
                button.Clicked += async (s, e) =>
                {
                    button.IsEnabled = false;
                    await AnimateModalExit();
                };
            }

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private Button FindButton(object element)
        {
            if (element is Button btn)
                return btn;

            if (element is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    var result = FindButton(child);
                    if (result != null)
                        return result;
                }
            }

            if (element is ContentView cv && cv.Content != null)
            {
                return FindButton(cv.Content);
            }

            if (element is Frame frame && frame.Content != null)
            {
                return FindButton(frame.Content);
            }

            return null;
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
            _parentPage.CloseSuccessModal();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await AnimateModalExit();
            });
            return true;
        }
    }

    public class PaymentOptionBackModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_PaymentOption _parentPage;

        public PaymentOptionBackModalPage(Frame modalContent, Grid blurOverlay, Commuter_PaymentOption parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var leaveButton = buttonGrid.Children[0] as Button;
            var cancelButton = buttonGrid.Children[1] as Button;
            var saveButton = buttonGrid.Children[2] as Button;

            leaveButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit("LEAVE");
            };

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit("CANCEL");
            };

            saveButton.Clicked += async (s, e) =>
            {
                saveButton.IsEnabled = false;
                await AnimateModalExit("SAVE");
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(string action)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();

            if (action != "CANCEL")
            {
                _parentPage.HandleBackAction(action);
            }
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await AnimateModalExit("CANCEL");
            });
            return true;
        }
    }

    public static class PaymentOptionGridExtensions
    {
        public static Button WithPaymentOptionGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}