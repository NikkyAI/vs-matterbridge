using System.Collections.Generic;

namespace Matterbridge
{
    internal class ModConfig
    {
        public string Uri = "ws://localhost:4242/api/websocket";

        public string Token = "";

        public bool SendApiConnectEvents = true;
        public bool SendPlayerJoinLeaveEvents = true;
        public bool SendPlayerDeathEvents = true;
        public bool SendStormNotification = true;
        public bool SendStormEarlyNotification = true;
        
        public string systemUsername = "system";
        public string systemAvatar = "";

        public string generalGateway = "general";
        public bool generalGeneratedAvatars = false;
        public List<ChannelMappingEntry> ChannelMapping = new List<ChannelMappingEntry>();

        public string TEXT_StormEarlyWarning { get; set; } = "It appears a {strength} storm is coming...";
        public string TEXT_StormBegin { get; set; } = "Harketh the storm doth come, Wary be thine self, as for thy own end be near.";
        public string TEXT_StormEnd { get; set; } = "The temporal storm seems to be waning...";
        public string TEXT_ServerStart { get; set; } = "Server is now up and running. Come on in!";
        public string TEXT_ServerStop { get; set; } = "Server is shutting down. Goodbye!";
    }

    internal class ChannelMappingEntry
    {
        public readonly string groupName;
        public readonly string gateway;
        public readonly bool isPrivate;
        public readonly bool skipStormWarning;
        public readonly bool generatedAvatars;

        public ChannelMappingEntry(string groupName, string gateway, bool isPrivate, bool skipStormWarning, bool generatedAvatars)
        {
            this.groupName = groupName;
            this.gateway = gateway;
            this.isPrivate = isPrivate;
            this.skipStormWarning = skipStormWarning;
            this.generatedAvatars = generatedAvatars;
        }
    }
}