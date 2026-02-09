# How to Become Admin

To give yourself admin (Owner) status in Rust, follow these steps:

1.  **Open Server Console**: Go to the window running your server (the command prompt/terminal).
2.  **Find your SteamID**: This is your Steam64 ID (starts with 7656...).
3.  **Run Command**:
    ```
    ownerid <SteamID> "YourName"
    ```
    Example: `ownerid 76561198012345678 "Monster"`
4.  **Save Config**:
    ```
    server.writecfg
    ```
5.  **Reconnect**: Disconnect and rejoin the server.

## Important: Secure Login
Since `NWGAdmin` is installed, once you reconnect as an admin:
1.  You will be **frozen**.
2.  You must set a password: `/setadminpass <password>`.
3.  You will be kicked.
4.  Reconnect and login: `/login <password>`.

## Standardized Admin Commands

All extensive commands have been standardized to follow a consistent naming convention.

| Feature | New Command | Old / Removed Command | Description |
| :--- | :--- | :--- | :--- |
| **Admin Duty** | `/adminduty` | `goadminduty` | Toggles admin mode (god, vanish, etc.) |
| **Raid Event** | `/startraid` | `raid.start` | Starts a standard base raid event |
| **Piracy Event** | `/spawnpiracy` | `piracy.spawn` | Spawns the piracy tugboat event |
| **Race Event** | `/startrace` | `race.start` | Starts a server-wide race |
| **Auto Code** | `/setautocode` | `autocode` | Sets/Checks code lock automation stats |
| **Set Home** | `/sethome <name>` | `/home add` | Sets a home teleport point |
| **Give Money** | `/givemoney <player> <amt>` | `market.deposit` | Gives currency to a player |
| **Set Balance** | `/setbalance <player> <amt>` | N/A | Sets a player's balance directly |
| **Teleport** | `/tp <player>` | N/A | Teleport to a player |
| **God Mode** | `/god` | N/A | Toggle invulnerability |
| **Vanish** | `/vanish` | N/A | Toggle invisibility |
| **Start Dungeon**| `/dungeon start global` | N/A | Starts the global dungeon event |

### Dungeon Fixes
- **Dungeon Teleportation**: Initiating a dungeon now correctly teleports you to a safe, grounded platform at Y=500. No more falling into the ocean.
- **Floor Stability**: Dungeon floors are now set to "Metal" grade and "Grounded" to prevent collapse.
