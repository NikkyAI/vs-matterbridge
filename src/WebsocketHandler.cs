using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using SuperSocket.ClientEngine;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using WebSocket4Net;

namespace Matterbridge
{
    internal class WebsocketHandler
    {
        private ICoreServerAPI api;
        private ModConfig config;


        private WebSocket? _websocket;
        private bool _reconnectWebsocket = true;
        private int _connectErrrors = 0;

        public WebsocketHandler(ICoreServerAPI api, ModConfig config)
        {
            this.api = api;
            this.config = config;
        }

        public void Connect()
        {
            try
            {
                var customHeaderItems = new List<KeyValuePair<string, string>>();
                if (!string.IsNullOrEmpty(config.Token))
                {
                    customHeaderItems.Add(new KeyValuePair<string, string>("Authorization", $"Bearer {config.Token}"));
                }

                _websocket = new WebSocket(
                    uri: config.Uri,
                    customHeaderItems: customHeaderItems
                );
                _websocket.Opened += websocket_Opened;
                _websocket.Error += websocket_Error;
                _websocket.Closed += websocket_Closed;
                _websocket.MessageReceived += websocket_MessageReceived;
                _websocket.Open();

                api.Logger.Debug("started websocket");
            }
            catch (Exception e)
            {
                api.Logger.Error("error connecting to websocket: {0} {1}", e, e.StackTrace);
            }
        }

        public void Close()
        {
            if (config.SendApiConnectEvents)
            {
                SendMessage(
                    username: "system",
                    text: config.TEXT_ServerStop,
                    @event: ApiMessage.EventJoinLeave,
                    gateway: config.generalGateway
                );
            }

            _reconnectWebsocket = false;
            _websocket?.Close();
        }

        private void websocket_Opened(object sender, EventArgs e)
        {
            _connectErrrors = 0;
            api.Logger.Debug("websocket_Opened");
            api.Logger.Debug($"sender: {sender}");
            //TODO: send `vs bridge connected`
            // websocket.Send("Hello World!");
        }

        private void websocket_Error(object sender, ErrorEventArgs errorEventArgs)
        {
            _connectErrrors++;
            api.Logger.Error($"connect error: {_connectErrrors}");
            api.Logger.Error($"websocket_Error: {errorEventArgs.Exception}");
        }

        private void websocket_Closed(object sender, EventArgs eventArgs)
        {
            api.Logger.Debug("websocket_Closed");

            if (_reconnectWebsocket && _connectErrrors < 10)
            {
                Thread.Sleep(100);
                Connect();
            }
            else
            {
                api.Logger.Error($"will not try to reconnect after {_connectErrrors} failed connection attempts");
            }
        }

        private void websocket_MessageReceived(object sender, MessageReceivedEventArgs eventArgs)
        {
            var text = eventArgs.Message;
            api.Logger.VerboseDebug("text: {0}", text);

            var message = JsonConvert.DeserializeObject<ApiMessage>(text);
            api.Logger.Debug("message: {0}", message);

            if (message.gateway == "")
            {
                switch (message.@event)
                {
                    case ApiMessage.EventAPIConnected:
                        api.Logger.Chat("api connected");

                        if (config.SendApiConnectEvents)
                        {
                            SendMessage(
                                username: "system",
                                text: config.TEXT_ServerStart,
                                gateway: config.generalGateway,
                                @event: ""
                            );
                        }

                        break;
                }

                return;
            }

            int groupUid;
            if (message.gateway == config.generalGateway)
            {
                groupUid = GlobalConstants.GeneralChatGroup;
            }
            else
            {
                var mappingEntry = config.ChannelMapping.FirstOrDefault(entry => entry.gateway == message.gateway);
                if (mappingEntry == null)
                {
                    api.Logger.Debug("no group found for channel {0}, skipping message", message.channel);
                    return;
                }

                var group = api.Groups.GetOrCreate(api, mappingEntry.groupName);
                groupUid = group.Uid;
            }

            switch (message.@event)
            {
                case ApiMessage.EventJoinLeave:
                {
                    api.SendMessageToGroup(
                        groupUid,
                        $"{message.gateway} <strong>{message.username}</strong>: {message.text.Replace(">", "&gt;").Replace("<", "&lt;")}",
                        EnumChatType.OthersMessage
                    );
                    break;
                }
                case ApiMessage.EventUserAction:
                {
                    api.SendMessageToGroup(
                        groupUid,
                        $"{message.gateway} <strong>{message.username}</strong> action: {message.text.Replace(">", "&gt;").Replace("<", "&lt;")}",
                        EnumChatType.OthersMessage
                    );
                    break;
                }
                case "":
                {
                    api.SendMessageToGroup(
                        groupUid,
                        $"{message.gateway} <strong>{message.username}</strong>: {message.text.Replace(">", "&gt;").Replace("<", "&lt;")}",
                        EnumChatType.OthersMessage
                    );
                    break;
                }
                default:
                {
                    api.Logger.Error("unhandled event type {0}", message.@event);
                    break;
                }
            }

            // api.SendMessageToGroup(
            //     GlobalConstants.GeneralChatGroup,
            //     $"{message.gateway} <strong>{message.username}</strong>: {message.text.Replace(">", "&gt;").Replace("<", "&lt;")}",
            //     EnumChatType.OthersMessage
            // );
        }

        public void SendMessage(string username, string text, string gateway, string @event = "", string account = "")
        {
            if (_websocket == null)
            {
                api.Logger.Error("websocket not initialized yet");
                return;
            }

            var message = new ApiMessage(
                text: text,
                gateway: gateway,
                channel: "api",
                username: username,
                // TODO: render face and get url to it
                avatar: "",
                @event: @event,
                account: account,
                protocol: "api"
            );

            var messageText = JsonConvert.SerializeObject(message);
            api.Logger.Debug("sending: {0}", messageText);
            _websocket.Send(messageText);
        }
    }
}