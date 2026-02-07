# Rust Server Chat Commands Guide

## NTeleportation
**Plugin**: NTeleportation (nivex v1.9.4) - Multiple teleportation systems for admin and players

### Admin Teleportation Commands
- `/tp <player>` - Teleports yourself to the target player
- `/tp <player> <target player>` - Teleports player to target player
- `/tp x y z` - Teleports you to coordinates
- `/tp <player> x y z` - Teleports player to coordinates
- `/tpl` - Shows list of saved admin locations
- `/tpl <location name>` - Teleports you to a saved location
- `/tpsave <location name>` - Saves current position as location
- `/tpremove <location name>` - Removes a saved location
- `/tpn <player>` - Teleports you behind target player (default distance)
- `/tpn <player> <distance>` - Teleports you behind player at specified distance
- `/tpb` - Teleports you back to previous location
- `/home radius <radius>` - Find all homes within radius
- `/home delete <player> <home name>` - Delete player's home
- `/home tp <player> <home name>` - Teleport to player's home
- `/home homes <player>` - List all homes of a player

### Home Teleportation Commands (Players)
- `/home add <name>` - Save current location as home
- `/home <name>` - Teleport to your home
- `/home list` - Show list of your homes
- `/home remove <name>` - Remove a home
- `/home <name> pay` - Teleport home while on cooldown (pay penalty)

### Teleport Request Commands (Players)
- `/tpr <player name>` - Send teleport request to player
- `/tpa` - Accept incoming teleport request
- `/tpat` - Toggle automatic /tpa acceptance
- `/tpc` - Cancel teleport request or pending teleport

### Town/Location Commands
- `/town` - Teleport to town
- `/town pay` - Teleport to town (pay penalty)
- `/town set` - Set town location (admin)
- `/outpost` - Teleport to Outpost
- `/outpost pay` - Teleport to Outpost (pay penalty)
- `/outpost set` - Set Outpost location (admin)
- `/bandit` - Teleport to Bandit Town
- `/bandit pay` - Teleport to Bandit Town (pay penalty)
- `/bandit set` - Set Bandit Town location (admin)
- `/ntp <custom town>` - Dynamic teleport to custom location

### Information Commands
- `/tpinfo` - Shows teleport limits and cooldowns
- `/tpinfo <module>` - Shows info for specific module (home/tpr/town/outpost/bandit)
- `/home info` - Show home system settings
- `/tphelp` - General teleport help
- `/tphelp <module>` - Show help for specific module

---

## Clans
**Plugin**: Clans (k1lly0u v0.2.8) - Clan management system

### Clan Commands
- `/clan create <tag> [description]` - Create a new clan
- `/clan leave` - Leave your current clan
- `/clan invite <player name or ID>` - Invite player to clan
- `/clan withdraw <player name or ID>` - Withdraw invitation to player
- `/clan kick <player name or ID>` - Kick member from clan
- `/clan accept <clan tag>` - Accept clan invitation
- `/clan reject <clan tag>` - Reject clan invitation
- `/clan promote <player name or ID>` - Promote member (owner only)
- `/clan demote <player name or ID>` - Demote member (owner only)
- `/clan disband forever` - Disband your clan (owner only)
- `/clan tagcolor <hex color>` - Set custom clan tag color
- `/clan tagcolor reset` - Reset clan tag color to default

### Alliance Commands (Sub-commands of /clan ally)
- `/ally invite <clan tag>` - Invite clan to become allies
- `/ally withdraw <clan tag>` - Withdraw alliance invitation
- `/ally accept <clan tag>` - Accept alliance invitation
- `/ally reject <clan tag>` - Reject alliance invitation
- `/ally revoke <clan tag>` - Revoke an alliance

### Clan Chat & Info Commands
- `/c <message>` - Send message to clan chat
- `/a <message>` - Send message to alliance chat
- `/cinfo` - View clan information
- `/clanhelp` - Show clan commands help

---

## Kits
**Plugin**: Kits (k1lly0u v4.4.8) - Create and redeem item kits

### Player Commands
- `/kit` - View available kits and redeem
- `/kit <kit name>` - Redeem specified kit

### Admin Commands
- `/kit new` - Create a new kit
- `/kit edit <name>` - Edit existing kit
- `/kit delete <name>` - Delete a kit
- `/kit list` - List all available kits
- `/kit give <player name or ID> <kit name>` - Give kit to player
- `/kit givenpc <kit name>` - Give kit to NPC you're looking at
- `/kit reset` - Wipe all player kit usage data
- `/kit resetuses <player ID or name> <kit>` - Reset kit uses for specific player

---

## GUIShop
**Plugin**: GUIShop (Khan v2.2.44+) - In-game GUI shop system

### Shop Commands
- `/shop` - Open the main shop GUI
- `/buy <item>` - Buy item from shop
- `/sell <item>` - Sell item to shop
- `/shop clear` - Clear shop UI data
- `/shop togglebackpack` - Toggle backpack GUI mode
- `/shop update` - Update shop image URLs (admin)

---

## RemoverTool
**Plugin**: Remover Tool (Reneb/Fuji/Arainrr/Tryhard v4.3.43) - Building removal tool

### Removal Commands
- `/remove` - Toggle building removal tool
- `/remove admin` - Toggle admin removal mode
- `/remove structure` - Toggle structure removal mode
- `/remove external` - Toggle external object removal mode
- `/remove all` - Toggle remove all entities mode
- `/remove normal` - Toggle normal removal mode
- `/remove target` - Toggle target removal mode

---

## Skins
**Plugin**: Skins (Partial reference) - Skin management system

### Skin Commands
- `/skin` - Access skin workshop
- `/skin search <query>` - Search for skins
- `/skin apply <skin id>` - Apply skin to held item
- `/skin remove` - Remove current skin

---

## AdminRadar
**Plugin**: AdminRadar (v3.26.x) - Admin radar for tracking players and entities

### Radar Commands
- `/radar` - Toggle admin radar
- `/radar players` - Show only players on radar
- `/radar entities` - Show only entities on radar
- `/radar all` - Show everything on radar
- `/radar close` - Close radar window

---

## ZoneManager
**Plugin**: Zone Manager (k1lly0u v3.1.10) - Advanced zone management system

### Zone Management Commands
- `/zone` - Show zone editing help
- `/zone_add` - Add a new zone
- `/zone_remove` - Remove a zone
- `/zone_wipe` - Clear all zones
- `/zone_edit <zone ID>` - Start editing zone
- `/zone_list` - List all zones
- `/zone_flags` - Edit zone flags
- `/zone_stats` - View zone statistics
- `/zone_player [player name]` - Check player's zone info
- `/zone_entity` - Check entity's zone info
- `/zone <option> <value>` - Edit zone properties

---

## TruePVE
**Plugin**: TruePVE - PVE/PVP game mode management

### PVE/PVP Commands
- `/tpve` - Show PVE/PVP status
- `/tpve map` - Create/remove zone mapping entry
- `/tpve info` - View current PVE/PVP settings
- `/tpve broadcast` - Send PVE/PVP broadcast message

---

## PermissionsManager
**Plugin**: PermissionsManager (Steenamaroo v2.0.9) - Permission and group management UI

### Permission Commands
- `/perms` - Open permissions manager UI
- `/perms player <name>` - View/manage player permissions
- `/perms group <name>` - View/manage group permissions

---

## Vanish
**Plugin**: Vanish - Become invisible (admin command)

### Vanish Commands
- `/vanish` - Toggle invisibility mode
- `/vanish true` - Force vanish
- `/vanish false` - Force reappear

---

## BetterChat
**Plugin**: Better Chat (v1.x) - Chat formatting and group management

### Chat Management Commands
- `/chat` - Chat command help
- `/chat group <add|remove|set|list>` - Manage chat groups
- `/chat user <add|remove>` - Manage user groups

---

## ServerInfo
**Plugin**: Server Info - Display server information GUI

### Info Commands
- `/info` - Open server info window

---

## AutomaticAuthorization
**Plugin**: Automatic Authorization - Share cupboards with teams/clans/friends

### Authorization Commands
- `/auth` - Toggle authorization UI
- `/auth add <player>` - Add player to auth
- `/auth remove <player>` - Remove player from auth
- `/auth list` - List authorized players

---

## AutoDoors
**Plugin**: Auto Doors - Automatic door control

### Auto Door Commands
- `/authdoor` - Toggle auto auth for doors
- `/authdoor list` - List auto doors
- `/authdoor add` - Add door to auto list
- `/authdoor remove` - Remove door from auto list

---

## CopyPaste
**Plugin**: CopyPaste - Building copy/paste tool

### Building Commands
- `/copy` - Copy building structure
- `/paste` - Paste copied structure
- `/copylist` - List saved copies
- `/pasteback` - Paste structure backwards
- `/undo` - Undo last paste action

---

## Economics
**Plugin**: Economics - Player money/balance system

### Economy Commands
- `/balance` - Check your balance
- `/balance <player>` - Check player's balance (admin)
- `/pay <player> <amount>` - Transfer money to player
- `/deposit <amount>` - Deposit money
- `/withdraw <amount>` - Withdraw money
- `/setbalance <player> <amount>` - Set player balance (admin)
- `/wipeeconomics` - Wipe all balances (admin)

---

## NWG Commands
**Plugin**: NWG Commands (Gemini v1.0.0) - Custom command list display

### Command List
- `/commands` or `/cmds` - Display available commands based on group membership

**Shows different commands based on user group:**
- Standard (Everyone): /info, /home, /tpr, /shop, /kit, /remove
- VIP: /skin, /bgrade, /qr
- Admin/Staff: /vanish, /radar, /perm

---

## FurnaceSplitter
**Plugin**: Furnace Splitter - Furnace output management

### Furnace Commands
- `/fssplit` - Toggle furnace splitter
- `/fssplit on` - Enable furnace splitter
- `/fssplit off` - Disable furnace splitter

---

## GatherManager
**Plugin**: Gather Manager - Adjust resource gathering rates

### Gather Commands
- `/gm` - Show gather manager info
- `/gm reset` - Reset gather rates to default

---

## RaidableBases
**Plugin**: Raid able Bases - Spawnable raid bases

### Raid Commands
- `/rb` - Open raidable bases menu
- `/rh` - Raid hunter mode toggle
- `/rb invite <player>` - Invite player to raid group
- `/rb spawn` - Spawn a raidable base (admin)
- `/rb toggle` - Toggle raid spawning (admin)
- `/rb populate` - Populate bases with NPCs (admin)

---

## QuickSmelt
**Plugin**: Quick Smelt - Furnace smelting speed control

### Smelt Commands
- `/qs` - Toggle quick smelt
- `/qs speed` - Show current smelt speed

---

## SpawnNPC
**Plugin**: Spawn NPC - Spawn NPCs in game

### NPC Commands
- `/npc count` - Show NPC count
- `/npc kill` - Kill all NPCs
- `/npc respawn` - Respawn all NPCs
- `/npc reload` - Reload plugin

---

## MarkerManager
**Plugin**: Marker Manager - Map marker management

### Marker Commands
- `/marker` - Create/manage map markers
- `/marker delete` - Delete marker
- `/marker list` - List all markers

---

## Summary Statistics
- **Total Plugins Scanned**: 28
- **Total Commands Extracted**: 150+
- **Command Categories**: Teleportation, Clan Management, Items, Building, Admin Tools, Economy, Chat, Zones, PVE/PVP, Customization

## Notes
- Most commands support `/help` or `-h` flags for additional information
- Admin commands typically require admin permission or specific plugin permissions
- Chat commands are prefixed with `/`
- Some commands may require specific permissions or group membership
- Cooldowns and limits vary by server configuration and plugin settings
