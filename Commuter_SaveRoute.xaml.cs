using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ServiceCo.Firebase;
using Microsoft.Maui.Storage;

namespace ServiceCo
{
    public partial class Commuter_SaveRoute : ContentPage
    {
        private readonly FirebaseConnection _firebase;
        private string _commuterId = "";
        private List<FavoriteItem> _favorites = new();
        private List<RecentItem> _recentItems = new();
        private FavoriteItem _selectedFavorite = null;
        private RecentItem _selectedRecent = null;
        private bool _isModalOpen = false;
        private Action _confirmYesAction = null;
        private Action _confirmNoAction = null;

        public Commuter_SaveRoute()
        {
            InitializeComponent();
            _firebase = new FirebaseConnection();
            _commuterId = AppSession.GetCommuterId();

            if (string.IsNullOrEmpty(_commuterId))
            {
                _commuterId = Preferences.Get("CommuterId", "");
            }

            BindingContext = this;

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => HideAllModals();
            ModalOverlay.GestureRecognizers.Add(tapGesture);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await this.FadeTo(1, 300, Easing.CubicOut);
            await LoadFavorites();
            await LoadRecent();
            UpdateCountLabels();
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                await this.FadeTo(0, 250, Easing.CubicIn);
                await Navigation.PopModalAsync();
            });
            return true;
        }

        private void ShowModal(Frame modal)
        {
            ModalOverlay.IsVisible = true;
            modal.IsVisible = true;
        }

        private void HideModal(Frame modal)
        {
            modal.IsVisible = false;
            CheckAllModalsHidden();
        }

        private void HideAllModals()
        {
            FavoriteOptionsModal.IsVisible = false;
            ConfirmationModal.IsVisible = false;
            DetailsModal.IsVisible = false;
            SuccessModal.IsVisible = false;
            PickupSelectionModal.IsVisible = false;
            DropoffSelectionModal.IsVisible = false;
            CheckAllModalsHidden();
        }

        private void CheckAllModalsHidden()
        {
            bool anyModalVisible =
                FavoriteOptionsModal.IsVisible ||
                ConfirmationModal.IsVisible ||
                DetailsModal.IsVisible ||
                SuccessModal.IsVisible ||
                PickupSelectionModal.IsVisible ||
                DropoffSelectionModal.IsVisible;

            ModalOverlay.IsVisible = anyModalVisible;
        }

        private async Task ShowCustomModal(string title, string message, string type, Action onOk = null, Action onCancel = null, string confirmText = "OK", string cancelText = null)
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "phone.png" : "complaint.png";
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
                WidthRequest = 280
            };

            var contentLayout = new VerticalStackLayout
            {
                Spacing = 20
            };

            contentLayout.Children.Add(new Image
            {
                Source = iconSource,
                HeightRequest = 60,
                WidthRequest = 60,
                HorizontalOptions = LayoutOptions.Center
            });

            contentLayout.Children.Add(new Label
            {
                Text = title,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = iconColor,
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center
            });

            contentLayout.Children.Add(new Label
            {
                Text = message,
                FontSize = 14,
                TextColor = Color.FromArgb("#666666"),
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center
            });

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection()
            };

            if (cancelText != null)
            {
                buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                buttonGrid.ColumnSpacing = 10;

                var cancelButton = new Button
                {
                    Text = cancelText,
                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                    TextColor = Color.FromArgb("#666666"),
                    CornerRadius = 10,
                    HeightRequest = 45,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold
                };
                cancelButton.Clicked += async (s, e) =>
                {
                    cancelButton.IsEnabled = false;
                    await AnimateModalExit(modalContent, blurOverlay);
                    onCancel?.Invoke();
                    _isModalOpen = false;
                };
                Grid.SetColumn(cancelButton, 0);
                buttonGrid.Children.Add(cancelButton);

                var okButton = new Button
                {
                    Text = confirmText,
                    BackgroundColor = iconColor,
                    TextColor = Colors.White,
                    CornerRadius = 10,
                    HeightRequest = 45,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold
                };
                okButton.Clicked += async (s, e) =>
                {
                    okButton.IsEnabled = false;
                    await AnimateModalExit(modalContent, blurOverlay);
                    onOk?.Invoke();
                    _isModalOpen = false;
                };
                Grid.SetColumn(okButton, 1);
                buttonGrid.Children.Add(okButton);
            }
            else
            {
                buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var okButton = new Button
                {
                    Text = confirmText,
                    BackgroundColor = iconColor,
                    TextColor = Colors.White,
                    CornerRadius = 10,
                    HeightRequest = 45,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold
                };
                okButton.Clicked += async (s, e) =>
                {
                    okButton.IsEnabled = false;
                    await AnimateModalExit(modalContent, blurOverlay);
                    onOk?.Invoke();
                    _isModalOpen = false;
                };
                Grid.SetColumn(okButton, 0);
                buttonGrid.Children.Add(okButton);
            }

            contentLayout.Children.Add(buttonGrid);
            modalContent.Content = contentLayout;

            var modalPage = new CommuterSaveRouteCustomModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task AnimateModalExit(Frame modalContent, Grid blurOverlay)
        {
            await Task.WhenAll(
                modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                modalContent.FadeTo(0, 200, Easing.CubicIn),
                modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );
        }

        public void CloseCustomModal()
        {
            _isModalOpen = false;
        }

        private void OnCloseModalTapped(object sender, EventArgs e)
        {
            HideAllModals();
        }

        private void OnConfirmationYesTapped(object sender, EventArgs e)
        {
            HideAllModals();
            _confirmYesAction?.Invoke();
            _confirmYesAction = null;
            _confirmNoAction = null;
        }

        private void OnConfirmationNoTapped(object sender, EventArgs e)
        {
            HideAllModals();
            _confirmNoAction?.Invoke();
            _confirmYesAction = null;
            _confirmNoAction = null;
        }

        private void OnCloseDetailsModalTapped(object sender, EventArgs e)
        {
            HideModal(DetailsModal);
        }

        private void OnCloseSuccessModalTapped(object sender, EventArgs e)
        {
            HideModal(SuccessModal);
        }

        private void OnClosePickupModalTapped(object sender, EventArgs e)
        {
            HideModal(PickupSelectionModal);
        }

        private void OnCloseDropoffModalTapped(object sender, EventArgs e)
        {
            HideModal(DropoffSelectionModal);
        }

        private void ShowFavoriteOptionsModal(FavoriteItem favorite)
        {
            _selectedFavorite = favorite;
            FavoriteModalTitle.Text = favorite.Name;
            FavoriteLocationName.Text = favorite.Name;
            FavoriteLocationAddress.Text = favorite.Address;

            HideAllModals();
            ShowModal(FavoriteOptionsModal);
        }

        private void OnFavoriteSetPickupTapped(object sender, EventArgs e)
        {
            HideModal(FavoriteOptionsModal);
            ShowCustomModal(
                "Set as Pickup",
                $"Set '{_selectedFavorite.Name}' as pickup?",
                "success",
                async () => await NavigateToSearchLocationWithPickup(_selectedFavorite),
                null,
                "Confirm",
                "Cancel"
            );
        }

        private void OnFavoriteSetDropoffTapped(object sender, EventArgs e)
        {
            HideModal(FavoriteOptionsModal);
            ShowCustomModal(
                "Set as Dropoff",
                $"Set '{_selectedFavorite.Name}' as dropoff?",
                "success",
                async () => await NavigateToSearchLocationWithDropoff(_selectedFavorite),
                null,
                "Confirm",
                "Cancel"
            );
        }

        private void OnFavoriteViewDetailsTapped(object sender, EventArgs e)
        {
            ShowDetailsModal(_selectedFavorite);
        }

        private void OnFavoriteRemoveTapped(object sender, EventArgs e)
        {
            HideModal(FavoriteOptionsModal);
            ShowCustomModal(
                "Remove Favorite",
                $"Remove '{_selectedFavorite.Name}'?",
                "warning",
                async () => await RemoveFavorite(_selectedFavorite),
                null,
                "Remove",
                "Cancel"
            );
        }

        private void ShowDetailsModal(FavoriteItem favorite)
        {
            DetailsName.Text = favorite.Name;
            DetailsAddress.Text = favorite.Address;
            DetailsDate.Text = favorite.AddedDate.ToString("MMMM dd, yyyy");
            DetailsCoordinates.Text = $"{favorite.Latitude:F6}, {favorite.Longitude:F6}";

            HideAllModals();
            ShowModal(DetailsModal);
        }

        private void ShowPickupSelectionModal()
        {
            PickupModalTitle.Text = "Select Pickup";
            PickupSearchEntry.Text = "";
            LoadPickupFavoritesList();
            HideAllModals();
            ShowModal(PickupSelectionModal);
        }

        private void ShowDropoffSelectionModal()
        {
            DropoffModalTitle.Text = "Select Dropoff";
            DropoffSearchEntry.Text = "";
            LoadDropoffFavoritesList();
            HideAllModals();
            ShowModal(DropoffSelectionModal);
        }

        private void OnPickupSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            LoadPickupFavoritesList(e.NewTextValue);
        }

        private void OnDropoffSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            LoadDropoffFavoritesList(e.NewTextValue);
        }

        private void LoadPickupFavoritesList(string searchText = "")
        {
            var filteredFavorites = _favorites;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filteredFavorites = _favorites
                    .Where(f => f.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                               f.Address.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            PickupFavoritesList.Children.Clear();

            if (filteredFavorites.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "No favorites found",
                    FontSize = 14,
                    TextColor = Colors.Gray,
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 20)
                };
                PickupFavoritesList.Children.Add(emptyLabel);
                return;
            }

            foreach (var favorite in filteredFavorites.Take(8))
            {
                var favoriteFrame = CreateFavoriteSelectionItem(favorite, OnPickupFavoriteSelected);
                PickupFavoritesList.Children.Add(favoriteFrame);
            }
        }

        private void LoadDropoffFavoritesList(string searchText = "")
        {
            var filteredFavorites = _favorites;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filteredFavorites = _favorites
                    .Where(f => f.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                               f.Address.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            DropoffFavoritesList.Children.Clear();

            if (filteredFavorites.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "No favorites found",
                    FontSize = 14,
                    TextColor = Colors.Gray,
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 20)
                };
                DropoffFavoritesList.Children.Add(emptyLabel);
                return;
            }

            foreach (var favorite in filteredFavorites.Take(8))
            {
                var favoriteFrame = CreateFavoriteSelectionItem(favorite, OnDropoffFavoriteSelected);
                DropoffFavoritesList.Children.Add(favoriteFrame);
            }
        }

        private Frame CreateFavoriteSelectionItem(FavoriteItem favorite, Action<FavoriteItem> onSelected)
        {
            var frame = new Frame
            {
                BackgroundColor = Colors.White,
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                HasShadow = false,
                BorderColor = Color.FromArgb("#E0E0E0"),
                CornerRadius = 12
            };

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => onSelected?.Invoke(favorite);
            frame.GestureRecognizers.Add(tapGesture);

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 12,
                VerticalOptions = LayoutOptions.Center
            };

            var iconFrame = new Frame
            {
                BackgroundColor = Color.FromArgb("#E8F5E9"),
                CornerRadius = 10,
                Padding = 8,
                WidthRequest = 36,
                HeightRequest = 36,
                VerticalOptions = LayoutOptions.Center,
                HasShadow = false
            };
            iconFrame.Content = new Image
            {
                Source = "dropoff.png",
                HeightRequest = 18,
                WidthRequest = 18
            };
            Grid.SetColumn(iconFrame, 0);
            grid.Children.Add(iconFrame);

            var detailsStack = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center
            };
            detailsStack.Children.Add(new Label
            {
                Text = favorite.Name,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            });
            detailsStack.Children.Add(new Label
            {
                Text = favorite.Address,
                FontSize = 12,
                TextColor = Colors.Gray,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            });
            Grid.SetColumn(detailsStack, 1);
            grid.Children.Add(detailsStack);

            frame.Content = grid;
            return frame;
        }

        private async void OnPickupFavoriteSelected(FavoriteItem favorite)
        {
            try
            {
                HideModal(PickupSelectionModal);

                Preferences.Set("SelectedPickupLat", favorite.Latitude.ToString());
                Preferences.Set("SelectedPickupLng", favorite.Longitude.ToString());

                await Task.Delay(100);

                await Navigation.PopModalAsync();
                await Navigation.PushModalAsync(new Commuter_SearchLocation(favorite.Name, ""));
            }
            catch (Exception ex)
            {
                await ShowCustomModal("Error", $"Failed to set pickup: {ex.Message}", "error", null);
            }
        }

        private async void OnDropoffFavoriteSelected(FavoriteItem favorite)
        {
            try
            {
                HideModal(DropoffSelectionModal);

                Preferences.Set("SelectedDropoffLat", favorite.Latitude.ToString());
                Preferences.Set("SelectedDropoffLng", favorite.Longitude.ToString());

                await Task.Delay(100);

                await Navigation.PopModalAsync();
                await Navigation.PushModalAsync(new Commuter_SearchLocation("", favorite.Name));
            }
            catch (Exception ex)
            {
                await ShowCustomModal("Error", $"Failed to set dropoff: {ex.Message}", "error", null);
            }
        }

        private async Task NavigateToSearchLocationWithPickup(FavoriteItem favorite)
        {
            try
            {
                Preferences.Set("SelectedPickupLat", favorite.Latitude.ToString());
                Preferences.Set("SelectedPickupLng", favorite.Longitude.ToString());

                HideAllModals();
                await Navigation.PopModalAsync();
                await Navigation.PushModalAsync(new Commuter_SearchLocation(favorite.Name, ""));
            }
            catch (Exception ex)
            {
                await ShowCustomModal("Error", $"Failed to set pickup: {ex.Message}", "error", null);
            }
        }

        private async Task NavigateToSearchLocationWithDropoff(FavoriteItem favorite)
        {
            try
            {
                Preferences.Set("SelectedDropoffLat", favorite.Latitude.ToString());
                Preferences.Set("SelectedDropoffLng", favorite.Longitude.ToString());

                HideAllModals();
                await Navigation.PopModalAsync();
                await Navigation.PushModalAsync(new Commuter_SearchLocation("", favorite.Name));
            }
            catch (Exception ex)
            {
                await ShowCustomModal("Error", $"Failed to set dropoff: {ex.Message}", "error", null);
            }
        }

        private async Task NavigateToSearchLocationWithRecent(RecentItem recent)
        {
            try
            {
                var rideRequest = await _firebase.GetRideRequestByIdAsync(recent.Id);

                if (rideRequest != null)
                {
                    Preferences.Set("SelectedPickupLat", rideRequest.PickupLat.ToString());
                    Preferences.Set("SelectedPickupLng", rideRequest.PickupLng.ToString());
                    Preferences.Set("SelectedDropoffLat", rideRequest.DropoffLat.ToString());
                    Preferences.Set("SelectedDropoffLng", rideRequest.DropoffLng.ToString());

                    HideAllModals();
                    await Navigation.PopModalAsync();
                    await Navigation.PushModalAsync(new Commuter_SearchLocation(
                        rideRequest.PickupLocation,
                        rideRequest.DropoffLocation));
                }
                else
                {
                    HideAllModals();
                    await Navigation.PopModalAsync();
                    await Navigation.PushModalAsync(new Commuter_SearchLocation(recent.Name, ""));
                }
            }
            catch (Exception ex)
            {
                await ShowCustomModal("Error", $"Failed to use recent ride: {ex.Message}", "error", null);
            }
        }

        private async Task RemoveFavorite(FavoriteItem favorite)
        {
            try
            {
                await _firebase.RemoveFavoriteAsync(_commuterId, favorite.Id);
                _favorites.RemoveAll(f => f.Id == favorite.Id);
                FavoritesList.ItemsSource = null;
                FavoritesList.ItemsSource = _favorites;
                UpdateCountLabels();

                if (_favorites.Count == 0)
                {
                    ShowEmptyState("No saved places yet", "Add your frequently visited locations");
                }
            }
            catch (Exception ex)
            {
                await ShowCustomModal("Error", $"Failed to remove: {ex.Message}", "error", null);
            }
        }

        private async Task LoadFavorites()
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                FavoritesList.IsVisible = false;
                EmptyState.IsVisible = false;

                if (string.IsNullOrEmpty(_commuterId))
                {
                    ShowEmptyState("Please Login", "Please log in to view your saved places");
                    return;
                }

                _favorites = await _firebase.GetFavoritesAsync(_commuterId);

                if (_favorites == null || _favorites.Count == 0)
                {
                    ShowEmptyState("No saved places yet", "Add your frequently visited locations");
                }
                else
                {
                    _favorites = _favorites.OrderByDescending(f => f.AddedDate).ToList();
                    FavoritesList.ItemsSource = _favorites;
                    FavoritesList.IsVisible = true;
                    EmptyState.IsVisible = false;
                    UpdateCountLabels();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading favorites: {ex.Message}");
                ShowEmptyState("Error loading favorites", "Please try again");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
            }
        }

        private async Task LoadRecent()
        {
            try
            {
                if (string.IsNullOrEmpty(_commuterId))
                {
                    _recentItems = new List<RecentItem>();
                    RecentList.IsVisible = false;
                    RecentEmptyState.IsVisible = true;
                    UpdateCountLabels();
                    return;
                }

                var completedRides = await _firebase.GetCommuterCompletedRidesAsync(_commuterId, 10);

                if (completedRides == null || completedRides.Count == 0)
                {
                    RecentList.IsVisible = false;
                    RecentEmptyState.IsVisible = true;
                }
                else
                {
                    var uniqueRecentItems = new List<RecentItem>();
                    var seenNames = new HashSet<string>();

                    foreach (var ride in completedRides.OrderByDescending(r => r.AddedDate))
                    {
                        if (!seenNames.Contains(ride.Name))
                        {
                            seenNames.Add(ride.Name);
                            uniqueRecentItems.Add(ride);
                            if (uniqueRecentItems.Count >= 5) break;
                        }
                    }

                    _recentItems = uniqueRecentItems.OrderByDescending(r => r.AddedDate).ToList();
                    RecentList.ItemsSource = _recentItems;
                    RecentList.IsVisible = true;
                    RecentEmptyState.IsVisible = false;
                    UpdateCountLabels();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading recent: {ex.Message}");
                RecentList.IsVisible = false;
                RecentEmptyState.IsVisible = true;
            }
        }

        private void UpdateCountLabels()
        {
            FavoriteCountLabel.Text = $"({_favorites?.Count ?? 0})";
            RecentCountLabel.Text = $"({_recentItems?.Count ?? 0})";
        }

        private void ShowEmptyState(string title, string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EmptyStateTitle.Text = title;
                EmptyStateMessage.Text = message;
                FavoritesList.IsVisible = false;
                EmptyState.IsVisible = true;
                LoadingIndicator.IsVisible = false;
                UpdateCountLabels();
            });
        }

        private void OnFavoriteItemTapped(object sender, EventArgs e)
        {
            if (sender is Frame frame && frame.BindingContext is FavoriteItem favorite)
            {
                ShowFavoriteOptionsModal(favorite);
            }
        }

        private async void OnRecentItemTapped(object sender, EventArgs e)
        {
            if (sender is Frame frame && frame.BindingContext is RecentItem recent)
            {
                _selectedRecent = recent;

                var rideRequest = await _firebase.GetRideRequestByIdAsync(recent.Id);

                string pickup = rideRequest?.PickupLocation ?? recent.Name;
                string dropoff = rideRequest?.DropoffLocation ?? "";

                string message = $"Use this recent ride?\n\nFrom: {pickup}\nTo: {dropoff}";

                await ShowCustomModal(
                    "Use Recent Ride",
                    message,
                    "success",
                    async () => await NavigateToSearchLocationWithRecent(recent),
                    null,
                    "Confirm",
                    "Cancel"
                );
            }
        }

        private async void OnRecentUseLocationTapped(object sender, EventArgs e)
        {
            if (sender is Frame frame && frame.BindingContext is RecentItem recent)
            {
                _selectedRecent = recent;

                var rideRequest = await _firebase.GetRideRequestByIdAsync(recent.Id);

                string pickup = rideRequest?.PickupLocation ?? recent.Name;
                string dropoff = rideRequest?.DropoffLocation ?? "";

                string message = $"Use this recent ride?\n\nFrom: {pickup}\nTo: {dropoff}";

                await ShowCustomModal(
                    "Use Recent Ride",
                    message,
                    "success",
                    async () => await NavigateToSearchLocationWithRecent(recent),
                    null,
                    "Confirm",
                    "Cancel"
                );
            }
        }

        private void OnUseLocationTapped(object sender, EventArgs e)
        {
            if (sender is Frame frame && frame.BindingContext is FavoriteItem favorite)
            {
                ShowFavoriteOptionsModal(favorite);
            }
        }

        private async void OnDeleteTapped(object sender, EventArgs e)
        {
            if (sender is Frame frame && frame.BindingContext is FavoriteItem favorite)
            {
                await ShowCustomModal(
                    "Remove Favorite",
                    $"Remove '{favorite.Name}'?",
                    "warning",
                    async () => await RemoveFavorite(favorite),
                    null,
                    "Remove",
                    "Cancel"
                );
            }
        }

        private async void OnQuickPickupTapped(object sender, EventArgs e)
        {
            if (_favorites.Count == 0)
            {
                await ShowCustomModal(
                    "No Favorites",
                    "Add some favorites first",
                    "info",
                    async () => await Navigation.PushModalAsync(new Commuter_SearchLocation()),
                    null,
                    "OK"
                );
                return;
            }

            ShowPickupSelectionModal();
        }

        private async void OnQuickDropoffTapped(object sender, EventArgs e)
        {
            if (_favorites.Count == 0)
            {
                await ShowCustomModal(
                    "No Favorites",
                    "Add some favorites first",
                    "info",
                    async () => await Navigation.PushModalAsync(new Commuter_SearchLocation()),
                    null,
                    "OK"
                );
                return;
            }

            ShowDropoffSelectionModal();
        }

        private async void OnAddNewTapped(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new Commuter_SearchLocation());
        }

        private async void OnAddFromSearchTapped(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new Commuter_SearchLocation());
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            await this.FadeTo(0, 250, Easing.CubicIn);
            await Navigation.PopModalAsync();
        }
    }

    public class CommuterSaveRouteCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_SaveRoute _parentPage;

        public CommuterSaveRouteCustomModalPage(Frame modalContent, Grid blurOverlay, Commuter_SaveRoute parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var buttonGrid = ((VerticalStackLayout)_modalContent.Content).Children.LastOrDefault() as Grid;
            if (buttonGrid != null)
            {
                foreach (var child in buttonGrid.Children)
                {
                    if (child is Button button)
                    {
                        button.Clicked += async (s, e) =>
                        {
                            button.IsEnabled = false;
                            await AnimateModalExit();
                        };
                    }
                }
            }

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
            _parentPage.CloseCustomModal();
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
}