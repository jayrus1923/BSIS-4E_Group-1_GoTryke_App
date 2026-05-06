using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class DriverDocumentsPage : ContentPage
    {
        private Dictionary<string, string> _documents;

        public DriverDocumentsPage(Dictionary<string, string> documents)
        {
            InitializeComponent();
            _documents = documents ?? new Dictionary<string, string>();
            LoadDocuments();
        }

        private void LoadDocuments()
        {
            try
            {
                DocumentsList.Children.Clear();

                if (_documents.Count == 0)
                {
                    EmptyState.IsVisible = true;
                    return;
                }

                EmptyState.IsVisible = false;

                foreach (var doc in _documents)
                {
                    if (!string.IsNullOrEmpty(doc.Value))
                    {
                        AddDocumentItem(doc.Key, doc.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error loading documents: {ex.Message}");
            }
        }

        private void AddDocumentItem(string documentType, string documentUrl)
        {
            var documentName = GetDocumentDisplayName(documentType);

            var frame = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 10,
                Padding = 15,
                HasShadow = true
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 10
            };

            var docInfo = new StackLayout
            {
                Spacing = 5
            };

            docInfo.Children.Add(new Label
            {
                Text = documentName,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            docInfo.Children.Add(new Label
            {
                Text = "Click to view document",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            });

            var viewButton = new ImageButton
            {
                Source = "view_document.png",
                BackgroundColor = Colors.Transparent,
                HeightRequest = 30,
                WidthRequest = 30
            };

            viewButton.Clicked += async (s, e) =>
            {
                await ViewDocument(documentName, documentUrl);
            };

            grid.Add(docInfo, 0, 0);
            grid.Add(viewButton, 1, 0);

            frame.Content = grid;
            DocumentsList.Children.Add(frame);
        }

        private string GetDocumentDisplayName(string documentKey)
        {
            return documentKey.ToUpper() switch
            {
                "OR_CR" => "OR/CR Document",
                "VEHICLE_INSPECTION" => "Vehicle Inspection Report",
                "TODA_MEMBERSHIP" => "TODA Membership Certificate",
                "DRIVERS_LICENSE" => "Driver's License",
                "NBI_CLEARANCE" => "NBI Clearance",
                "BARANGAY_CLEARANCE" => "Barangay Clearance",
                "VEHICLE_PHOTO" => "Vehicle Photo",
                "PROFILE_PHOTO" => "Profile Photo",
                _ => documentKey.Replace("_", " ")
            };
        }

        private async Task ViewDocument(string documentName, string documentUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(documentUrl))
                {
                    await DisplayAlert("Error", "Document URL is empty", "OK");
                    return;
                }

                if (documentUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    documentUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    documentUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    await Navigation.PushAsync(new ViewImagePage(documentName, documentUrl));
                }
                else if (documentUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await Launcher.OpenAsync(documentUrl);
                }
                else
                {
                    await Launcher.OpenAsync(documentUrl);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error viewing document: {ex.Message}");
                await DisplayAlert("Error", $"Cannot open document: {ex.Message}", "OK");
            }
        }

        private async void OnUploadDocumentsClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PopAsync();
                await Navigation.PushAsync(new Driver_Requirements());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error navigating to upload: {ex.Message}");
            }
        }
    }
}