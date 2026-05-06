using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;

namespace ServiceCo
{
    public partial class CancelRideModalPage : ContentPage
    {
        private bool _isDriverFound;
        private string _rideRequestId;
        private string _context;
        private FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _selectedAction = "";

        public event EventHandler<string> CancellationCompleted;

        public CancelRideModalPage(bool isDriverFound, string rideRequestId, string context)
        {
            InitializeComponent();
            _isDriverFound = isDriverFound;
            _rideRequestId = rideRequestId;
            _context = context;

            SetupUI();
        }

        private void SetupUI()
        {
            if (_context == "driver_cancelled")
            {
                TitleLabel.Text = "Driver Cancelled Ride";
                SubtitleLabel.Text = "Your driver has cancelled the ride. What would you like to do?";

                Phase1Section.IsVisible = false;
                Phase2Section.IsVisible = true;

                SearchNewDriverReasonSection.IsVisible = false;
                OtherReasonSection.IsVisible = false;
                SearchOtherReasonSection.IsVisible = false;

                CancelOption.IsVisible = true;
                SearchNewDriverOption.IsVisible = true;

                BackButton.IsVisible = false;

                ConfirmCancelButton.IsVisible = false;
                ConfirmSearchNewDriverButton.IsVisible = false;
            }
            else if (_context == "searching")
            {
                TitleLabel.Text = "Cancel Ride Request?";
                SubtitleLabel.Text = "We're still searching for a driver. What would you like to do?";

                SearchNewDriverOption.IsVisible = false;
                BackButton.IsVisible = true;
                ConfirmCancelButton.IsVisible = false;
                ConfirmSearchNewDriverButton.IsVisible = false;
            }
            else if (_context == "driver_found")
            {
                TitleLabel.Text = "Cancel Ride with Driver?";
                SubtitleLabel.Text = "You have an assigned driver. What would you like to do?";

                SearchNewDriverReasonSection.IsVisible = false;
                BackButton.IsVisible = true;
                ConfirmCancelButton.IsVisible = false;
                ConfirmSearchNewDriverButton.IsVisible = false;
            }

            if (_context != "driver_cancelled")
            {
                SelectCancellationReason(Reason1Option, "Driver is taking too long");
            }
        }

        private void SelectCancellationReason(Frame frame, string reason)
        {
            ResetCancellationReasonBorders();
            frame.BorderColor = Color.FromArgb("#2E7D32");
            NextButton.IsEnabled = true;
            NextButton.IsVisible = true;
        }

        private void ResetCancellationReasonBorders()
        {
            Reason1Option.BorderColor = Color.FromArgb("#E0E0E0");
            Reason2Option.BorderColor = Color.FromArgb("#E0E0E0");
            Reason3Option.BorderColor = Color.FromArgb("#E0E0E0");
            Reason4Option.BorderColor = Color.FromArgb("#E0E0E0");
            Reason5Option.BorderColor = Color.FromArgb("#E0E0E0");
            Reason6Option.BorderColor = Color.FromArgb("#E0E0E0");
        }

        private void OnReason1Tapped(object sender, EventArgs e)
        {
            SelectCancellationReason(Reason1Option, "Driver is taking too long");
        }

        private void OnReason2Tapped(object sender, EventArgs e)
        {
            SelectCancellationReason(Reason2Option, "Found cheaper alternative");
        }

        private void OnReason3Tapped(object sender, EventArgs e)
        {
            SelectCancellationReason(Reason3Option, "Change of plans");
        }

        private void OnReason4Tapped(object sender, EventArgs e)
        {
            SelectCancellationReason(Reason4Option, "Wrong pickup/dropoff");
        }

        private void OnReason5Tapped(object sender, EventArgs e)
        {
            SelectCancellationReason(Reason5Option, "Uncomfortable with driver");
        }

        private void OnReason6Tapped(object sender, EventArgs e)
        {
            SelectCancellationReason(Reason6Option, "Other reason");
            OtherReasonSection.IsVisible = true;
        }

        private void OnNextClicked(object sender, EventArgs e)
        {
            if (_context == "driver_cancelled")
            {
                GoToPhase2();
                return;
            }

            GoToPhase2();
        }

        private void GoToPhase2()
        {
            Phase1Section.IsVisible = false;
            Phase2Section.IsVisible = true;

            if (_context == "searching")
            {
                SearchNewDriverOption.IsVisible = false;
                CancelOption.BackgroundColor = Color.FromArgb("#FFF5F5");
                BackButton.IsVisible = true;
                ConfirmCancelButton.IsVisible = false;
                ConfirmSearchNewDriverButton.IsVisible = false;
                SearchNewDriverReasonSection.IsVisible = false;
            }
            else if (_context == "driver_cancelled")
            {
                TitleLabel.Text = "What would you like to do?";
                SubtitleLabel.Text = "Driver cancelled the ride";

                CancelOption.IsVisible = true;
                SearchNewDriverOption.IsVisible = true;

                SearchNewDriverReasonSection.IsVisible = false;
                ConfirmSearchNewDriverButton.IsVisible = false;
                ConfirmCancelButton.IsVisible = false;

                BackButton.IsVisible = false;

                _selectedAction = "";
            }
            else
            {
                SearchNewDriverOption.IsVisible = true;
                BackButton.IsVisible = true;
                ConfirmCancelButton.IsVisible = false;
                ConfirmSearchNewDriverButton.IsVisible = false;
                SearchNewDriverReasonSection.IsVisible = false;
            }

            TitleLabel.Text = "What would you like to do?";
            SubtitleLabel.Text = "Select an action";
        }

        private void OnCancelOptionTapped(object sender, EventArgs e)
        {
            _selectedAction = "Cancel";

            CancelOption.BorderColor = Color.FromArgb("#D32F2F");
            CancelOption.BackgroundColor = Color.FromArgb("#FFEBEE");

            SearchNewDriverOption.BorderColor = Color.FromArgb("#E0E0E0");
            SearchNewDriverOption.BackgroundColor = Color.FromArgb("#F5F5F5");

            ConfirmCancelButton.IsVisible = true;
            ConfirmCancelButton.IsEnabled = true;

            ConfirmSearchNewDriverButton.IsVisible = false;
            SearchNewDriverReasonSection.IsVisible = false;

            BackButton.IsVisible = (_context != "driver_cancelled");
        }

        private void OnSearchNewDriverOptionTapped(object sender, EventArgs e)
        {
            _selectedAction = "SearchNewDriver";

            SearchNewDriverOption.BorderColor = Color.FromArgb("#2E7D32");
            SearchNewDriverOption.BackgroundColor = Color.FromArgb("#E8F5E9");

            CancelOption.BorderColor = Color.FromArgb("#E0E0E0");
            CancelOption.BackgroundColor = Color.FromArgb("#FFF5F5");

            if (_context == "driver_cancelled")
            {
                ConfirmSearchNewDriverButton.IsVisible = true;
                ConfirmSearchNewDriverButton.IsEnabled = true;

                SearchNewDriverReasonSection.IsVisible = false;
                ConfirmCancelButton.IsVisible = false;

                BackButton.IsVisible = false;
            }
            else
            {
                SearchNewDriverReasonSection.IsVisible = true;
                ConfirmCancelButton.IsVisible = false;
                ConfirmSearchNewDriverButton.IsVisible = true;
                ConfirmSearchNewDriverButton.IsEnabled = true;

                BackButton.IsVisible = true;

                SelectSearchNewDriverReason(SearchReason1Option, "Uncomfortable with driver");
            }
        }

        private void OnBackButtonClicked(object sender, EventArgs e)
        {
            if (_context == "driver_cancelled")
            {
                Navigation.PopModalAsync();
                return;
            }

            Phase2Section.IsVisible = false;
            Phase1Section.IsVisible = true;

            TitleLabel.Text = "Cancel Ride?";
            SubtitleLabel.Text = "Select a reason for cancellation";
        }

        private void SelectSearchNewDriverReason(Frame frame, string reason)
        {
            ResetSearchNewDriverReasonBorders();
            frame.BorderColor = Color.FromArgb("#2E7D32");
            ConfirmSearchNewDriverButton.IsEnabled = true;
        }

        private void ResetSearchNewDriverReasonBorders()
        {
            SearchReason1Option.BorderColor = Color.FromArgb("#E0E0E0");
            SearchReason2Option.BorderColor = Color.FromArgb("#E0E0E0");
            SearchReason3Option.BorderColor = Color.FromArgb("#E0E0E0");
            SearchReason4Option.BorderColor = Color.FromArgb("#E0E0E0");
        }

        private void OnSearchReason1Tapped(object sender, EventArgs e)
        {
            SelectSearchNewDriverReason(SearchReason1Option, "Uncomfortable with driver");
            SearchOtherReasonSection.IsVisible = false;
        }

        private void OnSearchReason2Tapped(object sender, EventArgs e)
        {
            SelectSearchNewDriverReason(SearchReason2Option, "Driver is delayed");
            SearchOtherReasonSection.IsVisible = false;
        }

        private void OnSearchReason3Tapped(object sender, EventArgs e)
        {
            SelectSearchNewDriverReason(SearchReason3Option, "Communication issue");
            SearchOtherReasonSection.IsVisible = false;
        }

        private void OnSearchReason4Tapped(object sender, EventArgs e)
        {
            SelectSearchNewDriverReason(SearchReason4Option, "Other reason");
            SearchOtherReasonSection.IsVisible = true;
        }

        private async void OnConfirmCancelClicked(object sender, EventArgs e)
        {
            try
            {
                if (_context != "driver_cancelled")
                {
                    bool success = await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "CancellationReason", "User cancelled");
                    if (!success)
                    {
                        await DisplayAlert("Error", "Failed to save cancellation.", "OK");
                        return;
                    }
                }

                CancellationCompleted?.Invoke(this, "Cancelled");
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Failed to process cancellation.", "OK");
            }
        }

        private async void OnConfirmSearchNewDriverClicked(object sender, EventArgs e)
        {
            try
            {
                if (_context == "driver_cancelled")
                {
                    CancellationCompleted?.Invoke(this, "SearchNew");
                    await Navigation.PopModalAsync();
                    return;
                }

                bool success = true;
                success = await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "SearchNewDriverReason", "User requested new driver");

                if (success)
                {
                    CancellationCompleted?.Invoke(this, "SearchNew");
                    await Navigation.PopModalAsync();
                }
                else
                {
                    await DisplayAlert("Error", "Failed to save request.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Failed to process request.", "OK");
            }
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}