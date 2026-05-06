using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class EditTricyclePage : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private TricycleInfo _tricycleInfo;
        private Driver _driverInfo;
        private string _driverId;
        private bool _isProcessing = false;
        private bool _isModalOpen = false;

        private readonly Dictionary<string, string> _colorHexMap = new Dictionary<string, string>
        {
            { "Red", "#FF5252" }, { "Blue", "#2196F3" }, { "Black", "#000000" },
            { "White", "#FFFFFF" }, { "Silver", "#C0C0C0" }, { "Gray", "#9E9E9E" },
            { "Green", "#4CAF50" }, { "Yellow", "#FFEB3B" }, { "Orange", "#FF9800" },
            { "Brown", "#795548" }, { "Purple", "#9C27B0" }, { "Pink", "#E91E63" },
            { "Cyan", "#00BCD4" }, { "Teal", "#009688" }, { "Lime", "#CDDC39" },
            { "Gold", "#FFD700" }, { "Maroon", "#800000" }, { "Navy", "#000080" },
            { "Other", "#607D8B" }
        };

        private readonly List<string> _yearList = new List<string>
        {
            "2026", "2025", "2024", "2023", "2022", "2021", "2020", "2019", "2018",
            "2017", "2016", "2015", "2014", "2013", "2012", "2011", "2010",
            "2009", "2008", "2007", "2006", "2005", "2004", "2003", "2002",
            "2001", "2000", "1999", "1998", "1997", "1996", "1995", "Older"
        };

        private readonly List<string> _capacityList = new List<string> { "1-2 seater", "3-4 seater" };
        private readonly List<string> _statusList = new List<string> { "Active", "Inactive", "Under Maintenance", "For Repair" };

        private Dictionary<string, string> _originalValues = new Dictionary<string, string>();
        private bool _isColorOther = false;
        private bool _hasActiveRide = false;

        public EditTricyclePage(TricycleInfo tricycleInfo)
        {
            InitializeComponent();
            _tricycleInfo = tricycleInfo;
            _driverId = AppSession.GetDriverId();

            InitializePickers();
            LoadTricycleDataAsync().ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    Console.WriteLine($"Error loading data: {t.Exception.Message}");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;
            await CheckForUnsavedChanges();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_isProcessing && _isModalOpen)
                {
                    if (SuccessModal.IsVisible)
                    {
                        await HideModalAnimated(SuccessModal);
                        await Navigation.PopModalAsync();
                    }
                    else if (CancelModal.IsVisible)
                        await HideModalAnimated(CancelModal);
                    else if (ValidationErrorModal.IsVisible)
                        await HideModalAnimated(ValidationErrorModal);
                    else if (NoChangesModal.IsVisible)
                        await HideModalAnimated(NoChangesModal);
                    else if (ConfirmModal.IsVisible)
                        await HideModalAnimated(ConfirmModal);
                    else if (ErrorModal.IsVisible)
                        await HideModalAnimated(ErrorModal);
                }
                else if (!_isProcessing && !_isModalOpen)
                {
                    await CheckForUnsavedChanges();
                }
            });
            return true;
        }

        private async Task CheckForUnsavedChanges()
        {
            var changes = GetChangesSummary();
            if (changes.Count > 0)
            {
                await ShowCancelConfirmation();
            }
            else
            {
                await Navigation.PopModalAsync();
            }
        }

        private void InitializePickers()
        {
            YearPicker.Items.Clear();
            foreach (var year in _yearList) YearPicker.Items.Add(year);

            ColorPicker.Items.Clear();
            foreach (var color in _colorHexMap.Keys) ColorPicker.Items.Add(color);

            CapacityPicker.Items.Clear();
            foreach (var capacity in _capacityList) CapacityPicker.Items.Add(capacity);

            StatusPicker.Items.Clear();
            foreach (var status in _statusList) StatusPicker.Items.Add(status);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CheckIfDriverHasActiveRide();
            ApplyRestrictions();
        }

        private async Task CheckIfDriverHasActiveRide()
        {
            try
            {
                _driverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(_driverId)) return;

                var allRideRequests = await _firebaseConnection.GetAllRideRequestsAsync();
                if (allRideRequests != null && allRideRequests.Count > 0)
                {
                    _hasActiveRide = allRideRequests
                        .Where(r => r.DriverId == _driverId &&
                                   r.Status != "Completed" &&
                                   r.Status != "Cancelled")
                        .Any();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking active ride: {ex.Message}");
            }
        }

        private void ApplyRestrictions()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CapacityPicker.IsEnabled = !_hasActiveRide;
                StatusPicker.IsEnabled = !_hasActiveRide;
            });
        }

        private void StoreOriginalValues()
        {
            _originalValues["PlateNumber"] = _tricycleInfo.PlateNumber ?? "";
            _originalValues["LicenseNumber"] = _tricycleInfo.LicenseNumber ?? "";
            _originalValues["BodyNumber"] = _tricycleInfo.BodyNumber ?? "";
            _originalValues["MakeModel"] = _tricycleInfo.MakeModel ?? "";
            _originalValues["YearModel"] = _tricycleInfo.YearModel ?? "";
            _originalValues["Color"] = _tricycleInfo.Color ?? "";
            _originalValues["EngineNumber"] = _tricycleInfo.EngineNumber ?? "";
            _originalValues["ChassisNumber"] = _tricycleInfo.ChassisNumber ?? "";
            _originalValues["SeatingCapacity"] = _tricycleInfo.SeatingCapacity ?? "";
            _originalValues["Status"] = _tricycleInfo.Status ?? "";
        }

        private async Task LoadTricycleDataAsync()
        {
            try
            {
                await LoadDriverLicenseFromProfile();

                PlateNumberEntry.Text = _tricycleInfo.PlateNumber ?? "";

                string license = _tricycleInfo.LicenseNumber ?? "";
                string driverLicense = AppSession.GetDriverLicenseNumber() ?? "";
                LicenseNumberEntry.Text = !string.IsNullOrEmpty(license) ? license : driverLicense;

                BodyNumberEntry.Text = _tricycleInfo.BodyNumber ?? "";
                MakeModelEntry.Text = _tricycleInfo.MakeModel ?? "";

                string year = _tricycleInfo.YearModel ?? "";
                if (!string.IsNullOrEmpty(year))
                {
                    int index = YearPicker.Items.IndexOf(year);
                    if (index >= 0) YearPicker.SelectedIndex = index;
                }

                string color = _tricycleInfo.Color ?? "";
                if (!string.IsNullOrEmpty(color))
                {
                    var matchingColor = _colorHexMap.Keys.FirstOrDefault(c =>
                        c.Equals(color, StringComparison.OrdinalIgnoreCase));

                    if (matchingColor != null)
                    {
                        int index = ColorPicker.Items.IndexOf(matchingColor);
                        if (index >= 0)
                        {
                            ColorPicker.SelectedIndex = index;
                            _isColorOther = false;
                            CustomColorContainer.IsVisible = false;
                            UpdateColorDisplay(matchingColor);
                        }
                    }
                    else
                    {
                        int otherIndex = ColorPicker.Items.IndexOf("Other");
                        if (otherIndex >= 0) ColorPicker.SelectedIndex = otherIndex;
                        _isColorOther = true;
                        CustomColorEntry.Text = color;
                        CustomColorContainer.IsVisible = true;
                        UpdateColorDisplay(color);
                    }
                }

                EngineNumberEntry.Text = _tricycleInfo.EngineNumber ?? "";
                ChassisNumberEntry.Text = _tricycleInfo.ChassisNumber ?? "";

                string capacity = _tricycleInfo.SeatingCapacity ?? "";
                if (!string.IsNullOrEmpty(capacity))
                {
                    int index = CapacityPicker.Items.IndexOf(capacity);
                    if (index >= 0) CapacityPicker.SelectedIndex = index;
                }

                string status = _tricycleInfo.Status ?? "";
                if (!string.IsNullOrEmpty(status))
                {
                    int index = StatusPicker.Items.IndexOf(status);
                    if (index >= 0) StatusPicker.SelectedIndex = index;
                }

                StoreOriginalValues();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading data: {ex.Message}");
            }
        }

        private async Task LoadDriverLicenseFromProfile()
        {
            try
            {
                _driverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(_driverId)) return;

                _driverInfo = await _firebaseConnection.GetDriverByIdAsync(_driverId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading driver: {ex.Message}");
            }
        }

        private void OnPlateNumberTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string input = e.NewTextValue ?? "";
                string upperInput = input.ToUpper();

                if (upperInput.Length > 10)
                {
                    entry.Text = upperInput.Substring(0, 10);
                }
                else if (upperInput != input)
                {
                    entry.Text = upperInput;
                }
            }
        }

        private void OnBodyNumberTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string input = e.NewTextValue ?? "";
                string upperInput = input.ToUpper();

                if (upperInput.Length > 10)
                {
                    entry.Text = upperInput.Substring(0, 10);
                }
                else if (upperInput != input)
                {
                    entry.Text = upperInput;
                }
            }
        }

        private void OnLicenseNumberTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string input = e.NewTextValue ?? "";
                string upperInput = input.ToUpper();

                if (upperInput.Length > 15)
                {
                    entry.Text = upperInput.Substring(0, 15);
                }
                else if (upperInput != input)
                {
                    entry.Text = upperInput;
                }
            }
        }

        private void OnMakeModelTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string input = e.NewTextValue ?? "";
                if (input.Length > 50)
                {
                    entry.Text = input.Substring(0, 50);
                }
            }
        }

        private void OnEngineNumberTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string input = e.NewTextValue ?? "";
                string upperInput = input.ToUpper();

                if (upperInput.Length > 20)
                {
                    entry.Text = upperInput.Substring(0, 20);
                }
                else if (upperInput != input)
                {
                    entry.Text = upperInput;
                }
            }
        }

        private void OnChassisNumberTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string input = e.NewTextValue ?? "";
                string upperInput = input.ToUpper();

                if (upperInput.Length > 20)
                {
                    entry.Text = upperInput.Substring(0, 20);
                }
                else if (upperInput != input)
                {
                    entry.Text = upperInput;
                }
            }
        }

        private void OnCustomColorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string input = e.NewTextValue ?? "";
                if (input.Length > 30)
                {
                    entry.Text = input.Substring(0, 30);
                }
                else
                {
                    UpdateColorDisplay(input);
                }
            }
        }

        private void OnColorPickerSelectedIndexChanged(object sender, EventArgs e)
        {
            if (ColorPicker.SelectedItem is string selectedColor)
            {
                if (selectedColor == "Other")
                {
                    _isColorOther = true;
                    CustomColorContainer.IsVisible = true;
                    CustomColorEntry.Text = "";
                    CustomColorEntry.Focus();
                    UpdateColorDisplay(selectedColor);
                }
                else
                {
                    _isColorOther = false;
                    CustomColorContainer.IsVisible = false;
                    UpdateColorDisplay(selectedColor);
                }
                ColorErrorLabel.IsVisible = false;
            }
        }

        private void UpdateColorDisplay(string colorInput)
        {
            try
            {
                Color selectedColor;

                if (_colorHexMap.TryGetValue(colorInput, out string hexCode))
                {
                    selectedColor = Color.FromArgb(hexCode);
                }
                else
                {
                    try
                    {
                        if (colorInput.StartsWith("#"))
                            selectedColor = Color.FromArgb(colorInput);
                        else
                            selectedColor = GetColorFromName(colorInput) ?? Colors.Gray;
                    }
                    catch
                    {
                        selectedColor = Colors.Gray;
                    }
                }

                ColorDisplayBoxView.BackgroundColor = selectedColor;
                ColorNameLabel.Text = colorInput.Length > 10 ? colorInput.Substring(0, 10) + "..." : colorInput;
                ColorNameLabel.TextColor = Color.FromArgb("#37474F");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating color: {ex.Message}");
            }
        }

        private Color GetColorFromName(string colorName)
        {
            return colorName.ToLower() switch
            {
                "cyan" => Color.FromArgb("#00BCD4"),
                "magenta" => Color.FromArgb("#FF00FF"),
                "turquoise" => Color.FromArgb("#40E0D0"),
                "lavender" => Color.FromArgb("#E6E6FA"),
                "beige" => Color.FromArgb("#F5F5DC"),
                "salmon" => Color.FromArgb("#FA8072"),
                "coral" => Color.FromArgb("#FF7F50"),
                _ => null
            };
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            var changes = GetChangesSummary();
            if (changes.Count == 0)
            {
                await ShowNoChangesModal();
                return;
            }

            if (_hasActiveRide)
            {
                if (_originalValues.ContainsKey("SeatingCapacity") &&
                    CapacityPicker.SelectedItem != null &&
                    CapacityPicker.SelectedItem.ToString() != _originalValues["SeatingCapacity"])
                {
                    await ShowModal("Cannot Save", "You cannot change seating capacity while having an active ride.", "warning", false);
                    return;
                }

                if (StatusPicker.SelectedItem != null && StatusPicker.SelectedItem.ToString() != "Active")
                {
                    await ShowModal("Cannot Save", "You cannot change to non-active status while having an active ride.", "warning", false);
                    return;
                }
            }

            var validationErrors = ValidateForm();
            if (validationErrors.Count > 0)
            {
                await ShowValidationErrorModal(validationErrors);
                return;
            }

            await ShowConfirmModal();
        }

        private List<string> ValidateForm()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(PlateNumberEntry.Text))
            {
                errors.Add("• Plate Number is required");
            }
            else if (!IsValidPlateNumber(PlateNumberEntry.Text))
            {
                errors.Add("• Plate Number format should be like: ABC-123 or ABC-1234");
            }

            if (string.IsNullOrWhiteSpace(BodyNumberEntry.Text))
            {
                errors.Add("• Body Number is required");
            }
            else if (!IsValidBodyNumber(BodyNumberEntry.Text))
            {
                errors.Add("• Body Number format should be like: TR-123");
            }

            if (string.IsNullOrWhiteSpace(LicenseNumberEntry.Text))
            {
                errors.Add("• Driver's License is required");
            }
            else if (!IsValidLicenseNumber(LicenseNumberEntry.Text))
            {
                errors.Add("• Driver's License format should be like: A12-34-567890");
            }

            if (YearPicker.SelectedItem == null)
                errors.Add("• Year Model is required");

            if (ColorPicker.SelectedItem == null || (_isColorOther && string.IsNullOrWhiteSpace(CustomColorEntry.Text)))
                errors.Add("• Color is required");

            if (CapacityPicker.SelectedItem == null)
                errors.Add("• Seating Capacity is required");

            if (StatusPicker.SelectedItem == null)
                errors.Add("• Status is required");

            return errors;
        }

        private bool IsValidPlateNumber(string plateNumber)
        {
            string clean = Regex.Replace(plateNumber, "[\\s-]", "").ToUpper();
            return Regex.IsMatch(clean, @"^[A-Z]{2,3}\d{3,4}$");
        }

        private bool IsValidBodyNumber(string bodyNumber)
        {
            string clean = Regex.Replace(bodyNumber, "[\\s-]", "").ToUpper();
            return Regex.IsMatch(clean, @"^[A-Z]{2}\d{3}$");
        }

        private bool IsValidLicenseNumber(string licenseNumber)
        {
            string clean = Regex.Replace(licenseNumber, "[\\s-]", "").ToUpper();
            return Regex.IsMatch(clean, @"^[A-Z]{1,2}\d{2}\d{2}\d{6}$") && licenseNumber.Contains("-");
        }

        private async Task ShowConfirmModal()
        {
            _isModalOpen = true;

            ChangesSummary.Children.Clear();
            var changes = GetChangesSummary();

            if (changes.Count == 0)
                AddChangeItem("No changes detected", "#666666");
            else
                foreach (var change in changes)
                    AddChangeItem(change, "#2196F3");

            await ShowModalAnimated(ConfirmModal);
        }

        private async Task ShowValidationErrorModal(List<string> errors)
        {
            _isModalOpen = true;

            ValidationErrorSummary.Children.Clear();
            foreach (var error in errors)
            {
                ValidationErrorSummary.Children.Add(new Label
                {
                    Text = error,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#DC3545"),
                    Margin = new Thickness(0, 2)
                });
            }

            var headerLabel = (ValidationErrorModal.Content as VerticalStackLayout).Children[1] as Label;
            if (headerLabel != null)
                headerLabel.TextColor = Color.FromArgb("#DC3545");

            var okButton = (ValidationErrorModal.Content as VerticalStackLayout).Children[4] as Button;
            if (okButton != null)
                okButton.BackgroundColor = Color.FromArgb("#DC3545");

            await ShowModalAnimated(ValidationErrorModal);
        }

        private async Task ShowNoChangesModal()
        {
            _isModalOpen = true;
            await ShowModalAnimated(NoChangesModal);
        }

        private List<string> GetChangesSummary()
        {
            var changes = new List<string>();
            var newValues = GetCurrentValues();

            foreach (var kvp in newValues)
            {
                string key = "";

                if (kvp.Key == "Plate Number") key = "PlateNumber";
                else if (kvp.Key == "Driver's License") key = "LicenseNumber";
                else if (kvp.Key == "Body Number") key = "BodyNumber";
                else if (kvp.Key == "Make & Model") key = "MakeModel";
                else if (kvp.Key == "Year Model") key = "YearModel";
                else if (kvp.Key == "Color") key = "Color";
                else if (kvp.Key == "Engine Number") key = "EngineNumber";
                else if (kvp.Key == "Chassis Number") key = "ChassisNumber";
                else if (kvp.Key == "Seating Capacity") key = "SeatingCapacity";
                else if (kvp.Key == "Status") key = "Status";

                if (_originalValues.TryGetValue(key, out string originalValue))
                {
                    string originalDisplay = string.IsNullOrEmpty(originalValue) ? "Empty" : originalValue;
                    string newDisplay = string.IsNullOrEmpty(kvp.Value) ? "Empty" : kvp.Value;

                    if (originalDisplay != newDisplay)
                    {
                        changes.Add($"{kvp.Key}: {originalDisplay} → {newDisplay}");
                    }
                }
            }
            return changes;
        }

        private Dictionary<string, string> GetCurrentValues()
        {
            string selectedColor = "";
            if (ColorPicker.SelectedItem != null)
            {
                if (ColorPicker.SelectedItem.ToString() == "Other" && !string.IsNullOrWhiteSpace(CustomColorEntry.Text))
                    selectedColor = CustomColorEntry.Text?.Trim();
                else if (ColorPicker.SelectedItem.ToString() != "Other")
                    selectedColor = ColorPicker.SelectedItem.ToString();
            }

            return new Dictionary<string, string>
            {
                ["Plate Number"] = PlateNumberEntry.Text?.Trim().ToUpper() ?? "",
                ["Driver's License"] = LicenseNumberEntry.Text?.Trim().ToUpper() ?? "",
                ["Body Number"] = string.IsNullOrWhiteSpace(BodyNumberEntry.Text?.Trim()) ? "Empty" : BodyNumberEntry.Text?.Trim().ToUpper() ?? "Empty",
                ["Make & Model"] = string.IsNullOrWhiteSpace(MakeModelEntry.Text?.Trim()) ? "Empty" : MakeModelEntry.Text?.Trim() ?? "Empty",
                ["Year Model"] = YearPicker.SelectedItem?.ToString() ?? "Not selected",
                ["Color"] = string.IsNullOrWhiteSpace(selectedColor) ? "Not selected" : selectedColor,
                ["Engine Number"] = string.IsNullOrWhiteSpace(EngineNumberEntry.Text?.Trim()) ? "Empty" : EngineNumberEntry.Text?.Trim().ToUpper() ?? "Empty",
                ["Chassis Number"] = string.IsNullOrWhiteSpace(ChassisNumberEntry.Text?.Trim()) ? "Empty" : ChassisNumberEntry.Text?.Trim().ToUpper() ?? "Empty",
                ["Seating Capacity"] = CapacityPicker.SelectedItem?.ToString() ?? "Not selected",
                ["Status"] = StatusPicker.SelectedItem?.ToString() ?? "Not selected"
            };
        }

        private void AddChangeItem(string text, string color)
        {
            ChangesSummary.Children.Add(new Label
            {
                Text = $"• {text}",
                FontSize = 12,
                TextColor = Color.FromArgb(color),
                Margin = new Thickness(0, 2)
            });
        }

        private async Task ShowCancelConfirmation()
        {
            _isModalOpen = true;

            var headerLabel = (CancelModal.Content as VerticalStackLayout).Children[1] as Label;
            if (headerLabel != null)
                headerLabel.TextColor = Color.FromArgb("#DC3545");

            var buttonGrid = (CancelModal.Content as VerticalStackLayout).Children[3] as Grid;
            if (buttonGrid != null)
            {
                var yesButton = buttonGrid.Children[1] as Button;
                if (yesButton != null)
                    yesButton.BackgroundColor = Color.FromArgb("#DC3545");
            }

            await ShowModalAnimated(CancelModal);
        }

        private async Task ShowModalAnimated(Frame modal)
        {
            ModalOverlay.IsVisible = true;
            modal.IsVisible = true;

            await Task.WhenAll(
                ModalOverlay.FadeTo(0.6, 250, Easing.CubicIn),
                modal.FadeTo(1, 350, Easing.SpringOut),
                modal.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task HideModalAnimated(Frame modal)
        {
            await Task.WhenAll(
                ModalOverlay.FadeTo(0, 200, Easing.CubicIn),
                modal.FadeTo(0, 200, Easing.CubicIn),
                modal.ScaleTo(0.7, 200, Easing.CubicIn)
            );

            ModalOverlay.IsVisible = false;
            modal.IsVisible = false;
            _isModalOpen = false;
        }

        private async void OnModalSaveClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(ConfirmModal);
            await SaveChanges();
        }

        private async Task SaveChanges()
        {
            _isProcessing = true;
            ShowProcessing("Saving...");

            try
            {
                string selectedColor = "";
                if (ColorPicker.SelectedItem != null)
                {
                    if (ColorPicker.SelectedItem.ToString() == "Other" && !string.IsNullOrWhiteSpace(CustomColorEntry.Text))
                        selectedColor = CustomColorEntry.Text?.Trim();
                    else if (ColorPicker.SelectedItem.ToString() != "Other")
                        selectedColor = ColorPicker.SelectedItem.ToString();
                }

                _tricycleInfo.PlateNumber = PlateNumberEntry.Text?.Trim().ToUpper() ?? "";
                _tricycleInfo.LicenseNumber = LicenseNumberEntry.Text?.Trim().ToUpper() ?? "";
                _tricycleInfo.BodyNumber = BodyNumberEntry.Text?.Trim().ToUpper() ?? "";
                _tricycleInfo.MakeModel = MakeModelEntry.Text?.Trim() ?? "";
                _tricycleInfo.YearModel = YearPicker.SelectedItem?.ToString() ?? "";
                _tricycleInfo.Color = selectedColor;
                _tricycleInfo.EngineNumber = EngineNumberEntry.Text?.Trim().ToUpper() ?? "";
                _tricycleInfo.ChassisNumber = ChassisNumberEntry.Text?.Trim().ToUpper() ?? "";
                _tricycleInfo.SeatingCapacity = CapacityPicker.SelectedItem?.ToString() ?? "";
                _tricycleInfo.VehicleType = "Tricycle";
                _tricycleInfo.Status = StatusPicker.SelectedItem?.ToString() ?? "";
                _tricycleInfo.UpdatedAt = DateTime.UtcNow;

                bool success = await _firebaseConnection.SaveTricycleInfoAsync(_tricycleInfo);

                HideProcessing();
                _isProcessing = false;

                if (success)
                {
                    await UpdateDriverProfileWithLicenseAndPlate();
                    await ShowSuccessModal("Tricycle information updated successfully!");
                }
                else
                {
                    await ShowModal("Error", "Failed to save changes. Please try again.", "error", false);
                }
            }
            catch (Exception ex)
            {
                HideProcessing();
                _isProcessing = false;
                await ShowModal("Error", $"An error occurred: {ex.Message}", "error", false);
            }
        }

        private async Task UpdateDriverProfileWithLicenseAndPlate()
        {
            try
            {
                string currentLicenseNumber = LicenseNumberEntry.Text?.Trim().ToUpper();
                string currentPlateNumber = PlateNumberEntry.Text?.Trim().ToUpper();

                if (_driverInfo != null)
                {
                    _driverInfo.LicenseNumber = currentLicenseNumber;
                    _driverInfo.PlateNumber = currentPlateNumber;
                    _driverInfo.VehicleType = "Tricycle";

                    await _firebaseConnection.UpdateDriverVehicleInfoAsync(_driverId, _driverInfo);
                }

                AppSession.SaveDriverPlateNumber(currentPlateNumber);
                AppSession.SaveDriverLicenseNumber(currentLicenseNumber);
                AppSession.SaveTricycleStatus(_tricycleInfo.Status);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating driver: {ex.Message}");
            }
        }

        private async void OnModalCancelClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(ConfirmModal);
        }

        private async void OnModalKeepClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(CancelModal);
        }

        private async void OnModalDiscardClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(CancelModal);
            await Navigation.PopModalAsync();
        }

        private async void OnSuccessOkClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(SuccessModal);
            await Navigation.PopModalAsync();
        }

        private async void OnErrorOkClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(ErrorModal);
        }

        private async void OnValidationErrorOkClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(ValidationErrorModal);
        }

        private async void OnNoChangesOkClicked(object sender, EventArgs e)
        {
            await HideModalAnimated(NoChangesModal);
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await CheckForUnsavedChanges();
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

        private async Task ShowSuccessModal(string message)
        {
            _isModalOpen = true;
            SuccessModalMessage.Text = message;
            await ShowModalAnimated(SuccessModal);
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "document.png" : "sad.png";
            Color iconColor = type == "success" ? Color.FromArgb("#2E7D32") :
                             type == "warning" ? Color.FromArgb("#FF9800") :
                             type == "info" ? Color.FromArgb("#2196F3") : Color.FromArgb("#DC3545");

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

            var modal = new EditTricycleCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

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

    public class EditTricycleCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private EditTricyclePage _parentPage;

        public EditTricycleCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, EditTricyclePage parentPage)
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
}