using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using ServiceCo.Firebase;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_Chat : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private IDisposable _messageListener;
        
        private string _rideRequestId;
        private string _driverId;
        private string _driverName;
        private string _commuterId;
        private string _commuterName;
        
        public ObservableCollection<ChatMessageDisplay> Messages { get; } = new ObservableCollection<ChatMessageDisplay>();

        public Commuter_Chat(string rideRequestId, string driverId, string driverName)
        {
            InitializeComponent();
            
            _rideRequestId = rideRequestId;
            _driverId = driverId;
            _driverName = driverName;
            _commuterId = AppSession.GetCommuterId();
            _commuterName = AppSession.GetCommuterName();

            Console.WriteLine($"=== COMMUTER CHAT INITIALIZED ===");
            Console.WriteLine($"Ride: {_rideRequestId}");
            Console.WriteLine($"Driver: {_driverName}");
            Console.WriteLine($"Commuter: {_commuterName}");

            SetupUI();
            
            Task.Run(async () =>
            {
                await LoadMessages();
                StartMessageListener();
            });
        }

        private void SetupUI()
        {
            DriverNameLabel.Text = _driverName;
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
                        Messages.Add(new ChatMessageDisplay
                        {
                            Message = message.Message,
                            SenderName = message.SenderName,
                            TimeDisplay = message.Timestamp.ToLocalTime().ToString("h:mm tt"),
                            IsFromCommuter = message.SenderId == _commuterId,  
                            IsFromDriver = message.SenderId == _driverId     
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
                            Messages.Add(new ChatMessageDisplay
                            {
                                Message = message.Message,
                                SenderName = message.SenderName,
                                TimeDisplay = message.Timestamp.ToLocalTime().ToString("h:mm tt"),
                                IsFromCommuter = message.SenderId == _commuterId,  
                                IsFromDriver = message.SenderId == _driverId     
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
                
                Console.WriteLine($"?? Commuter sending: {messageText}");
                
                MessageEntry.Text = string.Empty;
                
                var localMessage = new ChatMessageDisplay
                {
                    Message = messageText,
                    SenderName = _commuterName,
                    TimeDisplay = DateTime.Now.ToString("h:mm tt"),
                    IsFromCommuter = true,   
                    IsFromDriver = false     
                };
                
                Messages.Add(localMessage);
                MessagesCollectionView.ScrollTo(Messages.Last(), animate: false);
                
                bool success = await _firebaseConnection.SaveMessageToFirebaseAsync(
                    _rideRequestId,
                    _commuterId,
                    "commuter",
                    messageText,
                    _commuterName
                );
                
                if (success)
                {
                    Console.WriteLine($"? Commuter message saved to Firebase");
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
                Console.WriteLine("?? Commuter going back...");
                _messageListener?.Dispose();
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error going back: {ex.Message}");
            }
        }

        private async void OnCallDriverClicked(object sender, EventArgs e)
        {
            try
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(_driverId);
                if (driver != null && !string.IsNullOrEmpty(driver.MobileNumber))
                {
                    PhoneDialer.Open(driver.MobileNumber);
                }
                else
                {
                    await DisplayAlert("Error", "Driver number not available", "OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error calling driver: {ex.Message}");
                await DisplayAlert("Error", "Cannot make call", "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Console.WriteLine("?? Commuter chat closing");
            _messageListener?.Dispose();
        }
    }

    // Chat message display model FOR COMMUTER
    public class ChatMessageDisplay
    {
        public string Message { get; set; }
        public string SenderName { get; set; }
        public string TimeDisplay { get; set; }
        public bool IsFromCommuter { get; set; }  
        public bool IsFromDriver { get; set; }    
    }
}