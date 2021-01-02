using System;
using Newtonsoft.Json;

namespace Matterbridge
{
    [JsonObject]
    public struct ApiMessage
    {
        [JsonProperty(PropertyName = "text")]
        public string Text;
        [JsonProperty(PropertyName = "channel")]
        public string Channel;
        [JsonProperty(PropertyName = "username")]
        public string Username;
        [JsonProperty(PropertyName = "avatar")]
        public string Avatar;
        [JsonProperty(PropertyName = "account")]
        public string Account;
        [JsonProperty(PropertyName = "event")]
        public string Event;
        [JsonProperty(PropertyName = "protocol")]
        public string Protocol;
        [JsonProperty(PropertyName = "gateway")]
        public string Gateway;
        [JsonProperty(PropertyName = "parent_id")]
        public string ParentId;
        [JsonProperty(PropertyName = "timestamp")]
        public DateTime Timestamp;
        [JsonProperty(PropertyName = "id")]
        public string Id;

        public const string EventJoinLeave = "join_leave";
        public const string EventTopicChange = "topic_change";
        public const string EventFailure = "failure";
        public const string EventFileFailureSize = "file_failure_size";
        public const string EventAvatarDownload = "avatar_download";
        public const string EventRejoinChannels = "rejoin_channels";
        public const string EventUserAction = "user_action";
        public const string EventMsgDelete = "msg_delete";
        public const string EventApiConnected = "api_connected";
        public const string EventUserTyping = "user_typing";
        public const string EventGetChannelMembers = "get_channel_members";
        public const string EventNoticeIrc = "notice_irc";

        public ApiMessage(
            string text = "",
            string channel = "",
            string username = "",
            string avatar = "",
            string account = "",
            string @event = "",
            string protocol = "",
            string gateway = "",
            string parentId = "",
            string id = ""
        )
        {
            this.Text = text;
            this.Channel = channel;
            this.Username = username;
            this.Avatar = avatar;
            this.Account = account;
            this.Event = @event;
            this.Protocol = protocol;
            this.Gateway = gateway;
            this.ParentId = parentId;
            this.Timestamp = DateTime.Now;
            this.Id = id;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}