using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_HelpSupport : ContentPage
    {
        private string currentlyExpandedFAQ = null;
        private bool isModalOpen = false;
        private FirebaseConnection _firebaseConnection;

        public Commuter_HelpSupport()
        {
            InitializeComponent();
            _firebaseConnection = new FirebaseConnection();
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            if (isModalOpen) return;
            await Navigation.PopModalAsync();
        }

        private async void OnAccountDeletionTapped(object sender, TappedEventArgs e)
        {
            if (isModalOpen) return;

            string commuterId = Preferences.Get("CommuterId", "");
            if (string.IsNullOrEmpty(commuterId))
            {
                await ShowErrorModal("Please log in again to continue.");
                return;
            }

            var existingRequest = await _firebaseConnection.GetPendingDeletionRequestByCommuterAsync(commuterId);

            if (existingRequest != null)
            {
                await ShowAlreadyRequestedModal(existingRequest.RequestDate);
                return;
            }

            await ShowDeleteConfirmationModal();
        }

        private async Task ShowAlreadyRequestedModal(DateTime requestDate)
        {
            isModalOpen = true;

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var okButton = new Button
            {
                Text = "OK",
                BackgroundColor = Color.FromArgb("#F59E0B"),
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 48,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            okButton.Clicked += async (s, ev) =>
            {
                await CloseModal(s);
            };

            DateTime availableDate = requestDate.AddDays(5);
            string formattedDate = availableDate.ToString("MMMM dd, yyyy");

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 24,
                Padding = 24,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 320,
                Opacity = 0,
                Scale = 0.7,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Frame
                        {
                            BackgroundColor = Color.FromArgb("#FEF3C7"),
                            CornerRadius = 40,
                            Padding = 15,
                            WidthRequest = 70,
                            HeightRequest = 70,
                            HorizontalOptions = LayoutOptions.Center,
                            HasShadow = false,
                            Content = new Label
                            {
                                Text = "⏳",
                                FontSize = 32,
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.Center
                            }
                        },
                        new Label
                        {
                            Text = "Request Already Submitted",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#F59E0B"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = $"You already have a pending deletion request submitted on {requestDate:MMMM dd, yyyy}.",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#6B7280"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        new Label
                        {
                            Text = $"Please wait until {formattedDate} before submitting a new request if your current request is still pending.",
                            FontSize = 13,
                            TextColor = Color.FromArgb("#9CA3AF"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        new Label
                        {
                            Text = "Our team will process your request within 3-5 business days.",
                            FontSize = 12,
                            TextColor = Color.FromArgb("#10B981"),
                            HorizontalOptions = LayoutOptions.Center,
                            Margin = new Thickness(0, 5, 0, 0)
                        },
                        okButton
                    }
                }
            };

            var modalPage = new HelpSupportModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task ShowDeleteConfirmationModal()
        {
            var tcs = new TaskCompletionSource<bool>();
            isModalOpen = true;

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var entry = new Entry
            {
                Placeholder = "Type DELETE to confirm",
                PlaceholderColor = Color.FromArgb("#9CA3AF"),
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#1F2937"),
                FontSize = 14,
                HeightRequest = 50,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                FontAttributes = FontAttributes.None
            };

            var entryFrame = new Frame
            {
                Content = entry,
                BackgroundColor = Color.FromArgb("#F9FAFB"),
                CornerRadius = 12,
                Padding = new Thickness(12, 0, 12, 0),
                BorderColor = Color.FromArgb("#E5E7EB"),
                HasShadow = false,
                HeightRequest = 50,
                Margin = new Thickness(0, 5, 0, 0)
            };

            var confirmButton = new Button
            {
                Text = "Request Deletion",
                BackgroundColor = Color.FromArgb("#DC3545"),
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 48,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                IsEnabled = false,
                Opacity = 0.6
            };
            confirmButton.Clicked += async (s, ev) =>
            {
                await CloseModal(s);
                await SubmitDeletionRequest();
                tcs.SetResult(true);
            };

            entry.TextChanged += (s, ev) =>
            {
                string text = ev.NewTextValue?.Trim() ?? string.Empty;
                bool isValid = text.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
                confirmButton.IsEnabled = isValid;
                confirmButton.Opacity = isValid ? 1 : 0.6;

                if (isValid)
                {
                    entryFrame.BorderColor = Color.FromArgb("#10B981");
                    entryFrame.BackgroundColor = Color.FromArgb("#F0FDF4");
                }
                else
                {
                    entryFrame.BorderColor = Color.FromArgb("#E5E7EB");
                    entryFrame.BackgroundColor = Color.FromArgb("#F9FAFB");
                }
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                BackgroundColor = Color.FromArgb("#F3F4F6"),
                TextColor = Color.FromArgb("#6B7280"),
                CornerRadius = 10,
                HeightRequest = 48,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            cancelButton.Clicked += async (s, ev) =>
            {
                await CloseModal(s);
                tcs.SetResult(false);
            };

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 12
            };
            buttonGrid.Children.Add(cancelButton);
            Grid.SetColumn(cancelButton, 0);
            buttonGrid.Children.Add(confirmButton);
            Grid.SetColumn(confirmButton, 1);

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 24,
                Padding = 24,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 320,
                Opacity = 0,
                Scale = 0.7,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Frame
                        {
                            BackgroundColor = Color.FromArgb("#FEE2E2"),
                            CornerRadius = 40,
                            Padding = 15,
                            WidthRequest = 70,
                            HeightRequest = 70,
                            HorizontalOptions = LayoutOptions.Center,
                            HasShadow = false,
                            Content = new Image
                            {
                                Source = "complaint.png",
                                HeightRequest = 40,
                                WidthRequest = 40,
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.Center
                            }
                        },
                        new Label
                        {
                            Text = "Delete Account",
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#DC3545"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = "This action cannot be undone. Your request will be reviewed by admin before processing.",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#6B7280"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        new Label
                        {
                            Text = "Type DELETE to confirm",
                            FontSize = 12,
                            TextColor = Color.FromArgb("#9CA3AF"),
                            HorizontalOptions = LayoutOptions.Center,
                            Margin = new Thickness(0, 5, 0, 0)
                        },
                        entryFrame,
                        buttonGrid
                    }
                }
            };

            var modalPage = new HelpSupportModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            await tcs.Task;
        }

        private async Task SubmitDeletionRequest()
        {
            try
            {
                string commuterId = Preferences.Get("CommuterId", "");
                string mobileNumber = Preferences.Get("MobileNumber", "");

                if (string.IsNullOrEmpty(commuterId))
                {
                    await ShowErrorModal("Unable to submit request. Please log in again.");
                    return;
                }

                var existingRequest = await _firebaseConnection.GetPendingDeletionRequestByCommuterAsync(commuterId);
                if (existingRequest != null)
                {
                    await ShowAlreadyRequestedModal(existingRequest.RequestDate);
                    return;
                }

                var commuter = await _firebaseConnection.GetCommuterByIdAsync(commuterId);
                if (commuter == null)
                {
                    await ShowErrorModal("Account not found. Please contact support.");
                    return;
                }

                var deletionRequest = new DeletionRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                    CommuterId = commuterId,
                    MobileNumber = mobileNumber,
                    FullName = $"{commuter.FirstName} {commuter.LastName}".Trim(),
                    Email = commuter.Email ?? "",
                    RequestDate = DateTime.UtcNow,
                    Status = "Pending",
                    RequestReason = "User requested account deletion"
                };

                bool success = await _firebaseConnection.SubmitDeletionRequestAsync(deletionRequest);

                if (success)
                {
                    await ShowSuccessModal("Request Submitted", "Your account deletion request has been submitted. Admin will review and process within 3-5 business days.\n\nYou will receive a notification once your request is processed.");
                }
                else
                {
                    await ShowErrorModal("Unable to submit request. Please try again or contact support.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error submitting deletion request: {ex.Message}");
                await ShowErrorModal("Unable to submit request. Please try again or contact support.");
            }
        }

        private async Task ShowSuccessModal(string title, string message)
        {
            isModalOpen = true;

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var okButton = new Button
            {
                Text = "OK",
                BackgroundColor = Color.FromArgb("#10B981"),
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 48,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            okButton.Clicked += async (s, ev) =>
            {
                await CloseModal(s);
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 24,
                Padding = 24,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 300,
                Opacity = 0,
                Scale = 0.7,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Frame
                        {
                            BackgroundColor = Color.FromArgb("#DCFCE7"),
                            CornerRadius = 40,
                            Padding = 15,
                            WidthRequest = 70,
                            HeightRequest = 70,
                            HorizontalOptions = LayoutOptions.Center,
                            HasShadow = false,
                            Content = new Image
                            {
                                Source = "happy.png",
                                HeightRequest = 40,
                                WidthRequest = 40,
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.Center
                            }
                        },
                        new Label
                        {
                            Text = title,
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#10B981"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = message,
                            FontSize = 14,
                            TextColor = Color.FromArgb("#6B7280"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        okButton
                    }
                }
            };

            var modalPage = new HelpSupportModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task ShowErrorModal(string message)
        {
            isModalOpen = true;

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var okButton = new Button
            {
                Text = "OK",
                BackgroundColor = Color.FromArgb("#EF4444"),
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 48,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            okButton.Clicked += async (s, ev) =>
            {
                await CloseModal(s);
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 24,
                Padding = 24,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 300,
                Opacity = 0,
                Scale = 0.7,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Frame
                        {
                            BackgroundColor = Color.FromArgb("#FEE2E2"),
                            CornerRadius = 40,
                            Padding = 15,
                            WidthRequest = 70,
                            HeightRequest = 70,
                            HorizontalOptions = LayoutOptions.Center,
                            HasShadow = false,
                            Content = new Label
                            {
                                Text = "⚠️",
                                FontSize = 32,
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.Center
                            }
                        },
                        new Label
                        {
                            Text = "Error",
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#EF4444"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = message,
                            FontSize = 14,
                            TextColor = Color.FromArgb("#6B7280"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        okButton
                    }
                }
            };

            var modalPage = new HelpSupportModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task CloseModal(object sender)
        {
            var modalPage = (sender as Button)?.Parent?.Parent?.Parent?.Parent?.Parent as HelpSupportModalPage;
            if (modalPage == null)
            {
                var btn = sender as Button;
                if (btn != null)
                {
                    var parent = btn.Parent?.Parent?.Parent?.Parent as HelpSupportModalPage;
                    if (parent != null)
                    {
                        await parent.AnimateModalExit();
                    }
                }
            }
            else
            {
                await modalPage.AnimateModalExit();
            }
        }

        public void CloseModal()
        {
            isModalOpen = false;
        }

        private void OnBookRideTapped(object sender, TappedEventArgs e)
        {
            if (isModalOpen) return;
            ToggleFAQ("BookRide", BookRideContent, BookRideArrow, BookRideDivider);
        }

        private void OnPaymentMethodsTapped(object sender, TappedEventArgs e)
        {
            if (isModalOpen) return;
            ToggleFAQ("Payment", PaymentContent, PaymentArrow, PaymentDivider);
        }

        private void OnCancelRideTapped(object sender, TappedEventArgs e)
        {
            if (isModalOpen) return;
            ToggleFAQ("CancelRide", CancelRideContent, CancelRideArrow, CancelRideDivider);
        }

        private void OnFileComplaintTapped(object sender, TappedEventArgs e)
        {
            if (isModalOpen) return;
            ToggleFAQ("Complaint", ComplaintContent, ComplaintArrow, ComplaintDivider);
        }

        private void ToggleFAQ(string faqName, VerticalStackLayout content, Label arrow, BoxView divider)
        {
            arrow.RotateTo(arrow.Rotation == 90 ? 0 : 90, 250, Easing.SpringOut);
            content.IsVisible = !content.IsVisible;
            divider.IsVisible = content.IsVisible;

            if (currentlyExpandedFAQ == faqName)
            {
                currentlyExpandedFAQ = null;
                return;
            }

            if (currentlyExpandedFAQ != null)
            {
                switch (currentlyExpandedFAQ)
                {
                    case "BookRide":
                        BookRideContent.IsVisible = false;
                        BookRideDivider.IsVisible = false;
                        BookRideArrow.RotateTo(0, 250, Easing.SpringOut);
                        break;
                    case "Payment":
                        PaymentContent.IsVisible = false;
                        PaymentDivider.IsVisible = false;
                        PaymentArrow.RotateTo(0, 250, Easing.SpringOut);
                        break;
                    case "CancelRide":
                        CancelRideContent.IsVisible = false;
                        CancelRideDivider.IsVisible = false;
                        CancelRideArrow.RotateTo(0, 250, Easing.SpringOut);
                        break;
                    case "Complaint":
                        ComplaintContent.IsVisible = false;
                        ComplaintDivider.IsVisible = false;
                        ComplaintArrow.RotateTo(0, 250, Easing.SpringOut);
                        break;
                }
            }

            currentlyExpandedFAQ = content.IsVisible ? faqName : null;
        }
    }

    public class HelpSupportModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_HelpSupport _parentPage;

        public HelpSupportModalPage(Frame modalContent, Grid blurOverlay, Commuter_HelpSupport parentPage)
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

        public async Task AnimateModalExit()
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );
            await Navigation.PopModalAsync();
            _parentPage.CloseModal();
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

    public class DeletionRequest
    {
        public string RequestId { get; set; } = "";
        public string CommuterId { get; set; } = "";
        public string MobileNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime RequestDate { get; set; }
        public string Status { get; set; } = "Pending";
        public string RequestReason { get; set; } = "";
        public DateTime? ProcessedDate { get; set; }
        public string ProcessedBy { get; set; } = "";
        public string AdminNotes { get; set; } = "";
    }
}