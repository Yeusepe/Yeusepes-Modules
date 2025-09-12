using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace YeusepesModules.VRChatAPI.Utils.Requests
{
    public class VRChatRequestContext : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public HttpClient HttpClient { get; set; }
        public string AuthToken { get; set; }

        // User Information
        private string _userId;
        public string UserId
        {
            get => _userId;
            set => SetProperty(ref _userId, value);
        }

        private string _username;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private string _bio;
        public string Bio
        {
            get => _bio;
            set => SetProperty(ref _bio, value);
        }

        private string _userIcon;
        public string UserIcon
        {
            get => _userIcon;
            set => SetProperty(ref _userIcon, value);
        }

        private string _userStatus;
        public string UserStatus
        {
            get => _userStatus;
            set => SetProperty(ref _userStatus, value);
        }

        private string _location;
        public string Location
        {
            get => _location;
            set => SetProperty(ref _location, value);
        }

        private bool _isFriend;
        public bool IsFriend
        {
            get => _isFriend;
            set => SetProperty(ref _isFriend, value);
        }

        // World Information
        private string _worldId;
        public string WorldId
        {
            get => _worldId;
            set => SetProperty(ref _worldId, value);
        }

        private string _worldName;
        public string WorldName
        {
            get => _worldName;
            set => SetProperty(ref _worldName, value);
        }

        private int _worldCapacity;
        public int WorldCapacity
        {
            get => _worldCapacity;
            set => SetProperty(ref _worldCapacity, value);
        }

        private int _worldOccupants;
        public int WorldOccupants
        {
            get => _worldOccupants;
            set => SetProperty(ref _worldOccupants, value);
        }

        // Instance Information
        private string _instanceId;
        public string InstanceId
        {
            get => _instanceId;
            set => SetProperty(ref _instanceId, value);
        }

        private int _instanceCapacity;
        public int InstanceCapacity
        {
            get => _instanceCapacity;
            set => SetProperty(ref _instanceCapacity, value);
        }

        private int _instanceOccupants;
        public int InstanceOccupants
        {
            get => _instanceOccupants;
            set => SetProperty(ref _instanceOccupants, value);
        }

        private bool _instanceCanRequestInvite;
        public bool InstanceCanRequestInvite
        {
            get => _instanceCanRequestInvite;
            set => SetProperty(ref _instanceCanRequestInvite, value);
        }

        private bool _instanceIsFull;
        public bool InstanceIsFull
        {
            get => _instanceIsFull;
            set => SetProperty(ref _instanceIsFull, value);
        }

        private bool _instanceIsHidden;
        public bool InstanceIsHidden
        {
            get => _instanceIsHidden;
            set => SetProperty(ref _instanceIsHidden, value);
        }

        private bool _instanceIsFriendsOnly;
        public bool InstanceIsFriendsOnly
        {
            get => _instanceIsFriendsOnly;
            set => SetProperty(ref _instanceIsFriendsOnly, value);
        }

        private bool _instanceIsFriendsOfFriends;
        public bool InstanceIsFriendsOfFriends
        {
            get => _instanceIsFriendsOfFriends;
            set => SetProperty(ref _instanceIsFriendsOfFriends, value);
        }

        private bool _instanceIsInviteOnly;
        public bool InstanceIsInviteOnly
        {
            get => _instanceIsInviteOnly;
            set => SetProperty(ref _instanceIsInviteOnly, value);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
