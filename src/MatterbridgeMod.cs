using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using SuperSocket.ClientEngine;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using WebSocket4Net;
using Newtonsoft.Json;

namespace Matterbridge
{
    public class MatterbridgeMod : ModSystem
    {
        private const string PLAYERDATA_LASTSEENKEY = "MATTERBRIDGE_LASTSEEN";
        private const string PLAYERDATA_TOTALPLAYTIMEKEY = "MATTERBRIDGE_TOTALPLAYTIME";
        private const string CONFIGNAME = "matterbridge.json";

        private static ICoreServerAPI? _api;
        private ModConfig? _config;
        private string? _generalGateway;

        private ICoreServerAPI api
        {
            get => _api!;
            set => _api = value;
        }
        
        private ModConfig config { 
            get => _config!;
            set => _config = value; 
        }

        private string generalGateway
        {
            get => _generalGateway!;
            set => _generalGateway = value;
        }

        private TemporalStormRunTimeData? _lastData;
        private SystemTemporalStability? _temporalSystem;

        private static readonly Dictionary<string, DateTime> connectTimeDict = new Dictionary<string, DateTime>();

        private WebSocket? _websocket;
        private bool _reconnectWebsocket = true;
        private int _connectErrrors = 0;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;

            try
            {
                config = api.LoadModConfig<ModConfig>(CONFIGNAME);
            }
            catch (Exception e)
            {
                api.Logger.Error("Failed to load mod config! {0}", e);
                return;
            }

            if (this.config == null)
            {
                api.Logger.Notification($"non-existant modconfig at 'ModConfig/{CONFIGNAME}', creating default...");
                config = new ModConfig();
            }

            api.StoreModConfig(config, CONFIGNAME);

            foreach (var entry in config.ChannelMapping)
            {
                foreach (var otherEntry in config.ChannelMapping)
                {
                    if (entry.groupName == otherEntry.groupName && entry.gateway != otherEntry.gateway)
                    {
                        api.Logger.Error("inconsistent channel mapping for group {0} to gateways {1} {2}",
                            entry.groupName,
                            entry.gateway, otherEntry.gateway);
                        return;
                    }

                    if (entry.groupName != otherEntry.groupName && entry.gateway == otherEntry.gateway)
                    {
                        api.Logger.Error("inconsistent channel mapping for gateway {0} to groups {1} {2}",
                            entry.gateway, entry.groupName, otherEntry.groupName);
                        return;
                    }
                }
            }

            var generalMappingEntries = config.ChannelMapping.FindAll(entry => entry.groupName == "");
            if (generalMappingEntries.Count == 1)
            {
                generalGateway = generalMappingEntries[0].gateway;
            }
            else if (generalMappingEntries.Count == 0)
            {
                api.Logger.Error("no mapping found for general group with name: \"\", adding default");
                config.ChannelMapping.Add(
                    new ChannelMappingEntry("", "general")
                );
                api.StoreModConfig(config, CONFIGNAME);
                return;
            }


            this.api.Event.SaveGameLoaded += Event_SaveGameLoaded;
            this.api.Event.PlayerChat += Event_PlayerChat;

            this.api.Event.PlayerJoin += Event_PlayerJoin;
            this.api.Event.PlayerDisconnect += Event_PlayerDisconnect;

            this.api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, Event_ServerStartup);
            this.api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, Event_ServerShutdown);

            this.api.Event.PlayerDeath += Event_PlayerDeath;
        }

        private void Event_PlayerDeath(IServerPlayer byPlayer, DamageSource? damageSource)
        {
            // var deathMessage = (byPlayer?.PlayerName ?? "Unknown player") + " ";
            var deathMessage = "";
            if (damageSource == null)
                deathMessage += "was killed by the unknown.";
            else
            {
                deathMessage += damageSource.Type switch
                {
                    EnumDamageType.Gravity => "smashed into the ground",
                    EnumDamageType.Fire => "burned to death",
                    EnumDamageType.Crushing => "was crushed",
                    EnumDamageType.BluntAttack => "was crushed",
                    EnumDamageType.SlashingAttack => "was sliced open",
                    EnumDamageType.PiercingAttack => "was pierced through",
                    EnumDamageType.Suffocation => "suffocated to death",
                    EnumDamageType.Heal => "was somehow *healed* to death",
                    EnumDamageType.Poison => "was poisoned",
                    EnumDamageType.Hunger => "starved to death",
                    EnumDamageType.Frost => "froze to death",
                    _ => "was killed"
                };

                deathMessage += " ";

                deathMessage += damageSource.Source switch
                {
                    EnumDamageSource.Block => "by a block.",
                    EnumDamageSource.Player => "when they failed at PVP.",
                    EnumDamageSource.Fall => "when they fell to their doom.",
                    EnumDamageSource.Drown => "when they tried to breath in water.",
                    EnumDamageSource.Revive => "just as they respawned.",
                    EnumDamageSource.Void => "when they fell screaming into the abyss.",
                    EnumDamageSource.Suicide => "when they killed themselves.",
                    EnumDamageSource.Internal => "when they took damage from the inside...",
                    EnumDamageSource.Entity => damageSource.SourceEntity.Code.Path switch
                    {
                        "wolf-male" => "and eaten by a wolf.",
                        "wolf-female" => "and eaten by a wolf.",
                        "pig-wild-male" => "by a boar.",
                        "pig-wild-female" => "by a sow.",
                        "sheep-bighorn-female" => "by a sheep.",
                        "sheep-bighorn-male" => "by a sheep.",
                        "chicken-rooster" => "by a... chicken.",
                        "locust" => "by a locust.",
                        "drifter" => "by a drifter.",
                        "beemob" => "by a swarm of bees.",
                        _ => "by a monster."
                    },
                    EnumDamageSource.Explosion => "when they stood by a bomb.",
                    EnumDamageSource.Machine => "when they got their hands stuck in a machine.",
                    EnumDamageSource.Unknown => "when they encountered the unknown.",
                    EnumDamageSource.Weather => "when the weather itself suddenly struck.",
                    _ => "by the unknown."
                };
            }

            if (config.SendPlayerDeathEvents)
            {
                Send_Message(
                    username: byPlayer.PlayerName,
                    text: deathMessage,
                    @event: ApiMessage.EventUserAction,
                    account: byPlayer.PlayerUID,
                    gateway: generalGateway
                );
            }
        }

        private void Event_ServerStartup()
        {
            Connect_Websocket();
        }

        private void Event_ServerShutdown()
        {
            if (config.SendApiConnectEvents)
            {
                Send_Message(
                    username: "system",
                    text: config.TEXT_ServerStop,
                    @event: ApiMessage.EventJoinLeave,
                    gateway: generalGateway
                );
            }

            _reconnectWebsocket = false;
            _websocket?.Close();
        }

        private void Event_PlayerDisconnect(IServerPlayer byPlayer)
        {
            var data = this.api.PlayerData.GetPlayerDataByUid(byPlayer.PlayerUID);
            if (data != null)
            {
                data.CustomPlayerData[PLAYERDATA_LASTSEENKEY] = JsonConvert.SerializeObject(DateTime.UtcNow);

                var timePlayed = DateTime.UtcNow - connectTimeDict[byPlayer.PlayerUID];
                if (data.CustomPlayerData.TryGetValue(PLAYERDATA_TOTALPLAYTIMEKEY, out var totalPlaytimeJson))
                {
                    data.CustomPlayerData[PLAYERDATA_TOTALPLAYTIMEKEY] =
                        JsonConvert.SerializeObject(timePlayed +
                                                    JsonConvert.DeserializeObject<TimeSpan>(totalPlaytimeJson));
                }

                data.CustomPlayerData[PLAYERDATA_TOTALPLAYTIMEKEY] = JsonConvert.SerializeObject(timePlayed);
            }

            connectTimeDict.Remove(byPlayer.PlayerUID);

            if (config.SendPlayerJoinLeaveEvents)
            {
                Send_Message(
                    username: "system",
                    text: $"{byPlayer.PlayerName} has disconnected from the server! " +
                          $"({api.Server.Players.Count(x => x.PlayerUID != byPlayer.PlayerUID && x.ConnectionState == EnumClientState.Playing)}/{api.Server.Config.MaxClients})",
                    gateway: generalGateway,
                    @event: ApiMessage.EventJoinLeave
                );
            }
        }

        private void Event_PlayerJoin(IServerPlayer byPlayer)
        {
            connectTimeDict.Add(byPlayer.PlayerUID, DateTime.UtcNow);
            foreach (var entry in config.ChannelMapping)
            {
                var group = Get_or_Create_Group(entry.groupName);

                AddPlayerToGroup(byPlayer, group);
            }

            if (config.SendPlayerJoinLeaveEvents)
            {
                Send_Message(
                    username: "system",
                    text: $"{byPlayer.PlayerName} has connected to the server! " +
                          $"({api.Server.Players.Count(x => x.ConnectionState != EnumClientState.Offline)}/{api.Server.Config.MaxClients})",
                    gateway: generalGateway,
                    @event: ApiMessage.EventJoinLeave,
                    account: byPlayer.PlayerUID
                );
            }
        }


        private void Event_SaveGameLoaded()
        {
            if ( /*this.config.SendStormNotification &&*/ api.World.Config.GetString("temporalStorms") != "off")
            {
                _temporalSystem = api.ModLoader.GetModSystem<SystemTemporalStability>();
                api.Event.RegisterGameTickListener(OnTempStormTick, 5000);
            }
        }

        private void OnTempStormTick(float t1)
        {
            if (_temporalSystem == null)
            {
                api.Logger.Error("temporalSystem not initialized yet");
                return;
            }

            var data = _temporalSystem.StormData;
            
            api.Logger.Debug($"storm day {data.stormDayNotify} {data.nextStormStrength}");
            
            if (_lastData?.stormDayNotify > 1 && data.stormDayNotify == 1 && config.SendStormEarlyNotification)
            {
                Send_Message(
                    username: "system",
                    text: config.TEXT_StormEarlyWarning.Replace("{strength}",data.nextStormStrength.ToString().ToLower()),
                    gateway: generalGateway
                );
            }

            if (_lastData?.stormDayNotify == 1 && data.stormDayNotify == 0)
            {
                Send_Message(
                    username: "system",
                    text: config.TEXT_StormBegin.Replace("{strength}", data.nextStormStrength.ToString().ToLower()),
                    gateway: generalGateway
                );
            }

            //double activeDaysLeft = data.stormActiveTotalDays - api.World.Calendar.TotalDays;
            if (_lastData?.stormDayNotify == 0 && data.stormDayNotify != 0)
            {
                Send_Message(
                    username: "system",
                    text: config.TEXT_StormEnd.Replace("{strength}", data.nextStormStrength.ToString().ToLower()),
                    gateway: generalGateway
                );
            }

            _lastData = JsonConvert.DeserializeObject<TemporalStormRunTimeData>(JsonConvert.SerializeObject(data));
        }

        private void Event_PlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data,
            Vintagestory.API.Datastructures.BoolRef consumed)
        {
            PlayerGroup group = channelId == GlobalConstants.GeneralChatGroup ? api.Groups.GetPlayerGroupByName("") : api.Groups.PlayerGroupsById[channelId];

            api.Logger.Debug("chat: {0}", message);
            api.Logger.Debug($"group: {group.Uid} {group.Name}");

            // look up gateway for group name
            var gateway = config.ChannelMapping.First(entry => entry.groupName == group.Name).gateway;

            var foundText = new Regex(@".*?> (.+)$").Match(message);
            if (!foundText.Success)
                return;

            api.Logger.Debug($"message: {message}");
            api.Logger.Debug($"data: {data}");
            api.Logger.Chat($"**{byPlayer.PlayerName}**: {foundText.Groups[1].Value}");

            Send_Message(
                username: byPlayer.PlayerName,
                text: foundText.Groups[1].Value,
                account: byPlayer.PlayerUID,
                gateway: gateway
            );
        }


        private void Connect_Websocket()
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
                Connect_Websocket();
            }
            else
            {
                api.Logger.Error($"will not try to reconnect after {_connectErrrors} failed connection attempts");
            }
        }

        private PlayerGroup Get_or_Create_Group(string groupName)
        {
            var group = api.Groups.GetPlayerGroupByName(groupName);

            // if (/*gateway != config.Gateway && */gateway != "")
            // {
            if (group == null)
            {
                api.Logger.Debug("group not found for name {0}", groupName);

                group = new PlayerGroup
                {
                    Name = groupName,
                    Uid = api.Groups.PlayerGroupsById.Keys.Max() + 1
                };
                var generalGroup = api.Groups.GetPlayerGroupByName("");
                group.OwnerUID = generalGroup.OwnerUID;

                api.Logger.Notification("creating group {0}", group.Name);
                api.Groups.AddPlayerGroup(group);

                api.Logger.Debug("adding players to group {0}", group.Name);
                foreach (var player in api.Server.Players)
                {
                    AddPlayerToGroup(player, group);
                }
            }

            // TODO: save group to gateway_groups.json
            // config.ChannelMapping.Add(
            //     new ChannelMappingEntry(
            //         group: group.Name,
            //         gateway: gateway)
            //     );
            // api.StoreModConfig(config, CONFIGNAME);
            // }
            // else
            // {
            //     group = api.Groups.GetPlayerGroupByName("");
            // }

            api.Logger.Debug($"group: {group.Name} {group.Uid}");
            return group;
        }

        private void AddPlayerToGroup(IServerPlayer player, PlayerGroup group)
        {
            var serverPlayer = player.ServerData;
            if (!serverPlayer.PlayerGroupMemberShips.ContainsKey(group.Uid))
            {
                api.Logger.Debug($"adding {player.PlayerName} to group {group.Name}");
                serverPlayer.PlayerGroupMemberShips.Add(
                    key: group.Uid,
                    value: new PlayerGroupMembership()
                    {
                        GroupUid = group.Uid,
                        GroupName = group.Name,
                        Level = EnumPlayerGroupMemberShip.Member
                    }
                );
            }
        }

        // websocket

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
                            Send_Message(
                                username: "system",
                                text: config.TEXT_ServerStart,
                                @event: ApiMessage.EventJoinLeave,
                                gateway: "general" //TODO: look up group to gateway mapping
                            );
                        }

                        break;
                }

                return;
            }


            var mappingEntry = config.ChannelMapping.FirstOrDefault(entry => entry.gateway == message.gateway);
            if (mappingEntry == null)
            {
                api.Logger.Debug("no group found for channel {0}, skipping message", message.channel);
                return;
            }

            var group = Get_or_Create_Group(mappingEntry.groupName);

            switch (message.@event)
            {
                case ApiMessage.EventJoinLeave:
                {
                    api.SendMessageToGroup(
                        group.Uid,
                        $"{message.gateway} <strong>{message.username}</strong>: {message.text.Replace(">", "&gt;").Replace("<", "&lt;")}",
                        EnumChatType.OthersMessage
                    );
                    break;
                }
                case ApiMessage.EventUserAction:
                {
                    api.SendMessageToGroup(
                        group.Uid,
                        $"{message.gateway} <strong>{message.username}</strong> action: {message.text.Replace(">", "&gt;").Replace("<", "&lt;")}",
                        EnumChatType.OthersMessage
                    );
                    break;
                }
                case "":
                {
                    api.SendMessageToGroup(
                        group.Uid,
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

        private void Send_Message(string username, string text, string gateway, string @event = "", string account = "")
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
                protocol: "vs"
            );

            var messageText = JsonConvert.SerializeObject(message);
            api.Logger.Debug("sending: {0}", messageText);
            _websocket.Send(messageText);
        }
    }
}