using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Newtonsoft.Json;
using Vintagestory.Server;

namespace Matterbridge
{
    public class MatterbridgeMod : ModSystem
    {
        // ReSharper disable InconsistentNaming
        private const string PLAYERDATA_LASTSEENKEY = "MATTERBRIDGE_LASTSEEN";
        private const string PLAYERDATA_TOTALPLAYTIMEKEY = "MATTERBRIDGE_TOTALPLAYTIME";

        private const string CONFIGNAME = "matterbridge.json";
        // ReSharper restore InconsistentNaming

        private static ICoreServerAPI? _api;
        private static ModConfig? _config;
        private static Mod? _mod;
        private static WebsocketHandler? _websocketHandler;

        // ReSharper disable ConvertToAutoProperty
        private static ICoreServerAPI Api
        {
            get => _api ?? throw new NullReferenceException("api is not initialized yet");
            set => _api = value;
        }

        private static ModConfig Config => _config ?? throw new NullReferenceException("config is not initialized yet");

        private static WebsocketHandler WebsocketHandler
        {
            get => _websocketHandler ?? throw new NullReferenceException("websocket handler is not initialized yet");
            set => _websocketHandler = value;
        }
        // ReSharper restore ConvertToAutoProperty

        private TemporalStormRunTimeData? _lastData;
        private SystemTemporalStability? _temporalSystem;

        private static readonly Dictionary<string, DateTime> ConnectTimeDict = new Dictionary<string, DateTime>();

        public override bool AllowRuntimeReload => true;

        private const string harmonyId = "matterbridge.fix";
        private Harmony harmony = new Harmony(harmonyId);

        public override void StartServerSide(ICoreServerAPI api)
        {
            _mod = base.Mod;
            Api = api;

            LoadConfig();

            WebsocketHandler = new WebsocketHandler(api, Mod, Config);

            api.RegisterCommand(
                command: "me",
                descriptionMsg: "a action",
                syntaxMsg: "/me <text>",
                handler: ActionCommandHandler,
                requiredPrivilege: "chat"
            );
            api.RegisterCommand(
                command: "bridge",
                descriptionMsg: "chatbridge controls",
                syntaxMsg: "/bridge join|leave|list|listall|version",
                handler: BridgeCommandHandler,
                requiredPrivilege: "chat"
            );
            api.RegisterCommand(
                command: "bridgereload",
                descriptionMsg: "reloads chat bridge",
                syntaxMsg: "/bridgereload",
                handler: BridgeReloadCommandHandler,
                requiredPrivilege: "controlserver"
            );

            Api.Event.SaveGameLoaded += Event_SaveGameLoaded;
            Api.Event.PlayerChat += Event_PlayerChat;

            Api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;
            Api.Event.PlayerDisconnect += Event_PlayerDisconnect;

            Api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, Event_ServerStartup);
            Api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, Event_ServerShutdown);

            // Api.Event.PlayerDeath += Event_PlayerDeath;

            harmony.PatchAll();
        }

        public override void Dispose()
        {
            WebsocketHandler.Dispose();
            harmony.UnpatchAll(harmonyId);
            base.Dispose();
        }

        private void LoadConfig()
        {
            try
            {
                _config = Api.LoadModConfig<ModConfig>(CONFIGNAME);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Failed to load mod config! {0}", e);
                return;
            }

            if (_config == null)
            {
                Mod.Logger.Notification($"non-existant modconfig at 'ModConfig/{CONFIGNAME}', creating default...");
                _config = new ModConfig();
            }

            Api.StoreModConfig(Config, CONFIGNAME);

            foreach (var entry in _config.ChannelMapping)
            {
                Api.Logger.Debug($"{entry.gateway} {entry.groupName} {entry.isPrivate}");
                foreach (var otherEntry in _config.ChannelMapping)
                {
                    if (entry.groupName == otherEntry.groupName && entry.gateway != otherEntry.gateway)
                    {
                        Mod.Logger.Error("inconsistent channel mapping for group {0} to gateways {1} {2}",
                            entry.groupName,
                            entry.gateway, otherEntry.gateway);
                        return;
                    }

                    if (entry.groupName != otherEntry.groupName && entry.gateway == otherEntry.gateway)
                    {
                        Mod.Logger.Error("inconsistent channel mapping for gateway {0} to groups {1} {2}",
                            entry.gateway, entry.groupName, otherEntry.groupName);
                        return;
                    }
                }
            }
        }

        private void ActionCommandHandler(IServerPlayer player, int groupid, CmdArgs args)
        {
            var message = args.PopAll();
            var cleanedMessage = message.Replace(">", "&gt;").Replace("<", "&lt;");

            Mod.Logger.Debug($"groupId: {groupid}");

            Api.SendMessageToGroup(
                groupid: groupid,
                message: $"<i><strong>{player.PlayerName}</strong> {cleanedMessage}</i>",
                chatType: EnumChatType.OwnMessage
            );

            string gateway;
            if (groupid == GlobalConstants.GeneralChatGroup)
            {
                gateway = Config.generalGateway;
            }
            else
            {
                var group = Api.Groups.PlayerGroupsById[groupid];
                var entry = Config.ChannelMapping.FirstOrDefault(e => e.groupName == group.Name);
                if (entry == null)
                {
                    Mod.Logger.Debug("no gateway found for group {0}, skipping message", group.Name);
                    return;
                }

                gateway = entry.gateway;
            }

            WebsocketHandler.SendUserMessage(
                player: player, 
                text: message, 
                gateway: gateway,
                @event: ApiMessage.EventUserAction
            );
        }

        private void BridgeCommandHandler(IServerPlayer player, int groupid, CmdArgs args)
        {
            var arg0 = args.PopWord();
            switch (arg0)
            {
                case "join":
                {
                    var gateway = args.PopWord();
                    var entry = Config.ChannelMapping.FirstOrDefault(e => e.gateway == gateway);
                    if (entry == null || entry.isPrivate)
                    {
                        player.SendMessage(groupid, $"no entry found matching: {gateway}", EnumChatType.Notification);
                        return;
                    }

                    var group = Api.Groups.GetOrCreate(Api, entry.groupName);
                    group.AddPlayer(Api, player);
                    // player.BroadcastPlayerData();
                    player.SendMessage(groupid, $"added to group: {gateway}, you may need to reconnect",
                        EnumChatType.Notification);
                    break;
                }
                case "leave":
                {
                    var gateway = args.PopWord();
                    var entry = Config.ChannelMapping.FirstOrDefault(e => e.gateway == gateway);
                    if (entry == null || entry.isPrivate)
                    {
                        player.SendMessage(groupid, $"no entry found matching: {gateway}", EnumChatType.Notification);
                        return;
                    }

                    var group = Api.Groups.GetOrCreate(Api, entry.groupName);
                    player.ServerData.PlayerGroupMemberShips.Remove(group.Uid);
                    // player.BroadcastPlayerData();
                    player.SendMessage(groupid, $"removed from group: {gateway}, you may need to reconnect",
                        EnumChatType.Notification);
                    break;
                }
                case "list":
                {
                    var groupNames = Config.ChannelMapping
                        .Where(entry => !entry.isPrivate)
                        .Where(entry =>
                            {
                                var group = Api.Groups.GetOrCreate(Api, entry.groupName);
                                return player.Groups.None(membership => membership.GroupUid == @group.Uid);
                            }
                        )
                        .Select(entry => entry.gateway)
                        .ToList();
                    player.SendMessage(groupid,
                        groupNames.Count == 0
                            ? $"you joined all bridges, try /bridge listall"
                            : $"bridges: \n  {string.Join("\n  ", groupNames)}",
                        EnumChatType.Notification);
                    break;
                }
                case "listall":
                {
                    var groupNames = Config.ChannelMapping
                        .Where(entry => !entry.isPrivate)
                        .Select(entry => entry.gateway)
                        .ToList();
                    player.SendMessage(groupid, $"bridges: \n  {string.Join("\n  ", groupNames)}",
                        EnumChatType.Notification);
                    break;
                }
                case "version":
                {
                    player.SendMessage(
                        groupid, 
                        $"{Mod.Info.Name} version {Mod.Info.Version}",
                        EnumChatType.Notification
                    );
                    break;
                }
                default:
                {
                    player.SendMessage(groupid, $"unknown subcommand {arg0}\n available: join|leave|list|listall|version", EnumChatType.Notification);
                    break;
                }
            }
        }

        private void BridgeReloadCommandHandler(IServerPlayer player, int groupid, CmdArgs args)
        {
            LoadConfig();
            WebsocketHandler.Close(skipMessage: true);
            WebsocketHandler.Connect();
        }

        // private void Event_PlayerDeath(IServerPlayer player, DamageSource? deathReason)
        // {
        //     if (Config.SendPlayerDeathEvents)
        //     {
        //         string deathMessage = this.GetDeathMessage(player, deathReason);
        //         deathMessage = deathMessage.Replace($"Player {player.PlayerName}", "");
        //         
        //         WebsocketHandler.SendUserMessage(
        //             player: player,
        //             text: deathMessage,
        //             @event: ApiMessage.EventUserAction,
        //             // account: byPlayer.PlayerUID,
        //             gateway: Config.generalGateway
        //         );
        //     }
        // }

        private void Event_ServerStartup()
        {
            WebsocketHandler.Connect();
        }

        private void Event_ServerShutdown()
        {
            WebsocketHandler.Close();
        }

        private void Event_PlayerDisconnect(IServerPlayer byPlayer)
        {
            var timePlayed = DateTime.UtcNow - ConnectTimeDict[byPlayer.PlayerUID];
            var data = Api.PlayerData.GetPlayerDataByUid(byPlayer.PlayerUID);
            if (data != null)
            {
                data.CustomPlayerData[PLAYERDATA_LASTSEENKEY] = DateTime.UtcNow.ToString(CultureInfo.CurrentCulture);

                TimeSpan totalPlaytime;
                if (data.CustomPlayerData.TryGetValue(PLAYERDATA_TOTALPLAYTIMEKEY, out var totalPlaytimeString))
                {
                    try
                    {
                        totalPlaytime = TimeSpan.Parse(totalPlaytimeString);
                    }
                    catch (FormatException e)
                    {
                        Mod.Logger.Error("error parsing timespan: {0}, {1}", totalPlaytimeString, e);
                        totalPlaytime = TimeSpan.Zero;
                    }
                }
                else
                {
                    totalPlaytime = TimeSpan.Zero;
                }
                data.CustomPlayerData[PLAYERDATA_TOTALPLAYTIMEKEY] = (timePlayed + totalPlaytime).ToString();

                data.CustomPlayerData[PLAYERDATA_TOTALPLAYTIMEKEY] = timePlayed.ToString();
            }

            var removed = ConnectTimeDict.Remove(byPlayer.PlayerUID);
            Mod.Logger.Debug("ConnectTimeDict.remove(\"{0}\")", byPlayer.PlayerUID);

            if (Config.SendPlayerJoinLeaveEvents)
            {
                WebsocketHandler.SendSystemMessage(
                    text: $"{byPlayer.PlayerName} has disconnected from the server! " +
                          // $"played for {timePlayed} " +
                          $"({Api.Server.Players.Count(x => x.PlayerUID != byPlayer.PlayerUID && x.ConnectionState == EnumClientState.Playing)}/{Api.Server.Config.MaxClients})",
                    gateway: Config.generalGateway,
                    @event: ApiMessage.EventJoinLeave
                );
            }
        }

        private void Event_PlayerNowPlaying(IServerPlayer byPlayer)
        {
            if (ConnectTimeDict.ContainsKey(byPlayer.PlayerUID))
            {
                ConnectTimeDict.Remove(byPlayer.PlayerUID);
            }
            ConnectTimeDict.Add(byPlayer.PlayerUID, DateTime.UtcNow);

            // this forces autojoin groups
            // foreach (var entry in config.ChannelMapping)
            // {
            //     var group = Get_or_Create_Group(entry.groupName);
            //
            //     AddPlayerToGroup(byPlayer, group);
            // }

            if (Config.SendPlayerJoinLeaveEvents)
            {
                WebsocketHandler.SendSystemMessage(
                    // username: "system",
                    text: $"{byPlayer.PlayerName} has connected to the server! " +
                          $"({Api.Server.Players.Count(x => x.ConnectionState != EnumClientState.Offline)}/{Api.Server.Config.MaxClients})",
                    gateway: Config.generalGateway,
                    @event: ApiMessage.EventJoinLeave
                    // account: byPlayer.PlayerUID
                );
            }
        }

        private void Event_SaveGameLoaded()
        {
            if (Config.SendStormNotification && Api.World.Config.GetString("temporalStorms") != "off")
            {
                _temporalSystem = Api.ModLoader.GetModSystem<SystemTemporalStability>();
                Api.Event.RegisterGameTickListener(OnTempStormTick, 5000);
            }
        }

        private void OnTempStormTick(float t1)
        {
            if (_temporalSystem == null)
            {
                Mod.Logger.Error("temporalSystem not initialized yet");
                return;
            }

            var data = _temporalSystem.StormData;

            if (_lastData?.stormDayNotify > 1 && data.stormDayNotify == 1 && Config.SendStormEarlyNotification)
            {
                WebsocketHandler.SendSystemMessage(
                    text: Config.TEXT_StormEarlyWarning.Replace("{strength}",
                        data.nextStormStrength.ToString().ToLower()),
                    gateway: Config.generalGateway
                );
            }

            if (_lastData?.stormDayNotify == 1 && data.stormDayNotify == 0)
            {
                WebsocketHandler.SendSystemMessage(
                    text: Config.TEXT_StormBegin.Replace("{strength}", data.nextStormStrength.ToString().ToLower()),
                    gateway: Config.generalGateway
                );
            }

            //double activeDaysLeft = data.stormActiveTotalDays - api.World.Calendar.TotalDays;
            if (_lastData?.stormDayNotify == 0 && data.stormDayNotify != 0)
            {
                WebsocketHandler.SendSystemMessage(
                    text: Config.TEXT_StormEnd.Replace("{strength}", data.nextStormStrength.ToString().ToLower()),
                    gateway: Config.generalGateway
                );
            }

            _lastData = JsonConvert.DeserializeObject<TemporalStormRunTimeData>(JsonConvert.SerializeObject(data));
        }

        private void Event_PlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data,
            Vintagestory.API.Datastructures.BoolRef consumed)
        {
            string gateway;
            if (channelId == GlobalConstants.GeneralChatGroup)
            {
                gateway = Config.generalGateway;
            }
            else
            {
                PlayerGroup group = Api.Groups.PlayerGroupsById[channelId];

                Mod.Logger.Debug($"group: {group.Uid} {group.Name}");

                // look up gateway for group name
                gateway = Config.ChannelMapping.First(entry => entry.groupName == group.Name).gateway;
            }

            Mod.Logger.Debug("chat: {0}", message);

            var foundText = new Regex(@".*?> (.+)$").Match(message);
            if (!foundText.Success)
                return;

            Mod.Logger.Debug($"message: {message}");
            Mod.Logger.Debug($"data: {data}");
            Mod.Logger.Chat($"**{byPlayer.PlayerName}**: {foundText.Groups[1].Value}");

            WebsocketHandler.SendUserMessage(
                player: byPlayer,
                text: foundText.Groups[1].Value,
                gateway: gateway
            );
        }
        
        [HarmonyPatch(typeof(ServerSystemEntitySimulation), "GetDeathMessage")]
        class GetDeathMessagePatch
        {
            static void Postfix(
                ConnectedClient client, 
                DamageSource src,
                ref string __result
            )
            {
                if (Config.SendPlayerDeathEvents)
                {
                    string deathMessage = __result.Replace($"Player {client.PlayerName} ", "");
                
                    WebsocketHandler.SendUserMessage(
                        playerName: client.PlayerName,
                        playerUid: client.SentPlayerUid,
                        text: deathMessage,
                        @event: ApiMessage.EventUserAction,
                        gateway: Config.generalGateway
                    );
                }
            }
        }
    }
}