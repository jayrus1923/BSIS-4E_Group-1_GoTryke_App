using Microsoft.Maui;
using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceCo
{
    public partial class Driver_Name : ContentPage
    {
        private readonly string _phoneNumber;
        private readonly string _pin;
        private Firebase.DriverData _driverData = new Firebase.DriverData();

        public Driver_Name(string phoneNumber, string pin = null)
        {
            InitializeComponent();
            _phoneNumber = phoneNumber;
            _pin = pin;

            if (!string.IsNullOrEmpty(_pin))
            {
                NextButton.Text = "Complete Registration";
            }
            else
            {
                NextButton.Text = "Continue";
            }

            AttachFocus(FirstNameEntry, FirstNameBorder);
            AttachFocus(MiddleNameEntry, MiddleNameBorder);
            AttachFocus(LastNameEntry, LastNameBorder);
            AttachFocus(HouseEntry, HouseBorder);
            AttachFocus(StreetEntry, StreetBorder);

            AttachPickerFocus(BarangayPicker, BarangayBorder);
            AttachPickerFocus(MunicipalityPicker, MunicipalityBorder);
            AttachPickerFocus(ProvincePicker, ProvinceBorder);

            InitializeDropdowns();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            NavigationPage.SetHasNavigationBar(this, false);

            if (Parent is NavigationPage navPage)
            {
                navPage.BarBackgroundColor = Colors.Transparent;
                navPage.BarTextColor = Colors.Transparent;
            }
        }

        private void InitializeDropdowns()
        {
            BarangayPicker.ItemsSource = new string[] {
                "Anilao", "Atlag", "Babatnin", "Bagna", "Bagong Bayan",
                "Balayong", "Balite", "Bangkal", "Barihan", "Bulihan",
                "Bungahan", "Caingin", "Calero", "Caliligawan", "Canalate",
                "Caniogan", "Catmon", "Cofradia", "Dakila", "Guinhawa",
                "Ligas", "Liyang", "Longos", "Look 1st", "Look 2nd",
                "Lugam", "Mabolo", "Mambog", "Masile", "Matimbo",
                "Mojon", "Namayan", "Niugan", "Pamarawan", "Panasahan",
                "Pinagbakahan", "San Agustin", "San Gabriel", "San Juan",
                "San Pablo", "San Vicente", "Santiago", "Santisima Trinidad",
                "Santo Cristo", "Santo Niño", "Santo Rosario", "Sikatuna",
                "Sumapang Bata", "Sumapang Matanda", "Taal", "Tikay"
            };

            MunicipalityPicker.ItemsSource = new string[] {
                "Malolos",
                "Baliuag",
                "Bocaue",
                "Bulakan",
                "Bustos",
                "Calumpit",
                "Guiguinto",
                "Hagonoy",
                "Marilao",
                "Meycauayan",
                "Norzagaray",
                "Obando",
                "Pandi",
                "Paombong",
                "Plaridel",
                "Pulilan",
                "San Ildefonso",
                "San Jose del Monte",
                "San Miguel",
                "San Rafael",
                "Santa Maria"
            };

            ProvincePicker.ItemsSource = new string[] {
                "Bulacan",
                "Aurora",
                "Bataan",
                "Nueva Ecija",
                "Pampanga",
                "Tarlac",
                "Zambales"
            };
        }

        private void AttachFocus(Entry entry, Border border)
        {
            entry.Focused += (sender, e) => OnBorderFocusChanged(border, true);
            entry.Unfocused += (sender, e) => OnBorderFocusChanged(border, false);
        }

        private void AttachPickerFocus(Picker picker, Border border)
        {
            picker.Focused += (sender, e) => OnBorderFocusChanged(border, true);
            picker.Unfocused += (sender, e) => OnBorderFocusChanged(border, false);
        }

        private void OnBorderFocusChanged(Border border, bool isFocused)
        {
            if (border != null)
            {
                border.Stroke = isFocused
                    ? Color.FromArgb("#2E7D32")
                    : Color.FromArgb("#E0E0E0");
            }
        }

        private void OnFieldChanged(object sender, TextChangedEventArgs e) => ValidateForm();
        private void OnPickerChanged(object sender, EventArgs e) => ValidateForm();

        private void ValidateForm()
        {
            bool valid =
                !string.IsNullOrWhiteSpace(FirstNameEntry.Text) &&
                !string.IsNullOrWhiteSpace(LastNameEntry.Text) &&
                !string.IsNullOrWhiteSpace(HouseEntry.Text) &&
                !string.IsNullOrWhiteSpace(StreetEntry.Text) &&
                BarangayPicker.SelectedIndex >= 0 &&
                MunicipalityPicker.SelectedIndex >= 0 &&
                ProvincePicker.SelectedIndex >= 0;

            NextButton.IsEnabled = valid;
            NextButton.BackgroundColor = valid ? Color.FromArgb("#2E7D32") : Color.FromArgb("#DADADA");
            NextButton.TextColor = valid ? Colors.White : Color.FromArgb("#666666");
        }

        private async void OnNextClicked(object sender, EventArgs e)
        {
            try
            {
                _driverData.PhoneNumber = _phoneNumber;
                _driverData.FirstName = FirstNameEntry.Text?.Trim();
                _driverData.MiddleName = MiddleNameEntry.Text?.Trim();
                _driverData.LastName = LastNameEntry.Text?.Trim();
                _driverData.HouseNumber = HouseEntry.Text?.Trim();
                _driverData.Street = StreetEntry.Text?.Trim();
                _driverData.Barangay = BarangayPicker.SelectedItem?.ToString();
                _driverData.Municipality = MunicipalityPicker.SelectedItem?.ToString();
                _driverData.Province = ProvincePicker.SelectedItem?.ToString();

                await Navigation.PushAsync(new Driver_SubmitRequirements(_driverData, _pin));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to proceed: {ex.Message}", "OK");
            }
        }
    }
}