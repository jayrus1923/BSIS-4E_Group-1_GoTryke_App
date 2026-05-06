using Microsoft.Maui.Controls;
using System;

namespace ServiceCo
{
    public partial class ViewImagePage : ContentPage
    {
        private double _currentScale = 1;
        private double _startScale = 1;
        private double _xOffset = 0;
        private double _yOffset = 0;

        public ViewImagePage(string title, string imageUrl)
        {
            InitializeComponent();
            Title = title;
            LoadImage(imageUrl);
        }

        private void LoadImage(string imageUrl)
        {
            try
            {
                DocumentImage.Source = ImageSource.FromUri(new Uri(imageUrl));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error loading image: {ex.Message}");
            }
        }

        private void OnPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            if (e.Status == GestureStatus.Started)
            {
                _startScale = _currentScale;
            }
            else if (e.Status == GestureStatus.Running)
            {
                _currentScale = Math.Max(1, _startScale * e.Scale);
                DocumentImage.Scale = _currentScale;
            }
        }

        private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            if (_currentScale > 1)
            {
                switch (e.StatusType)
                {
                    case GestureStatus.Running:
                        _xOffset = e.TotalX;
                        _yOffset = e.TotalY;
                        DocumentImage.TranslationX = _xOffset;
                        DocumentImage.TranslationY = _yOffset;
                        break;

                    case GestureStatus.Completed:
                        break;
                }
            }
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}