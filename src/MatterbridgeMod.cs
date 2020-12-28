using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using SuperSocket.ClientEngine;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using WebSocket4Net;
using Newtonsoft.Json;

// [assembly: ModInfo(
//     name: "Matterbridge API Connector",
//     modID: "matterbridge",
//     Description = "A matterbridge api connector for VS server",
//     Website = "https://github.com/NikkyAI/vs-matterbridge",
//     Authors = new[] {"Nikky"},
//     Version = "0.0.1",
//     RequiredOnClient = false,
//     Side = "Server"
// )]
//
// [assembly: ModDependency("game", "1.8.0")]
// [assembly: ModDependency("survival", "")]

namespace Matterbridge
{
    public class MatterbridgeMod : ModSystem
    {
        private const string PLAYERDATA_LASTSEENKEY = "MATTERBRIDGE_LASTSEEN";
        private const string PLAYERDATA_TOTALPLAYTIMEKEY = "MATTERBRIDGE_TOTALPLAYTIME";
        private const string CONFIGNAME = "matterbridge.json";

        private static ICoreServerAPI Api;

        private ICoreServerAPI api
        {
            get => Api;
            set => Api = value;
        }

        private TemporalStormRunTimeData lastData;
        private SystemTemporalStability temporalSystem;

        private static readonly Dictionary<string, DateTime> connectTimeDict = new Dictionary<string, DateTime>();

        private WebSocket websocket;
        private bool reconnect_websocket = true;

        private Random random = new Random();

        private ModConfig config;

        private string generalGateway;

        private int connectErrrors = 0;

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


            // if (this.config.SendServerMessages)
            // {
            this.api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, Event_ServerStartup);
            this.api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, Event_ServerShutdown);
            // }

            this.api.Event.PlayerDeath += Event_PlayerDeath;
        }

        private void Event_PlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
        {
            // var deathMessage = (byPlayer?.PlayerName ?? "Unknown player") + " ";
            var deathMessage = "";
            if (damageSource == null)
                deathMessage += "was killed by the unknown.";
            else
            {
                switch (damageSource.Type)
                {
                    case EnumDamageType.Gravity:
                        deathMessage += "smashed into the ground";
                        break;
                    case EnumDamageType.Fire:
                        deathMessage += "burned to death";
                        break;
                    case EnumDamageType.Crushing:
                    case EnumDamageType.BluntAttack:
                        deathMessage += "was crushed";
                        break;
                    case EnumDamageType.SlashingAttack:
                        deathMessage += "was sliced open";
                        break;
                    case EnumDamageType.PiercingAttack:
                        deathMessage += "was pierced through";
                        break;
                    case EnumDamageType.Suffocation:
                        deathMessage += "suffocated to death";
                        break;
                    case EnumDamageType.Heal:
                        deathMessage += "was somehow *healed* to death";
                        break;
                    case EnumDamageType.Poison:
                        deathMessage += "was poisoned";
                        break;
                    case EnumDamageType.Hunger:
                        deathMessage += "starved to death";
                        break;
                    default:
                        deathMessage += "was killed";
                        break;
                }

                deathMessage += " ";

                switch (damageSource.Source)
                {
                    case EnumDamageSource.Block:
                        deathMessage += "by a block.";
                        break;
                    case EnumDamageSource.Player:
                        deathMessage += "when they failed at PVP.";
                        break;
                    case EnumDamageSource.Fall:
                        deathMessage += "when they fell to their doom.";
                        break;
                    case EnumDamageSource.Drown:
                        deathMessage += "when they tried to breath in water.";
                        break;
                    case EnumDamageSource.Revive:
                        deathMessage += "just as they respawned.";
                        break;
                    case EnumDamageSource.Void:
                        deathMessage += "when they fell screaming into the abyss.";
                        break;
                    case EnumDamageSource.Suicide:
                        deathMessage += "when they killed themselves.";
                        break;
                    case EnumDamageSource.Internal:
                        deathMessage += "when they took damage from the inside...";
                        break;
                    case EnumDamageSource.Entity:
                        switch (damageSource.SourceEntity.Code.Path)
                        {
                            case "wolf-male":
                            case "wolf-female":
                                deathMessage += "and eaten by a wolf.";
                                break;
                            case "pig-wild-male":
                                deathMessage += "by a boar.";
                                break;
                            case "pig-wild-female":
                                deathMessage += "by a sow.";
                                break;
                            case "sheep-bighorn-female":
                            case "sheep-bighorn-male":
                                deathMessage += "by a sheep.";
                                break;
                            case "chicken-rooster":
                                deathMessage += "by a... chicken.";
                                break;
                            case "locust":
                                deathMessage += "by a locust.";
                                break;
                            case "drifter":
                                deathMessage += "by a drifter.";
                                break;
                            case "beemob":
                                deathMessage += "by a swarm of bees.";
                                break;
                            default:
                                deathMessage += "by a monster.";
                                break;
                        }

                        break;
                    case EnumDamageSource.Explosion:
                        deathMessage += "when they stood by a bomb.";
                        break;
                    case EnumDamageSource.Machine:
                        deathMessage += "when they got their hands stuck in a machine.";
                        break;
                    case EnumDamageSource.Unknown:
                        deathMessage += "when they encountered the unknown.";
                        break;
                    case EnumDamageSource.Weather:
                        deathMessage += "when the weather itself suddenly struck.";
                        break;
                    default:
                        deathMessage += "by the unknown.";
                        break;
                }
            }

            //TODO if config.sendDeathEvents
            Send_Message(
                username: byPlayer.PlayerName,
                text: deathMessage,
                @event: ApiMessage.EventUserAction,
                account: byPlayer.PlayerUID,
                gateway: generalGateway
            );
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

            reconnect_websocket = false;
            websocket.Close();
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
                var groupName = entry.groupName;
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
                temporalSystem = api.ModLoader.GetModSystem<SystemTemporalStability>();
                api.Event.RegisterGameTickListener(onTempStormTick, 5000);
            }
        }

        private void onTempStormTick(float t1)
        {
            var data = this.temporalSystem.StormData;

            if (lastData?.stormDayNotify > 1 && data.stormDayNotify == 1 && this.config.SendStormEarlyNotification)
            {
                Send_Message(
                    username: "system",
                    text: config.TEXT_StormEarlyWarning.Replace("{strength}", Enum.GetName(typeof(EnumTempStormStrength), data.nextStormStrength).ToLower()),
                    gateway: generalGateway
                );
            }

            if (lastData?.stormDayNotify == 1 && data.stormDayNotify == 0)
            {
                Send_Message(
                    username: "system",
                    text: config.TEXT_StormBegin.Replace("{strength}", Enum.GetName(typeof(EnumTempStormStrength), data.nextStormStrength).ToLower()),
                    gateway: generalGateway
                );
            }

            //double activeDaysLeft = data.stormActiveTotalDays - api.World.Calendar.TotalDays;
            if (lastData?.stormDayNotify == 0 && data.stormDayNotify == -1)
            {
                Send_Message(
                    username: "system",
                    text: config.TEXT_StormEnd.Replace("{strength}", Enum.GetName(typeof(EnumTempStormStrength), data.nextStormStrength).ToLower()),
                    gateway: generalGateway
                );
            }

            lastData = JsonConvert.DeserializeObject<TemporalStormRunTimeData>(JsonConvert.SerializeObject(data));
        }

        private void Event_PlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data,
            Vintagestory.API.Datastructures.BoolRef consumed)
        {
            PlayerGroup group;
            if (channelId == GlobalConstants.GeneralChatGroup)
            {
                group = api.Groups.GetPlayerGroupByName("");
            }
            else
            {
                group = api.Groups.PlayerGroupsById[channelId];
            }

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

                websocket = new WebSocket(
                    uri: config.Uri,
                    customHeaderItems: customHeaderItems
                );
                websocket.Opened += new EventHandler(websocket_Opened);
                websocket.Error += new EventHandler<ErrorEventArgs>(websocket_Error);
                websocket.Closed += new EventHandler(websocket_Closed);
                websocket.MessageReceived += new EventHandler<MessageReceivedEventArgs>(websocket_MessageReceived);
                websocket.Open();

                api.Logger.Debug("started websocket");
            }
            catch (Exception e)
            {
                api.Logger.Error("error connecting to websocket: {0} {1}", e, e.StackTrace);
            }
        }

        private void websocket_Opened(object sender, EventArgs e)
        {
            connectErrrors = 0;
            api.Logger.Debug("websocket_Opened");
            api.Logger.Debug($"sender: {sender}");
            //TODO: send `vs bridge connected`
            // websocket.Send("Hello World!");
        }

        private void websocket_Error(object sender, ErrorEventArgs errorEventArgs)
        {
            connectErrrors++;
            api.Logger.Error($"connect error: {connectErrrors}");
            api.Logger.Error($"websocket_Error: {errorEventArgs.Exception}");
        }

        private void websocket_Closed(object sender, EventArgs eventArgs)
        {
            api.Logger.Debug("websocket_Closed");

            if (reconnect_websocket && connectErrrors < 10)
            {
                Thread.Sleep(100);
                Connect_Websocket();
            }
            else
            {
                api.Logger.Error($"will not try to reconnect after {connectErrrors} failed connection attempts");
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

            var message_text = JsonConvert.SerializeObject(message);
            api.Logger.Debug("sending: {0}", message_text);
            websocket.Send(message_text);
        }
    }
}