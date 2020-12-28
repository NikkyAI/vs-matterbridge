using System;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace Matterbridge
{
    public struct ApiMessage{
        public string text;
        public string channel;
        public string username;
        public string avatar;
        public string account;
        public string @event;
        public string protocol;
        public string gateway;
        public string parent_id;
        public DateTime timestamp;
        public string id;
        // public string Extra;
        
        public const string EventJoinLeave         = "join_leave";
        public const string EventTopicChange       = "topic_change";
        public const string EventFailure           = "failure";
        public const string EventFileFailureSize   = "file_failure_size";
        public const string EventAvatarDownload    = "avatar_download";
        public const string EventRejoinChannels    = "rejoin_channels";
        public const string EventUserAction        = "user_action";
        public const string EventMsgDelete         = "msg_delete";
        public const string EventAPIConnected      = "api_connected";
        public const string EventUserTyping        = "user_typing";
        public const string EventGetChannelMembers = "get_channel_members";
        public const string EventNoticeIRC         = "notice_irc";

        public ApiMessage(
            string text = "",
            string channel = "",
            string username = "",
            string avatar = "",
            string account = "",
            string @event = "",
            string protocol = "",
            string gateway = "",
            string parent_id = "",
            string id = ""
            // string Extra = null
        ) {
            this.text = text;
            this.channel = channel;
            this.username = username;
            this.avatar = avatar;
            this.account = account;
            this.@event = @event;
            this.protocol = protocol;
            this.gateway = gateway;
            this.parent_id = parent_id;
            this.timestamp = DateTime.Now;
            this.id = id;
            // this.Extra = Extra;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static void main()
        {
            var text = JsonConvert.SerializeObject(new ApiMessage());
            Console.WriteLine(text);
        }

        //{
        //   "text":"",
        //   "channel":"",
        //   "username":"",
        //   "userid":"",
        //   "avatar":"",
        //   "account":"",
        //   "event":"api_connected",
        //   "protocol":"",
        //   "gateway":"",
        //   "parent_id":"",
        //   "timestamp":"2020-12-27T10:15:32.191049535+01:00",
        //   "id":"",
        //   "Extra":null
        // }
    }
}