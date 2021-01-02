using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Newtonsoft.Json;

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
        private static WebsocketHandler? _websocketHandler;

        // ReSharper disable ConvertToAutoProperty
        private static ICoreServerAPI Api
        {
            get => _api ?? throw new NullReferenceException("api is not initialized yet");
            set => _api = value;
        }

        private static ModConfig Config
        {
            get => _config ?? throw new NullReferenceException("config is not initialized yet");
            set => _config = value;
        }

        private WebsocketHandler WebsocketHandler
        {
            get => _websocketHandler ?? throw new NullReferenceException("websocket handler is not initialized yet");
            set => _websocketHandler = value;
        }
        // ReSharper restore ConvertToAutoProperty

        private TemporalStormRunTimeData? _lastData;
        private SystemTemporalStability? _temporalSystem;

        private static readonly Dictionary<string, DateTime> ConnectTimeDict = new Dictionary<string, DateTime>();

        public override void StartServerSide(ICoreServerAPI api)
        {
            Api = api;

            try
            {
                Config = api.LoadModConfig<ModConfig>(CONFIGNAME);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Failed to load mod config! {0}", e);
                return;
            }

            if (Config == null)
            {
                Mod.Logger.Notification($"non-existant modconfig at 'ModConfig/{CONFIGNAME}', creating default...");
                Config = new ModConfig();
            }

            api.StoreModConfig(Config, CONFIGNAME);

            foreach (var entry in Config.ChannelMapping)
            {
                foreach (var otherEntry in Config.ChannelMapping)
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

            WebsocketHandler = new WebsocketHandler(api, Mod, Config);

            api.RegisterCommand(
                command: "me",
                descriptionMsg: "a action",
                syntaxMsg: "/me <text>",
                handler: ActionCommandHandler
            );
            api.RegisterCommand(
                command: "bridge",
                descriptionMsg: "chatbridge controls",
                syntaxMsg: "/bridge join|leave|list|listall",
                handler: BridgeCommandHandler
            );

            Api.Event.SaveGameLoaded += Event_SaveGameLoaded;
            Api.Event.PlayerChat += Event_PlayerChat;

            Api.Event.PlayerJoin += Event_PlayerJoin;
            Api.Event.PlayerDisconnect += Event_PlayerDisconnect;

            Api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, Event_ServerStartup);
            Api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, Event_ServerShutdown);

            Api.Event.PlayerDeath += Event_PlayerDeath;
        }

        private void ActionCommandHandler(IServerPlayer player, int groupid, CmdArgs args)
        {
            var message = args.PopAll();

            Mod.Logger.Debug($"groupId: {groupid}");
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

            Api.SendMessageToGroup(
                groupid: groupid,
                message:
                $"<i><strong>{player.PlayerName}</strong> {message.Replace(">", "&gt;").Replace("<", "&lt;")}</i>",
                chatType: EnumChatType.OwnMessage
            );
            WebsocketHandler.SendMessage(player.PlayerName, message, gateway, ApiMessage.EventUserAction);
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
                    if (entry == null)
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
                    if (entry == null)
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
                        .Select(entry => entry.gateway)
                        .ToList();
                    player.SendMessage(groupid, $"bridges: \n  {string.Join("\n  ", groupNames)}",
                        EnumChatType.Notification);
                    break;
                }
                default:
                {
                    player.SendMessage(groupid, $"unknown subcommand {arg0}", EnumChatType.Notification);
                    break;
                }
            }
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

            if (Config.SendPlayerDeathEvents)
            {
                WebsocketHandler.SendMessage(
                    username: byPlayer.PlayerName,
                    text: deathMessage,
                    @event: ApiMessage.EventUserAction,
                    account: byPlayer.PlayerUID,
                    gateway: Config.generalGateway
                );
            }
        }

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

                if (data.CustomPlayerData.TryGetValue(PLAYERDATA_TOTALPLAYTIMEKEY, out var totalPlaytimeString))
                {
                    data.CustomPlayerData[PLAYERDATA_TOTALPLAYTIMEKEY] =
                        (timePlayed + TimeSpan.Parse(totalPlaytimeString)).ToString();
                }

                data.CustomPlayerData[PLAYERDATA_TOTALPLAYTIMEKEY] = timePlayed.ToString();
            }

            ConnectTimeDict.Remove(byPlayer.PlayerUID);

            if (Config.SendPlayerJoinLeaveEvents)
            {
                WebsocketHandler.SendMessage(
                    username: "system",
                    text: $"{byPlayer.PlayerName} has disconnected from the server! " +
                          // $"played for {timePlayed} " +
                          $"({Api.Server.Players.Count(x => x.PlayerUID != byPlayer.PlayerUID && x.ConnectionState == EnumClientState.Playing)}/{Api.Server.Config.MaxClients})",
                    gateway: Config.generalGateway,
                    @event: ApiMessage.EventJoinLeave
                );
            }
        }

        private void Event_PlayerJoin(IServerPlayer byPlayer)
        {
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
                WebsocketHandler.SendMessage(
                    username: "system",
                    text: $"{byPlayer.PlayerName} has connected to the server! " +
                          $"({Api.Server.Players.Count(x => x.ConnectionState != EnumClientState.Offline)}/{Api.Server.Config.MaxClients})",
                    gateway: Config.generalGateway,
                    @event: ApiMessage.EventJoinLeave,
                    account: byPlayer.PlayerUID
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
                WebsocketHandler.SendMessage(
                    username: "system",
                    text: Config.TEXT_StormEarlyWarning.Replace("{strength}",
                        data.nextStormStrength.ToString().ToLower()),
                    gateway: Config.generalGateway
                );
            }

            if (_lastData?.stormDayNotify == 1 && data.stormDayNotify == 0)
            {
                WebsocketHandler.SendMessage(
                    username: "system",
                    text: Config.TEXT_StormBegin.Replace("{strength}", data.nextStormStrength.ToString().ToLower()),
                    gateway: Config.generalGateway
                );
            }

            //double activeDaysLeft = data.stormActiveTotalDays - api.World.Calendar.TotalDays;
            if (_lastData?.stormDayNotify == 0 && data.stormDayNotify != 0)
            {
                WebsocketHandler.SendMessage(
                    username: "system",
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

            WebsocketHandler.SendMessage(
                username: byPlayer.PlayerName,
                text: foundText.Groups[1].Value,
                account: byPlayer.PlayerUID,
                gateway: gateway
            );
        }
    }
}