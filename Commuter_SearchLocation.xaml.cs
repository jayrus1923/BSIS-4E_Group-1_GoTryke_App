using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;
using ServiceCo.Firebase;
using ServiceCo.Services;
using Microsoft.Maui.Storage;
using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace ServiceCo
{
    public class BoolToStarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool isFavorite && isFavorite) ? "star_filled.png" : "star_outline.png";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class Commuter_SearchLocation : ContentPage
    {
        private const string GoogleMapsApiKey = Constants.GoogleMapsApiKey;
        private readonly HttpClient _httpClient;
        private readonly FirebaseConnection _firebase;
        private CancellationTokenSource _debounceTokenSource;
        private string _currentFieldType = "pickup";
        private bool _isSearching = false;
        private bool _isPickupFocused = false;
        private bool _isDropoffFocused = false;
        private double _pickupLat = 0, _pickupLng = 0, _dropoffLat = 0, _dropoffLng = 0;
        private string _pickupAddress = "", _dropoffAddress = "";
        private string _pickupPlaceId = "", _dropoffPlaceId = "";
        private List<FavoriteItem> _favorites = new();
        private List<RecentItem> _recentItems = new();
        private string _commuterId = "";
        private bool _pageLoaded = false;
        private bool _isRecentTabSelected = true;
        private readonly SemaphoreSlim _searchSemaphore = new SemaphoreSlim(1, 1);
        private HashSet<string> _favoriteNames = new HashSet<string>();

        private readonly double MALOLOS_NORTH = 14.8950;
        private readonly double MALOLOS_SOUTH = 14.8200;
        private readonly double MALOLOS_EAST = 120.8450;
        private readonly double MALOLOS_WEST = 120.7600;

        private bool _isProcessing = false;
        private bool _isGettingLocation = false;
        private bool _isModalOpen = false;

        public Commuter_SearchLocation()
        {
            InitializeComponent();
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _firebase = new FirebaseConnection();
            _commuterId = Preferences.Get("CommuterId", "");
            SetupTabTaps();
        }

        public Commuter_SearchLocation(string preFilledPickup, string preFilledDropoff) : this()
        {
            if (!string.IsNullOrEmpty(preFilledPickup))
            {
                PickupEntry.Text = preFilledPickup;
                PickupClearButton.IsVisible = true;
                _pickupAddress = preFilledPickup;

                if (Preferences.ContainsKey("SelectedPickupLat"))
                {
                    _pickupLat = double.Parse(Preferences.Get("SelectedPickupLat", "0"));
                    _pickupLng = double.Parse(Preferences.Get("SelectedPickupLng", "0"));
                    Preferences.Remove("SelectedPickupLat");
                    Preferences.Remove("SelectedPickupLng");
                }
            }
            if (!string.IsNullOrEmpty(preFilledDropoff))
            {
                DropoffEntry.Text = preFilledDropoff;
                DropoffClearButton.IsVisible = true;
                _dropoffAddress = preFilledDropoff;

                if (Preferences.ContainsKey("SelectedDropoffLat"))
                {
                    _dropoffLat = double.Parse(Preferences.Get("SelectedDropoffLat", "0"));
                    _dropoffLng = double.Parse(Preferences.Get("SelectedDropoffLng", "0"));
                    Preferences.Remove("SelectedDropoffLat");
                    Preferences.Remove("SelectedDropoffLng");
                }
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!_pageLoaded)
            {
                _pageLoaded = true;
                await Task.Delay(100);
                await LoadRecentAndFavorites();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ShowRecentTab();
                    PickupEntry.Focus();
                });
            }
        }

        private void SetupTabTaps()
        {
            var recentTap = new TapGestureRecognizer();
            recentTap.Tapped += (s, e) => ShowRecentTab();
            RecentTab.GestureRecognizers.Add(recentTap);

            var favoriteTap = new TapGestureRecognizer();
            favoriteTap.Tapped += (s, e) => ShowFavoriteTab();
            FavoriteTab.GestureRecognizers.Add(favoriteTap);
        }

        private void ShowRecentTab()
        {
            _isRecentTabSelected = true;
            RecentTab.BackgroundColor = Color.FromArgb("#2E7D32");
            ((Label)RecentTab.Content).TextColor = Colors.White;
            ((Label)RecentTab.Content).FontAttributes = FontAttributes.Bold;
            FavoriteTab.BackgroundColor = Colors.Transparent;
            ((Label)FavoriteTab.Content).TextColor = Color.FromArgb("#616161");
            ((Label)FavoriteTab.Content).FontAttributes = FontAttributes.None;

            SuggestionsList.IsVisible = false;
            RecentList.IsVisible = true;
            FavoriteList.IsVisible = false;
            SearchingIndicator.IsVisible = false;

            RecentList.ItemsSource = null;
            RecentList.ItemsSource = _recentItems.Take(10).ToList();
            ShowEmptyStateIfNeeded();
        }

        private void ShowFavoriteTab()
        {
            _isRecentTabSelected = false;
            FavoriteTab.BackgroundColor = Color.FromArgb("#2E7D32");
            ((Label)FavoriteTab.Content).TextColor = Colors.White;
            ((Label)FavoriteTab.Content).FontAttributes = FontAttributes.Bold;
            RecentTab.BackgroundColor = Colors.Transparent;
            ((Label)RecentTab.Content).TextColor = Color.FromArgb("#616161");
            ((Label)RecentTab.Content).FontAttributes = FontAttributes.None;

            SuggestionsList.IsVisible = false;
            RecentList.IsVisible = false;
            FavoriteList.IsVisible = true;
            SearchingIndicator.IsVisible = false;

            FavoriteList.ItemsSource = null;
            FavoriteList.ItemsSource = _favorites.Take(10).ToList();
            ShowEmptyStateIfNeeded();
        }

        private void ShowSuggestionsTab()
        {
            RecentTab.BackgroundColor = Colors.Transparent;
            ((Label)RecentTab.Content).TextColor = Color.FromArgb("#616161");
            ((Label)RecentTab.Content).FontAttributes = FontAttributes.None;
            FavoriteTab.BackgroundColor = Colors.Transparent;
            ((Label)FavoriteTab.Content).TextColor = Color.FromArgb("#616161");
            ((Label)FavoriteTab.Content).FontAttributes = FontAttributes.None;

            SuggestionsList.IsVisible = true;
            RecentList.IsVisible = false;
            FavoriteList.IsVisible = false;
            SearchingIndicator.IsVisible = true;
        }

        private void ShowEmptyStateIfNeeded()
        {
            try
            {
                if (SuggestionsList.IsVisible)
                {
                    var items = SuggestionsList.ItemsSource as IEnumerable<object>;
                    bool hasItems = items != null && items.Any();
                    EmptyState.IsVisible = !hasItems;
                    EmptyStateImage.Source = "search.png";
                    EmptyStateLabel1.Text = "No results found";
                    EmptyStateLabel2.Text = "Try a different search";
                }
                else if (RecentList.IsVisible)
                {
                    var items = RecentList.ItemsSource as IEnumerable<object>;
                    bool hasItems = items != null && items.Any();
                    EmptyState.IsVisible = !hasItems;
                    EmptyStateImage.Source = "recent_empty.png";
                    EmptyStateLabel1.Text = "No recent rides";
                    EmptyStateLabel2.Text = "Your completed rides will appear here";
                }
                else if (FavoriteList.IsVisible)
                {
                    var items = FavoriteList.ItemsSource as IEnumerable<object>;
                    bool hasItems = items != null && items.Any();
                    EmptyState.IsVisible = !hasItems;
                    EmptyStateImage.Source = "star_filled.png";
                    EmptyStateLabel1.Text = "No favorites yet";
                    EmptyStateLabel2.Text = "Tap the star icon to add favorites";
                }
                else
                {
                    EmptyState.IsVisible = false;
                }
            }
            catch
            {
                EmptyState.IsVisible = false;
            }
        }

        private bool IsInMalolos(double lat, double lng)
        {
            return lat >= MALOLOS_SOUTH && lat <= MALOLOS_NORTH && lng >= MALOLOS_WEST && lng <= MALOLOS_EAST;
        }

        private void OnPickupFocused(object sender, FocusEventArgs e)
        {
            _isPickupFocused = true;
            _isDropoffFocused = false;
            _currentFieldType = "pickup";
            if (string.IsNullOrEmpty(PickupEntry.Text) && _isRecentTabSelected)
            {
                ShowRecentTab();
            }
        }

        private void OnPickupUnfocused(object sender, FocusEventArgs e)
        {
            _isPickupFocused = false;
        }

        private void OnDropoffFocused(object sender, FocusEventArgs e)
        {
            _isDropoffFocused = true;
            _isPickupFocused = false;
            _currentFieldType = "dropoff";
            if (string.IsNullOrEmpty(DropoffEntry.Text) && _isRecentTabSelected)
            {
                ShowRecentTab();
            }
        }

        private void OnDropoffUnfocused(object sender, FocusEventArgs e)
        {
            _isDropoffFocused = false;
        }

        private async void OnPickupTextChanged(object sender, TextChangedEventArgs e)
        {
            await HandleTextChanged(e.NewTextValue, "pickup");
        }

        private async void OnDropoffTextChanged(object sender, TextChangedEventArgs e)
        {
            await HandleTextChanged(e.NewTextValue, "dropoff");
        }

        private async Task HandleTextChanged(string newText, string fieldType)
        {
            try
            {
                if (fieldType == "pickup")
                {
                    PickupClearButton.IsVisible = !string.IsNullOrEmpty(newText);
                }
                else
                {
                    DropoffClearButton.IsVisible = !string.IsNullOrEmpty(newText);
                }

                if (string.IsNullOrEmpty(newText))
                {
                    _isSearching = false;
                    if (_isRecentTabSelected)
                        ShowRecentTab();
                    else
                        ShowFavoriteTab();
                    return;
                }

                _currentFieldType = fieldType;
                _isSearching = true;
                ShowSuggestionsTab();

                _debounceTokenSource?.Cancel();
                _debounceTokenSource = new CancellationTokenSource();
                var token = _debounceTokenSource.Token;

                await Task.Delay(300, token);

                if (!token.IsCancellationRequested && _isSearching)
                {
                    await PerformGooglePlacesSearch(newText, token);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch
            {
            }
        }

        private async Task PerformGooglePlacesSearch(string query, CancellationToken token)
        {
            await _searchSemaphore.WaitAsync(token);

            try
            {
                if (string.IsNullOrWhiteSpace(query) || !_isSearching || token.IsCancellationRequested)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        SearchingIndicator.IsVisible = false;
                        SuggestionsList.ItemsSource = null;
                        SuggestionsList.ItemsSource = new List<GoogleSuggestionItem>();
                        ShowEmptyStateIfNeeded();
                    });
                    return;
                }

                string url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(query)}&key={GoogleMapsApiKey}&components=country:ph&types=geocode|establishment";

                var response = await _httpClient.GetAsync(url, token);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<GooglePlacesAutocompleteResponse>(json);

                    if (token.IsCancellationRequested || !_isSearching)
                        return;

                    var suggestions = new List<GoogleSuggestionItem>();

                    if (result?.predictions != null)
                    {
                        foreach (var prediction in result.predictions.Take(10))
                        {
                            string mainText = prediction.structured_formatting?.main_text ?? prediction.description ?? "";
                            string secondaryText = prediction.structured_formatting?.secondary_text ?? "";

                            suggestions.Add(new GoogleSuggestionItem
                            {
                                PlaceId = prediction.place_id ?? "",
                                MainText = mainText,
                                SecondaryText = secondaryText,
                                FullDescription = prediction.description ?? "",
                                IconSource = "nav.png",
                                IsFavorite = _favoriteNames.Contains(mainText),
                                StarIcon = _favoriteNames.Contains(mainText) ? "star_filled.png" : "star_outline.png"
                            });
                        }
                    }

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try
                        {
                            if (_isSearching && !token.IsCancellationRequested)
                            {
                                SearchingIndicator.IsVisible = false;
                                SuggestionsList.ItemsSource = null;
                                SuggestionsList.ItemsSource = suggestions;
                                ShowEmptyStateIfNeeded();
                            }
                        }
                        catch
                        {
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SearchingIndicator.IsVisible = false;
                });
            }
            catch
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        if (_isSearching)
                        {
                            SearchingIndicator.IsVisible = false;
                            SuggestionsList.ItemsSource = null;
                            SuggestionsList.ItemsSource = new List<GoogleSuggestionItem>();
                            ShowEmptyStateIfNeeded();
                        }
                    }
                    catch
                    {
                    }
                });
            }
            finally
            {
                _searchSemaphore.Release();
            }
        }

        private async Task<(double lat, double lng, string address)> GetPlaceDetails(string placeId)
        {
            try
            {
                string url = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={placeId}&key={GoogleMapsApiKey}&fields=geometry,formatted_address";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<GooglePlaceDetailsResponse>(json);

                    if (result?.result?.geometry?.location != null)
                    {
                        double lat = result.result.geometry.location.lat;
                        double lng = result.result.geometry.location.lng;
                        string address = result.result.formatted_address ?? "";

                        return (lat, lng, address);
                    }
                }
            }
            catch
            {
            }

            return (0, 0, "");
        }

        private async Task LoadRecentAndFavorites()
        {
            try
            {
                await Task.WhenAll(LoadRecent(), LoadFavorites());
            }
            catch
            {
            }
        }

        private async Task LoadRecent()
        {
            try
            {
                if (string.IsNullOrEmpty(_commuterId)) return;
                var completedRides = await _firebase.GetCommuterCompletedRidesAsync(_commuterId, 50) ?? new();

                var uniqueRecent = new List<RecentItem>();
                var seenNames = new HashSet<string>();

                foreach (var ride in completedRides.OrderByDescending(r => r.AddedDate))
                {
                    if (!seenNames.Contains(ride.Name))
                    {
                        seenNames.Add(ride.Name);
                        ride.IsFavorite = _favoriteNames.Contains(ride.Name);
                        uniqueRecent.Add(ride);
                    }
                }

                _recentItems = uniqueRecent;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (RecentList.IsVisible)
                    {
                        RecentList.ItemsSource = null;
                        RecentList.ItemsSource = _recentItems.Take(10).ToList();
                    }
                    ShowEmptyStateIfNeeded();
                });
            }
            catch
            {
                _recentItems = new();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (RecentList.IsVisible)
                    {
                        RecentList.ItemsSource = null;
                        RecentList.ItemsSource = _recentItems;
                    }
                    ShowEmptyStateIfNeeded();
                });
            }
        }

        private async Task LoadFavorites()
        {
            try
            {
                _favorites = await _firebase.GetFavoritesAsync(_commuterId) ?? new();
                _favoriteNames = new HashSet<string>(_favorites.Select(f => f.Name));

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (FavoriteList.IsVisible)
                    {
                        FavoriteList.ItemsSource = null;
                        FavoriteList.ItemsSource = _favorites.Take(10).ToList();
                    }

                    if (SuggestionsList.ItemsSource is List<GoogleSuggestionItem> suggestions)
                    {
                        bool needsRefresh = false;
                        foreach (var item in suggestions)
                        {
                            bool isFav = _favoriteNames.Contains(item.MainText);
                            if (item.IsFavorite != isFav)
                            {
                                item.IsFavorite = isFav;
                                item.StarIcon = isFav ? "star_filled.png" : "star_outline.png";
                                needsRefresh = true;
                            }
                        }
                        if (needsRefresh)
                        {
                            SuggestionsList.ItemsSource = null;
                            SuggestionsList.ItemsSource = suggestions;
                        }
                    }

                    bool recentNeedsRefresh = false;
                    foreach (var recent in _recentItems)
                    {
                        bool isFav = _favoriteNames.Contains(recent.Name);
                        if (recent.IsFavorite != isFav)
                        {
                            recent.IsFavorite = isFav;
                            recentNeedsRefresh = true;
                        }
                    }
                    if (recentNeedsRefresh && RecentList.IsVisible)
                    {
                        RecentList.ItemsSource = null;
                        RecentList.ItemsSource = _recentItems.Take(10).ToList();
                    }

                    ShowEmptyStateIfNeeded();
                });
            }
            catch
            {
                _favorites = new();
                _favoriteNames = new();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (FavoriteList.IsVisible)
                    {
                        FavoriteList.ItemsSource = null;
                        FavoriteList.ItemsSource = _favorites;
                    }
                    ShowEmptyStateIfNeeded();
                });
            }
        }

        private async void OnSuggestionStarTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is GoogleSuggestionItem suggestion)
            {
                var details = await GetPlaceDetails(suggestion.PlaceId);
                await ToggleFavorite(suggestion.MainText, suggestion.FullDescription, suggestion.PlaceId, details.lat, details.lng, !suggestion.IsFavorite);
            }
        }

        private async void OnRecentStarTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is RecentItem recentItem)
            {
                double lat = recentItem.Latitude;
                double lng = recentItem.Longitude;
                string placeId = recentItem.MapboxId;

                if (lat == 0 && lng == 0 && !string.IsNullOrEmpty(placeId))
                {
                    var details = await GetPlaceDetails(placeId);
                    lat = details.lat;
                    lng = details.lng;
                }

                await ToggleFavorite(recentItem.Name, recentItem.Address, placeId, lat, lng, !recentItem.IsFavorite);
            }
        }

        private async Task ToggleFavorite(string name, string address, string placeId, double lat, double lng, bool addToFavorites)
        {
            try
            {
                if (addToFavorites)
                {
                    var favoriteItem = new FavoriteItem
                    {
                        Name = name,
                        Address = address,
                        Latitude = lat,
                        Longitude = lng,
                        MapboxId = placeId,
                        AddedDate = DateTime.UtcNow
                    };

                    await _firebase.ToggleFavoriteLocationAsync(_commuterId, favoriteItem);
                }
                else
                {
                    var existing = _favorites.FirstOrDefault(f => f.Name == name);
                    if (existing != null)
                    {
                        await _firebase.RemoveFavoriteAsync(_commuterId, existing.Id);
                    }
                }

                await LoadFavorites();
            }
            catch
            {
                await ShowModal("Error", "Failed to update favorites.", "error");
            }
        }

        private async void OnRecentItemTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is RecentItem recentItem)
            {
                if (recentItem.Latitude != 0 && recentItem.Longitude != 0)
                {
                    SetLocation(recentItem.Name, recentItem.Latitude, recentItem.Longitude, recentItem.Address, recentItem.MapboxId);
                }
                else
                {
                    if (!string.IsNullOrEmpty(recentItem.MapboxId))
                    {
                        var details = await GetPlaceDetails(recentItem.MapboxId);
                        if (details.lat != 0 && details.lng != 0)
                        {
                            SetLocation(recentItem.Name, details.lat, details.lng, details.address, recentItem.MapboxId);
                        }
                        else
                        {
                            await ShowModal("Error", "Could not get location details", "error");
                        }
                    }
                    else
                    {
                        await ShowModal("Error", "Location details not available", "error");
                    }
                }
            }
        }

        private async void OnFavoriteItemTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is FavoriteItem favoriteItem)
            {
                if (favoriteItem.Latitude != 0 && favoriteItem.Longitude != 0)
                {
                    SetLocation(favoriteItem.Name, favoriteItem.Latitude, favoriteItem.Longitude, favoriteItem.Address, favoriteItem.MapboxId);
                }
                else
                {
                    if (!string.IsNullOrEmpty(favoriteItem.MapboxId))
                    {
                        var details = await GetPlaceDetails(favoriteItem.MapboxId);
                        if (details.lat != 0 && details.lng != 0)
                        {
                            SetLocation(favoriteItem.Name, details.lat, details.lng, details.address, favoriteItem.MapboxId);
                        }
                        else
                        {
                            await ShowModal("Error", "Could not get location details", "error");
                        }
                    }
                    else
                    {
                        await ShowModal("Error", "Location details not available", "error");
                    }
                }
            }
        }

        private async void OnFavoriteDeleteTapped(object sender, TappedEventArgs e)
        {
            try
            {
                if (e.Parameter is FavoriteItem favorite)
                {
                    bool confirm = await ShowConfirmModal("Remove Favorite", $"Remove '{favorite.Name}' from favorites?");
                    if (confirm)
                    {
                        await _firebase.RemoveFavoriteAsync(_commuterId, favorite.Id);
                        await LoadFavorites();
                    }
                }
            }
            catch
            {
            }
        }

        private async void OnSuggestionItemTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is GoogleSuggestionItem suggestion && !string.IsNullOrEmpty(suggestion.PlaceId))
            {
                ShowSuggestionsTab();
                var details = await GetPlaceDetails(suggestion.PlaceId);
                if (details.lat != 0 && details.lng != 0)
                {
                    SetLocation(suggestion.MainText, details.lat, details.lng, details.address, suggestion.PlaceId);
                }
                else
                {
                    await ShowModal("Error", "Could not get location details", "error");
                }
            }
        }

        private void SetLocation(string name, double lat, double lng, string address, string placeId)
        {
            try
            {
                if (_currentFieldType == "pickup")
                {
                    PickupEntry.Text = name;
                    PickupClearButton.IsVisible = true;
                    _pickupLat = lat;
                    _pickupLng = lng;
                    _pickupAddress = address;
                    _pickupPlaceId = placeId;
                }
                else
                {
                    DropoffEntry.Text = name;
                    DropoffClearButton.IsVisible = true;
                    _dropoffLat = lat;
                    _dropoffLng = lng;
                    _dropoffAddress = address;
                    _dropoffPlaceId = placeId;
                }
                _isSearching = false;
                if (_isRecentTabSelected)
                    ShowRecentTab();
                else
                    ShowFavoriteTab();
            }
            catch
            {
            }
        }

        private void OnPickupClearTapped(object sender, TappedEventArgs e)
        {
            try
            {
                PickupEntry.Text = "";
                PickupClearButton.IsVisible = false;
                _isSearching = false;
                if (_isRecentTabSelected)
                    ShowRecentTab();
                else
                    ShowFavoriteTab();
                _pickupLat = 0;
                _pickupLng = 0;
                _pickupAddress = "";
                _pickupPlaceId = "";
            }
            catch
            {
            }
        }

        private void OnDropoffClearTapped(object sender, TappedEventArgs e)
        {
            try
            {
                DropoffEntry.Text = "";
                DropoffClearButton.IsVisible = false;
                _isSearching = false;
                if (_isRecentTabSelected)
                    ShowRecentTab();
                else
                    ShowFavoriteTab();
                _dropoffLat = 0;
                _dropoffLng = 0;
                _dropoffAddress = "";
                _dropoffPlaceId = "";
            }
            catch
            {
            }
        }

        private async void OnPickupMapTapped(object sender, TappedEventArgs e)
        {
            await OpenMapPicker("pickup");
        }

        private async void OnDropoffMapTapped(object sender, TappedEventArgs e)
        {
            await OpenMapPicker("dropoff");
        }

        private async Task OpenMapPicker(string fieldType)
        {
            try
            {
                string currentFieldType = fieldType;
                var mapPage = new MapPickerPage(fieldType, GoogleMapsApiKey);
                mapPage.OnLocationSelected = (result) =>
                {
                    if (!string.IsNullOrEmpty(result))
                    {
                        var parts = result.Split('|');
                        if (parts.Length >= 3 && double.TryParse(parts[1], out double lat) && double.TryParse(parts[2], out double lng))
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                try
                                {
                                    if (currentFieldType == "pickup")
                                    {
                                        PickupEntry.Text = parts[0];
                                        PickupClearButton.IsVisible = true;
                                        _pickupLat = lat;
                                        _pickupLng = lng;
                                        _pickupAddress = parts[0];
                                    }
                                    else
                                    {
                                        DropoffEntry.Text = parts[0];
                                        DropoffClearButton.IsVisible = true;
                                        _dropoffLat = lat;
                                        _dropoffLng = lng;
                                        _dropoffAddress = parts[0];
                                    }
                                    _isSearching = false;
                                    if (_isRecentTabSelected)
                                        ShowRecentTab();
                                    else
                                        ShowFavoriteTab();
                                }
                                catch
                                {
                                }
                            });
                        }
                    }
                };
                await Navigation.PushModalAsync(mapPage);
            }
            catch
            {
            }
        }

        private void OnSwapTapped(object sender, EventArgs e)
        {
            try
            {
                var tempText = PickupEntry.Text;
                PickupEntry.Text = DropoffEntry.Text;
                DropoffEntry.Text = tempText;

                var tempLat = _pickupLat;
                var tempLng = _pickupLng;
                _pickupLat = _dropoffLat;
                _pickupLng = _dropoffLng;
                _dropoffLat = tempLat;
                _dropoffLng = tempLng;

                var tempAddress = _pickupAddress;
                _pickupAddress = _dropoffAddress;
                _dropoffAddress = tempAddress;

                var tempPlaceId = _pickupPlaceId;
                _pickupPlaceId = _dropoffPlaceId;
                _dropoffPlaceId = tempPlaceId;

                PickupClearButton.IsVisible = !string.IsNullOrEmpty(PickupEntry.Text);
                DropoffClearButton.IsVisible = !string.IsNullOrEmpty(DropoffEntry.Text);

                _isSearching = false;
                if (_isRecentTabSelected)
                    ShowRecentTab();
                else
                    ShowFavoriteTab();
                ValidationContainer.IsVisible = false;
            }
            catch
            {
            }
        }

        private async void OnEnterClicked(object sender, EventArgs e)
        {
            try
            {
                ValidationContainer.IsVisible = false;
                var pickup = PickupEntry.Text?.Trim() ?? "";
                var dropoff = DropoffEntry.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(pickup) && string.IsNullOrWhiteSpace(dropoff))
                {
                    ShowValidation("Please select pickup and drop-off location.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(pickup))
                {
                    ShowValidation("Please enter a pickup location.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(dropoff))
                {
                    ShowValidation("Please enter a drop-off location.");
                    return;
                }

                if (pickup.Equals(dropoff, StringComparison.OrdinalIgnoreCase))
                {
                    ShowValidation("Pickup and drop-off cannot be the same location.");
                    return;
                }

                bool isPickupValid = _pickupLat != 0 && _pickupLng != 0 && IsInMalolos(_pickupLat, _pickupLng);
                bool isDropoffValid = _dropoffLat != 0 && _dropoffLng != 0 && IsInMalolos(_dropoffLat, _dropoffLng);

                if (!isPickupValid || !isDropoffValid)
                {
                    string invalidLocations = "";
                    if (!isPickupValid && !isDropoffValid)
                        invalidLocations = "Pickup and drop-off locations are";
                    else if (!isPickupValid)
                        invalidLocations = "Pickup location is";
                    else
                        invalidLocations = "Drop-off location is";

                    await ShowModal("Location Not Supported", $"{invalidLocations} outside Malolos service area.", "warning");
                    return;
                }

                SearchingIndicator.IsVisible = false;
                ValidationContainer.IsVisible = false;

                await Navigation.PushModalAsync(new Commuter_SelectRide(
                    pickup, dropoff, _pickupLat, _pickupLng, _dropoffLat, _dropoffLng));
            }
            catch
            {
                ShowValidation("Error processing locations.");
            }
        }

        private void ShowValidation(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    ValidationLabel.Text = message;
                    ValidationLabel.TextColor = Color.FromArgb("#B71C1C");
                    ValidationContainer.IsVisible = true;
                    _isSearching = false;
                    SearchingIndicator.IsVisible = false;
                    if (_isRecentTabSelected)
                        ShowRecentTab();
                    else
                        ShowFavoriteTab();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Validation error: {ex.Message}");
                }
            });
        }

        private async void OnUseCurrentLocationClicked(object sender, EventArgs e)
        {
            if (_isGettingLocation || _isModalOpen) return;

            try
            {
                _isGettingLocation = true;

                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Opacity = 0.5;
                }

                var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    await ShowModal("Permission Required", "Location permission is required.", "warning");
                    return;
                }

                var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
                var location = await Geolocation.GetLocationAsync(request);

                if (location != null)
                {
                    bool isWithinService = IsInMalolos(location.Latitude, location.Longitude);

                    if (isWithinService)
                    {
                        string address = await GetAddressFromCoordinates(location.Latitude, location.Longitude);
                        PickupEntry.Text = string.IsNullOrEmpty(address) ? "Current Location" : address;
                        PickupClearButton.IsVisible = true;
                        _pickupLat = location.Latitude;
                        _pickupLng = location.Longitude;
                        _pickupAddress = address;
                        await ShowModal("Success", "Current location set!", "success");
                    }
                    else
                    {
                        await ShowModal("Location Not Supported", "Your current location is outside Malolos service area.", "warning");
                    }
                }
                else
                {
                    await ShowModal("Error", "Unable to get current location.", "error");
                }
            }
            catch
            {
                if (!_isModalOpen)
                {
                    await ShowModal("Error", "Unable to get current location.", "error");
                }
            }
            finally
            {
                await Task.Delay(1000);

                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Opacity = 1;
                }
                _isGettingLocation = false;
            }
        }

        private async Task<string> GetAddressFromCoordinates(double lat, double lng)
        {
            try
            {
                string url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={lat},{lng}&key={GoogleMapsApiKey}";

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(json);
                    if (result?.results != null && result.results.Count > 0)
                    {
                        return result.results[0].formatted_address ?? "Current Location";
                    }
                }
            }
            catch
            {
            }
            return "Current Location";
        }

        private async void OnBackTapped(object sender, TappedEventArgs e)
        {
            try
            {
                await Navigation.PopModalAsync();
            }
            catch
            {
            }
        }

        private void OnRecentTabTapped(object sender, EventArgs e)
        {
            ShowRecentTab();
        }

        private void OnFavoriteTabTapped(object sender, EventArgs e)
        {
            ShowFavoriteTab();
        }

        private async Task ShowModal(string title, string message, string type = "info")
        {
            if (_isModalOpen) return;

            try
            {
                _isModalOpen = true;

                string iconSource = type switch
                {
                    "success" => "happy.png",
                    "error" => "cancelled.png",
                    "warning" => "sad.png",
                    _ => "signal.png"
                };

                Color iconColor = type switch
                {
                    "success" => Color.FromArgb("#2E7D32"),
                    "error" => Color.FromArgb("#D32F2F"),
                    "warning" => Color.FromArgb("#D32F2F"),
                    _ => Color.FromArgb("#1976D2")
                };

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
                                HorizontalOptions = LayoutOptions.Center
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

                var contentGrid = new Grid
                {
                    Children = { blurOverlay, modalContent }
                };

                var modal = new ModalPage(modalContent, blurOverlay);
                await Navigation.PushModalAsync(modal);

                await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
                await Task.WhenAll(
                    modalContent.FadeTo(1, 350, Easing.SpringOut),
                    modalContent.ScaleTo(1, 450, Easing.SpringOut)
                );

                var button = (modalContent.Content as VerticalStackLayout).Children[3] as Button;
                button.Clicked += async (s, e) =>
                {
                    button.IsEnabled = false;
                    await AnimateModalExit(modal, modalContent, blurOverlay);
                };
            }
            catch
            {
                _isModalOpen = false;
            }
        }

        private async Task<bool> ShowConfirmModal(string title, string message)
        {
            if (_isModalOpen) return false;

            var tcs = new TaskCompletionSource<bool>();

            try
            {
                _isModalOpen = true;

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
                                Source = "star_filled.png",
                                HeightRequest = 60,
                                WidthRequest = 60,
                                HorizontalOptions = LayoutOptions.Center
                            },
                            new Label
                            {
                                Text = title,
                                FontSize = 18,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#2E7D32"),
                                HorizontalOptions = LayoutOptions.Center
                            },
                            new Label
                            {
                                Text = message,
                                FontSize = 14,
                                TextColor = Color.FromArgb("#666666"),
                                HorizontalOptions = LayoutOptions.Center,
                                HorizontalTextAlignment = TextAlignment.Center
                            },
                            new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitionCollection
                                {
                                    new ColumnDefinition { Width = GridLength.Star },
                                    new ColumnDefinition { Width = GridLength.Star }
                                },
                                ColumnSpacing = 10,
                                Children =
                                {
                                    new Button
                                    {
                                        Text = "No",
                                        BackgroundColor = Color.FromArgb("#9E9E9E"),
                                        TextColor = Colors.White,
                                        CornerRadius = 10,
                                        HeightRequest = 45,
                                        FontSize = 14,
                                        FontAttributes = FontAttributes.Bold
                                    }.WithColumn(0),
                                    new Button
                                    {
                                        Text = "Yes",
                                        BackgroundColor = Color.FromArgb("#2E7D32"),
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

                var contentGrid = new Grid
                {
                    Children = { blurOverlay, modalContent }
                };

                var modal = new ModalPage(modalContent, blurOverlay);
                await Navigation.PushModalAsync(modal);

                await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
                await Task.WhenAll(
                    modalContent.FadeTo(1, 350, Easing.SpringOut),
                    modalContent.ScaleTo(1, 450, Easing.SpringOut)
                );

                var noButton = ((modalContent.Content as VerticalStackLayout).Children[3] as Grid).Children[0] as Button;
                var yesButton = ((modalContent.Content as VerticalStackLayout).Children[3] as Grid).Children[1] as Button;

                noButton.Clicked += async (s, e) =>
                {
                    noButton.IsEnabled = false;
                    yesButton.IsEnabled = false;
                    await AnimateModalExit(modal, modalContent, blurOverlay);
                    tcs.TrySetResult(false);
                };

                yesButton.Clicked += async (s, e) =>
                {
                    noButton.IsEnabled = false;
                    yesButton.IsEnabled = false;
                    await AnimateModalExit(modal, modalContent, blurOverlay);
                    tcs.TrySetResult(true);
                };

                return await tcs.Task;
            }
            catch
            {
                _isModalOpen = false;
                return false;
            }
        }

        private async Task AnimateModalExit(ContentPage modal, Frame modalContent, Grid blurOverlay)
        {
            await Task.WhenAll(
                modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                modalContent.FadeTo(0, 200, Easing.CubicIn),
                modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );
            await Navigation.PopModalAsync();
            _isModalOpen = false;
        }

        public class ModalPage : ContentPage
        {
            private Frame _modalContent;
            private Grid _blurOverlay;

            public ModalPage(Frame modalContent, Grid blurOverlay)
            {
                _modalContent = modalContent;
                _blurOverlay = blurOverlay;
                BackgroundColor = Colors.Transparent;
                Content = new Grid
                {
                    Children = { blurOverlay, modalContent }
                };
            }

            protected override bool OnBackButtonPressed()
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var stackLayout = _modalContent.Content as VerticalStackLayout;
                    if (stackLayout != null && stackLayout.Children.Count > 3)
                    {
                        var button = stackLayout.Children[3] as Button;
                        if (button != null)
                            button.IsEnabled = false;

                        var grid = stackLayout.Children[3] as Grid;
                        if (grid != null)
                        {
                            var noButton = grid.Children[0] as Button;
                            var yesButton = grid.Children[1] as Button;
                            if (noButton != null) noButton.IsEnabled = false;
                            if (yesButton != null) yesButton.IsEnabled = false;
                        }
                    }

                    await Task.WhenAll(
                        _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                        _modalContent.FadeTo(0, 200, Easing.CubicIn),
                        _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                        _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                    );

                    await Navigation.PopModalAsync();
                });

                return true;
            }
        }

        public class GoogleSuggestionItem
        {
            public string PlaceId { get; set; } = "";
            public string MainText { get; set; } = "";
            public string SecondaryText { get; set; } = "";
            public string FullDescription { get; set; } = "";
            public string IconSource { get; set; } = "nav.png";
            public bool IsFavorite { get; set; }
            public string StarIcon { get; set; } = "star_outline.png";
        }

        public class GooglePlacesAutocompleteResponse
        {
            public List<GooglePrediction> predictions { get; set; } = new();
            public string status { get; set; } = "";
        }

        public class GooglePrediction
        {
            public string description { get; set; } = "";
            public string place_id { get; set; } = "";
            public List<string> types { get; set; } = new();
            public GoogleStructuredFormatting structured_formatting { get; set; } = new();
        }

        public class GoogleStructuredFormatting
        {
            public string main_text { get; set; } = "";
            public string secondary_text { get; set; } = "";
        }

        public class GooglePlaceDetailsResponse
        {
            public GooglePlaceResult result { get; set; } = new();
            public string status { get; set; } = "";
        }

        public class GooglePlaceResult
        {
            public GoogleGeometry geometry { get; set; } = new();
            public string formatted_address { get; set; } = "";
        }

        public class GoogleGeometry
        {
            public GoogleLocation location { get; set; } = new();
        }

        public class GoogleLocation
        {
            public double lat { get; set; }
            public double lng { get; set; }
        }

        public class GoogleGeocodeResponse
        {
            public List<GoogleGeocodeResult> results { get; set; } = new();
        }

        public class GoogleGeocodeResult
        {
            public string formatted_address { get; set; } = "";
        }
    }

    public static class ViewExtensions
    {
        public static T WithColumn<T>(this T view, int column) where T : View
        {
            Grid.SetColumn(view, column);
            return view;
        }
    }
}