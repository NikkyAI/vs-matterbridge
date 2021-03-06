using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;
using WebSocket = WebSocket4Net.WebSocket;

namespace Matterbridge
{
    internal class WebsocketHandler : IDisposable
    {
        private readonly ICoreServerAPI _api;
        private readonly Mod _mod;
        private readonly ModConfig _config;

        // ReSharper disable ConvertToAutoProperty
        private ICoreServerAPI Api => _api;
        private Mod Mod => _mod;

        private ModConfig Config => _config!;
        // ReSharper restore ConvertToAutoProperty

        private WebSocket? _websocket;
        private bool _reconnectWebsocket = true;
        private int _connectErrrors = 0;

        public WebsocketHandler(ICoreServerAPI api, Mod mod, ModConfig config)
        {
            this._api = api;
            this._mod = mod;
            this._config = config;
        }

        public void Connect()
        {
            Close(skipMessage: true);
            _reconnectWebsocket = true;
            try
            {
                var customHeaderItems = new List<KeyValuePair<string, string>>();
                if (!string.IsNullOrEmpty(Config.Token))
                {
                    customHeaderItems.Add(new KeyValuePair<string, string>("Authorization", $"Bearer {Config.Token}"));
                }

                _websocket = new WebSocket(
                    uri: Config.Uri,
                    customHeaderItems: customHeaderItems
                )
                {
                    EnableAutoSendPing = true,
                    AutoSendPingInterval = 100
                };
                _websocket.Opened += websocket_Opened;
                _websocket.Error += websocket_Error;
                _websocket.Closed += websocket_Closed;
                _websocket.MessageReceived += websocket_MessageReceived;
                _websocket.Open();

                Mod.Logger.Debug("started websocket");
            }
            catch (Exception e)
            {
                Mod.Logger.Error("error connecting to websocket: {0} {1}", e, e.StackTrace);
            }
        }

        public void Close(bool skipMessage = false)
        {
            if (Config.SendApiConnectEvents && !skipMessage)
            {

                Api.Event.EnqueueMainThreadTask(() =>
                {
                    SendSystemMessage(
                        text: Config.TEXT_ServerStop,
                        @event: ApiMessage.EventJoinLeave,
                        gateway: Config.generalGateway
                    );
                }, "vs-matterbridge");
            }

            _reconnectWebsocket = false;
            _websocket?.Close();
        }

        public void SendSystemMessage(string text, string gateway, string @event = "")
        {
            SendMessage(
                username: _config.systemUsername,
                text: text,
                gateway: gateway,
                @event: @event,
                account: "",
                avatar: _config.systemAvatar
            );
        }

        public void SendUserMessage(IServerPlayer player, string text, string gateway, bool generateAvatar,
            string @event = "")
        {
            SendMessage(
                username: player.PlayerName,
                text: text,
                gateway: gateway,
                @event: @event,
                account: player.PlayerUID,
                avatar: generateAvatar ? Identicon.GenerateUrl(player.PlayerUID) : ""
            );
        }

        public void SendUserMessage(string playerName, string playerUid, string text, string gateway,
            bool generateAvatar, string @event = "")
        {
            SendMessage(
                username: playerName,
                text: text,
                gateway: gateway,
                @event: @event,
                account: playerUid,
                avatar: generateAvatar ? Identicon.GenerateUrl(playerUid) : ""
            );
        }

        private void SendMessage(string username, string text, string gateway, string @event, string account,
            string avatar)
        {
            if (_websocket == null)
            {
                Mod.Logger.Error("websocket not initialized yet");
                return;
            }

            var message = new ApiMessage(
                text: text,
                gateway: gateway,
                channel: "api",
                username: username,
                avatar: avatar,
                @event: @event,
                account: account,
                protocol: "api"
            );

            var messageText = JsonConvert.SerializeObject(message);
            Mod.Logger.Debug("sending: {0}", messageText);
            _websocket.Send(messageText);
        }

        private void websocket_Opened(object sender, EventArgs e)
        {
            _connectErrrors = 0;
            Mod.Logger.Debug("websocket opened");
        }

        private void websocket_Error(object sender, ErrorEventArgs errorEventArgs)
        {
            _connectErrrors++;
            Mod.Logger.Error($"connect errors: {_connectErrrors}");
            Mod.Logger.Error($"websocket_Error: {errorEventArgs.Exception}");
        }

        private void websocket_Closed(object sender, EventArgs eventArgs)
        {
            Mod.Logger.Debug("websocket closed");

            if (Api.Server.IsShuttingDown)
            {
                Mod.Logger.Debug($"will not try to reconnect during shutdown");
                return;
            }

            if (_reconnectWebsocket)
            {
                if (_connectErrrors < 10)
                {
                    Thread.Sleep(100);
                    Connect();
                }
                else
                {
                    Mod.Logger.Error($"will not try to reconnect after {_connectErrrors} failed connection attempts");
                }
            }
            else
            {
                Mod.Logger.Debug("will not try to reconnect");
            }
        }

        private void websocket_MessageReceived(object sender, MessageReceivedEventArgs eventArgs)
        {
            var text = eventArgs.Message;
            Mod.Logger.VerboseDebug("text: {0}", text);

            var message = JsonConvert.DeserializeObject<ApiMessage>(text);
            Mod.Logger.Debug("message: {0}", message);

            if (message.Gateway == "")
            {
                switch (message.Event)
                {
                    case ApiMessage.EventApiConnected:
                        Mod.Logger.Chat("api connected");

                        if (Config.SendApiConnectEvents)
                        {
                            SendSystemMessage(
                                text: Config.TEXT_ServerStart,
                                gateway: Config.generalGateway,
                                @event: ""
                            );
                        }

                        break;
                }

                return;
            }

            int groupUid;
            if (message.Gateway == Config.generalGateway)
            {
                groupUid = GlobalConstants.GeneralChatGroup;
            }
            else
            {
                var mappingEntry = Config.ChannelMapping.FirstOrDefault(entry => entry.gateway == message.Gateway);
                if (mappingEntry == null)
                {
                    Mod.Logger.Debug("no group found for channel {0}, skipping message", message.Channel);
                    return;
                }

                var group = Api.Groups.GetOrCreate(Api, mappingEntry.groupName);
                groupUid = group.Uid;
            }

            var cleanedMessageText = message.Text.Replace(">", "&gt;").Replace("<", "&lt;");

            cleanedMessageText = string.Join(
                " ",
                cleanedMessageText
                    .Split(new[] {' '}, StringSplitOptions.None)
                    .Select(
                        word => Uri.IsWellFormedUriString(word, UriKind.Absolute)
                            ? $"<a href=\"{word}\">{word}</a>"
                            : word
                    )
                    .ToArray()
            );

            if (groupUid == null)
            {
                Mod.Logger.Error("groupId was null");
                return;
            }

            switch (message.Event)
            {
                case ApiMessage.EventJoinLeave:
                {
                    Api.Event.EnqueueMainThreadTask(() =>
                    {
                        Api.SendMessageToGroup(
                            groupUid,
                            $"<strong>{message.Username}</strong>: {cleanedMessageText}",
                            EnumChatType.OthersMessage
                        );
                    }, "vs-matterbridge");
                    break;
                }
                case ApiMessage.EventUserAction:
                {
                    Api.Event.EnqueueMainThreadTask(() =>
                    {
                        Api.SendMessageToGroup(
                            groupUid,
                            $"<strong>{message.Username}</strong> <i>{cleanedMessageText}</i>",
                            EnumChatType.OthersMessage
                        );
                    }, "vs-matterbridge");
                    break;
                }
                case "":
                {
                    Api.Event.EnqueueMainThreadTask(() =>
                    {
                        Api.SendMessageToGroup(
                            groupUid,
                            $"<strong>{message.Username}</strong>: {cleanedMessageText}",
                            EnumChatType.OthersMessage
                        );
                    }, "vs-matterbridge");
                    break;
                }
                default:
                {
                    Mod.Logger.Error("unhandled event type {0}", message.Event);
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (_websocket != null)
            {
                _websocket.Close();
                ((IDisposable) _websocket).Dispose();
            }
        }
    }
}