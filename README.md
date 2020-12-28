# Matterbridge API link for Vintage Story Server

this mod is serverside **ONLY**
and you need to run a matterbridge: [https://github.com/42wim/matterbridge](https://github.com/42wim/matterbridge)
First of all.. you need to run a matterbridge: [https://github.com/42wim/matterbridge](https://github.com/42wim/matterbridge)
and for this mod to connect the websocket to the api  [https://github.com/42wim/matterbridge/wiki/API](https://github.com/42wim/matterbridge/wiki/API) must be accessible

matteridge allows for many complex chat brdige setups
it is worthwile to read through their wiki: https://github.com/42wim/matterbridge/wiki

## configuration

the mod will create a default config file in `ModConfig/matterbidge.json`

with the following properties

#### `string` Uri

websocket uri, configure this differently if you setup reverse procies for `wss://` or different paths

#### `string` Token

matterbridge api token, leave empty if not defined on matterbridge

#### `bool` SendApiConnectEvents

announces when the mod connects or when the server shuts down

#### `bool` SendPlayerJoinLeaveEvents

announces when players join or leave the server

#### `bool` SendStormNotification

announces storm events

#### `List<ChannelMappingEntry>` ChannelMapping

example: 
```json
{
  "ChannelMapping": [
    {
      "groupName": "",
      "gateway": "general"
    },
    {
      "groupName": "announcements",
      "gateway": "announcements"
    }
  ]
}
```

`groupName` is the group name in VS, the groups are created automatically
`gateway` is matching `gateway.name` in the matterbridge config (see: [Gateway config (basic)](https://github.com/42wim/matterbridge/wiki/Gateway-config-%28basic%29))

#### `string` TEXT_*

text templates for messages sent to matterbridge

## how to setup matterbridge

https://github.com/42wim/matterbridge/wiki/How-to-create-your-config

matterbridge gateways are a collections of channels from different platforms
each channel can be receive-only (`in`), send-only (`out`) or send-and-receive (`in-out`) 
example: announcements channel bridged to VS

## example configs

a complete sample of all matterbridge configs with their default values is available here: https://github.com/42wim/matterbridge/blob/master/matterbridge.toml.sample

`matterbridge.toml`

```toml
#WARNING: as this file contains credentials, be sure to set correct file permissions
[irc]
    [irc.freenode]
    Server="irc.freenode.net:6667"
    Nick="matterbot"

[api.vintagestory]
     #Address to listen on for API
     #REQUIRED
     BindAddress="0.0.0.0:4242"

     #Bearer token used for authentication
     #curl -H "Authorization: Bearer secret" http://nikky.moe:4242/api/messages
     #OPTIONAL (no authorization if token is empty)
     Token="5YZJgQwRb4nzMgLicMFn"

     #Amount of messages to keep in memory
     Buffer=1000

     # RemoteNickFormat defines how remote users appear on this bridge
     # The string "{NICK}" (case sensitive) will be replaced by the actual nick / username.
     # The string "{BRIDGE}" (case sensitive) will be replaced by the sending bridge
     # The string "{LABEL}" (case sensitive) will be replaced by Label= field of the sending bridge
     # The string "{PROTOCOL}" (case sensitive) will be replaced by the protocol used by the bridge
     # OPTIONAL (default empty)
     RemoteNickFormat="{NICK}"
     ShowJoinPart = true

     #extra label that can be used in the RemoteNickFormat
     #optional (default empty)
     Label="api.vintagestory"

[[gateway]]
name="main"
enable=true
    [[gateway.inout]]
    account="irc.freenode"
    channel="#test_matterbridge"

    [[gateway.inout]]
    account="api.vintagestory"
    channel="api"

[[gateway]]
name="oneway"
enable=true
    [[gateway.in]]
    account="irc.freenode"
    channel="#test_matterbridge_oneway"

    [[gateway.out]]
    account="api.vintagestory"
    channel="api"
    
#simpler config possible since v0.10.2
[[gateway]]
name="short"
enable=true
inout = [
    { account="irc.freenode", channel="#test_matterbridge_offtopic", options={key="channelkey"}},
    { account="api.vintagestory", channel="api" },
]
```

`ModConfig/matterbridge.json`

```json
{
  "Uri": "ws://localhost:4242/api/websocket",
  "Token": "5YZJgQwRb4nzMgLicMFn",
  "SendApiConnectEvents": true,
  "SendPlayerJoinLeaveEvents": true,
  "SendStormNotification": true,
  "SendStormEarlyNotification": true,
  "ChannelMapping": [
    {
      "groupName": "",
      "gateway": "main"
    },
    {
      "groupName": "announcements",
      "gateway": "oneway"
    },
    {
      "groupName": "offtopic",
      "gateway": "short"
    }
  ],
  "TEXT_StormEarlyWarning": "It appears a {strength} storm is coming...",
  "TEXT_StormBegin": "Harketh the storm doth come, Wary be thine self, as for thy own end be near.",
  "TEXT_StormEnd": "The temporal storm seems to be waning...",
  "TEXT_ServerStart": "Server is now up and running. Come on in!",
  "TEXT_ServerStop": "Server is shutting down. Goodbye!"
}
```

## contributions

thanks to @copygirl for providing a quick to setup sample repo 
https://github.com/copygirl/howto-example-mod

huge thanks to @Capsup 
the game related features are mostly based on https://gitlab.com/vsmods-public/vschatbot
and https://gitlab.com/vsmods-public/proximitychat

without that as a sample i could not have written my first VS mod


## development setup

follow the README at https://github.com/copygirl/howto-example-mod

(mainly the ENV variable setup)