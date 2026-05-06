using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using ServiceCo.Firebase;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_Chat : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private IDisposable _messageListener;

        private string _rideRequestId;
        private string _commuterId;
        private string _commuterName;
        private string _driverId;
        private string _driverName;

        public ObservableCollection<DriverChatMessage> Messages { get; } = new ObservableCollection<DriverChatMessage>();

        public Driver_Chat(string rideRequestId, string commuterId, string commuterName)
        {
            InitializeComponent();

            _rideRequestId = rideRequestId;
            _commuterId = commuterId;
            _commuterName = commuterName;
            _driverId = AppSession.GetDriverId();
            _driverName = AppSession.GetDriverName();

            Console.WriteLine($"=== DRIVER CHAT INITIALIZED ===");
            Console.WriteLine($"Ride: {_rideRequestId}");
            Console.WriteLine($"Commuter: {_commuterName}");
            Console.WriteLine($"Driver: {_driverName}");

            SetupUI();

            Task.Run(async () =>
            {
                await LoadMessages();
                StartMessageListener();
            });
        }

        private void SetupUI()
        {
            CommuterNameLabel.Text = _commuterName;
            MessagesCollectionView.ItemsSource = Messages;
        }

        private async Task LoadMessages()
        {
            try
            {
                Console.WriteLine($"?? Loading messages from Firebase...");

                var messages = await _firebaseConnection.GetChatMessagesAsync(_rideRequestId);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Messages.Clear();

                    foreach (var message in messages)
                    {
                        Messages.Add(new DriverChatMessage
                        {
                            Message = message.Message,
                            SenderName = message.SenderName,
                            TimeDisplay = message.Timestamp.ToLocalTime().ToString("h:mm tt"),
                            IsFromDriver = message.SenderId == _driverId,     
                            IsFromCommuter = message.SenderId == _commuterId   
                        });
                    }

                    if (Messages.Any())
                    {
                        MessagesCollectionView.ScrollTo(Messages.Last(), animate: false);
                    }

                    Console.WriteLine($"? Loaded {Messages.Count} messages");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error loading messages: {ex.Message}");
            }
        }

        private void StartMessageListener()
        {
            try
            {
                Console.WriteLine($"?? Starting message listener...");

                _messageListener?.Dispose();

                _messageListener = _firebaseConnection.ListenToMessages(_rideRequestId, async (message) =>
                {
                    Console.WriteLine($"?? New message received: {message.Message} (Sender: {message.SenderName})");

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        var exists = Messages.Any(m =>
                            m.Message == message.Message &&
                            m.TimeDisplay == message.Timestamp.ToLocalTime().ToString("h:mm tt"));

                        if (!exists)
                        {
                            Messages.Add(new DriverChatMessage
                            {
                                Message = message.Message,
                                SenderName = message.SenderName,
                                TimeDisplay = message.Timestamp.ToLocalTime().ToString("h:mm tt"),
                                IsFromDriver = message.SenderId == _driverId,
                                IsFromCommuter = message.SenderId == _commuterId
                            });

                            MessagesCollectionView.ScrollTo(Messages.Last(), animate: false);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error starting listener: {ex.Message}");
            }
        }

        private async void OnSendMessageClicked(object sender, EventArgs e)
        {
            await SendMessageAsync();
        }

        private async void OnMessageEntryCompleted(object sender, EventArgs e)
        {
            await SendMessageAsync();
        }

        private async Task SendMessageAsync()
        {
            try
            {
                string messageText = MessageEntry.Text?.Trim();

                if (string.IsNullOrWhiteSpace(messageText))
                {
                    Console.WriteLine("?? Message is empty");
                    return;
                }

                Console.WriteLine($"?? Driver sending: {messageText}");

                MessageEntry.Text = string.Empty;

                var localMessage = new DriverChatMessage
                {
                    Message = messageText,
                    SenderName = _driverName,
                    TimeDisplay = DateTime.Now.ToString("h:mm tt"),
                    IsFromDriver = true,     
                    IsFromCommuter = false   
                };

                Messages.Add(localMessage);
                MessagesCollectionView.ScrollTo(Messages.Last(), animate: false);

                bool success = await _firebaseConnection.SaveMessageToFirebaseAsync(
                    _rideRequestId,
                    _driverId,
                    "driver",
                    messageText,
                    _driverName
                );

                if (success)
                {
                    Console.WriteLine($"? Driver message saved to Firebase");
                }
                else
                {
                    Console.WriteLine($"? Failed to save to Firebase");
                    await DisplayAlert("Error", "Message sent but not saved to server", "OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error in SendMessageAsync: {ex.Message}");
                await DisplayAlert("Error", "Failed to send message", "OK");
            }
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("?? Driver going back...");
                _messageListener?.Dispose();
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error going back: {ex.Message}");
            }
        }

        private async void OnCallCommuterClicked(object sender, EventArgs e)
        {
            try
            {
                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);
                if (rideRequest != null && !string.IsNullOrEmpty(rideRequest.CommuterMobile))
                {
                    PhoneDialer.Open(rideRequest.CommuterMobile);
                }
                else
                {
                    await DisplayAlert("Error", "Commuter number not available", "OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error calling commuter: {ex.Message}");
                await DisplayAlert("Error", "Cannot make call", "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Console.WriteLine("?? Driver chat closing");
            _messageListener?.Dispose();
        }
    }

    public class DriverChatMessage
    {
        public string Message { get; set; }
        public string SenderName { get; set; }
        public string TimeDisplay { get; set; }
        public bool IsFromDriver { get; set; }   
        public bool IsFromCommuter { get; set; }  
    }
}