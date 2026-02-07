# Plugin Integration Management Commands
**Quick Reference Guide for Admins**

---

## 🎮 Essential Admin Commands

### **Economy Management**
```
/economy balance @playername          - Check player's balance
/economy give @playername 1000        - Give player currency
/economy take @playername 500         - Remove currency from player
/economy set @playername 5000         - Set exact balance
```

### **Raid Event Management**
```
/raidbase                             - Spawn random raid base
/raidbase [profilename]               - Spawn specific base
/raidbase despawn                     - Despawn all active raids
/raidbase despawn_inactive            - Despawn only inactive raids
/raidbase active                      - Show active raids and queue
/rbhunter blockraids                  - Block raids at your position
/rbhunter invite @playername          - Invite player to nearest raid
/rbhunter version                     - Show plugin version
/rbhunter wipe                        - Wipe all raid data
/rb.reloadconfig                      - Reload RaidableBases config
/rb.reloadprofiles                    - Reload building profiles
/rb.reloadtables                      - Reload loot tables
/rb.config                            - In-game config editor
/rb.populate                          - Force spawn raids
/rb.toggle                            - Toggle raid spawning
```

### **Zone Management**
```
/zm list                              - List all zones
/zm create [name] [radius]            - Create a zone
/zm remove [name]                     - Delete a zone
/zm info [name]                       - Zone information
/zm enter [zonename] @player          - Move player to zone
/zm reset                             - Reset zone cache
```

### **Shop Management**
```
/shop                                 - Open shop interface
/shop [category]                      - Open specific category
/shop buy [item]                      - Buy item from shop
/shop sell [item]                     - Sell item to shop
```

### **Clan Management**
```
/clan create [tagname]                - Create a clan
/clan invite @playername              - Invite to clan
/clan kick @playername                - Remove from clan
/clan disband                         - Dissolve clan
/clan list                            - Show all clans
```

### **Information & Help**
```
/info                                 - Open server information
/info [tab]                           - Open specific info tab
/rbhelp                               - Player guide to raids
/rbadminhelp                          - Admin configuration guide
/hud                                  - Toggle HUD display
```

---

## 🔧 Configuration Management Commands

### **Reload Configurations (Without Restart)**
```
/oxide.reload Economics               - Reload economy config
/oxide.reload GUIShop                 - Reload shop config
/oxide.reload RaidableBases           - Reload raids + config
/oxide.reload TruePVE                 - Reload PVE rules
/oxide.reload ZoneManager             - Reload zone system
/oxide.reload BetterChat              - Reload chat system
```

### **Reload Specific Subsystems**
```
/rb.reloadconfig                      - Reload RaidableBases settings
/rb.reloadprofiles                    - Reload raid profiles
/rb.reloadtables                      - Reload loot tables
/zm reset                             - Reset ZoneManager cache
```

### **Plugin Status**
```
/plugins                              - List all loaded plugins
/plugins info [pluginname]            - Plugin details
/oxide.plugins                        - Oxide plugin management
```

---

## 📊 System Status Monitoring

### **Check Plugin Status**
```
/plugins info economics               ← Currency system
/plugins info guishop                 ← Shop system
/plugins info raidablebases           ← Raid system
/plugins info zonemanager             ← Zone system
/plugins info truepve                 ← PVE/PVP system
/plugins info clans                   ← Clan system
/plugins info nwg_hud                 ← HUD system
```

### **View Active Events**
```
/raidbase active                      - See all active raids with:
                                        - Current type (PVE/PVP)
                                        - Completion percentage
                                        - Players in raid
                                        - Despawn countdown
```

### **Check Zones**
```
/zm list                              - All zones:
                                        - Zone name
                                        - Radius
                                        - Player count
                                        - Active status
```

---

## 🎯 Integration Workflow Examples

### **Scenario 1: Player Raids Base and Earns Reward**

**Admin verification commands:**
```
1. Check active raids:
   /raidbase active
   
2. Wait for player to complete raid
   
3. Verify reward given:
   /economy balance @playername
   
4. If balance not updated:
   /oxide.reload RaidableBases
```

### **Scenario 2: Create PVP Zone for Raids**

**Setup commands:**
```
1. Create zone:
   /zm create pvp_zone 150
   
2. Configure zone (edit JSON config for details)
   
3. Set RaidableBases to use zone:
   - Edit RaidableBases.json
   - Add "pvp_zone" to "Allowed Zone Manager Zones"
   
4. Reload config:
   /rb.reloadconfig
   
5. Verify zone:
   /zm list
```

### **Scenario 3: Add New Shop Item**

**Configuration commands:**
```
1. Edit GUIShop.json
   - Add item to "Shop - Shop Categories"
   - Set price in economics
   
2. Reload shop:
   /oxide.reload GUIShop
   
3. Verify item in shop:
   /shop
```

### **Scenario 4: Adjust Raid Rewards**

**Management commands:**
```
1. Edit RaidableBases.json:
   - Find "Difficulty Rewards" section
   - Adjust: Easy, Normal, Hard amounts
   
2. Reload config:
   /rb.reloadconfig
   
3. Spawn test raid:
   /raidbase
   
4. Complete raid and check reward:
   /economy balance @playername
```

---

## 🚨 Troubleshooting Commands

### **If Raids Don't Spawn**
```
/raidbase active                      - Check if queue is stuck
/rb.populate                          - Force spawn attempt
/rb.reloadconfig                      - Reload configuration
/plugins info raidablebases           - Check plugin status
```

### **If Rewards Not Given**
```
/economy balance @player              - Check balance
/plugins info economics               - Verify plugin loaded
/oxide.reload Economics               - Reload economy
/oxide.reload RaidableBases           - Reload raids
```

### **If Zones Not Working**
```
/zm list                              - List all zones
/plugins info zonemanager             - Verify loaded
/zm reset                             - Reset zone cache
/oxide.reload ZoneManager             - Full reload
```

### **If PVE/PVP Rules Not Applied**
```
/zm list                              - Check zone exists
/plugins info truepve                 - Verify loaded
/plugins info raidablebases           - Verify loaded
/oxide.reload TruePVE                 - Reload rules
```

### **If Shop Doesn't Show Economy**
```
/shop                                 - Open shop
/economy balance @self                - Check personal balance
/plugins info guishop                 - Verify loaded
/plugins info economics               - Verify loaded
/oxide.reload GUIShop                 - Reload shop
```

---

## 📋 Daily Admin Checklist

**Start of Day:**
```
☐ Check plugins loaded: /plugins
☐ Check active raids: /raidbase active
☐ Check active zones: /zm list
☐ Monitor server logs for errors
```

**During Play:**
```
☐ Monitor economy: /economy balance @player
☐ Watch raid completion: /raidbase active
☐ Track player progress: /info
☐ Check PVP/PVE compliance: Zone enforcement working
```

**End of Day:**
```
☐ Check for any error messages
☐ Verify raid despawns working: /raidbase active
☐ Confirm player data saved
☐ Review troublesome players
```

---

## 🔐 Permission Commands

### **View Permissions**
```
/oxide.grant user @playername raidablebases.allow
  - Allow player to raid bases

/oxide.grant user @playername guishop.use
  - Allow player to use shop

/oxide.grant user @playername zonemanager.visit
  - Allow player to enter zones

/oxide.grant user @playername truepve.raids
  - Allow player in raid PVE zones
```

### **Remove Permissions**
```
/oxide.revoke user @playername [permission]

Example:
/oxide.revoke user @playername raidablebases.allow
```

### **View User Permissions**
```
/oxide.show perms @playername
  - Lists all permissions for player
```

---

## 📝 Server Config Files (Edit Locations)

```
Economy:
  oxide/config/Economics.json
  
Shop:
  oxide/config/GUIShop.json
  
Raids:
  oxide/config/RaidableBases.json
  
Zones:
  (Created in-game with /zm commands)
  
PVE/PVP:
  oxide/config/TruePVE.json
  
Clans:
  oxide/config/Clans.json
  
Server Info:
  oxide/config/ServerInfo.json
```

---

## 🆘 Support References

**Documentation Files:**
```
oxide/PLUGIN_INTEGRATION_GUIDE.md
  - Complete plugin linkage guide
  - Configuration examples
  - Integration workflows
  
oxide/PLUGIN_INTEGRATION_STATUS.md
  - Current integration status
  - Dependency matrix
  - Verification checklist
  
oxide/PLUGIN_CODE_REVIEW.md
  - Code quality report
  - Known issues
  - Recommendations
  
oxide/PLUGIN_AUDIT_SUMMARY.md
  - Plugin audit results
  - Status matrix
```

---

## 📞 Emergency Reset Commands

**If system is unstable:**
```
1. Stop spawning new raids:
   /rb.toggle
   
2. Despawn all active raids:
   /raidbase despawn
   
3. Reload all plugins:
   /oxide.reload *
   
4. Re-enable raids:
   /rb.toggle
```

**Full system reset (more aggressive):**
```
1. Close shop:
   (Edit GUIShop.json, disable or unload)
   
2. Stop raids:
   /raidbase despawn
   
3. Clear zones:
   /zm reset
   
4. Restart server
   (Last resort)
```

---

**Quick Access Bookmark These Commands!**

Most Used:
- `/raidbase active` - Current raid status
- `/economy balance @self` - Check balance
- `/zm list` - Show zones
- `/shop` - Open shop
- `/rbhelp` - Player guide
- `/rbadminhelp` - Admin guide

---

*This guide was created January 29, 2026 for your server's integrated plugin system.*
