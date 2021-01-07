# Matterbridge API Connector for Vintage Story Server

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

#### `bool` SendPlayerDeathEvents

announces when players die

#### `bool` SendStormNotification

announces storm events

#### `string` generalGateway

main gateway for status updates and linking to general chat

#### `List<ChannelMappingEntry>` ChannelMapping

mapping of group names to gateways

example: 
```json
{
  "ChannelMapping": [
    {
      "groupName": "announcements",
      "gateway": "announcements",
      "isPrivate": false
    }
  ]
}
```

`groupName` is the group name in VS, the groups are created automatically
`gateway` is matching `gateway.name` in the matterbridge config (see: [Gateway config (basic)](https://github.com/42wim/matterbridge/wiki/Gateway-config-%28basic%29))
`isPrivate` controls if people can use `/bridge join|leave` and listing the

#### `string` TEXT_*

text templates for messages sent to matterbridge

## commands

#### me

`/me <message>`

do a action

### bridge join

`/bridge join <gateway>`  
join a gateway/bridge

### bridge leave

`/bridge leave <gateway>`  
leave a gateway/bridge

### bridge list

`/bridge list`  
lists gateways that you are not in

### bridge listall

`/bridge listall` 
list all gateways

### bridgereload

`/bridgereload`

admin only  
reloads config and
restarts the websocket connection to matterbridge

## how to setup matterbridge

https://github.com/42wim/matterbridge/wiki/How-to-create-your-config

matterbridge gateways are a collections of channels from different platforms
each channel can be receive-only (`in`), send-only (`out`) or send-and-receive (`in-out`) 
example: announcements channel bridged to VS

## example configs

- [example1](./sample/example1) 
- [example2](./sample/example2) readonly twitch chat & irc

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