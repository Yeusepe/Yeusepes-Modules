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

        // Collections for VRChat data
        private List<FriendInfo> _friends;
        public List<FriendInfo> Friends
        {
            get => _friends ??= new List<FriendInfo>();
            set => SetProperty(ref _friends, value);
        }

        private List<WorldInfo> _worlds;
        public List<WorldInfo> Worlds
        {
            get => _worlds ??= new List<WorldInfo>();
            set => SetProperty(ref _worlds, value);
        }

        private List<InstanceInfo> _instances;
        public List<InstanceInfo> Instances
        {
            get => _instances ??= new List<InstanceInfo>();
            set => SetProperty(ref _instances, value);
        }

        private List<CalendarEvent> _calendarEvents;
        public List<CalendarEvent> CalendarEvents
        {
            get => _calendarEvents ??= new List<CalendarEvent>();
            set => SetProperty(ref _calendarEvents, value);
        }

        private List<Notification> _notifications;
        public List<Notification> Notifications
        {
            get => _notifications ??= new List<Notification>();
            set => SetProperty(ref _notifications, value);
        }

        private List<Favorite> _favorites;
        public List<Favorite> Favorites
        {
            get => _favorites ??= new List<Favorite>();
            set => SetProperty(ref _favorites, value);
        }

        private List<GroupInfo> _groups;
        public List<GroupInfo> Groups
        {
            get => _groups ??= new List<GroupInfo>();
            set => SetProperty(ref _groups, value);
        }

        private List<AvatarInfo> _avatars;
        public List<AvatarInfo> Avatars
        {
            get => _avatars ??= new List<AvatarInfo>();
            set => SetProperty(ref _avatars, value);
        }

        private List<AvatarInfo> _avatarFavorites;
        public List<AvatarInfo> AvatarFavorites
        {
            get => _avatarFavorites ??= new List<AvatarInfo>();
            set => SetProperty(ref _avatarFavorites, value);
        }

        private string _currentAvatarId;
        public string CurrentAvatarId
        {
            get => _currentAvatarId;
            set => SetProperty(ref _currentAvatarId, value);
        }

        private string _currentAvatarName;
        public string CurrentAvatarName
        {
            get => _currentAvatarName;
            set => SetProperty(ref _currentAvatarName, value);
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

    // Data models for VRChat API responses
    public class FriendInfo
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Status { get; set; }
        public string StatusDescription { get; set; }
        public string Location { get; set; }
        public string UserIcon { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastLogin { get; set; }
    }

    public class WorldInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Capacity { get; set; }
        public int Occupants { get; set; }
        public string[] Tags { get; set; }
        public string ImageUrl { get; set; }
        public string AuthorName { get; set; }
        public string AuthorId { get; set; }
        public bool IsPublic { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsFeatured { get; set; }
        public bool IsLabs { get; set; }
        public bool IsCommunityLabs { get; set; }
        public bool IsLive { get; set; }
    }

    public class InstanceInfo
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Owner { get; set; }
        public int Capacity { get; set; }
        public int Occupants { get; set; }
        public bool CanRequestInvite { get; set; }
        public bool IsFull { get; set; }
        public bool IsHidden { get; set; }
        public bool IsFriendsOnly { get; set; }
        public bool IsFriendsOfFriends { get; set; }
        public bool IsInviteOnly { get; set; }
        public bool IsActive { get; set; }
    }

    public class CalendarEvent
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Location { get; set; }
        public string WorldId { get; set; }
        public string InstanceId { get; set; }
    }

    public class Notification
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
    }

    public class Favorite
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string WorldId { get; set; }
        public string InstanceId { get; set; }
    }

    public class GroupInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int MemberCount { get; set; }
        public bool IsActive { get; set; }
    }

    public class AvatarInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string AuthorName { get; set; }
        public string AuthorId { get; set; }
        public string ImageUrl { get; set; }
        public bool IsPublic { get; set; }
        public bool IsFeatured { get; set; }
    }
}
