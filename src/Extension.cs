using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Matterbridge
{
    public static class Extension
    { 
        public static bool None<T>(this IEnumerable<T> collection, System.Func<T, bool> predicate)
        {
            return collection.All(p=>predicate(p)==false);
        }

        public static PlayerGroup GetOrCreate(this IGroupManager groupManager, ICoreServerAPI api, string groupName)
        {
            var group = groupManager.GetPlayerGroupByName(groupName);

            if (group == null)
            {
                api.Logger.Debug("group not found for name {0}", groupName);

                group = new PlayerGroup
                {
                    Name = groupName
                };

                api.Logger.Notification("creating group {0}", group.Name);
                groupManager.AddPlayerGroup(group);

                // TODO: add all currently connected players to new group
                // api.Logger.Debug("adding players to group {0}", group.Name);
                // foreach (var player in api.Server.Players)
                // {
                //     AddPlayerToGroup(player, group);
                // }
            }

            return group;
        }
        
        public static void AddPlayer(this PlayerGroup group, ICoreServerAPI api, IServerPlayer player)
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
        
        
    }

}